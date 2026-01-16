using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Interfaces;

/// <summary>
/// Service for interacting with LLM for chat completions
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Generates a complete answer to a user query using retrieved context
    /// </summary>
    /// <param name="userQuery">The user's question</param>
    /// <param name="systemPrompt">System instructions for the LLM</param>
    /// <param name="userMessage">User message with context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete chat response with answer and metadata</returns>
    Task<ChatResponse> GenerateAnswerAsync(
        string userQuery,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a streaming answer to a user query using retrieved context
    /// </summary>
    /// <param name="userQuery">The user's question</param>
    /// <param name="systemPrompt">System instructions for the LLM</param>
    /// <param name="userMessage">User message with context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of response chunks</returns>
    IAsyncEnumerable<string> GenerateAnswerStreamAsync(
        string userQuery,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default);
}
