using OpenAI.Embeddings;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Resilience;
using Polly;

namespace PolicyRAG.Api.Services;

public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly int _batchSize = 100;
    private readonly int _rateLimitDelayMs = 100;

    public OpenAIEmbeddingService(
        IConfiguration configuration, 
        ILogger<OpenAIEmbeddingService> logger,
        OpenAIResiliencePipeline? resiliencePipeline = null)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        var model = configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        _client = new EmbeddingClient(model, apiKey);
        _logger = logger;
        _resiliencePipeline = resiliencePipeline?.Pipeline ?? ResiliencePipeline.Empty;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Attempted to generate embedding for empty text");
            return Array.Empty<float>();
        }

        try
        {
            return await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var embedding = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
                return embedding.Value.ToFloats().ToArray();
            });
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            _logger.LogError("OpenAI circuit breaker is open - embedding service unavailable");
            throw new InvalidOperationException("Embedding service is temporarily unavailable. Please try again later.");
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            _logger.LogError("OpenAI embedding request timed out");
            throw new TimeoutException("Embedding generation timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text");
            throw;
        }
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(
        List<string> texts, 
        IProgress<int>? progress = null)
    {
        if (texts == null || texts.Count == 0)
        {
            _logger.LogWarning("Attempted to generate embeddings for empty text list");
            return new List<float[]>();
        }

        var results = new List<float[]>();
        var processed = 0;
        var totalBatches = (int)Math.Ceiling(texts.Count / (double)_batchSize);

        _logger.LogInformation("Starting batch embedding generation for {Count} texts in {Batches} batches", 
            texts.Count, totalBatches);

        for (int i = 0; i < texts.Count; i += _batchSize)
        {
            var batch = texts.Skip(i).Take(_batchSize).ToList();
            var batchNumber = (i / _batchSize) + 1;
            
            try
            {
                _logger.LogDebug("Processing batch {BatchNumber}/{TotalBatches} with {Count} texts", 
                    batchNumber, totalBatches, batch.Count);

                // Use resilience pipeline for batch embedding
                var batchEmbeddings = await _resiliencePipeline.ExecuteAsync(async ct =>
                {
                    var embeddings = await _client.GenerateEmbeddingsAsync(batch, cancellationToken: ct);
                    return embeddings.Value.Select(e => e.ToFloats().ToArray()).ToList();
                });
                
                results.AddRange(batchEmbeddings);
                processed += batch.Count;
                progress?.Report(processed);

                _logger.LogDebug("Completed batch {BatchNumber}/{TotalBatches}", batchNumber, totalBatches);
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            {
                _logger.LogError("OpenAI circuit breaker is open - aborting batch embedding at batch {BatchNumber}", batchNumber);
                throw new InvalidOperationException("Embedding service is temporarily unavailable. Please try again later.");
            }
            catch (Polly.Timeout.TimeoutRejectedException)
            {
                _logger.LogError("Batch {BatchNumber} timed out", batchNumber);
                throw new TimeoutException($"Embedding generation timed out at batch {batchNumber}. Please try again.");
            }
            catch (Exception ex) when (IsRateLimitError(ex))
            {
                _logger.LogWarning("Rate limit hit on batch {BatchNumber}, retrying after delay", batchNumber);
                
                // Exponential backoff
                await Task.Delay(_rateLimitDelayMs * 2);
                i -= _batchSize; // Retry this batch
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch {BatchNumber}/{TotalBatches}", 
                    batchNumber, totalBatches);
                throw;
            }
            
            // Rate limiting between batches
            if (i + _batchSize < texts.Count)
            {
                await Task.Delay(_rateLimitDelayMs);
            }
        }

        _logger.LogInformation("Completed embedding generation for {Count} texts", texts.Count);
        return results;
    }

    private bool IsRateLimitError(Exception ex) 
        => ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase);
}
