using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PolicyRAG.Api.Configuration;
using Qdrant.Client;

namespace PolicyRAG.Api.HealthChecks;

/// <summary>
/// Health check for Qdrant vector database connectivity
/// </summary>
public class QdrantHealthCheck : IHealthCheck
{
    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly ILogger<QdrantHealthCheck> _logger;

    public QdrantHealthCheck(
        IOptions<QdrantOptions> options,
        ILogger<QdrantHealthCheck> logger)
    {
        var config = options.Value;
        _client = new QdrantClient(config.Host, config.Port);
        _collectionName = config.CollectionName;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to list collections as a connectivity check
            var collections = await _client.ListCollectionsAsync(cancellationToken);
            
            var collectionExists = collections.Contains(_collectionName);
            
            var data = new Dictionary<string, object>
            {
                { "collections_count", collections.Count },
                { "target_collection_exists", collectionExists },
                { "collection_name", _collectionName }
            };

            if (collectionExists)
            {
                // Get collection info for more details
                try
                {
                    var collectionInfo = await _client.GetCollectionInfoAsync(_collectionName, cancellationToken);
                    data["points_count"] = collectionInfo.PointsCount;
                    data["status"] = collectionInfo.Status.ToString();
                }
                catch
                {
                    // Collection info is optional
                }
            }

            _logger.LogDebug("Qdrant health check passed. Collections: {Count}", collections.Count);
            
            return HealthCheckResult.Healthy(
                "Qdrant is healthy",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Qdrant health check failed");
            
            return HealthCheckResult.Unhealthy(
                "Qdrant is unhealthy",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

/// <summary>
/// Health check for OpenAI API connectivity
/// </summary>
public class OpenAIHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIHealthCheck> _logger;
    private readonly HttpClient _httpClient;

    public OpenAIHealthCheck(
        IConfiguration configuration,
        ILogger<OpenAIHealthCheck> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory?.CreateClient("OpenAIHealth") ?? new HttpClient();
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = _configuration["OpenAI:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey))
            {
                return HealthCheckResult.Unhealthy(
                    "OpenAI API key is not configured",
                    data: new Dictionary<string, object>
                    {
                        { "error", "Missing API key" }
                    });
            }

            // Check if API key format looks valid (starts with sk-)
            var isValidFormat = apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
            
            var data = new Dictionary<string, object>
            {
                { "api_key_configured", true },
                { "api_key_format_valid", isValidFormat },
                { "embedding_model", _configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small" },
                { "chat_model", _configuration["OpenAI:ChatModel"] ?? "gpt-4o" }
            };

            // Optional: Make a lightweight API call to verify connectivity
            // Note: This costs tokens, so we just verify configuration by default
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                
                var response = await _httpClient.SendAsync(request, cts.Token);
                
                data["api_reachable"] = response.IsSuccessStatusCode;
                data["api_status_code"] = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OpenAI API returned status {StatusCode}", response.StatusCode);
                    return HealthCheckResult.Degraded(
                        $"OpenAI API returned status {response.StatusCode}",
                        data: data);
                }
            }
            catch (TaskCanceledException)
            {
                data["api_reachable"] = false;
                data["error"] = "Request timed out";
                return HealthCheckResult.Degraded("OpenAI API request timed out", data: data);
            }
            catch (HttpRequestException ex)
            {
                data["api_reachable"] = false;
                data["error"] = ex.Message;
                return HealthCheckResult.Degraded($"OpenAI API unreachable: {ex.Message}", data: data);
            }

            _logger.LogDebug("OpenAI health check passed");
            
            return HealthCheckResult.Healthy("OpenAI is healthy", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI health check failed");
            
            return HealthCheckResult.Unhealthy(
                "OpenAI health check failed",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

/// <summary>
/// Health check for document repository
/// </summary>
public class DocumentRepositoryHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentRepositoryHealthCheck> _logger;

    public DocumentRepositoryHealthCheck(
        IServiceProvider serviceProvider,
        ILogger<DocumentRepositoryHealthCheck> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<Interfaces.IDocumentRepository>();
            
            // Try to get document count
            var (documents, totalCount) = await repository.GetDocumentsAsync(1, 1);
            
            var data = new Dictionary<string, object>
            {
                { "total_documents", totalCount },
                { "repository_type", repository.GetType().Name }
            };

            _logger.LogDebug("Document repository health check passed. Documents: {Count}", totalCount);
            
            return HealthCheckResult.Healthy("Document repository is healthy", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document repository health check failed");
            
            return HealthCheckResult.Unhealthy(
                "Document repository is unhealthy",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

/// <summary>
/// Startup health check that verifies system is ready to accept requests
/// </summary>
public class StartupHealthCheck : IHealthCheck
{
    private volatile bool _isReady;

    public bool IsReady
    {
        get => _isReady;
        set => _isReady = value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_isReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Application has completed startup"));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Application is still starting up"));
    }
}
