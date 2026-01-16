using Microsoft.AspNetCore.Mvc;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;
using PolicyRAG.Api.Models.Responses;

namespace PolicyRAG.Api.Controllers;

/// <summary>
/// Controller for document management operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestionService;
    private readonly IDocumentRepository _documentRepository;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentIngestionService ingestionService,
        IDocumentRepository documentRepository,
        ILogger<DocumentsController> logger)
    {
        _ingestionService = ingestionService;
        _documentRepository = documentRepository;
        _logger = logger;
    }

    /// <summary>
    /// Upload one or more documents for ingestion
    /// </summary>
    /// <param name="files">Files to upload</param>
    /// <param name="department">Department to associate with the documents</param>
    /// <returns>Upload results for each file</returns>
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)] // 50MB limit
    [ProducesResponseType(typeof(List<DocumentUploadResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload([FromForm] List<IFormFile> files, [FromForm] string department = "General")
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest("No files provided");
        }

        var results = new List<DocumentUploadResult>();
        var allowedExtensions = new[] { ".pdf", ".docx", ".txt" };

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
            {
                results.Add(new DocumentUploadResult
                {
                    FileName = file.FileName,
                    Success = false,
                    Error = $"File type {extension} not supported. Allowed types: PDF, DOCX, TXT"
                });
                continue;
            }

            try
            {
                _logger.LogInformation("Processing upload for file: {FileName}", file.FileName);

                using var stream = file.OpenReadStream();
                var (documentId, chunkCount) = await _ingestionService.IngestDocumentAsync(
                    stream,
                    file.FileName,
                    department);

                results.Add(new DocumentUploadResult
                {
                    FileName = file.FileName,
                    Success = true,
                    DocumentId = documentId,
                    ChunkCount = chunkCount
                });

                _logger.LogInformation(
                    "Successfully processed document {DocumentId}: {FileName} ({ChunkCount} chunks)",
                    documentId,
                    file.FileName,
                    chunkCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {FileName}", file.FileName);
                
                results.Add(new DocumentUploadResult
                {
                    FileName = file.FileName,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return Ok(results);
    }

    /// <summary>
    /// Get paginated list of documents
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of documents per page</param>
    /// <returns>Paginated document list</returns>
    [HttpGet]
    [ProducesResponseType(typeof(DocumentListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1)
        {
            return BadRequest("Page must be greater than 0");
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return BadRequest("PageSize must be between 1 and 100");
        }

        var (documents, totalCount) = await _documentRepository.GetDocumentsAsync(page, pageSize);

        return Ok(new DocumentListResponse
        {
            Documents = documents,
            TotalCount = totalCount,
            CurrentPage = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Get a specific document by ID
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>Document information</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(DocumentInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(Guid id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        
        if (document == null)
        {
            return NotFound($"Document {id} not found");
        }

        return Ok(document);
    }

    /// <summary>
    /// Delete a document and all its chunks
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        if (document == null)
        {
            return NotFound($"Document {id} not found");
        }

        await _ingestionService.DeleteDocumentAsync(id);
        
        _logger.LogInformation("Deleted document {DocumentId}", id);
        return NoContent();
    }

    /// <summary>
    /// Reprocess an existing document
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>Accepted status</returns>
    [HttpPost("{id}/reprocess")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IActionResult> ReprocessDocument(Guid id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        if (document == null)
        {
            return NotFound($"Document {id} not found");
        }

        try
        {
            await _ingestionService.ReprocessDocumentAsync(id);
            return Accepted();
        }
        catch (NotImplementedException)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, 
                "Document reprocessing requires storing original files (not yet implemented)");
        }
    }
}
