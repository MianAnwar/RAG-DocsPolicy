using System.ComponentModel.DataAnnotations;

namespace PolicyRAG.Api.Models.Requests;

/// <summary>
/// Request model for search endpoint
/// </summary>
public class SearchRequest
{
    /// <summary>
    /// The search query
    /// </summary>
    [Required(ErrorMessage = "Query is required")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Query must be between 1 and 2000 characters")]
    public required string Query { get; init; }
    
    /// <summary>
    /// Number of results to return (default: 10)
    /// </summary>
    [Range(1, 100, ErrorMessage = "TopK must be between 1 and 100")]
    public int? TopK { get; init; }
    
    /// <summary>
    /// Minimum relevance score threshold (default: 0.5)
    /// </summary>
    [Range(0, 1, ErrorMessage = "ScoreThreshold must be between 0 and 1")]
    public float? ScoreThreshold { get; init; }
    
    /// <summary>
    /// Optional department filter
    /// </summary>
    [StringLength(100, ErrorMessage = "Department filter must be less than 100 characters")]
    public string? DepartmentFilter { get; init; }
}
