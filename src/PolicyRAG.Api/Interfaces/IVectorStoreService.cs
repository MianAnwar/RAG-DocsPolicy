using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Interfaces;

public interface IVectorStoreService
{
    Task InitializeCollectionAsync();
    Task UpsertChunksAsync(List<DocumentChunk> chunks);
    Task DeleteByDocumentIdAsync(Guid documentId);
    Task<List<SearchResult>> SearchAsync(float[] queryVector, SearchOptions options);
}

public class SearchOptions
{
    public int TopK { get; set; } = 5;
    public float ScoreThreshold { get; set; } = 0.7f;
    public string? DepartmentFilter { get; set; }
    public Guid? DocumentIdFilter { get; set; }
    public bool UseMmr { get; set; } = true;
    public float MmrDiversity { get; set; } = 0.3f;
}

public class SearchResult
{
    public Guid ChunkId { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
}
