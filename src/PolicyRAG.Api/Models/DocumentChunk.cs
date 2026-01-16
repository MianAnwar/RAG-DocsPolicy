namespace PolicyRAG.Api.Models;

public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int PageNumber { get; set; }
    public int StartCharIndex { get; set; }
    public int EndCharIndex { get; set; }
    public int TokenCount { get; set; }
    
    // Metadata for filtering
    public string DocumentTitle { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    
    // Embedding (populated after embedding generation)
    public float[]? Embedding { get; set; }
}
