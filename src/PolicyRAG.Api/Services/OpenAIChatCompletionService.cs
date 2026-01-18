using OpenAI.Chat;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;
using PolicyRAG.Api.Resilience;
using Polly;
using System.Runtime.CompilerServices;
using System.Text;

namespace PolicyRAG.Api.Services;

/// <summary>
/// Service for generating chat completions using OpenAI's GPT models
/// </summary>
public class OpenAIChatCompletionService : IChatCompletionService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIChatCompletionService> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly string _modelName;

    public OpenAIChatCompletionService(
        IConfiguration configuration,
        ILogger<OpenAIChatCompletionService> logger,
        OpenAIResiliencePipeline? resiliencePipeline = null)
    {
        var apiKey = configuration["OpenAI:ApiKey"] 
            ?? throw new InvalidOperationException("OpenAI API key not configured");
        
        _modelName = configuration["OpenAI:ChatModel"] ?? "gpt-4o";
        _chatClient = new ChatClient(_modelName, apiKey);
        _logger = logger;
        _resiliencePipeline = resiliencePipeline?.Pipeline ?? ResiliencePipeline.Empty;

        _logger.LogInformation("Initialized OpenAI Chat Completion Service with model {Model}", _modelName);
    }

    public async Task<ChatResponse> GenerateAnswerAsync(
        string userQuery,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating answer for query: {Query}", userQuery);

        try
        {
            return await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userMessage)
                };

                var completion = await _chatClient.CompleteChatAsync(
                    messages,
                    cancellationToken: ct);

                var response = completion.Value;
                var answer = response.Content[0].Text;
                
                _logger.LogInformation(
                    "Generated answer with {InputTokens} input tokens, {OutputTokens} output tokens",
                    response.Usage.InputTokenCount,
                    response.Usage.OutputTokenCount);

                return new ChatResponse
                {
                    Answer = answer,
                    TokensUsed = response.Usage.TotalTokenCount,
                    IsGrounded = !answer.Contains("I don't have enough information", StringComparison.OrdinalIgnoreCase) &&
                                !answer.Contains("cannot answer", StringComparison.OrdinalIgnoreCase)
                };
            }, cancellationToken);
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            _logger.LogError("OpenAI circuit breaker is open - service unavailable");
            return new ChatResponse
            {
                Answer = "I'm temporarily unable to process your request due to service issues. Please try again in a few moments.",
                IsGrounded = false,
                Warning = "Service temporarily unavailable"
            };
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            _logger.LogError("OpenAI request timed out");
            return new ChatResponse
            {
                Answer = "The request took too long to process. Please try again with a simpler question.",
                IsGrounded = false,
                Warning = "Request timed out"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating chat completion");
            throw;
        }
    }

    public async IAsyncEnumerable<string> GenerateAnswerStreamAsync(
        string userQuery,
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating streaming answer for query: {Query}", userQuery);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        var streamingUpdates = _chatClient.CompleteChatStreamingAsync(
            messages,
            cancellationToken: cancellationToken);

        var chunkCount = 0;
        await foreach (var update in streamingUpdates.WithCancellation(cancellationToken))
        {
            foreach (var contentPart in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(contentPart.Text))
                {
                    chunkCount++;
                    yield return contentPart.Text;
                }
            }
        }

        _logger.LogInformation("Streamed {ChunkCount} chunks for query", chunkCount);
    }
}
