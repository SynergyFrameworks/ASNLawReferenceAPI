using ASNLawReferenceAPI.Services;
using System.Threading.Channels;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ASNLawReferenceAPI.Background
{
    public class DocumentProcessingBackgroundService : BackgroundService
    {
        private readonly Channel<Guid> _documentQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DocumentProcessingBackgroundService> _logger;

        public DocumentProcessingBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<DocumentProcessingBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            // Create an unbounded channel for document processing
            _documentQueue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public async Task QueueDocumentForProcessingAsync(Guid documentId)
        {
            await _documentQueue.Writer.WriteAsync(documentId);
            _logger.LogInformation("Document {DocumentId} queued for processing", documentId);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Document processing background service started");

            await foreach (var documentId in _documentQueue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    _logger.LogInformation("Processing document {DocumentId}", documentId);

                    // Create scope to resolve services
                    using var scope = _serviceProvider.CreateScope();
                    var indexingService = scope.ServiceProvider.GetRequiredService<IIndexingService>();

                    // Process document
                    await indexingService.ProcessDocumentAsync(documentId);

                    _logger.LogInformation("Document {DocumentId} processed successfully", documentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing document {DocumentId}", documentId);
                }
            }
        }
    }

    // Add health check for services
    public class BlobStorageHealthCheck : IHealthCheck
    {
        private readonly IBlobStorageService _blobStorageService;

        public BlobStorageHealthCheck(IBlobStorageService blobStorageService)
        {
            _blobStorageService = blobStorageService;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if a test container exists (or any other simple operation)
                var exists = await _blobStorageService.ExistsAsync("health-check", "test.txt");
                return HealthCheckResult.Healthy("Blob storage is accessible");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Blob storage is not accessible", ex);
            }
        }
    }

    public class VectorDbHealthCheck : IHealthCheck
    {
        private readonly IVectorDbService _vectorDbService;

        public VectorDbHealthCheck(IVectorDbService vectorDbService)
        {
            _vectorDbService = vectorDbService;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if the collection exists
                await _vectorDbService.CreateCollectionIfNotExistsAsync();
                return HealthCheckResult.Healthy("Vector database is accessible");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Vector database is not accessible", ex);
            }
        }
    }

    public class OpenSearchHealthCheck : IHealthCheck
    {
        private readonly IFullTextSearchService _fullTextSearchService;

        public OpenSearchHealthCheck(IFullTextSearchService fullTextSearchService)
        {
            _fullTextSearchService = fullTextSearchService;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if the index exists
                await _fullTextSearchService.CreateIndexIfNotExistsAsync();
                return HealthCheckResult.Healthy("OpenSearch is accessible");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("OpenSearch is not accessible", ex);
            }
        }
    }
}