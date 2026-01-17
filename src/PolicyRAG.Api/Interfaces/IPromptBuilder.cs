using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Interfaces;

/// <summary>
/// Service for constructing prompts with context from retrieved documents
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Builds a complete prompt with system instructions and retrieved context
    /// </summary>
    (string SystemPrompt, string UserMessage) BuildPrompt(
        string userQuery, 
        IReadOnlyList<SearchResult> retrievedChunks,
        int maxContextTokens = 8000);
}
