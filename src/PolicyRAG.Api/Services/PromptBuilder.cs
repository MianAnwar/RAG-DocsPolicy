using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;
using System.Text;

namespace PolicyRAG.Api.Services;

/// <summary>
/// Constructs prompts for RAG-based question answering with token budget management
/// </summary>
public class PromptBuilder : IPromptBuilder
{
    private readonly ITokenizer _tokenizer;
    private readonly ILogger<PromptBuilder> _logger;
    
    private const string SystemPromptTemplate = @"You are a helpful assistant that answers questions based on corporate policy documents.

IMPORTANT RULES:
1. Answer ONLY based on the provided context below
2. If the context doesn't contain enough information to answer the question, say so clearly
3. Always cite your sources using the [Source N] format
4. Be concise but comprehensive
5. If you're uncertain, acknowledge the uncertainty
6. Do not make up information not present in the context

Your goal is to provide accurate, policy-compliant answers.";

    public PromptBuilder(ITokenizer tokenizer, ILogger<PromptBuilder> logger)
    {
        _tokenizer = tokenizer;
        _logger = logger;
    }

    public (string SystemPrompt, string UserMessage) BuildPrompt(
        string userQuery, 
        IReadOnlyList<SearchResult> retrievedChunks,
        int maxContextTokens = 8000)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            throw new ArgumentException("User query cannot be empty", nameof(userQuery));
        }

        _logger.LogInformation("Building prompt for query with {ChunkCount} retrieved chunks", retrievedChunks.Count);

        // Build context from chunks with token budget
        var context = BuildContextWithTokenBudget(retrievedChunks, maxContextTokens);
        
        // Construct user message with context and query
        var userMessage = BuildUserMessage(context, userQuery);
        
        _logger.LogInformation(
            "Built prompt with {ContextTokens} context tokens, {TotalTokens} total tokens",
            _tokenizer.CountTokens(context),
            _tokenizer.CountTokens(SystemPromptTemplate + userMessage));

        return (SystemPromptTemplate, userMessage);
    }

    private string BuildContextWithTokenBudget(IReadOnlyList<SearchResult> chunks, int maxTokens)
    {
        var contextBuilder = new StringBuilder();
        var currentTokens = 0;
        var includedChunks = 0;

        foreach (var chunk in chunks.OrderByDescending(c => c.Score))
        {
            var sourceHeader = $"\n[Source {includedChunks + 1}]\n";
            var documentInfo = $"Document: {chunk.DocumentTitle}\n";
            var sectionInfo = !string.IsNullOrEmpty(chunk.Section)
                ? $"Section: {chunk.Section}\n"
                : "";
            var pageInfo = chunk.PageNumber > 0
                ? $"Page: {chunk.PageNumber}\n"
                : "";
            var content = $"Content: {chunk.Content}\n";

            var chunkText = sourceHeader + documentInfo + sectionInfo + pageInfo + content;
            var chunkTokens = _tokenizer.CountTokens(chunkText);

            if (currentTokens + chunkTokens > maxTokens)
            {
                _logger.LogWarning(
                    "Reached token budget limit. Including {IncludedChunks} of {TotalChunks} chunks",
                    includedChunks,
                    chunks.Count);
                break;
            }

            contextBuilder.Append(chunkText);
            currentTokens += chunkTokens;
            includedChunks++;
        }

        _logger.LogInformation(
            "Built context with {IncludedChunks} chunks using {Tokens} tokens",
            includedChunks,
            currentTokens);

        return contextBuilder.ToString();
    }

    private string BuildUserMessage(string context, string userQuery)
    {
        return $@"CONTEXT:
{context}

QUESTION:
{userQuery}

Please answer the question based on the context provided above. Remember to cite your sources using [Source N] notation.";
    }
}
