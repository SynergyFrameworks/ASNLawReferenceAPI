using LegalReferenceAPI.Data;
using LegalReferenceAPI.Models;
using System.Security.Claims;

namespace LegalReferenceAPI.Services
{
    public interface ISearchService
    {
        Task<IEnumerable<SearchResultDto>> SearchAsync(SearchQueryDto searchQuery, ClaimsPrincipal user);
        Task<IEnumerable<SearchResultDto>> GetRecommendedDocumentsAsync(Guid documentId, int count, ClaimsPrincipal user);
    }

    public class SearchService : ISearchService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IChunkRepository _chunkRepository;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorDbService _vectorDbService;
        private readonly IFullTextSearchService _fullTextSearchService;
        private readonly ILogger<SearchService> _logger;

        public SearchService(
            IDocumentRepository documentRepository,
            IChunkRepository chunkRepository,
            IEmbeddingService embeddingService,
            IVectorDbService vectorDbService,
            IFullTextSearchService fullTextSearchService,
            ILogger<SearchService> logger)
        {
            _documentRepository = documentRepository;
            _chunkRepository = chunkRepository;
            _embeddingService = embeddingService;
            _vectorDbService = vectorDbService;
            _fullTextSearchService = fullTextSearchService;
            _logger = logger;
        }

        public async Task<IEnumerable<SearchResultDto>> SearchAsync(SearchQueryDto searchQuery, ClaimsPrincipal user)
        {
            // Set default values if not provided
            var topK = searchQuery.TopK ?? 10;
            var semanticWeight = searchQuery.SemanticWeight;
            var keywordWeight = searchQuery.KeywordWeight;

            // Normalize weights if needed
            if (semanticWeight + keywordWeight != 1)
            {
                var sum = semanticWeight + keywordWeight;
                semanticWeight /= sum;
                keywordWeight /= sum;
            }

            var results = new List<SearchResultDto>();

            // Perform semantic search if enabled
            if (searchQuery.UseSemanticSearch)
            {
                try
                {
                    // Generate embeddings for the query
                    var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(searchQuery.Query);
                    
                    // Search for similar vectors in the vector database
                    var semanticResults = await _vectorDbService.SearchSimilarVectorsAsync(
                        queryEmbedding,
                        searchQuery.JurisdictionFilters,
                        searchQuery.StartDate,
                        searchQuery.EndDate,
                        topK * 2 // Fetch more results for hybrid search
                    );
                    
                    foreach (var vectorResult in semanticResults)
                    {
                        // Parse the chunk ID from the vector database result
                        if (Guid.TryParse(vectorResult.Id, out var chunkId))
                        {
                            var chunk = await _chunkRepository.GetByIdAsync(chunkId);
                            if (chunk != null)
                            {
                                var document = await _documentRepository.GetByIdAsync(chunk.DocumentId);
                                if (document != null)
                                {
                                    results.Add(new SearchResultDto
                                    {
                                        ChunkId = chunk.Id,
                                        DocumentId = document.Id,
                                        DocumentTitle = document.Title,
                                        Jurisdiction = document.Jurisdiction,
                                        Page = chunk.Page,
                                        Text = chunk.Text,
                                        Score = vectorResult.Score * semanticWeight,
                                        PdfUrl = document.PdfUrl
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error performing semantic search");
                }
            }

            // Perform keyword search if enabled
            if (searchQuery.UseKeywordSearch)
            {
                try
                {
                    // Search for keywords in the full-text search index
                    var keywordResults = await _fullTextSearchService.SearchAsync(
                        searchQuery.Query,
                        searchQuery.JurisdictionFilters,
                        searchQuery.StartDate,
                        searchQuery.EndDate,
                        topK * 2 // Fetch more results for hybrid search
                    );
                    
                    foreach (var textResult in keywordResults)
                    {
                        // Parse the chunk ID from the full-text search result
                        if (Guid.TryParse(textResult.Id, out var chunkId))
                        {
                            var chunk = await _chunkRepository.GetByIdAsync(chunkId);
                            if (chunk != null)
                            {
                                var document = await _documentRepository.GetByIdAsync(chunk.DocumentId);
                                if (document != null)
                                {
                                    // Check if result already exists from semantic search
                                    var existingResult = results.FirstOrDefault(r => r.ChunkId == chunk.Id);
                                    if (existingResult != null)
                                    {
                                        // Update score with hybrid approach
                                        existingResult.Score += textResult.Score * keywordWeight;
                                    }
                                    else
                                    {
                                        // Add new result
                                        results.Add(new SearchResultDto
                                        {
                                            ChunkId = chunk.Id,
                                            DocumentId = document.Id,
                                            DocumentTitle = document.Title,
                                            Jurisdiction = document.Jurisdiction,
                                            Page = chunk.Page,
                                            Text = chunk.Text,
                                            Score = textResult.Score * keywordWeight,
                                            PdfUrl = document.PdfUrl
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error performing keyword search");
                }
            }

            // Apply custom ranking boosts based on document metadata
            await ApplyCustomBoostsAsync(results);

            // Sort by score (descending) and take top K results
            return results
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();
        }

        public async Task<IEnumerable<SearchResultDto>> GetRecommendedDocumentsAsync(Guid documentId, int count, ClaimsPrincipal user)
        {
            // Get reference document
            var document = await _documentRepository.GetByIdAsync(documentId);
            if (document == null)
                return Enumerable.Empty<SearchResultDto>();

            // Get chunks for the document
            var chunks = await _chunkRepository.GetByDocumentIdAsync(documentId);
            if (!chunks.Any())
                return Enumerable.Empty<SearchResultDto>();

            // Use a random selection of chunk texts as queries to find similar content
            var results = new List<SearchResultDto>();
            var random = new Random();
            var sampleChunks = chunks
                .OrderBy(_ => random.Next())
                .Take(3)
                .ToList();

            foreach (var chunk in sampleChunks)
            {
                // Generate embeddings for the chunk text
                var chunkEmbedding = await _embeddingService.CreateEmbeddingAsync(chunk.Text);
                
                // Search for similar vectors excluding the current document
                var similarResults = await _vectorDbService.SearchSimilarVectorsExcludingDocumentAsync(
                    chunkEmbedding,
                    document.Id.ToString(),
                    count * 2
                );
                
                foreach (var vectorResult in similarResults)
                {
                    // Parse the chunk ID from the vector database result
                    if (Guid.TryParse(vectorResult.Id, out var chunkId))
                    {
                        var similarChunk = await _chunkRepository.GetByIdAsync(chunkId);
                        if (similarChunk != null)
                        {
                            var similarDocument = await _documentRepository.GetByIdAsync(similarChunk.DocumentId);
                            if (similarDocument != null)
                            {
                                // Check if document already exists in results
                                if (!results.Any(r => r.DocumentId == similarDocument.Id))
                                {
                                    results.Add(new SearchResultDto
                                    {
                                        ChunkId = similarChunk.Id,
                                        DocumentId = similarDocument.Id,
                                        DocumentTitle = similarDocument.Title,
                                        Jurisdiction = similarDocument.Jurisdiction,
                                        Page = similarChunk.Page,
                                        Text = similarChunk.Text,
                                        Score = vectorResult.Score,
                                        PdfUrl = similarDocument.PdfUrl
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // Sort by score (descending) and take requested count
            return results
                .OrderByDescending(r => r.Score)
                .Take(count)
                .ToList();
        }

        private async Task ApplyCustomBoostsAsync(List<SearchResultDto> results)
        {
            // Get document IDs from results
            var documentIds = results.Select(r => r.DocumentId).Distinct().ToList();
            
            // Get documents with their search weights
            var documents = new List<Document>();
            foreach (var id in documentIds)
            {
                var document = await _documentRepository.GetByIdAsync(id);
                if (document != null)
                {
                    documents.Add(document);
                }
            }

            // Apply boosts based on document metadata
            foreach (var result in results)
            {
                var document = documents.FirstOrDefault(d => d.Id == result.DocumentId);
                if (document != null)
                {
                    // Apply jurisdiction priority boost
                    if (document.SearchWeight != null)
                    {
                        // Apply direct boosts from the SearchWeight table
                        result.Score *= (1 + document.SearchWeight.JurisdictionScore * 0.1f);
                        result.Score *= (1 + document.SearchWeight.RecencyScore * 0.1f);
                        result.Score *= (1 + document.SearchWeight.ManualBoost * 0.1f);
                    }
                    else
                    {
                        // Apply basic recency boost if no explicit weights
                        var ageInDays = (DateTime.UtcNow - document.CreatedAt).TotalDays;
                        var recencyFactor = Math.Max(0, 1 - (ageInDays / 365)); // Decay over a year
                        float boost = (float)(recencyFactor * 0.05);
                        result.Score *= (1 + boost);
                    }
                }
            }
        }
    }
}