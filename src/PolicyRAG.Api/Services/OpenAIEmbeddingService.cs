using OpenAI.Embeddings;
using PolicyRAG.Api.Interfaces;

namespace PolicyRAG.Api.Services;

public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly int _batchSize = 100;
    private readonly int _rateLimitDelayMs = 100;

    public OpenAIEmbeddingService(IConfiguration configuration, ILogger<OpenAIEmbeddingService> logger)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        var model = configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        _client = new EmbeddingClient(model, apiKey);
        _logger = logger;
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
            var embedding = await _client.GenerateEmbeddingAsync(text);
            return embedding.Value.ToFloats().ToArray();
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

                var embeddings = await _client.GenerateEmbeddingsAsync(batch);
                results.AddRange(embeddings.Value.Select(e => e.ToFloats().ToArray()));

                processed += batch.Count;
                progress?.Report(processed);

                _logger.LogDebug("Completed batch {BatchNumber}/{TotalBatches}", batchNumber, totalBatches);
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
