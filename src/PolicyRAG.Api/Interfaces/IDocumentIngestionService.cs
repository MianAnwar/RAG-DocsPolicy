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
    Task<(Guid DocumentId, int ChunkCount)> IngestDocumentAsync(
        Stream fileStream,
        string fileName,
        string department,
        IProgress<string>? progress = null);

    Task DeleteDocumentAsync(Guid documentId);

    Task ReprocessDocumentAsync(Guid documentId);
}
