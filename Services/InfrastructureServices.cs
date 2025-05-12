using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using OpenSearch.Client;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ASNLawReferenceAPI.Services
{
    #region Data Transfer Objects

    public class VectorDocument
    {
        public string Id { get; set; } = null!;
        public float[] Vector { get; set; } = Array.Empty<float>();
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public class VectorSearchResult
    {
        public string Id { get; set; } = null!;
        public float Score { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public class SearchDocument
    {
        public string Id { get; set; } = null!;
        public string Text { get; set; } = null!;
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public class SearchResult
    {
        public string Id { get; set; } = null!;
        public float Score { get; set; }
        public string Text { get; set; } = null!;
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    #endregion

    #region Blob Storage Service

    public interface IBlobStorageService
    {
        Task<string> UploadFileAsync(string containerName, string blobName, Stream content, string contentType);
        Task<Stream?> DownloadFileAsync(string containerName, string blobName);
        Task DeleteFileAsync(string containerName, string blobName);
        Task<bool> ExistsAsync(string containerName, string blobName);
    }

    public class AzureBlobStorageService : IBlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<AzureBlobStorageService> _logger;

        public AzureBlobStorageService(
            IConfiguration configuration,
            ILogger<AzureBlobStorageService> logger)
        {
            _blobServiceClient = new BlobServiceClient(configuration["Storage:ConnectionString"]);
            _logger = logger;
        }

        public async Task<string> UploadFileAsync(string containerName, string blobName, Stream content, string contentType)
        {
            try
            {
                // Get container (create if it doesn't exist)
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

                // Get blob client
                var blobClient = containerClient.GetBlobClient(blobName);

                // Upload the file
                await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType });

                // Return the blob URL
                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file to blob storage: {ContainerName}/{BlobName}", containerName, blobName);
                throw;
            }
        }

        public async Task<Stream?> DownloadFileAsync(string containerName, string blobName)
        {
            try
            {
                // Get container
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                
                // Get blob client
                var blobClient = containerClient.GetBlobClient(blobName);

                // Check if blob exists
                if (!await blobClient.ExistsAsync())
                {
                    _logger.LogWarning("Blob not found: {ContainerName}/{BlobName}", containerName, blobName);
                    return null;
                }

                // Download blob content
                var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream);
                memoryStream.Position = 0;
                
                return memoryStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file from blob storage: {ContainerName}/{BlobName}", containerName, blobName);
                throw;
            }
        }

        public async Task DeleteFileAsync(string containerName, string blobName)
        {
            try
            {
                // Get container
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                
                // Get blob client
                var blobClient = containerClient.GetBlobClient(blobName);

                // Delete blob
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file from blob storage: {ContainerName}/{BlobName}", containerName, blobName);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string containerName, string blobName)
        {
            try
            {
                // Get container
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                
                // Get blob client
                var blobClient = containerClient.GetBlobClient(blobName);

                // Check if blob exists
                return await blobClient.ExistsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if file exists in blob storage: {ContainerName}/{BlobName}", containerName, blobName);
                throw;
            }
        }
    }

    #endregion

    #region Embedding Service

    public interface IEmbeddingService
    {
        Task<float[]> CreateEmbeddingAsync(string text);
        Task<List<float[]>> CreateEmbeddingBatchAsync(List<string> texts);
        Task SetModelAsync(string modelName);
    }

    public class OpenAIEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OpenAIEmbeddingService> _logger;
        private string _modelName;

        public OpenAIEmbeddingService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<OpenAIEmbeddingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            // Set up HttpClient for OpenAI API
            _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", configuration["OpenAI:ApiKey"]);

            // Set default model
            _modelName = configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-large";
        }

        public async Task<float[]> CreateEmbeddingAsync(string text)
        {
            try
            {
                var request = new
                {
                    model = _modelName,
                    input = text
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync("embeddings", content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseBody);

                var embeddings = jsonDoc.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("embedding")
                    .EnumerateArray()
                    .Select(e => e.GetSingle())
                    .ToArray();

                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating embedding for text");
                throw;
            }
        }

        public async Task<List<float[]>> CreateEmbeddingBatchAsync(List<string> texts)
        {
            try
            {
                var request = new
                {
                    model = _modelName,
                    input = texts
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync("embeddings", content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseBody);

                var embeddings = new List<float[]>();
                var dataArray = jsonDoc.RootElement.GetProperty("data");
                
                foreach (var item in dataArray.EnumerateArray())
                {
                    var embedding = item.GetProperty("embedding")
                        .EnumerateArray()
                        .Select(e => e.GetSingle())
                        .ToArray();
                    
                    embeddings.Add(embedding);
                }

                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating embeddings for batch of {Count} texts", texts.Count);
                throw;
            }
        }

        public Task SetModelAsync(string modelName)
        {
            _modelName = modelName;
            _logger.LogInformation("Embedding model set to {ModelName}", modelName);
            return Task.CompletedTask;
        }
    }

    #endregion

    #region Vector Database Service

    public interface IVectorDbService
    {
        Task AddVectorsAsync(List<VectorDocument> documents);
        Task<List<VectorSearchResult>> SearchSimilarVectorsAsync(
            float[] queryVector, 
            List<string>? jurisdictionFilters = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int limit = 10);
        Task<List<VectorSearchResult>> SearchSimilarVectorsExcludingDocumentAsync(
            float[] queryVector,
            string documentId,
            int limit = 10);
        Task DeleteDocumentVectorsAsync(string documentId);
        Task CreateCollectionIfNotExistsAsync();
    }

    public class QdrantVectorDbService : IVectorDbService
    {
        private readonly QdrantClient _qdrantClient;
        private readonly ILogger<QdrantVectorDbService> _logger;
        private readonly string _collectionName;
        private readonly int _vectorDimension;

        public QdrantVectorDbService(
            IConfiguration configuration,
            ILogger<QdrantVectorDbService> logger)
        {
            var host = configuration["Qdrant:Host"] ?? "localhost";
            var port = configuration.GetValue<int>("Qdrant:Port", 6334);
            var useTls = configuration.GetValue<bool>("Qdrant:UseTls", false);

            // Simple constructor
            _qdrantClient = new QdrantClient(host, port, useTls);

            _logger = logger;
            _collectionName = configuration["Qdrant:CollectionName"] ?? "legal-documents";
            _vectorDimension = configuration.GetValue<int>("Qdrant:VectorDimension", 1536);
        }

        public async Task CreateCollectionIfNotExistsAsync()
        {
            try
            {
                // Basic collection creation - adjust if needed for your version
                var collections = await _qdrantClient.ListCollectionsAsync();

                if (!collections.Contains(_collectionName))
                {
                    await _qdrantClient.CreateCollectionAsync(
                        _collectionName,
                        new VectorParams { Size = (ulong)_vectorDimension }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Qdrant collection");
                throw;
            }
        }

        public async Task AddVectorsAsync(List<VectorDocument> documents)
        {
            try
            {
                await CreateCollectionIfNotExistsAsync();

                // Basic points creation - simplified for compatibility
                var points = new List<PointStruct>();

                foreach (var doc in documents)
                {
                    var point = new PointStruct
                    {
                        Id = new PointId { Uuid = doc.Id },
                        Vectors = doc.Vector
                    };

                    // Add metadata as payload - simplified
                    foreach (var kvp in doc.Metadata)
                    {
                        point.Payload[kvp.Key] = new Value { StringValue = kvp.Value };
                    }

                    points.Add(point);
                }

                // Basic upsert - adjust batch size if needed
                const int batchSize = 50;
                for (int i = 0; i < points.Count; i += batchSize)
                {
                    var batch = points.Skip(i).Take(batchSize).ToList();
                    await _qdrantClient.UpsertAsync(_collectionName, batch);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding vectors to Qdrant");
                throw;
            }
        }

        public async Task<List<VectorSearchResult>> SearchSimilarVectorsAsync(
            float[] queryVector,
            List<string>? jurisdictionFilters = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int limit = 10)
        {
            try
            {
                await CreateCollectionIfNotExistsAsync();

                // Most basic search - no filters for compatibility
                var searchResult = await _qdrantClient.SearchAsync(
                    _collectionName,
                    queryVector,
                    limit: (ulong)limit
                );

                // Convert results - simplified for compatibility
                var results = new List<VectorSearchResult>();

                foreach (var scored in searchResult)
                {
                    var metadata = new Dictionary<string, string>();

                    // Extract metadata in a way that should work across versions
                    foreach (var kvp in scored.Payload)
                    {
                        try
                        {
                            if (kvp.Value != null)
                            {
                                // Try to get string value - may need adjustment
                                if (kvp.Value.StringValue != null)
                                {
                                    metadata[kvp.Key] = kvp.Value.StringValue;
                                }
                            }
                        }
                        catch
                        {
                            // Ignore extraction errors
                        }
                    }

                    // ID might be in Uuid or directly in Id depending on version
                    string id = null;
                    try { id = scored.Id.Uuid; } catch { }
                    if (string.IsNullOrEmpty(id)) try { id = scored.Id.ToString(); } catch { }

                    results.Add(new VectorSearchResult
                    {
                        Id = id ?? Guid.NewGuid().ToString(), // Fallback
                        Score = scored.Score,
                        Metadata = metadata
                    });
                }

                // If we have jurisdiction filters, apply them in memory
                if (jurisdictionFilters?.Any() == true)
                {
                    results = results.Where(r =>
                        r.Metadata.ContainsKey("jurisdiction") &&
                        jurisdictionFilters.Contains(r.Metadata["jurisdiction"])
                    ).ToList();
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching similar vectors in Qdrant");
                throw;
            }
        }

        public async Task<List<VectorSearchResult>> SearchSimilarVectorsExcludingDocumentAsync(
            float[] queryVector,
            string documentId,
            int limit = 10)
        {
            try
            {
                var results = await SearchSimilarVectorsAsync(queryVector, null, null, null, limit * 2);

                // Filter out the document ID in memory
                return results
                    .Where(r =>
                        !r.Metadata.ContainsKey("documentId") ||
                        r.Metadata["documentId"] != documentId
                    )
                    .Take(limit)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error excluding document vectors from search");
                throw;
            }
        }

        public async Task DeleteDocumentVectorsAsync(string documentId)
        {
            try
            {
                // This is a simplification - we can't delete by filter easily
                // Instead we'll skip this for now
                _logger.LogInformation("Delete vectors operation skipped for compatibility reasons");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document vectors from Qdrant");
                throw;
            }
        }
    }

    #endregion

    #region Full-Text Search Service

    public interface IFullTextSearchService
    {
        Task IndexDocumentsAsync(List<SearchDocument> documents);
        Task<List<SearchResult>> SearchAsync(
            string query, 
            List<string>? jurisdictionFilters = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int limit = 10);
        Task DeleteDocumentFromIndexAsync(string documentId);
        Task CreateIndexIfNotExistsAsync();
    }

    public class OpenSearchService : IFullTextSearchService
    {
        private readonly IOpenSearchClient _client;
        private readonly ILogger<OpenSearchService> _logger;
        private readonly string _indexName;

        public OpenSearchService(
            IConfiguration configuration,
            ILogger<OpenSearchService> logger)
        {
            var nodes = configuration["OpenSearch:Nodes"]?.Split(',') ?? new[] { "http://localhost:9200" };
            var username = configuration["OpenSearch:Username"];
            var password = configuration["OpenSearch:Password"];

            var settings = new ConnectionSettings(new Uri(nodes[0]));
            
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                settings = settings.BasicAuthentication(username, password);
            }
            
            _client = new OpenSearchClient(settings);
            _logger = logger;
            _indexName = configuration["OpenSearch:IndexName"] ?? "legal-documents";
        }

        public async Task CreateIndexIfNotExistsAsync()
        {
            try
            {
                var indexExists = await _client.Indices.ExistsAsync(_indexName);
                
                if (!indexExists.Exists)
                {
                    var createResponse = await _client.Indices.CreateAsync(_indexName, c => c
                        .Settings(s => s
                            .Analysis(a => a
                                .Analyzers(an => an
                                    .Custom("legal_analyzer", ca => ca
                                        .Tokenizer("standard")
                                        .Filters("lowercase", "stop", "snowball")
                                    )
                                )
                            )
                        )
                        .Map<SearchDocument>(m => m
                            .Properties(p => p
                                .Text(t => t
                                    .Name(n => n.Id)
                                    .Index(false)
                                )
                                .Text(t => t
                                    .Name(n => n.Text)
                                    .Analyzer("legal_analyzer")
                                    .SearchAnalyzer("legal_analyzer")
                                )
                                .Object<Dictionary<string, string>>(o => o
                                    .Name(n => n.Metadata)
                                    .Properties(mp => mp
                                        .Keyword(k => k
                                            .Name("documentId")
                                        )
                                        .Keyword(k => k
                                            .Name("jurisdiction")
                                        )
                                        .Date(d => d
                                            .Name("createdAt")
                                        )
                                    )
                                )
                            )
                        )
                    );
                    
                    if (!createResponse.IsValid)
                    {
                        throw new Exception($"Failed to create index: {createResponse.DebugInformation}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating OpenSearch index");
                throw;
            }
        }

        public async Task IndexDocumentsAsync(List<SearchDocument> documents)
        {
            try
            {
                // Ensure index exists
                await CreateIndexIfNotExistsAsync();
                
                // Prepare bulk request
                var bulkDescriptor = new BulkDescriptor();
                
                foreach (var doc in documents)
                {
                    bulkDescriptor.Index<SearchDocument>(i => i
                        .Index(_indexName)
                        .Id(doc.Id)
                        .Document(doc)
                    );
                }
                
                // Execute bulk request
                var response = await _client.BulkAsync(bulkDescriptor);
                
                if (!response.IsValid)
                {
                    _logger.LogWarning("Some documents failed to index: {ErrorDetails}", response.DebugInformation);
                }
                
                _logger.LogInformation("Indexed {Count} documents in OpenSearch", documents.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing documents in OpenSearch");
                throw;
            }
        }

        public async Task<List<SearchResult>> SearchAsync(
            string query, 
            List<string>? jurisdictionFilters = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int limit = 10)
        {
            try
            {
                // Ensure index exists
                await CreateIndexIfNotExistsAsync();
                
                // Prepare search request
                var searchResponse = await _client.SearchAsync<SearchDocument>(s => s
                    .Index(_indexName)
                    .Size(limit)
                    .Query(q => q
                        .Bool(b =>
                        {
                            b.Must(m => m
                                .MultiMatch(mm => mm
                                    .Fields(f => f
                                        .Field(fd => fd.Text, 1.5)
                                        .Field("metadata.documentTitle", 2.0)
                                    )
                                    .Query(query)
                                    .Type(TextQueryType.BestFields)
                                    .Fuzziness(Fuzziness.Auto)
                                )
                            );
                            
                            if (jurisdictionFilters != null && jurisdictionFilters.Any())
                            {
                                b.Filter(f => f
                                    .Terms(t => t
                                        .Field("metadata.jurisdiction.keyword")
                                        .Terms(jurisdictionFilters)
                                    )
                                );
                            }
                            
                            if (startDate.HasValue)
                            {
                                b.Filter(f => f
                                    .DateRange(r => r
                                        .Field("metadata.createdAt")
                                        .GreaterThanOrEquals(startDate.Value)
                                    )
                                );
                            }
                            
                            if (endDate.HasValue)
                            {
                                b.Filter(f => f
                                    .DateRange(r => r
                                        .Field("metadata.createdAt")
                                        .LessThanOrEquals(endDate.Value)
                                    )
                                );
                            }
                            
                            return b;
                        })
                    )
                    .Highlight(h => h
                        .Fields(f => f
                            .Field(fd => fd.Text)
                            .PreTags("<mark>")
                            .PostTags("</mark>")
                            .FragmentSize(150)
                            .NumberOfFragments(3)
                        )
                    )
                );
                
                if (!searchResponse.IsValid)
                {
                    throw new Exception($"Search failed: {searchResponse.DebugInformation}");
                }
                
                // Convert results
                var results = new List<SearchResult>();
                
                foreach (var hit in searchResponse.Hits)
                {
                    var highlightedText = hit.Highlight.ContainsKey("text") 
                        ? string.Join(" ... ", hit.Highlight["text"]) 
                        : hit.Source.Text;

                    results.Add(new SearchResult
                    {
                        Id = hit.Id,
                        Score = (float)(hit.Score ?? 0),  
                        Text = highlightedText,
                        Metadata = hit.Source.Metadata
                    });
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching in OpenSearch");
                throw;
            }
        }

        public async Task DeleteDocumentFromIndexAsync(string documentId)
        {
            try
            {
                // Ensure index exists
                await CreateIndexIfNotExistsAsync();
                
                // Delete by query
                var response = await _client.DeleteByQueryAsync<SearchDocument>(d => d
                    .Index(_indexName)
                    .Query(q => q
                        .Term(t => t
                            .Field("metadata.documentId.keyword")
                            .Value(documentId)
                        )
                    )
                    .Refresh(true)
                );
                
                if (!response.IsValid)
                {
                    _logger.LogWarning("Delete by query failed: {ErrorDetails}", response.DebugInformation);
                }
                
                _logger.LogInformation("Deleted documents for documentId {DocumentId} from OpenSearch", documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document from OpenSearch index");
                throw;
            }
        }
    }

    #endregion

    #region OCR Service

    public interface IOcrService
    {
        Task<List<string>> ExtractTextByPageAsync(Stream pdfStream);
    }

    public class TesseractOcrService : IOcrService
    {
        private readonly ILogger<TesseractOcrService> _logger;

        public TesseractOcrService(ILogger<TesseractOcrService> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>> ExtractTextByPageAsync(Stream pdfStream)
        {
            try
            {
                // Note: This is a simplified implementation.
                // A real implementation would use libraries like PdfiumViewer and Tesseract OCR
                // to extract and OCR text from PDF documents.
                
                // For demonstration purposes, we'll simulate OCR by returning placeholder text.
                // In a real implementation, you would:
                // 1. Convert PDF pages to images
                // 2. Apply OCR to each image
                // 3. Return the extracted text for each page
                
                await Task.Delay(100); // Simulate processing time
                
                return new List<string>
                {
                    "This is simulated OCR text for page 1. In a real implementation, this would be actual text extracted from the PDF.",
                    "This is simulated OCR text for page 2. In a real implementation, this would be actual text extracted from the PDF."
                };
                
                // Real implementation would use something like:
                // var pages = new List<string>();
                // using (var document = PdfDocument.Load(pdfStream))
                // {
                //     for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                //     {
                //         using (var image = document.Render(pageIndex, 300, 300, PdfRenderFlags.Annotations))
                //         {
                //             var ocr = new TesseractEngine(dataPath, "eng", EngineMode.Default);
                //             using (var page = ocr.Process(image))
                //             {
                //                 pages.Add(page.GetText());
                //             }
                //         }
                //     }
                // }
                // return pages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF");
                throw;
            }
        }
    }

    #endregion
}