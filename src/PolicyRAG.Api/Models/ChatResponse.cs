namespace PolicyRAG.Api.Models;

/// <summary>
/// Represents a complete chat response with answer and sources
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// The generated answer from the LLM
    /// </summary>
    public required string Answer { get; init; }
    
    /// <summary>
    /// Source documents that were used to generate the answer
    /// </summary>
    public List<SourceReference> Sources { get; init; } = new();
    
    /// <summary>
    /// Total number of tokens used in the request
    /// </summary>
    public int? TokensUsed { get; init; }
    
    /// <summary>
    /// Whether the answer was fully grounded in the provided context
    /// </summary>
    public bool IsGrounded { get; init; } = true;
    
    /// <summary>
    /// Optional warning message if the answer couldn't be fully grounded
    /// </summary>
    public string? Warning { get; init; }
}
