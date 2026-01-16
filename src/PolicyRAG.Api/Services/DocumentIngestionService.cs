using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Services;

/// <summary>
/// Orchestrates the complete document ingestion pipeline
/// </summary>
public class DocumentIngestionService : IDocumentIngestionService
{
    private readonly DocumentParserFactory _parserFactory;
    private readonly ChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStoreService _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        DocumentParserFactory parserFactory,
        ChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IVectorStoreService vectorStore,
        IDocumentRepository documentRepository,
        ILogger<DocumentIngestionService> logger)
    {
        _parserFactory = parserFactory;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _logger = logger;
    }

    public async Task<(Guid DocumentId, int ChunkCount)> IngestDocumentAsync(
        Stream fileStream,
        string fileName,
        string department,
        IProgress<string>? progress = null)
    {
        var documentId = Guid.NewGuid();
        _logger.LogInformation("Starting ingestion for document {DocumentId}: {FileName}", documentId, fileName);

        try
        {
            // Create document metadata record
            var fileSize = fileStream.Length;
            var fileType = Path.GetExtension(fileName).TrimStart('.');
            
            var documentInfo = new DocumentInfo
            {
                Id = documentId,
                FileName = fileName,
                Title = Path.GetFileNameWithoutExtension(fileName),
                FileType = fileType,
                FileSizeBytes = fileSize,
                Department = department,
                UploadDate = DateTime.UtcNow,
                Status = DocumentStatus.Processing
            };

            await _documentRepository.AddAsync(documentInfo);
            progress?.Report("Document metadata created");

            // 1. Parse document
            _logger.LogInformation("Parsing document {DocumentId}", documentId);
            progress?.Report("Parsing document...");
            
            var parser = _parserFactory.GetParser(fileName);
            var parsedDocument = await parser.ParseAsync(fileStream, fileName);
            
            documentInfo.PageCount = parsedDocument.PageCount;
            documentInfo.Author = parsedDocument.Metadata.GetValueOrDefault("Author");
            if (!string.IsNullOrEmpty(parsedDocument.Metadata.GetValueOrDefault("Title")))
            {
                documentInfo.Title = parsedDocument.Metadata["Title"];
            }
            
            _logger.LogInformation("Parsed document {DocumentId}: {PageCount} pages", documentId, parsedDocument.PageCount);
            progress?.Report($"Parsed {parsedDocument.PageCount} pages");

            // 2. Chunk document
            _logger.LogInformation("Chunking document {DocumentId}", documentId);
            progress?.Report("Creating chunks...");
            
            var metadata = new DocumentMetadata
            {
                Title = documentInfo.Title,
                Department = department,
                Author = documentInfo.Author ?? string.Empty
            };
            
            var chunks = _chunkingService.ChunkDocument(parsedDocument, documentId, metadata);
            documentInfo.ChunkCount = chunks.Count;
            
            _logger.LogInformation("Created {ChunkCount} chunks for document {DocumentId}", chunks.Count, documentId);
            progress?.Report($"Created {chunks.Count} chunks");

            // 3. Generate embeddings
            _logger.LogInformation("Generating embeddings for {ChunkCount} chunks", chunks.Count);
            progress?.Report("Generating embeddings...");
            
            var chunkContents = chunks.Select(c => c.Content).ToList();
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
                chunkContents,
                new Progress<int>(processed => 
                {
                    progress?.Report($"Embedded {processed}/{chunks.Count} chunks");
                }));

            // Attach embeddings to chunks
            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].Embedding = embeddings[i];
            }

            _logger.LogInformation("Generated embeddings for {ChunkCount} chunks", chunks.Count);
            progress?.Report("Embeddings generated");

            // 4. Store in vector database
            _logger.LogInformation("Storing chunks in vector database");
            progress?.Report("Storing in vector database...");
            
            await _vectorStore.UpsertChunksAsync(chunks);
            
            _logger.LogInformation("Successfully stored {ChunkCount} chunks for document {DocumentId}", chunks.Count, documentId);
            progress?.Report("Complete");

            // 5. Update document status
            documentInfo.Status = DocumentStatus.Completed;
            await _documentRepository.UpdateAsync(documentInfo);

            return (documentId, chunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting document {DocumentId}: {FileName}", documentId, fileName);
            
            // Update document status to failed
            var documentInfo = await _documentRepository.GetByIdAsync(documentId);
            if (documentInfo != null)
            {
                documentInfo.Status = DocumentStatus.Failed;
                documentInfo.ErrorMessage = ex.Message;
                await _documentRepository.UpdateAsync(documentInfo);
            }
            
            throw;
        }
    }

    public async Task DeleteDocumentAsync(Guid documentId)
    {
        _logger.LogInformation("Deleting document {DocumentId}", documentId);

        try
        {
            // Delete from vector store
            await _vectorStore.DeleteByDocumentIdAsync(documentId);
            
            // Delete from repository
            await _documentRepository.DeleteAsync(documentId);
            
            _logger.LogInformation("Successfully deleted document {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {DocumentId}", documentId);
            throw;
        }
    }

    public async Task ReprocessDocumentAsync(Guid documentId)
    {
        _logger.LogInformation("Reprocessing document {DocumentId}", documentId);

        var documentInfo = await _documentRepository.GetByIdAsync(documentId);
        if (documentInfo == null)
        {
            throw new InvalidOperationException($"Document {documentId} not found");
        }

        // For reprocessing, we would need to store the original file
        // This is a simplified implementation that just updates the status
        documentInfo.Status = DocumentStatus.Processing;
        await _documentRepository.UpdateAsync(documentInfo);

        _logger.LogWarning("Reprocessing not fully implemented - would require storing original files");
        
        // In production: retrieve original file, delete old chunks, re-ingest
        throw new NotImplementedException("Document reprocessing requires storing original files");
    }
}
