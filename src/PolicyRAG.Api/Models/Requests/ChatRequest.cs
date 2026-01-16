using System.ComponentModel.DataAnnotations;

namespace PolicyRAG.Api.Models.Requests;

/// <summary>
/// Request model for chat endpoint
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The user's question or message
    /// </summary>
    [Required(ErrorMessage = "Message is required")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Message must be between 1 and 2000 characters")]
    public required string Message { get; init; }
    
    /// <summary>
    /// Optional department filter to scope the search
    /// </summary>
    [StringLength(100, ErrorMessage = "Department filter must be less than 100 characters")]
    public string? DepartmentFilter { get; init; }
}
