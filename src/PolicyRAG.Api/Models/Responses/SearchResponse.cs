using PolicyRAG.Api.Interfaces;

namespace PolicyRAG.Api.Models.Responses;

/// <summary>
/// Response model for search endpoint
/// </summary>
public class SearchResponse
{
    /// <summary>
    /// List of search results
    /// </summary>
    public required List<SearchResult> Results { get; init; }
    
    /// <summary>
    /// Total number of results returned
    /// </summary>
    public int TotalCount { get; init; }
}
