using ASNLawReferenceAPI.Data;
using ASNLawReferenceAPI.Models;
using System.Security.Claims;

namespace ASNLawReferenceAPI.Services
{
    public interface IAnnotationService
    {
        Task<IEnumerable<AnnotationDto>> GetByDocumentIdAsync(Guid documentId, ClaimsPrincipal user);
        Task<IEnumerable<AnnotationDto>> GetByChunkIdAsync(Guid chunkId, ClaimsPrincipal user);
        Task<IEnumerable<AnnotationDto>> GetThreadAsync(Guid annotationId, ClaimsPrincipal user);
        Task<AnnotationDto?> GetByIdAsync(Guid id, ClaimsPrincipal user);
        Task<IEnumerable<AnnotationDto>> GetUserAnnotationsAsync(ClaimsPrincipal user);
        Task<AnnotationDto> CreateAsync(CreateAnnotationDto createAnnotationDto, ClaimsPrincipal user);
        Task<AnnotationDto?> UpdateAsync(Guid id, CreateAnnotationDto updateAnnotationDto, ClaimsPrincipal user);
        Task<bool> DeleteAsync(Guid id, ClaimsPrincipal user);
        Task<bool> ApproveAsync(Guid id, ClaimsPrincipal user);
    }

    public class AnnotationService : IAnnotationService
    {
        private readonly IAnnotationRepository _annotationRepository;
        private readonly IDocumentRepository _documentRepository;
        private readonly IChunkRepository _chunkRepository;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<AnnotationService> _logger;

        public AnnotationService(
            IAnnotationRepository annotationRepository,
            IDocumentRepository documentRepository,
            IChunkRepository chunkRepository,
            IUserRepository userRepository,
            ILogger<AnnotationService> logger)
        {
            _annotationRepository = annotationRepository;
            _documentRepository = documentRepository;
            _chunkRepository = chunkRepository;
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task<IEnumerable<AnnotationDto>> GetByDocumentIdAsync(Guid documentId, ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Enumerable.Empty<AnnotationDto>();

            var annotations = await _annotationRepository.GetByDocumentIdAsync(documentId);
            
            // Filter annotations - show only shared annotations or user's own annotations
            var filteredAnnotations = annotations
                .Where(a => a.IsShared || a.UserId == userId)
                .ToList();

            // Load replies for each annotation
            var result = new List<AnnotationDto>();
            foreach (var annotation in filteredAnnotations)
            {
                var dto = MapToDto(annotation);
                var replies = await _annotationRepository.GetThreadAsync(annotation.Id);
                
                // Filter replies - show only shared replies or user's own replies
                dto.Replies = replies
                    .Where(r => r.Id != annotation.Id) // Exclude the parent annotation
                    .Where(r => r.IsShared || r.UserId == userId)
                    .Select(MapToDto)
                    .ToList();
                
                result.Add(dto);
            }

            return result;
        }

        public async Task<IEnumerable<AnnotationDto>> GetByChunkIdAsync(Guid chunkId, ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Enumerable.Empty<AnnotationDto>();

            var annotations = await _annotationRepository.GetByChunkIdAsync(chunkId);
            
            // Filter annotations - show only shared annotations or user's own annotations
            var filteredAnnotations = annotations
                .Where(a => a.IsShared || a.UserId == userId)
                .ToList();

            // Load replies for each annotation
            var result = new List<AnnotationDto>();
            foreach (var annotation in filteredAnnotations)
            {
                var dto = MapToDto(annotation);
                var replies = await _annotationRepository.GetThreadAsync(annotation.Id);
                
                // Filter replies - show only shared replies or user's own replies
                dto.Replies = replies
                    .Where(r => r.Id != annotation.Id) // Exclude the parent annotation
                    .Where(r => r.IsShared || r.UserId == userId)
                    .Select(MapToDto)
                    .ToList();
                
                result.Add(dto);
            }

            return result;
        }

        public async Task<IEnumerable<AnnotationDto>> GetThreadAsync(Guid annotationId, ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Enumerable.Empty<AnnotationDto>();

            var thread = await _annotationRepository.GetThreadAsync(annotationId);
            
            // Filter annotations - show only shared annotations or user's own annotations
            return thread
                .Where(a => a.IsShared || a.UserId == userId)
                .Select(MapToDto)
                .ToList();
        }

        public async Task<AnnotationDto?> GetByIdAsync(Guid id, ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return null;

            var annotation = await _annotationRepository.GetByIdAsync(id);
            if (annotation == null || (!annotation.IsShared && annotation.UserId != userId))
                return null;

            return MapToDto(annotation);
        }

        public async Task<IEnumerable<AnnotationDto>> GetUserAnnotationsAsync(ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Enumerable.Empty<AnnotationDto>();

            var annotations = await _annotationRepository.GetByUserIdAsync(userId);
            return annotations.Select(MapToDto).ToList();
        }

        public async Task<AnnotationDto> CreateAsync(CreateAnnotationDto createAnnotationDto, ClaimsPrincipal user)
        {
            // Extract user ID from claims
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) 
                ?? throw new UnauthorizedAccessException("User ID not found in claims");

            // Validate document exists
            var document = await _documentRepository.GetByIdAsync(createAnnotationDto.DocumentId);
            if (document == null)
                throw new ArgumentException("Document not found");

            // Validate chunk if provided
            if (createAnnotationDto.ChunkId.HasValue)
            {
                var chunk = await _chunkRepository.GetByIdAsync(createAnnotationDto.ChunkId.Value);
                if (chunk == null)
                    throw new ArgumentException("Chunk not found");
                
                // Ensure chunk belongs to the document
                if (chunk.DocumentId != createAnnotationDto.DocumentId)
                    throw new ArgumentException("Chunk does not belong to the specified document");
            }

            // Validate parent annotation if provided
            if (createAnnotationDto.ParentId.HasValue)
            {
                var parentAnnotation = await _annotationRepository.GetByIdAsync(createAnnotationDto.ParentId.Value);
                if (parentAnnotation == null)
                    throw new ArgumentException("Parent annotation not found");
                
                // Ensure parent annotation belongs to the document
                if (parentAnnotation.DocumentId != createAnnotationDto.DocumentId)
                    throw new ArgumentException("Parent annotation does not belong to the specified document");
            }

            // Create annotation
            var annotation = new Annotation
            {
                Id = Guid.NewGuid(),
                DocumentId = createAnnotationDto.DocumentId,
                ChunkId = createAnnotationDto.ChunkId,
                Page = createAnnotationDto.Page,
                Type = createAnnotationDto.Type,
                Text = createAnnotationDto.Text,
                UserId = userId,
                IsShared = createAnnotationDto.IsShared,
                IsApproved = false, // New annotations are not approved by default
                ParentId = createAnnotationDto.ParentId,
                CreatedAt = DateTime.UtcNow
            };

            await _annotationRepository.AddAsync(annotation);

            return MapToDto(annotation);
        }

        public async Task<AnnotationDto?> UpdateAsync(Guid id, CreateAnnotationDto updateAnnotationDto, ClaimsPrincipal user)
        {
            // Extract user ID from claims
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) 
                ?? throw new UnauthorizedAccessException("User ID not found in claims");

            // Get existing annotation
            var existingAnnotation = await _annotationRepository.GetByIdAsync(id);
            if (existingAnnotation == null)
                return null;

            // Check if user is the owner of the annotation
            if (existingAnnotation.UserId != userId)
                throw new UnauthorizedAccessException("You can only update your own annotations");

            // Update annotation properties
            existingAnnotation.Type = updateAnnotationDto.Type;
            existingAnnotation.Text = updateAnnotationDto.Text;
            existingAnnotation.IsShared = updateAnnotationDto.IsShared;
            
            // If page is being updated, validate it's within the document
            if (existingAnnotation.Page != updateAnnotationDto.Page)
            {
                existingAnnotation.Page = updateAnnotationDto.Page;
            }

            // Save changes
            await _annotationRepository.UpdateAsync(existingAnnotation);

            return MapToDto(existingAnnotation);
        }

        public async Task<bool> DeleteAsync(Guid id, ClaimsPrincipal user)
        {
            // Extract user ID from claims
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) 
                ?? throw new UnauthorizedAccessException("User ID not found in claims");

            // Get existing annotation
            var existingAnnotation = await _annotationRepository.GetByIdAsync(id);
            if (existingAnnotation == null)
                return false;

            // Check if user is the owner of the annotation or has admin rights
            var isAdmin = user.IsInRole("Admin");
            if (existingAnnotation.UserId != userId && !isAdmin)
                throw new UnauthorizedAccessException("You can only delete your own annotations");

            // Delete annotation
            return await _annotationRepository.DeleteAsync(id);
        }

        public async Task<bool> ApproveAsync(Guid id, ClaimsPrincipal user)
        {
            // Check if user has approval rights (admin or researcher)
            var isAdmin = user.IsInRole("Admin");
            var isResearcher = user.IsInRole("Researcher");
            
            if (!isAdmin && !isResearcher)
                throw new UnauthorizedAccessException("You do not have permission to approve annotations");

            // Get existing annotation
            var existingAnnotation = await _annotationRepository.GetByIdAsync(id);
            if (existingAnnotation == null)
                return false;

            // Update approval status
            existingAnnotation.IsApproved = true;
            await _annotationRepository.UpdateAsync(existingAnnotation);

            return true;
        }

        private static AnnotationDto MapToDto(Annotation annotation)
        {
            return new AnnotationDto
            {
                Id = annotation.Id,
                DocumentId = annotation.DocumentId,
                ChunkId = annotation.ChunkId,
                Page = annotation.Page,
                Type = annotation.Type,
                Text = annotation.Text,
                UserId = annotation.UserId,
                UserName = annotation.User?.Name ?? annotation.UserId,
                IsShared = annotation.IsShared,
                IsApproved = annotation.IsApproved,
                ParentId = annotation.ParentId,
                CreatedAt = annotation.CreatedAt,
                Replies = new List<AnnotationDto>() // Populated separately when needed
            };
        }
    }
}