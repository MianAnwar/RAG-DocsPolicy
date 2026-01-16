namespace PolicyRAG.Api.Models.Responses;

/// <summary>
/// Response model for document list endpoint
/// </summary>
public class DocumentListResponse
{
    /// <summary>
    /// List of documents
    /// </summary>
    public required List<DocumentInfo> Documents { get; init; }
    
    /// <summary>
    /// Total number of documents
    /// </summary>
    public int TotalCount { get; init; }
    
    /// <summary>
    /// Current page number
    /// </summary>
    public int CurrentPage { get; init; }
    
    /// <summary>
    /// Page size
    /// </summary>
    public int PageSize { get; init; }
}
