using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Interfaces;

/// <summary>
/// Repository for storing and retrieving document metadata
/// </summary>
public interface IDocumentRepository
{
    Task<DocumentInfo> AddAsync(DocumentInfo document);
    
    Task<DocumentInfo?> GetByIdAsync(Guid id);
    
    Task<(List<DocumentInfo> Documents, int TotalCount)> GetDocumentsAsync(int page, int pageSize);
    
    Task UpdateAsync(DocumentInfo document);
    
    Task DeleteAsync(Guid id);
}
