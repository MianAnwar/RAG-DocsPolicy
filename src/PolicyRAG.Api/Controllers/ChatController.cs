using Microsoft.AspNetCore.Mvc;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;
using PolicyRAG.Api.Models.Requests;
using PolicyRAG.Api.Services;
using System.Text;
using System.Text.Json;

namespace PolicyRAG.Api.Controllers;

/// <summary>
/// Controller for chat/question-answering operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatCompletionService _chatService;
    private readonly RetrievalService _retrievalService;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IChatCompletionService chatService,
        RetrievalService retrievalService,
        IPromptBuilder promptBuilder,
        ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _retrievalService = retrievalService;
        _promptBuilder = promptBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Send a chat message and receive a complete response
    /// </summary>
    /// <param name="request">Chat request with message and optional filters</param>
    /// <returns>Complete chat response with answer and sources</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Processing chat request: {Message}", request.Message);

        try
        {
            // 1. Retrieve relevant context
            var retrievalOptions = new RetrievalOptions
            {
                TopK = 5,
                ScoreThreshold = 0.7f,
                DepartmentFilter = request.DepartmentFilter
            };

            var contexts = await _retrievalService.RetrieveContextAsync(request.Message, retrievalOptions);

            if (contexts.Count == 0)
            {
                _logger.LogWarning("No relevant documents found for query: {Query}", request.Message);
                
                return Ok(new ChatResponse
                {
                    Answer = "I couldn't find any relevant policy documents to answer your question. Please try rephrasing your question or contact your administrator.",
                    Sources = new List<SourceReference>(),
                    IsGrounded = false,
                    Warning = "No relevant documents found"
                });
            }

            // 2. Build prompt with retrieved context
            var (systemPrompt, userMessage) = _promptBuilder.BuildPrompt(request.Message, contexts);

            // 3. Generate response from LLM
            var response = await _chatService.GenerateAnswerAsync(
                request.Message,
                systemPrompt,
                userMessage);

            // 4. Add source references
            var sources = contexts.Select((c, idx) => new SourceReference
            {
                ChunkId = c.ChunkId.ToString(),
                DocumentTitle = c.DocumentTitle,
                Section = c.Section,
                PageNumber = c.PageNumber > 0 ? c.PageNumber : null,
                RelevanceScore = c.Score,
                ContentExcerpt = c.Content.Length > 200 
                    ? c.Content.Substring(0, 200) + "..." 
                    : c.Content
            }).ToList();
            
            response = new ChatResponse
            {
                Answer = response.Answer,
                Sources = sources,
                TokensUsed = response.TokensUsed,
                IsGrounded = response.IsGrounded,
                Warning = response.Warning
            };

            _logger.LogInformation(
                "Generated response for query with {SourceCount} sources, {TokenCount} tokens",
                response.Sources.Count,
                response.TokensUsed);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while processing your request. Please try again.");
        }
    }

    /// <summary>
    /// Send a chat message and receive a streaming response
    /// </summary>
    /// <param name="message">The user's question</param>
    /// <param name="department">Optional department filter</param>
    /// <returns>Server-Sent Events stream with response chunks</returns>
    [HttpGet("stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task StreamChat(
        [FromQuery] string message,
        [FromQuery] string? department = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Message parameter is required");
            return;
        }

        _logger.LogInformation("Processing streaming chat request: {Message}", message);

        // Set up SSE headers
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no"); // Disable nginx buffering

        try
        {
            // 1. Retrieve relevant context
            var retrievalOptions = new RetrievalOptions
            {
                TopK = 5,
                ScoreThreshold = 0.7f,
                DepartmentFilter = department
            };

            var contexts = await _retrievalService.RetrieveContextAsync(message, retrievalOptions);

            // 2. Send sources as first event
            var sourcesEvent = $"event: sources\ndata: {JsonSerializer.Serialize(contexts.Select(c => new
            {
                chunkId = c.ChunkId,
                documentTitle = c.DocumentTitle,
                section = c.Section,
                pageNumber = c.PageNumber,
                score = c.Score
            }))}\n\n";
            
            await Response.WriteAsync(sourcesEvent);
            await Response.Body.FlushAsync();

            if (contexts.Count == 0)
            {
                await Response.WriteAsync("event: error\ndata: {\"message\": \"No relevant documents found\"}\n\n");
                await Response.WriteAsync("event: done\ndata: {}\n\n");
                return;
            }

            // 3. Build prompt
            var (systemPrompt, userMessage) = _promptBuilder.BuildPrompt(message, contexts);

            // 4. Stream response from LLM
            await foreach (var chunk in _chatService.GenerateAnswerStreamAsync(message, systemPrompt, userMessage, HttpContext.RequestAborted))
            {
                var data = $"data: {JsonSerializer.Serialize(new { content = chunk })}\n\n";
                await Response.WriteAsync(data);
                await Response.Body.FlushAsync();
            }

            // 5. Send completion event
            await Response.WriteAsync("event: done\ndata: {}\n\n");
            await Response.Body.FlushAsync();

            _logger.LogInformation("Completed streaming response for query");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming request cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during streaming response");
            await Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { message = "An error occurred" })}\n\n");
        }
    }
}
