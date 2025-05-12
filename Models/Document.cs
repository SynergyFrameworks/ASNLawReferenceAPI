using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ASNLawReferenceAPI.Models
{
    public class Document
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = null!;
        
        [Required]
        [MaxLength(100)]
        public string Jurisdiction { get; set; } = null!;
        
        [Required]
        public string PdfUrl { get; set; } = null!;
        
        [Required]
        public string Version { get; set; } = null!;
        
        public Guid? ParentDocumentId { get; set; }
        
        [ForeignKey("ParentDocumentId")]
        public Document? ParentDocument { get; set; }
        
        [Required]
        public string CreatedBy { get; set; } = null!;
        
        [Required]
        public DateTime CreatedAt { get; set; }
        
        public DateTime? LastModified { get; set; }
        
        public string? LastModifiedBy { get; set; }
        
        // Navigation properties
        public ICollection<Chunk> Chunks { get; set; } = new List<Chunk>();
        public ICollection<Annotation> Annotations { get; set; } = new List<Annotation>();
        public ICollection<Document> ChildDocuments { get; set; } = new List<Document>();
        public SearchWeight? SearchWeight { get; set; }
    }

    public class Chunk
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        public Guid DocumentId { get; set; }
        
        [ForeignKey("DocumentId")]
        public Document Document { get; set; } = null!;
        
        [Required]
        public int Page { get; set; }
        
        [Required]
        public string Text { get; set; } = null!;
        
        [Required]
        [Column(TypeName = "jsonb")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
        
        public int StartOffset { get; set; }
        
        public int EndOffset { get; set; }
        
        // Navigation properties
        public ICollection<Annotation> Annotations { get; set; } = new List<Annotation>();
    }

    public class Annotation
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        public Guid DocumentId { get; set; }
        
        [ForeignKey("DocumentId")]
        public Document Document { get; set; } = null!;
        
        public Guid? ChunkId { get; set; }
        
        [ForeignKey("ChunkId")]
        public Chunk? Chunk { get; set; }
        
        [Required]
        public int Page { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = null!; // Highlight, Note, Comment, Strikethrough
        
        public string? Text { get; set; }
        
        [Required]
        public string UserId { get; set; } = null!;
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; } = null!;
        
        public bool IsShared { get; set; }
        
        public bool IsApproved { get; set; }
        
        public Guid? ParentId { get; set; }
        
        [ForeignKey("ParentId")]
        public Annotation? Parent { get; set; }
        
        [Required]
        public DateTime CreatedAt { get; set; }
        
        // Navigation property for thread replies
        public ICollection<Annotation> Replies { get; set; } = new List<Annotation>();
    }

    public class ApplicationUser
    {
        [Key]
        public string Id { get; set; } = null!;
        
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = null!;
        
        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = null!;
        
        [Required]
        [EmailAddress]
        public string Email { get; set; } = null!;
        
        // Navigation properties
        public ICollection<Annotation> Annotations { get; set; } = new List<Annotation>();
    }

    public class SearchWeight
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        public Guid DocumentId { get; set; }
        
        [ForeignKey("DocumentId")]
        public Document Document { get; set; } = null!;
        
        [Range(0, 10)]
        public float JurisdictionScore { get; set; }
        
        [Range(0, 10)]
        public float RecencyScore { get; set; }
        
        [Range(0, 10)]
        public float ManualBoost { get; set; }
    }

    public class AccessLog
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        public string UserId { get; set; } = null!;
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; } = null!;
        
        [Required]
        public Guid DocumentId { get; set; }
        
        [ForeignKey("DocumentId")]
        public Document Document { get; set; } = null!;
        
        [Required]
        [MaxLength(50)]
        public string ActionType { get; set; } = null!; // View, Download, Annotate, etc.
        
        [Required]
        public DateTime Timestamp { get; set; }
    }

    // DTOs for API requests and responses
    
    public class DocumentDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string Jurisdiction { get; set; } = null!;
        public string PdfUrl { get; set; } = null!;
        public string Version { get; set; } = null!;
        public Guid? ParentDocumentId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = null!;
    }
    
    public class CreateDocumentDto
    {
        [Required]
        public string Title { get; set; } = null!;
        
        [Required]
        public string Jurisdiction { get; set; } = null!;
        
        public Guid? ParentDocumentId { get; set; }
        
        [JsonIgnore]
        public IFormFile PdfFile { get; set; } = null!;
    }
    
    public class ChunkDto
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public int Page { get; set; }
        public string Text { get; set; } = null!;
    }
    
    public class AnnotationDto
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public Guid? ChunkId { get; set; }
        public int Page { get; set; }
        public string Type { get; set; } = null!;
        public string? Text { get; set; }
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public bool IsShared { get; set; }
        public bool IsApproved { get; set; }
        public Guid? ParentId { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<AnnotationDto> Replies { get; set; } = new();
    }
    
    public class CreateAnnotationDto
    {
        [Required]
        public Guid DocumentId { get; set; }
        
        public Guid? ChunkId { get; set; }
        
        [Required]
        public int Page { get; set; }
        
        [Required]
        public string Type { get; set; } = null!;
        
        public string? Text { get; set; }
        
        public bool IsShared { get; set; }
        
        public Guid? ParentId { get; set; }
    }
    
    public class SearchQueryDto
    {
        [Required]
        public string Query { get; set; } = null!;
        
        public bool UseSemanticSearch { get; set; } = true;
        
        public bool UseKeywordSearch { get; set; } = true;
        
        public float SemanticWeight { get; set; } = 0.7f;
        
        public float KeywordWeight { get; set; } = 0.3f;
        
        public int? TopK { get; set; }
        
        public List<string>? JurisdictionFilters { get; set; }
        
        public DateTime? StartDate { get; set; }
        
        public DateTime? EndDate { get; set; }
    }
    
    public class SearchResultDto
    {
        public Guid ChunkId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = null!;
        public string Jurisdiction { get; set; } = null!;
        public int Page { get; set; }
        public string Text { get; set; } = null!;
        public float Score { get; set; }
        public string PdfUrl { get; set; } = null!;
    }
}