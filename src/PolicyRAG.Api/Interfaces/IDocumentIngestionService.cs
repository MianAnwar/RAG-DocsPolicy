using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Interfaces;

/// <summary>
/// Service for orchestrating document ingestion pipeline
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>
    /// Ingest a document through the full processing pipeline
    /// </summary>
    /// <param name="fileStream">Document file stream</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="department">Department associated with the document</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>Document ID and chunk count</returns>
    Task<(Guid DocumentId, int ChunkCount)> IngestDocumentAsync(
        Stream fileStream,
        string fileName,
        string department,
        IProgress<string>? progress = null);

    /// <summary>
    /// Delete a document and all its chunks
    /// </summary>
    Task DeleteDocumentAsync(Guid documentId);

    /// <summary>
    /// Reprocess an existing document (re-chunk and re-embed)
    /// </summary>
    Task ReprocessDocumentAsync(Guid documentId);
}
