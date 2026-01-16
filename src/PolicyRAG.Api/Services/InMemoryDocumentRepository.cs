using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;
using System.Collections.Concurrent;

namespace PolicyRAG.Api.Services;

/// <summary>
/// In-memory implementation of document repository for development
/// In production, replace with database implementation (EF Core, Dapper, etc.)
/// </summary>
public class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<Guid, DocumentInfo> _documents = new();
    private readonly ILogger<InMemoryDocumentRepository> _logger;

    public InMemoryDocumentRepository(ILogger<InMemoryDocumentRepository> logger)
    {
        _logger = logger;
    }

    public Task<DocumentInfo> AddAsync(DocumentInfo document)
    {
        _documents[document.Id] = document;
        _logger.LogInformation("Added document {DocumentId} to repository", document.Id);
        return Task.FromResult(document);
    }

    public Task<DocumentInfo?> GetByIdAsync(Guid id)
    {
        _documents.TryGetValue(id, out var document);
        return Task.FromResult(document);
    }

    public Task<(List<DocumentInfo> Documents, int TotalCount)> GetDocumentsAsync(int page, int pageSize)
    {
        var allDocuments = _documents.Values
            .OrderByDescending(d => d.UploadDate)
            .ToList();

        var totalCount = allDocuments.Count;
        var documents = allDocuments
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult((documents, totalCount));
    }

    public Task UpdateAsync(DocumentInfo document)
    {
        if (_documents.ContainsKey(document.Id))
        {
            _documents[document.Id] = document;
            _logger.LogInformation("Updated document {DocumentId}", document.Id);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        _documents.TryRemove(id, out _);
        _logger.LogInformation("Deleted document {DocumentId}", id);
        return Task.CompletedTask;
    }
}
