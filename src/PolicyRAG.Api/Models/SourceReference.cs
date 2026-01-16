namespace PolicyRAG.Api.Models;

/// <summary>
/// Represents a source document reference with citation information
/// </summary>
public class SourceReference
{
    /// <summary>
    /// The unique identifier of the source chunk
    /// </summary>
    public required string ChunkId { get; init; }
    
    /// <summary>
    /// The title of the source document
    /// </summary>
    public required string DocumentTitle { get; init; }
    
    /// <summary>
    /// The section within the document
    /// </summary>
    public string? Section { get; init; }
    
    /// <summary>
    /// The page number (if available)
    /// </summary>
    public int? PageNumber { get; init; }
    
    /// <summary>
    /// The relevance score from the vector search
    /// </summary>
    public float RelevanceScore { get; init; }
    
    /// <summary>
    /// Excerpt of the relevant content
    /// </summary>
    public required string ContentExcerpt { get; init; }
}
