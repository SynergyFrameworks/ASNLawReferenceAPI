using ASNLawReferenceAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ASNLawReferenceAPI.Data
{
    public class DocumentDbContext : DbContext
    {
        public DocumentDbContext(DbContextOptions<DocumentDbContext> options)
            : base(options)
        {
        }

        public DbSet<Document> Documents { get; set; } = null!;
        public DbSet<Chunk> Chunks { get; set; } = null!;
        public DbSet<Annotation> Annotations { get; set; } = null!;
        public DbSet<ApplicationUser> Users { get; set; } = null!;
        public DbSet<SearchWeight> SearchWeights { get; set; } = null!;
        public DbSet<AccessLog> AccessLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Document configurations
            modelBuilder.Entity<Document>()
                .HasMany(d => d.ChildDocuments)
                .WithOne(d => d.ParentDocument)
                .HasForeignKey(d => d.ParentDocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Document>()
                .HasOne(d => d.SearchWeight)
                .WithOne(sw => sw.Document)
                .HasForeignKey<SearchWeight>(sw => sw.DocumentId);

            // Chunk configurations
            modelBuilder.Entity<Chunk>()
                .HasOne(c => c.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Annotation configurations
            modelBuilder.Entity<Annotation>()
                .HasOne(a => a.Document)
                .WithMany(d => d.Annotations)
                .HasForeignKey(a => a.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Annotation>()
                .HasOne(a => a.Chunk)
                .WithMany(c => c.Annotations)
                .HasForeignKey(a => a.ChunkId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Annotation>()
                .HasOne(a => a.Parent)
                .WithMany(a => a.Replies)
                .HasForeignKey(a => a.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Annotation>()
                .HasOne(a => a.User)
                .WithMany(u => u.Annotations)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Access logs configurations
            modelBuilder.Entity<AccessLog>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AccessLog>()
                .HasOne(a => a.Document)
                .WithMany()
                .HasForeignKey(a => a.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes for better performance
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Title);

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Jurisdiction);

            modelBuilder.Entity<Chunk>()
                .HasIndex(c => c.DocumentId);

            modelBuilder.Entity<Chunk>()
                .HasIndex(c => c.Page);

            modelBuilder.Entity<Annotation>()
                .HasIndex(a => a.DocumentId);

            modelBuilder.Entity<Annotation>()
                .HasIndex(a => a.ChunkId);

            modelBuilder.Entity<Annotation>()
                .HasIndex(a => a.UserId);

            modelBuilder.Entity<AccessLog>()
                .HasIndex(a => a.UserId);

            modelBuilder.Entity<AccessLog>()
                .HasIndex(a => a.DocumentId);

            modelBuilder.Entity<AccessLog>()
                .HasIndex(a => a.Timestamp);
        }
    }
}