using ASNLawReferenceAPI.Data;
using ASNLawReferenceAPI.Models;
using System.Text.RegularExpressions;

namespace ASNLawReferenceAPI.Services
{
    public interface IIndexingService
    {
        Task ProcessDocumentAsync(Guid documentId);
        Task ReindexAllDocumentsAsync();
        Task ReprocessDocumentAsync(Guid documentId);
        Task<(int Success, int Failed)> UpdateVectorModelAsync(string? modelName = null);
    }

    public class IndexingService : IIndexingService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IChunkRepository _chunkRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorDbService _vectorDbService;
        private readonly IFullTextSearchService _fullTextSearchService;
        private readonly IOcrService _ocrService;
        private readonly ILogger<IndexingService> _logger;

        // Configuration settings
        private readonly int _chunkSize;
        private readonly int _chunkOverlap;
        private readonly int _maxTokensPerChunk;

        public IndexingService(
            IDocumentRepository documentRepository,
            IChunkRepository chunkRepository,
            IBlobStorageService blobStorageService,
            IEmbeddingService embeddingService,
            IVectorDbService vectorDbService,
            IFullTextSearchService fullTextSearchService,
            IOcrService ocrService,
            IConfiguration configuration,
            ILogger<IndexingService> logger)
        {
            _documentRepository = documentRepository;
            _chunkRepository = chunkRepository;
            _blobStorageService = blobStorageService;
            _embeddingService = embeddingService;
            _vectorDbService = vectorDbService;
            _fullTextSearchService = fullTextSearchService;
            _ocrService = ocrService;
            _logger = logger;

            // Load configuration settings
            _chunkSize = configuration.GetValue<int>("Indexing:ChunkSize", 512);
            _chunkOverlap = configuration.GetValue<int>("Indexing:ChunkOverlap", 128);
            _maxTokensPerChunk = configuration.GetValue<int>("Indexing:MaxTokensPerChunk", 1024);
        }

        public async Task ProcessDocumentAsync(Guid documentId)
        {
            try
            {
                _logger.LogInformation("Starting to process document {DocumentId}", documentId);

                // Get document from database
                var document = await _documentRepository.GetByIdAsync(documentId);
                if (document == null)
                {
                    _logger.LogWarning("Document {DocumentId} not found", documentId);
                    return;
                }

                // Download PDF file from blob storage
                var blobName = GetBlobNameFromUrl(document.PdfUrl);
                if (string.IsNullOrEmpty(blobName))
                {
                    _logger.LogError("Could not extract blob name from URL: {PdfUrl}", document.PdfUrl);
                    return;
                }

                var pdfStream = await _blobStorageService.DownloadFileAsync("legal-documents", blobName);
                if (pdfStream == null)
                {
                    _logger.LogError("Could not download PDF file: {BlobName}", blobName);
                    return;
                }

                // Extract text from PDF using OCR if needed
                var textByPage = await _ocrService.ExtractTextByPageAsync(pdfStream);
                
                // Process each page and create chunks
                var allChunks = new List<Chunk>();
                
                for (int pageNum = 0; pageNum < textByPage.Count; pageNum++)
                {
                    var pageText = textByPage[pageNum];
                    var pageChunks = ChunkTextBySections(pageText, pageNum + 1, documentId);
                    allChunks.AddRange(pageChunks);
                }
                
                // Create embeddings for chunks in batches
                const int batchSize = 10; // Adjust based on rate limits and performance
                
                for (int i = 0; i < allChunks.Count; i += batchSize)
                {
                    var batch = allChunks.Skip(i).Take(batchSize).ToList();
                    var texts = batch.Select(c => c.Text).ToList();
                    
                    var embeddings = await _embeddingService.CreateEmbeddingBatchAsync(texts);
                    
                    // Assign embeddings to chunks
                    for (int j = 0; j < batch.Count && j < embeddings.Count; j++)
                    {
                        batch[j].Embedding = embeddings[j];
                    }
                }
                
                // Save chunks to database
                await _chunkRepository.AddRangeAsync(allChunks);
                
                // Add vectors to vector database
                var vectorDocs = allChunks.Select(c => new VectorDocument
                {
                    Id = c.Id.ToString(),
                    Vector = c.Embedding,
                    Metadata = new Dictionary<string, string>
                    {
                        ["documentId"] = c.DocumentId.ToString(),
                        ["jurisdiction"] = document.Jurisdiction,
                        ["page"] = c.Page.ToString(),
                        ["createdAt"] = document.CreatedAt.ToString("o")
                    }
                }).ToList();
                
                await _vectorDbService.AddVectorsAsync(vectorDocs);
                
                // Add documents to full-text search index
                var searchDocs = allChunks.Select(c => new SearchDocument
                {
                    Id = c.Id.ToString(),
                    Text = c.Text,
                    Metadata = new Dictionary<string, string>
                    {
                        ["documentId"] = c.DocumentId.ToString(),
                        ["documentTitle"] = document.Title,
                        ["jurisdiction"] = document.Jurisdiction,
                        ["page"] = c.Page.ToString(),
                        ["createdAt"] = document.CreatedAt.ToString("o")
                    }
                }).ToList();
                
                await _fullTextSearchService.IndexDocumentsAsync(searchDocs);
                
                _logger.LogInformation("Successfully processed document {DocumentId} with {ChunkCount} chunks",
                    documentId, allChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document {DocumentId}", documentId);
                throw;
            }
        }

        public async Task ReindexAllDocumentsAsync()
        {
            try
            {
                _logger.LogInformation("Starting to reindex all documents");

                // Get all documents from database
                var documents = await _documentRepository.GetAllAsync();
                var documentIds = documents.Select(d => d.Id).ToList();

                _logger.LogInformation("Found {DocumentCount} documents to reindex", documentIds.Count);

                // Process each document
                var successCount = 0;
                var failedCount = 0;

                foreach (var documentId in documentIds)
                {
                    try
                    {
                        await ReprocessDocumentAsync(documentId);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reprocessing document {DocumentId}", documentId);
                        failedCount++;
                    }
                }

                _logger.LogInformation("Completed reindexing all documents. Success: {SuccessCount}, Failed: {FailedCount}",
                    successCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reindexing all documents");
                throw;
            }
        }

        public async Task ReprocessDocumentAsync(Guid documentId)
        {
            try
            {
                _logger.LogInformation("Starting to reprocess document {DocumentId}", documentId);

                // Get document from database
                var document = await _documentRepository.GetByIdAsync(documentId);
                if (document == null)
                {
                    _logger.LogWarning("Document {DocumentId} not found", documentId);
                    return;
                }

                // Delete existing chunks
                await _chunkRepository.DeleteByDocumentIdAsync(documentId);

                // Delete from vector database
                try
                {
                    await _vectorDbService.DeleteDocumentVectorsAsync(documentId.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error deleting vectors for document {DocumentId}", documentId);
                }

                // Delete from full-text search index
                try
                {
                    await _fullTextSearchService.DeleteDocumentFromIndexAsync(documentId.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error deleting document from full-text index {DocumentId}", documentId);
                }

                // Process document again
                await ProcessDocumentAsync(documentId);

                _logger.LogInformation("Successfully reprocessed document {DocumentId}", documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reprocessing document {DocumentId}", documentId);
                throw;
            }
        }

        public async Task<(int Success, int Failed)> UpdateVectorModelAsync(string? modelName = null)
        {
            try
            {
                _logger.LogInformation("Starting to update vector model to {ModelName}", modelName ?? "default model");

                // Get all documents from database
                var documents = await _documentRepository.GetAllAsync();
                var documentIds = documents.Select(d => d.Id).ToList();

                _logger.LogInformation("Found {DocumentCount} documents to update", documentIds.Count);

                // Set the model name if provided
                if (!string.IsNullOrEmpty(modelName))
                {
                    await _embeddingService.SetModelAsync(modelName);
                }

                // Process each document
                var successCount = 0;
                var failedCount = 0;

                foreach (var documentId in documentIds)
                {
                    try
                    {
                        await ReprocessDocumentAsync(documentId);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating vectors for document {DocumentId}", documentId);
                        failedCount++;
                    }
                }

                _logger.LogInformation("Completed updating vector model. Success: {SuccessCount}, Failed: {FailedCount}",
                    successCount, failedCount);

                return (successCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating vector model");
                throw;
            }
        }

        private IEnumerable<Chunk> ChunkTextBySections(string text, int pageNumber, Guid documentId)
        {
            var chunks = new List<Chunk>();
            
            // Try to split by semantic units (sections, paragraphs)
            var sections = SplitIntoSections(text);
            
            int startOffset = 0;
            
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section))
                    continue;
                
                // If section is too large, split further
                if (section.Length > _maxTokensPerChunk)
                {
                    var subChunks = SplitLargeSection(section, startOffset);
                    foreach (var (subText, subStart, subEnd) in subChunks)
                    {
                        chunks.Add(new Chunk
                        {
                            Id = Guid.NewGuid(),
                            DocumentId = documentId,
                            Page = pageNumber,
                            Text = subText,
                            StartOffset = subStart,
                            EndOffset = subEnd,
                            Embedding = Array.Empty<float>() // Filled later
                        });
                    }
                }
                else
                {
                    // Add section as a chunk
                    chunks.Add(new Chunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = documentId,
                        Page = pageNumber,
                        Text = section,
                        StartOffset = startOffset,
                        EndOffset = startOffset + section.Length,
                        Embedding = Array.Empty<float>() // Filled later
                    });
                }
                
                startOffset += section.Length;
            }
            
            return chunks;
        }

        private List<string> SplitIntoSections(string text)
        {
            // Split by section markers, common in legal documents
            var sectionMarkers = new[]
            {
                @"(?<=\n\s*§\s*\d+\.|\n\s*Section\s+\d+\.)",  // § 1. or Section 1.
                @"(?<=\n\s*\d+\.\d+\s+|\n\s*\d+\.\s+)",        // 1.1 or 1.
                @"(?<=\n\s*\([a-z]\)\s+|\n\s*\(\d+\)\s+)",     // (a) or (1)
                @"(?<=\n\n+)",                                 // Multiple newlines
                @"(?<=\.\s+)"                                  // Period followed by whitespace
            };
            
            var sections = new List<string>();
            var currentText = text;
            
            foreach (var marker in sectionMarkers)
            {
                if (string.IsNullOrWhiteSpace(currentText))
                    break;
                
                // Try to split by current marker
                var parts = Regex.Split(currentText, marker).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                
                if (parts.Count > 1)
                {
                    sections.AddRange(parts);
                    break;
                }
            }
            
            // If no good splits were found, fall back to simple paragraph splitting
            if (sections.Count == 0)
            {
                sections.Add(text);
            }
            
            return sections;
        }

        private List<(string Text, int Start, int End)> SplitLargeSection(string section, int baseOffset)
        {
            var chunks = new List<(string, int, int)>();
            
            // Simple sliding window approach
            for (int i = 0; i < section.Length; i += (_chunkSize - _chunkOverlap))
            {
                var length = Math.Min(_chunkSize, section.Length - i);
                var chunk = section.Substring(i, length);
                
                // Add chunk with offsets
                chunks.Add((chunk, baseOffset + i, baseOffset + i + length));
                
                // Break if we've covered the whole section
                if (i + length >= section.Length)
                    break;
            }
            
            return chunks;
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
    }
}