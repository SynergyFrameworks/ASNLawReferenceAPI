using ASNLawReferenceAPI.Data;
using ASNLawReferenceAPI.Models;
using System.Security.Claims;

namespace ASNLawReferenceAPI.Services
{
    public interface IDocumentService
    {
        Task<IEnumerable<DocumentDto>> GetAllAsync();
        Task<DocumentDto?> GetByIdAsync(Guid id);
        Task<IEnumerable<DocumentDto>> GetByJurisdictionAsync(string jurisdiction);
        Task<IEnumerable<DocumentDto>> GetVersionHistoryAsync(Guid id);
        Task<DocumentDto> UploadAsync(CreateDocumentDto createDocumentDto, ClaimsPrincipal user);
        Task<DocumentDto?> UpdateAsync(Guid id, CreateDocumentDto updateDocumentDto, ClaimsPrincipal user);
        Task<bool> DeleteAsync(Guid id, ClaimsPrincipal user);
        Task LogAccessAsync(Guid documentId, string actionType, ClaimsPrincipal user);
    }

    public class DocumentService : IDocumentService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IChunkRepository _chunkRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorDbService _vectorDbService;
        private readonly IFullTextSearchService _fullTextSearchService;
        private readonly IIndexingService _indexingService;
        private readonly ILogger<DocumentService> _logger;

        public DocumentService(
            IDocumentRepository documentRepository,
            IChunkRepository chunkRepository,
            IBlobStorageService blobStorageService,
            IEmbeddingService embeddingService,
            IVectorDbService vectorDbService,
            IFullTextSearchService fullTextSearchService,
            IIndexingService indexingService,
            ILogger<DocumentService> logger)
        {
            _documentRepository = documentRepository;
            _chunkRepository = chunkRepository;
            _blobStorageService = blobStorageService;
            _embeddingService = embeddingService;
            _vectorDbService = vectorDbService;
            _fullTextSearchService = fullTextSearchService;
            _indexingService = indexingService;
            _logger = logger;
        }

        public async Task<IEnumerable<DocumentDto>> GetAllAsync()
        {
            var documents = await _documentRepository.GetAllAsync();
            return documents.Select(MapToDto);
        }

        public async Task<DocumentDto?> GetByIdAsync(Guid id)
        {
            var document = await _documentRepository.GetByIdAsync(id);
            return document != null ? MapToDto(document) : null;
        }

        public async Task<IEnumerable<DocumentDto>> GetByJurisdictionAsync(string jurisdiction)
        {
            var documents = await _documentRepository.GetByJurisdictionAsync(jurisdiction);
            return documents.Select(MapToDto);
        }

        public async Task<IEnumerable<DocumentDto>> GetVersionHistoryAsync(Guid id)
        {
            var documents = await _documentRepository.GetVersionHistoryAsync(id);
            return documents.Select(MapToDto);
        }

        public async Task<DocumentDto> UploadAsync(CreateDocumentDto createDocumentDto, ClaimsPrincipal user)
        {
            // Extract user ID from claims
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) 
                ?? throw new UnauthorizedAccessException("User ID not found in claims");

            // Upload PDF to blob storage
            var containerName = "legal-documents";
            var blobName = $"{Guid.NewGuid()}-{createDocumentDto.Title.Replace(" ", "-").ToLower()}.pdf";
            
            using var stream = createDocumentDto.PdfFile.OpenReadStream();
            var pdfUrl = await _blobStorageService.UploadFileAsync(containerName, blobName, stream, "application/pdf");

            // Create document in database
            var document = new Document
            {
                Id = Guid.NewGuid(),
                Title = createDocumentDto.Title,
                Jurisdiction = createDocumentDto.Jurisdiction,
                PdfUrl = pdfUrl,
                Version = "1.0", // Default version for new documents
                ParentDocumentId = createDocumentDto.ParentDocumentId,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _documentRepository.AddAsync(document);

            // Start async processing
            await Task.Run(() => _indexingService.ProcessDocumentAsync(document.Id));

            return MapToDto(document);
        }

        public async Task<DocumentDto?> UpdateAsync(Guid id, CreateDocumentDto updateDocumentDto, ClaimsPrincipal user)
        {
            var existingDocument = await _documentRepository.GetByIdAsync(id);
            if (existingDocument == null)
                return null;

            // Extract user ID from claims
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) 
                ?? throw new UnauthorizedAccessException("User ID not found in claims");

            // Create new version of document
            var newVersion = CalculateNewVersion(existingDocument.Version);
            
            // Upload new PDF to blob storage (if provided)
            string pdfUrl = existingDocument.PdfUrl;
            
            if (updateDocumentDto.PdfFile != null)
            {
                var containerName = "legal-documents";
                var blobName = $"{Guid.NewGuid()}-{updateDocumentDto.Title.Replace(" ", "-").ToLower()}.pdf";
                
                using var stream = updateDocumentDto.PdfFile.OpenReadStream();
                pdfUrl = await _blobStorageService.UploadFileAsync(containerName, blobName, stream, "application/pdf");
            }

            // Create new document version
            var newDocument = new Document
            {
                Id = Guid.NewGuid(),
                Title = updateDocumentDto.Title,
                Jurisdiction = updateDocumentDto.Jurisdiction,
                PdfUrl = pdfUrl,
                Version = newVersion,
                ParentDocumentId = id, // Reference to previous version
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _documentRepository.AddAsync(newDocument);

            // Start async processing
            await Task.Run(() => _indexingService.ProcessDocumentAsync(newDocument.Id));

            return MapToDto(newDocument);
        }

        public async Task<bool> DeleteAsync(Guid id, ClaimsPrincipal user)
        {
            // Check if document exists
            var document = await _documentRepository.GetByIdAsync(id);
            if (document == null)
                return false;

            // Delete document
            await _documentRepository.DeleteAsync(id);

            // Clean up associated resources
            await _chunkRepository.DeleteByDocumentIdAsync(id);
            
            // Delete from vector database (ignore errors if it doesn't exist)
            try
            {
                await _vectorDbService.DeleteDocumentVectorsAsync(id.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting vectors for document {DocumentId}", id);
            }

            // Delete from full-text search index (ignore errors if it doesn't exist)
            try
            {
                await _fullTextSearchService.DeleteDocumentFromIndexAsync(id.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting document from full-text index {DocumentId}", id);
            }

            // Delete PDF from blob storage (ignore errors if it doesn't exist)
            try
            {
                var blobName = GetBlobNameFromUrl(document.PdfUrl);
                if (!string.IsNullOrEmpty(blobName))
                {
                    await _blobStorageService.DeleteFileAsync("legal-documents", blobName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting PDF blob for document {DocumentId}", id);
            }

            return true;
        }

        public async Task LogAccessAsync(Guid documentId, string actionType, ClaimsPrincipal user)
        {
            // Implementation would create an AccessLog entry
            // Omitted for brevity
        }

        private string CalculateNewVersion(string currentVersion)
        {
            if (!Version.TryParse(currentVersion, out var version))
                return "1.0"; // Default if parsing fails
            
            return $"{version.Major}.{version.Minor + 1}";
        }

        private string? GetBlobNameFromUrl(string url)
        {
            try
            {
                // Extract the blob name from the URL (implementation depends on storage provider)
                var uri = new Uri(url);
                var segments = uri.Segments;
                return segments.Length > 0 ? segments[^1] : null;
            }
            catch
            {
                return null;
            }
        }

        private static DocumentDto MapToDto(Document document)
        {
            return new DocumentDto
            {
                Id = document.Id,
                Title = document.Title,
                Jurisdiction = document.Jurisdiction,
                PdfUrl = document.PdfUrl,
                Version = document.Version,
                ParentDocumentId = document.ParentDocumentId,
                CreatedAt = document.CreatedAt,
                CreatedBy = document.CreatedBy
            };
        }
    }
}