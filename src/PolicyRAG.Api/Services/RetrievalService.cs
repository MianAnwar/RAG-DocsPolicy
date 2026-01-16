using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Services;

public class RetrievalService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStoreService _vectorStore;
    private readonly ILogger<RetrievalService> _logger;

    public RetrievalService(
        IEmbeddingService embeddingService,
        IVectorStoreService vectorStore,
        ILogger<RetrievalService> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<List<SearchResult>> RetrieveContextAsync(
        string query,
        RetrievalOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("Attempted to retrieve context for empty query");
            return new List<SearchResult>();
        }

        options ??= new RetrievalOptions();

        try
        {
            _logger.LogInformation("Retrieving context for query with TopK={TopK}, ScoreThreshold={ScoreThreshold}",
                options.TopK, options.ScoreThreshold);

            // 1. Generate query embedding
            _logger.LogDebug("Generating query embedding");
            var queryVector = await _embeddingService.GenerateEmbeddingAsync(query);

            // 2. Search vector store with MMR
            var searchOptions = new SearchOptions
            {
                TopK = options.TopK,
                ScoreThreshold = options.ScoreThreshold,
                DepartmentFilter = options.DepartmentFilter,
                UseMmr = true,
                MmrDiversity = 0.3f
            };

            _logger.LogDebug("Searching vector store");
            var results = await _vectorStore.SearchAsync(queryVector, searchOptions);

            // 3. Filter and rank results
            var filteredResults = results
                .Where(r => r.Score >= options.ScoreThreshold)
                .OrderByDescending(r => r.Score)
                .Take(options.TopK)
                .ToList();

            _logger.LogInformation("Retrieved {Count} context results for query", filteredResults.Count);

            return filteredResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving context for query");
            throw;
        }
    }
}
