namespace PolicyRAG.Api.Models.Responses;

/// <summary>
/// Result of a document upload operation
/// </summary>
public class DocumentUploadResult
{
    /// <summary>
    /// Name of the uploaded file
    /// </summary>
    public required string FileName { get; init; }
    
    /// <summary>
    /// Whether the upload was successful
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Document ID if upload was successful
    /// </summary>
    public Guid? DocumentId { get; init; }
    
    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? Error { get; init; }
    
    /// <summary>
    /// Number of chunks created from the document
    /// </summary>
    public int? ChunkCount { get; init; }
}
