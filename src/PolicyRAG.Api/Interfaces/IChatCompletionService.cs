using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Interfaces;

/// <summary>
/// Service for interacting with LLM for chat completions using retrieved context
/// </summary>
public interface IChatCompletionService
{
    Task<ChatResponse> GenerateAnswerAsync(
        string userQuery,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> GenerateAnswerStreamAsync(
        string userQuery,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default);
}
