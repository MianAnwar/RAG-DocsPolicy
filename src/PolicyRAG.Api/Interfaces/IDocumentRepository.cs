using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Interfaces;

/// <summary>
/// Repository for storing and retrieving document metadata
/// </summary>
public interface IDocumentRepository
{
    /// <summary>
    /// Add a new document to the repository
    /// </summary>
    Task<DocumentInfo> AddAsync(DocumentInfo document);
    
    /// <summary>
    /// Get document by ID
    /// </summary>
    Task<DocumentInfo?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Get paginated list of documents
    /// </summary>
    Task<(List<DocumentInfo> Documents, int TotalCount)> GetDocumentsAsync(int page, int pageSize);
    
    /// <summary>
    /// Update document information
    /// </summary>
    Task UpdateAsync(DocumentInfo document);
    
    /// <summary>
    /// Delete document by ID
    /// </summary>
    Task DeleteAsync(Guid id);
}
