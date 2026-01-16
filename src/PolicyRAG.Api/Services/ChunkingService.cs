using System.Text;
using Microsoft.Extensions.Options;
using PolicyRAG.Api.Configuration;
using PolicyRAG.Api.Interfaces;
using PolicyRAG.Api.Models;

namespace PolicyRAG.Api.Services;

public class ChunkingService
{
    private readonly ChunkingOptions _options;
    private readonly ITokenizer _tokenizer;

    public ChunkingService(IOptions<ChunkingOptions> options, ITokenizer tokenizer)
    {
        _options = options.Value;
        _tokenizer = tokenizer;
    }

    public List<DocumentChunk> ChunkDocument(ParsedDocument document, Guid documentId, DocumentMetadata metadata)
    {
        var chunks = new List<DocumentChunk>();
        int globalChunkIndex = 0;

        foreach (var (pageNumber, pageContent) in document.PageContents)
        {
            var pageChunks = SplitRecursively(pageContent, _options.Separators, 0);
            
            foreach (var chunkContent in pageChunks)
            {
                if (string.IsNullOrWhiteSpace(chunkContent)) continue;
                
                chunks.Add(new DocumentChunk
                {
                    DocumentId = documentId,
                    Content = chunkContent.Trim(),
                    ChunkIndex = globalChunkIndex++,
                    PageNumber = pageNumber,
                    TokenCount = _tokenizer.CountTokens(chunkContent),
                    DocumentTitle = metadata.Title,
                    Department = metadata.Department,
                    Section = ExtractSection(chunkContent),
                    UploadDate = DateTime.UtcNow
                });
            }
        }

        return chunks;
    }

    private List<string> SplitRecursively(string text, string[] separators, int separatorIndex)
    {
        if (separatorIndex >= separators.Length)
        {
            // Base case: split by character count
            return SplitByTokens(text);
        }

        var separator = separators[separatorIndex];
        var parts = text.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var part in parts)
        {
            var partTokens = _tokenizer.CountTokens(part);
            var currentTokens = _tokenizer.CountTokens(currentChunk.ToString());

            if (currentTokens + partTokens <= _options.MaxChunkSize)
            {
                if (currentChunk.Length > 0)
                    currentChunk.Append(separator);
                currentChunk.Append(part);
            }
            else
            {
                if (currentChunk.Length > 0)
                {
                    result.Add(currentChunk.ToString());
                    
                    // Add overlap from previous chunk
                    var overlapText = GetOverlapText(currentChunk.ToString());
                    currentChunk.Clear();
                    currentChunk.Append(overlapText);
                    currentChunk.Append(separator);
                }
                
                // If single part exceeds max, recurse with next separator
                if (partTokens > _options.MaxChunkSize)
                {
                    result.AddRange(SplitRecursively(part, separators, separatorIndex + 1));
                }
                else
                {
                    currentChunk.Append(part);
                }
            }
        }

        if (currentChunk.Length > 0)
            result.Add(currentChunk.ToString());

        return result;
    }

    private string GetOverlapText(string text)
    {
        var tokens = _tokenizer.Encode(text);
        if (tokens.Count <= _options.ChunkOverlap)
            return text;
            
        var overlapTokens = tokens.TakeLast(_options.ChunkOverlap).ToList();
        return _tokenizer.Decode(overlapTokens);
    }

    private List<string> SplitByTokens(string text)
    {
        var tokens = _tokenizer.Encode(text);
        var chunks = new List<string>();
        
        for (int i = 0; i < tokens.Count; i += _options.MaxChunkSize - _options.ChunkOverlap)
        {
            var chunkTokens = tokens.Skip(i).Take(_options.MaxChunkSize).ToList();
            chunks.Add(_tokenizer.Decode(chunkTokens));
        }
        
        return chunks;
    }

    private string ExtractSection(string content)
    {
        // Simple heuristic: first line if it looks like a heading
        var firstLine = content.Split('\n').FirstOrDefault()?.Trim() ?? "";
        if (firstLine.Length < 100 && (firstLine.EndsWith(":") || char.IsUpper(firstLine.FirstOrDefault())))
            return firstLine;
        return "";
    }
}
