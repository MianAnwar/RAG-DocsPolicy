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
    /// <param name="userQuery">The user's question</param>
    /// <param name="retrievedChunks">Retrieved document chunks with relevance scores</param>
    /// <param name="maxContextTokens">Maximum tokens to allocate for context (default: 8000)</param>
    /// <returns>System prompt and user message with context</returns>
    (string SystemPrompt, string UserMessage) BuildPrompt(
        string userQuery, 
        IReadOnlyList<SearchResult> retrievedChunks,
        int maxContextTokens = 8000);
}
