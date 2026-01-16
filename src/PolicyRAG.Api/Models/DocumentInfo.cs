namespace PolicyRAG.Api.Models;

/// <summary>
/// Metadata information about an uploaded document
/// </summary>
public class DocumentInfo
{
    /// <summary>
    /// Unique identifier for the document
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Original file name
    /// </summary>
    public required string FileName { get; set; }
    
    /// <summary>
    /// Document title (extracted from metadata or filename)
    /// </summary>
    public required string Title { get; set; }
    
    /// <summary>
    /// File type (PDF, DOCX, etc.)
    /// </summary>
    public required string FileType { get; set; }
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSizeBytes { get; set; }
    
    /// <summary>
    /// Number of pages in the document
    /// </summary>
    public int PageCount { get; set; }
    
    /// <summary>
    /// Number of chunks created from the document
    /// </summary>
    public int ChunkCount { get; set; }
    
    /// <summary>
    /// Department associated with the document
    /// </summary>
    public string? Department { get; set; }
    
    /// <summary>
    /// Document author
    /// </summary>
    public string? Author { get; set; }
    
    /// <summary>
    /// When the document was uploaded
    /// </summary>
    public DateTime UploadDate { get; set; }
    
    /// <summary>
    /// Processing status
    /// </summary>
    public DocumentStatus Status { get; set; }
    
    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Document processing status
/// </summary>
public enum DocumentStatus
{
    Uploading,
    Processing,
    Completed,
    Failed
}
