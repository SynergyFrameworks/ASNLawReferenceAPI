using LegalReferenceAPI.Data;
using LegalReferenceAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LegalReferenceAPI.Data
{
    // Document Repository
    public interface IDocumentRepository
    {
        Task<IEnumerable<Document>> GetAllAsync();
        Task<Document?> GetByIdAsync(Guid id);
        Task<IEnumerable<Document>> GetByJurisdictionAsync(string jurisdiction);
        Task<IEnumerable<Document>> GetVersionHistoryAsync(Guid documentId);
        Task<Document> AddAsync(Document document);
        Task UpdateAsync(Document document);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> ExistsAsync(Guid id);
    }

    public class DocumentRepository : IDocumentRepository
    {
        private readonly DocumentDbContext _context;

        public DocumentRepository(DocumentDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Document>> GetAllAsync()
        {
            return await _context.Documents
                .Include(d => d.SearchWeight)
                .ToListAsync();
        }

        public async Task<Document?> GetByIdAsync(Guid id)
        {
            return await _context.Documents
                .Include(d => d.SearchWeight)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<IEnumerable<Document>> GetByJurisdictionAsync(string jurisdiction)
        {
            return await _context.Documents
                .Include(d => d.SearchWeight)
                .Where(d => d.Jurisdiction == jurisdiction)
                .ToListAsync();
        }

        public async Task<IEnumerable<Document>> GetVersionHistoryAsync(Guid documentId)
        {
            // Get the root document
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId);
            
            if (document == null)
                return Enumerable.Empty<Document>();

            // Find the root document (if the current document is not the root)
            Guid rootId = documentId;
            while (document?.ParentDocumentId != null)
            {
                document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == document.ParentDocumentId);
                if (document != null)
                    rootId = document.Id;
            }

            // Get all documents in the version tree
            var versionTree = new List<Document>();
            await GetDocumentTreeAsync(rootId, versionTree);
            
            return versionTree.OrderByDescending(d => d.CreatedAt);
        }

        private async Task GetDocumentTreeAsync(Guid documentId, List<Document> versionTree)
        {
            var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
            if (document != null)
            {
                versionTree.Add(document);
                
                var children = await _context.Documents
                    .Where(d => d.ParentDocumentId == documentId)
                    .ToListAsync();
                
                foreach (var child in children)
                {
                    await GetDocumentTreeAsync(child.Id, versionTree);
                }
            }
        }

        public async Task<Document> AddAsync(Document document)
        {
            await _context.Documents.AddAsync(document);
            await _context.SaveChangesAsync();
            return document;
        }

        public async Task UpdateAsync(Document document)
        {
            _context.Entry(document).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var document = await _context.Documents.FindAsync(id);
            if (document == null)
                return false;
            
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(Guid id)
        {
            return await _context.Documents.AnyAsync(d => d.Id == id);
        }
    }

    // Chunk Repository
    public interface IChunkRepository
    {
        Task<IEnumerable<Chunk>> GetByDocumentIdAsync(Guid documentId);
        Task<Chunk?> GetByIdAsync(Guid id);
        Task<IEnumerable<Chunk>> GetByPageAsync(Guid documentId, int page);
        Task<Chunk> AddAsync(Chunk chunk);
        Task UpdateAsync(Chunk chunk);
        Task AddRangeAsync(IEnumerable<Chunk> chunks);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> DeleteByDocumentIdAsync(Guid documentId);
    }

    public class ChunkRepository : IChunkRepository
    {
        private readonly DocumentDbContext _context;

        public ChunkRepository(DocumentDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Chunk>> GetByDocumentIdAsync(Guid documentId)
        {
            return await _context.Chunks
                .Where(c => c.DocumentId == documentId)
                .OrderBy(c => c.Page)
                .ThenBy(c => c.StartOffset)
                .ToListAsync();
        }

        public async Task<Chunk?> GetByIdAsync(Guid id)
        {
            return await _context.Chunks.FindAsync(id);
        }

        public async Task<IEnumerable<Chunk>> GetByPageAsync(Guid documentId, int page)
        {
            return await _context.Chunks
                .Where(c => c.DocumentId == documentId && c.Page == page)
                .OrderBy(c => c.StartOffset)
                .ToListAsync();
        }

        public async Task<Chunk> AddAsync(Chunk chunk)
        {
            await _context.Chunks.AddAsync(chunk);
            await _context.SaveChangesAsync();
            return chunk;
        }

        public async Task UpdateAsync(Chunk chunk)
        {
            _context.Entry(chunk).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task AddRangeAsync(IEnumerable<Chunk> chunks)
        {
            await _context.Chunks.AddRangeAsync(chunks);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var chunk = await _context.Chunks.FindAsync(id);
            if (chunk == null)
                return false;
            
            _context.Chunks.Remove(chunk);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteByDocumentIdAsync(Guid documentId)
        {
            var chunks = await _context.Chunks
                .Where(c => c.DocumentId == documentId)
                .ToListAsync();
            
            if (!chunks.Any())
                return false;
            
            _context.Chunks.RemoveRange(chunks);
            await _context.SaveChangesAsync();
            return true;
        }
    }

    // Annotation Repository
    public interface IAnnotationRepository
    {
        Task<IEnumerable<Annotation>> GetByDocumentIdAsync(Guid documentId);
        Task<IEnumerable<Annotation>> GetByChunkIdAsync(Guid chunkId);
        Task<IEnumerable<Annotation>> GetByUserIdAsync(string userId);
        Task<Annotation?> GetByIdAsync(Guid id);
        Task<IEnumerable<Annotation>> GetThreadAsync(Guid parentAnnotationId);
        Task<Annotation> AddAsync(Annotation annotation);
        Task UpdateAsync(Annotation annotation);
        Task<bool> DeleteAsync(Guid id);
    }

    public class AnnotationRepository : IAnnotationRepository
    {
        private readonly DocumentDbContext _context;

        public AnnotationRepository(DocumentDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Annotation>> GetByDocumentIdAsync(Guid documentId)
        {
            return await _context.Annotations
                .Include(a => a.User)
                .Where(a => a.DocumentId == documentId && a.ParentId == null)
                .OrderBy(a => a.Page)
                .ThenBy(a => a.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Annotation>> GetByChunkIdAsync(Guid chunkId)
        {
            return await _context.Annotations
                .Include(a => a.User)
                .Where(a => a.ChunkId == chunkId && a.ParentId == null)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Annotation>> GetByUserIdAsync(string userId)
        {
            return await _context.Annotations
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();
        }

        public async Task<Annotation?> GetByIdAsync(Guid id)
        {
            return await _context.Annotations
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<IEnumerable<Annotation>> GetThreadAsync(Guid parentAnnotationId)
        {
            return await _context.Annotations
                .Include(a => a.User)
                .Where(a => a.ParentId == parentAnnotationId || a.Id == parentAnnotationId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();
        }

        public async Task<Annotation> AddAsync(Annotation annotation)
        {
            await _context.Annotations.AddAsync(annotation);
            await _context.SaveChangesAsync();
            return annotation;
        }

        public async Task UpdateAsync(Annotation annotation)
        {
            _context.Entry(annotation).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var annotation = await _context.Annotations.FindAsync(id);
            if (annotation == null)
                return false;
            
            _context.Annotations.Remove(annotation);
            await _context.SaveChangesAsync();
            return true;
        }
    }

    // User Repository
    public interface IUserRepository
    {
        Task<IEnumerable<ApplicationUser>> GetAllAsync();
        Task<ApplicationUser?> GetByIdAsync(string id);
        Task<ApplicationUser?> GetByEmailAsync(string email);
        Task<ApplicationUser> AddAsync(ApplicationUser user);
        Task UpdateAsync(ApplicationUser user);
        Task<bool> DeleteAsync(string id);
    }

    public class UserRepository : IUserRepository
    {
        private readonly DocumentDbContext _context;

        public UserRepository(DocumentDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ApplicationUser>> GetAllAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public async Task<ApplicationUser?> GetByIdAsync(string id)
        {
            return await _context.Users.FindAsync(id);
        }

        public async Task<ApplicationUser?> GetByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<ApplicationUser> AddAsync(ApplicationUser user)
        {
            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task UpdateAsync(ApplicationUser user)
        {
            _context.Entry(user).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> DeleteAsync(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return false;
            
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}