using Microsoft.Extensions.Options;
using PolicyRAG.Api.Configuration;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;
using PolicyRAG.Api.Resilience;
using Polly;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace PolicyRAG.Api.Services;

public class QdrantVectorStoreService : IVectorStoreService
{
    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly ulong _vectorSize;
    private readonly ILogger<QdrantVectorStoreService> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public QdrantVectorStoreService(
        QdrantClient client,
        IOptions<QdrantOptions> options,
        ILogger<QdrantVectorStoreService> logger,
        QdrantResiliencePipeline? resiliencePipeline = null)
    {
        var config = options.Value;
        _client = client;
        _collectionName = config.CollectionName;
        _vectorSize = (ulong)config.VectorSize;
        _logger = logger;
        _resiliencePipeline = resiliencePipeline?.Pipeline ?? ResiliencePipeline.Empty;
    }

    public async Task InitializeCollectionAsync()
    {
        try
        {
            await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var collections = await _client.ListCollectionsAsync(ct);
                
                if (!collections.Contains(_collectionName))
                {
                    _logger.LogInformation("Creating collection: {CollectionName}", _collectionName);
                    
                    await _client.CreateCollectionAsync(_collectionName, new VectorParams
                    {
                        Size = _vectorSize,
                        Distance = Distance.Cosine
                    }, cancellationToken: ct);

                    // Create payload indexes for filtering
                    await _client.CreatePayloadIndexAsync(
                        _collectionName, 
                        "document_id", 
                        PayloadSchemaType.Keyword,
                        cancellationToken: ct);
                        
                    await _client.CreatePayloadIndexAsync(
                        _collectionName, 
                        "department", 
                        PayloadSchemaType.Keyword,
                        cancellationToken: ct);
                        
                    await _client.CreatePayloadIndexAsync(
                        _collectionName, 
                        "upload_date", 
                        PayloadSchemaType.Datetime,
                        cancellationToken: ct);

                    _logger.LogInformation("Collection created successfully: {CollectionName}", _collectionName);
                }
                else
                {
                    _logger.LogInformation("Collection already exists: {CollectionName}", _collectionName);
                }
            });
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            _logger.LogError("Qdrant circuit breaker is open - cannot initialize collection");
            throw new InvalidOperationException("Vector database is temporarily unavailable. Please try again later.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing collection: {CollectionName}", _collectionName);
            throw;
        }
    }

    public async Task UpsertChunksAsync(List<DocumentChunk> chunks)
    {
        if (chunks == null || chunks.Count == 0)
        {
            _logger.LogWarning("Attempted to upsert empty chunk list");
            return;
        }

        try
        {
            var points = chunks.Select(chunk => new PointStruct
            {
                Id = new PointId { Uuid = chunk.Id.ToString() },
                Vectors = chunk.Embedding!,
                Payload =
                {
                    ["document_id"] = chunk.DocumentId.ToString(),
                    ["content"] = chunk.Content,
                    ["chunk_index"] = chunk.ChunkIndex,
                    ["page_number"] = chunk.PageNumber,
                    ["document_title"] = chunk.DocumentTitle,
                    ["section"] = chunk.Section,
                    ["department"] = chunk.Department,
                    ["upload_date"] = chunk.UploadDate.ToString("O"),
                    ["token_count"] = chunk.TokenCount
                }
            }).ToList();

            // Batch upsert in groups of 100
            const int batchSize = 100;
            var totalBatches = (int)Math.Ceiling(points.Count / (double)batchSize);
            
            _logger.LogInformation("Upserting {Count} chunks in {Batches} batches to collection: {Collection}", 
                points.Count, totalBatches, _collectionName);

            for (int i = 0; i < points.Count; i += batchSize)
            {
                var batch = points.Skip(i).Take(batchSize).ToList();
                var batchNumber = (i / batchSize) + 1;
                
                _logger.LogDebug("Upserting batch {BatchNumber}/{TotalBatches} with {Count} points", 
                    batchNumber, totalBatches, batch.Count);
                
                await _resiliencePipeline.ExecuteAsync(async ct =>
                {
                    await _client.UpsertAsync(_collectionName, batch, cancellationToken: ct);
                });
            }

            _logger.LogInformation("Successfully upserted {Count} chunks to collection: {Collection}", 
                points.Count, _collectionName);
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            _logger.LogError("Qdrant circuit breaker is open - cannot upsert chunks");
            throw new InvalidOperationException("Vector database is temporarily unavailable. Please try again later.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting chunks to collection: {Collection}", _collectionName);
            throw;
        }
    }

    public async Task DeleteByDocumentIdAsync(Guid documentId)
    {
        try
        {
            _logger.LogInformation("Deleting chunks for document: {DocumentId}", documentId);
            
            await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                await _client.DeleteAsync(
                    _collectionName,
                    MatchKeyword("document_id", documentId.ToString()),
                    cancellationToken: ct);
            });

            _logger.LogInformation("Successfully deleted chunks for document: {DocumentId}", documentId);
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            _logger.LogError("Qdrant circuit breaker is open - cannot delete document chunks");
            throw new InvalidOperationException("Vector database is temporarily unavailable. Please try again later.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting chunks for document: {DocumentId}", documentId);
            throw;
        }
    }

    public async Task<List<SearchResult>> SearchAsync(float[] queryVector, SearchOptions options)
    {
        try
        {
            // Build filter conditions
            var conditions = new List<Condition>();
            
            if (!string.IsNullOrEmpty(options.DepartmentFilter))
            {
                conditions.Add(MatchKeyword("department", options.DepartmentFilter));
                _logger.LogDebug("Adding department filter: {Department}", options.DepartmentFilter);
            }
            
            if (options.DocumentIdFilter.HasValue)
            {
                conditions.Add(MatchKeyword("document_id", options.DocumentIdFilter.Value.ToString()));
                _logger.LogDebug("Adding document ID filter: {DocumentId}", options.DocumentIdFilter.Value);
            }

            Filter? filter = conditions.Count > 0 
                ? new Filter { Must = { conditions } } 
                : null;

            // Two-stage search: broader initial retrieval + MMR for diversity
            var candidateLimit = options.UseMmr ? options.TopK * 10 : options.TopK;
            
            _logger.LogDebug("Searching collection: {Collection} with TopK={TopK}, CandidateLimit={CandidateLimit}, UseMmr={UseMmr}", 
                _collectionName, options.TopK, candidateLimit, options.UseMmr);
            
            var searchResults = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                return await _client.SearchAsync(
                    _collectionName,
                    queryVector,
                    filter: filter,
                    limit: (ulong)candidateLimit,
                    scoreThreshold: options.ScoreThreshold,
                    cancellationToken: ct);
            });

            var results = searchResults.Select(r => new SearchResult
            {
                ChunkId = Guid.Parse(r.Id.Uuid),
                Content = r.Payload["content"].StringValue,
                Score = r.Score,
                DocumentTitle = r.Payload["document_title"].StringValue,
                PageNumber = (int)r.Payload["page_number"].IntegerValue,
                Section = r.Payload["section"].StringValue
            }).ToList();

            _logger.LogInformation("Found {Count} candidate results", results.Count);

            // Apply MMR for diversity
            if (options.UseMmr && results.Count > options.TopK)
            {
                _logger.LogDebug("Applying MMR with diversity weight: {Diversity}", options.MmrDiversity);
                results = ApplyMmr(results, queryVector, options.TopK, options.MmrDiversity);
            }

            var finalResults = results.Take(options.TopK).ToList();
            _logger.LogInformation("Returning {Count} final results", finalResults.Count);

            return finalResults;
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            _logger.LogError("Qdrant circuit breaker is open - search unavailable");
            throw new InvalidOperationException("Vector search is temporarily unavailable. Please try again later.");
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            _logger.LogError("Qdrant search request timed out");
            throw new TimeoutException("Search request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching collection: {Collection}", _collectionName);
            throw;
        }
    }

    private List<SearchResult> ApplyMmr(
        List<SearchResult> candidates, 
        float[] queryVector, 
        int topK, 
        float diversityWeight)
    {
        // Maximal Marginal Relevance for result diversity
        var selected = new List<SearchResult>();
        var remaining = candidates.ToList();

        while (selected.Count < topK && remaining.Count > 0)
        {
            SearchResult? best = null;
            float bestScore = float.MinValue;

            foreach (var candidate in remaining)
            {
                float relevance = candidate.Score;
                float redundancy = selected.Count > 0
                    ? selected.Max(s => ComputeSimilarity(s.Content, candidate.Content))
                    : 0;

                float mmrScore = (1 - diversityWeight) * relevance - diversityWeight * redundancy;

                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    best = candidate;
                }
            }

            if (best != null)
            {
                selected.Add(best);
                remaining.Remove(best);
            }
        }

        return selected;
    }

    private float ComputeSimilarity(string text1, string text2)
    {
        // Simple Jaccard similarity for redundancy check
        var words1 = text1.ToLower().Split(' ').ToHashSet();
        var words2 = text2.ToLower().Split(' ').ToHashSet();
        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();
        return union > 0 ? (float)intersection / union : 0;
    }
}
