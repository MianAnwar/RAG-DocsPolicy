# RAG Application Implementation Plan
## Policy Document Q&A System

**Tech Stack:** Qdrant (Vector DB) | .NET Core API (Backend) | Angular (Frontend)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ANGULAR FRONTEND                                   │
│  ┌───────────────┐  ┌─────────────────┐  ┌────────────────────────────────┐ │
│  │   Chat UI     │  │  File Upload    │  │     Document Browser           │ │
│  │  (Streaming)  │  │  (Drag & Drop)  │  │   (List/Status/Delete)         │ │
│  └───────────────┘  └─────────────────┘  └────────────────────────────────┘ │
└────────────────────────────────┬────────────────────────────────────────────┘
                                 │ HTTP / SSE (Server-Sent Events)
┌────────────────────────────────▼────────────────────────────────────────────┐
│                          .NET CORE WEB API                                   │
│  ┌───────────────┐  ┌─────────────────┐  ┌────────────────────────────────┐ │
│  │ ChatController│  │DocumentController│  │     SearchController          │ │
│  │  POST /chat   │  │ POST /upload    │  │     POST /search              │ │
│  │  GET /stream  │  │ GET /documents  │  │                                │ │
│  └───────┬───────┘  └────────┬────────┘  └──────────────┬─────────────────┘ │
│          │                   │                          │                    │
│  ┌───────▼───────────────────▼──────────────────────────▼─────────────────┐ │
│  │                      RAG SERVICE LAYER                                  │ │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌──────────────────┐  │ │
│  │  │ Document   │  │ Chunking   │  │ Embedding  │  │ Prompt           │  │ │
│  │  │ Parser     │  │ Service    │  │ Service    │  │ Builder          │  │ │
│  │  └────────────┘  └────────────┘  └────────────┘  └──────────────────┘  │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
└──────────────┬────────────────────────────────────────────┬─────────────────┘
               │                                            │
       ┌───────▼───────┐                           ┌────────▼────────┐
       │    QDRANT     │                           │  AZURE OPENAI   │
       │  Vector Store │                           │    / OpenAI     │
       │  (localhost:  │                           │                 │
       │   6333/6334)  │                           │  - Embeddings   │
       │               │                           │  - Chat (GPT-4o)│
       └───────────────┘                           └─────────────────┘
```

---

## Phase 1: Project Scaffolding & Infrastructure Setup

### 1.1 .NET Core Web API Project

```bash
# Create solution and API project
dotnet new sln -n PolicyRAG
dotnet new webapi -n PolicyRAG.Api
dotnet sln add PolicyRAG.Api

# Add required NuGet packages
cd PolicyRAG.Api
dotnet add package Qdrant.Client --version 1.16.1
dotnet add package OpenAI --version 2.8.0
dotnet add package Azure.AI.OpenAI --version 2.1.0
dotnet add package PdfPig --version 0.1.13
dotnet add package DocumentFormat.OpenXml --version 3.4.1
dotnet add package Microsoft.SemanticKernel --version 1.39.0
```

### 1.2 Angular Frontend Project

```bash
# Create Angular project
ng new policy-rag-ui --routing --style=scss
cd policy-rag-ui

# Add dependencies
npm install @angular/material @angular/cdk
npm install marked --save  # For markdown rendering
```

### 1.3 Qdrant Docker Setup

```yaml
# docker-compose.yml
version: '3.8'
services:
  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"  # REST API
      - "6334:6334"  # gRPC
    volumes:
      - qdrant_storage:/qdrant/storage
    environment:
      - QDRANT__SERVICE__GRPC_PORT=6334

volumes:
  qdrant_storage:
```

```bash
docker-compose up -d
```

### 1.4 Configuration (appsettings.json)

```json
{
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "CollectionName": "policy_documents",
    "VectorSize": 1536
  },
  "OpenAI": {
    "ApiKey": "${OPENAI_API_KEY}",
    "EmbeddingModel": "text-embedding-3-small",
    "ChatModel": "gpt-4o"
  },
  "AzureOpenAI": {
    "Endpoint": "${AZURE_OPENAI_ENDPOINT}",
    "ApiKey": "${AZURE_OPENAI_API_KEY}",
    "EmbeddingDeployment": "text-embedding-3-small",
    "ChatDeployment": "gpt-4o"
  }
}
```

---

## Phase 2: Document Ingestion & Preprocessing Pipeline

### 2.1 Document Parser Interface

```csharp
// Interfaces/IDocumentParser.cs
public interface IDocumentParser
{
    bool CanParse(string fileExtension);
    Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName);
}

public record ParsedDocument(
    string Content,
    string FileName,
    int PageCount,
    Dictionary<int, string> PageContents,  // Page number -> content
    Dictionary<string, string> Metadata
);
```

### 2.2 PDF Parser Implementation (PdfPig)

```csharp
// Services/Parsers/PdfDocumentParser.cs
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

public class PdfDocumentParser : IDocumentParser
{
    public bool CanParse(string fileExtension) 
        => fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName)
    {
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);
        var pageContents = new Dictionary<int, string>();
        var fullContent = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            // ContentOrderTextExtractor preserves reading order - best for RAG
            var text = ContentOrderTextExtractor.GetText(page);
            var cleanedText = CleanText(text);
            
            pageContents[page.Number] = cleanedText;
            fullContent.AppendLine(cleanedText);
        }

        return new ParsedDocument(
            Content: fullContent.ToString(),
            FileName: fileName,
            PageCount: document.NumberOfPages,
            PageContents: pageContents,
            Metadata: new Dictionary<string, string>
            {
                ["Author"] = document.Information?.Author ?? "",
                ["Title"] = document.Information?.Title ?? fileName,
                ["CreatedDate"] = document.Information?.CreationDate?.ToString() ?? ""
            }
        );
    }

    private string CleanText(string text)
    {
        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ");
        // Remove page numbers/headers (customize based on your documents)
        text = Regex.Replace(text, @"Page \d+ of \d+", "");
        return text.Trim();
    }
}
```

### 2.3 DOCX Parser Implementation

```csharp
// Services/Parsers/DocxDocumentParser.cs
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

public class DocxDocumentParser : IDocumentParser
{
    public bool CanParse(string fileExtension) 
        => fileExtension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName)
    {
        using var document = WordprocessingDocument.Open(fileStream, false);
        var body = document.MainDocumentPart?.Document.Body;
        
        if (body == null)
            throw new InvalidOperationException("Document body is empty");

        var content = new StringBuilder();
        var pageContents = new Dictionary<int, string>();
        int currentPage = 1;
        var currentPageContent = new StringBuilder();

        foreach (var element in body.Elements())
        {
            if (element is Paragraph paragraph)
            {
                var text = paragraph.InnerText;
                
                // Check for page breaks
                if (paragraph.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page))
                {
                    pageContents[currentPage] = currentPageContent.ToString().Trim();
                    currentPage++;
                    currentPageContent.Clear();
                }
                
                content.AppendLine(text);
                currentPageContent.AppendLine(text);
            }
        }
        
        // Add last page
        pageContents[currentPage] = currentPageContent.ToString().Trim();

        var coreProps = document.PackageProperties;
        
        return new ParsedDocument(
            Content: content.ToString(),
            FileName: fileName,
            PageCount: currentPage,
            PageContents: pageContents,
            Metadata: new Dictionary<string, string>
            {
                ["Author"] = coreProps.Creator ?? "",
                ["Title"] = coreProps.Title ?? fileName,
                ["CreatedDate"] = coreProps.Created?.ToString() ?? ""
            }
        );
    }
}
```

### 2.4 Document Parser Factory

```csharp
// Services/DocumentParserFactory.cs
public class DocumentParserFactory
{
    private readonly IEnumerable<IDocumentParser> _parsers;

    public DocumentParserFactory(IEnumerable<IDocumentParser> parsers)
    {
        _parsers = parsers;
    }

    public IDocumentParser GetParser(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return _parsers.FirstOrDefault(p => p.CanParse(extension))
            ?? throw new NotSupportedException($"File type {extension} is not supported");
    }
}
```

---

## Phase 3: Chunking Strategy & Metadata Design

### 3.1 Chunk Configuration

```csharp
// Configuration/ChunkingOptions.cs
public class ChunkingOptions
{
    public int MaxChunkSize { get; set; } = 512;      // tokens
    public int ChunkOverlap { get; set; } = 50;       // tokens
    public int MinChunkSize { get; set; } = 100;      // tokens
    public string[] Separators { get; set; } = new[] { "\n\n", "\n", ". ", " " };
}
```

### 3.2 Document Chunk Model

```csharp
// Models/DocumentChunk.cs
public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int PageNumber { get; set; }
    public int StartCharIndex { get; set; }
    public int EndCharIndex { get; set; }
    public int TokenCount { get; set; }
    
    // Metadata for filtering
    public string DocumentTitle { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    
    // Embedding (populated after embedding generation)
    public float[]? Embedding { get; set; }
}
```

### 3.3 Recursive Text Chunking Service

```csharp
// Services/ChunkingService.cs
public class ChunkingService
{
    private readonly ChunkingOptions _options;
    private readonly ITokenizer _tokenizer;  // e.g., TiktokenSharp

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
```

---

## Phase 4: Embedding Generation & Batch Processing

### 4.1 Embedding Service Interface

```csharp
// Interfaces/IEmbeddingService.cs
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, IProgress<int>? progress = null);
}
```

### 4.2 OpenAI Embedding Service

```csharp
// Services/OpenAIEmbeddingService.cs
using OpenAI.Embeddings;

public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly int _batchSize = 100;
    private readonly int _rateLimitDelayMs = 100;

    public OpenAIEmbeddingService(IConfiguration configuration)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        var model = configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        _client = new EmbeddingClient(model, apiKey);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var embedding = await _client.GenerateEmbeddingAsync(text);
        return embedding.Value.ToFloats().ToArray();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(
        List<string> texts, 
        IProgress<int>? progress = null)
    {
        var results = new List<float[]>();
        var processed = 0;

        for (int i = 0; i < texts.Count; i += _batchSize)
        {
            var batch = texts.Skip(i).Take(_batchSize).ToList();
            
            try
            {
                var embeddings = await _client.GenerateEmbeddingsAsync(batch);
                results.AddRange(embeddings.Value.Select(e => e.ToFloats().ToArray()));
            }
            catch (Exception ex) when (IsRateLimitError(ex))
            {
                // Exponential backoff
                await Task.Delay(_rateLimitDelayMs * 2);
                i -= _batchSize; // Retry this batch
                continue;
            }

            processed += batch.Count;
            progress?.Report(processed);
            
            // Rate limiting
            await Task.Delay(_rateLimitDelayMs);
        }

        return results;
    }

    private bool IsRateLimitError(Exception ex) 
        => ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
}
```

### 4.3 Azure OpenAI Embedding Service (Alternative)

```csharp
// Services/AzureOpenAIEmbeddingService.cs
using Azure.AI.OpenAI;
using Azure.Identity;

public class AzureOpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;

    public AzureOpenAIEmbeddingService(IConfiguration configuration)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"];
        var deploymentName = configuration["AzureOpenAI:EmbeddingDeployment"];
        
        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint!),
            new DefaultAzureCredential());  // Uses managed identity in production
            
        _client = azureClient.GetEmbeddingClient(deploymentName);
    }

    // Same implementation as OpenAIEmbeddingService
    // ...
}
```

---

## Phase 5: Qdrant Vector Storage

### 5.1 Qdrant Service Interface

```csharp
// Interfaces/IVectorStoreService.cs
public interface IVectorStoreService
{
    Task InitializeCollectionAsync();
    Task UpsertChunksAsync(List<DocumentChunk> chunks);
    Task DeleteByDocumentIdAsync(Guid documentId);
    Task<List<SearchResult>> SearchAsync(float[] queryVector, SearchOptions options);
}

public class SearchOptions
{
    public int TopK { get; set; } = 5;
    public float ScoreThreshold { get; set; } = 0.7f;
    public string? DepartmentFilter { get; set; }
    public Guid? DocumentIdFilter { get; set; }
    public bool UseMmr { get; set; } = true;
    public float MmrDiversity { get; set; } = 0.3f;
}

public class SearchResult
{
    public Guid ChunkId { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
}
```

### 5.2 Qdrant Vector Store Implementation

```csharp
// Services/QdrantVectorStoreService.cs
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

public class QdrantVectorStoreService : IVectorStoreService
{
    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly ulong _vectorSize;

    public QdrantVectorStoreService(IConfiguration configuration)
    {
        var host = configuration["Qdrant:Host"] ?? "localhost";
        var port = int.Parse(configuration["Qdrant:Port"] ?? "6334");
        _collectionName = configuration["Qdrant:CollectionName"] ?? "policy_documents";
        _vectorSize = ulong.Parse(configuration["Qdrant:VectorSize"] ?? "1536");

        _client = new QdrantClient(host, port);
    }

    public async Task InitializeCollectionAsync()
    {
        var collections = await _client.ListCollectionsAsync();
        
        if (!collections.Contains(_collectionName))
        {
            await _client.CreateCollectionAsync(_collectionName, new VectorParams
            {
                Size = _vectorSize,
                Distance = Distance.Cosine
            });

            // Create payload indexes for filtering
            await _client.CreatePayloadIndexAsync(
                _collectionName, 
                "document_id", 
                PayloadSchemaType.Keyword);
                
            await _client.CreatePayloadIndexAsync(
                _collectionName, 
                "department", 
                PayloadSchemaType.Keyword);
                
            await _client.CreatePayloadIndexAsync(
                _collectionName, 
                "upload_date", 
                PayloadSchemaType.Datetime);
        }
    }

    public async Task UpsertChunksAsync(List<DocumentChunk> chunks)
    {
        var points = chunks.Select(chunk => new PointStruct
        {
            Id = new PointId { Uuid = chunk.Id.ToString() },
            Vectors = chunk.Embedding!,
            Payload =
            {
                ["document_id"] = chunk.DocumentId.ToString(),
                ["content"] = chunk.Content,
                ["chunk_index"] = chunk.ChunkIndex,
                ["page_number"] = chunk.PageNumber,
                ["document_title"] = chunk.DocumentTitle,
                ["section"] = chunk.Section,
                ["department"] = chunk.Department,
                ["upload_date"] = chunk.UploadDate.ToString("O"),
                ["token_count"] = chunk.TokenCount
            }
        }).ToList();

        // Batch upsert in groups of 100
        const int batchSize = 100;
        for (int i = 0; i < points.Count; i += batchSize)
        {
            var batch = points.Skip(i).Take(batchSize).ToList();
            await _client.UpsertAsync(_collectionName, batch);
        }
    }

    public async Task DeleteByDocumentIdAsync(Guid documentId)
    {
        await _client.DeleteAsync(
            _collectionName,
            Match("document_id", documentId.ToString()));
    }

    public async Task<List<SearchResult>> SearchAsync(float[] queryVector, SearchOptions options)
    {
        // Build filter conditions
        var conditions = new List<Condition>();
        
        if (!string.IsNullOrEmpty(options.DepartmentFilter))
        {
            conditions.Add(Match("department", options.DepartmentFilter));
        }
        
        if (options.DocumentIdFilter.HasValue)
        {
            conditions.Add(Match("document_id", options.DocumentIdFilter.Value.ToString()));
        }

        Filter? filter = conditions.Count > 0 
            ? new Filter { Must = { conditions } } 
            : null;

        // Two-stage search: broader initial retrieval + MMR for diversity
        var candidateLimit = options.UseMmr ? options.TopK * 10 : options.TopK;
        
        var searchResults = await _client.SearchAsync(
            _collectionName,
            queryVector,
            filter: filter,
            limit: (ulong)candidateLimit,
            scoreThreshold: options.ScoreThreshold);

        var results = searchResults.Select(r => new SearchResult
        {
            ChunkId = Guid.Parse(r.Id.Uuid),
            Content = r.Payload["content"].StringValue,
            Score = r.Score,
            DocumentTitle = r.Payload["document_title"].StringValue,
            PageNumber = (int)r.Payload["page_number"].IntegerValue,
            Section = r.Payload["section"].StringValue
        }).ToList();

        // Apply MMR for diversity
        if (options.UseMmr && results.Count > options.TopK)
        {
            results = ApplyMmr(results, queryVector, options.TopK, options.MmrDiversity);
        }

        return results.Take(options.TopK).ToList();
    }

    private List<SearchResult> ApplyMmr(
        List<SearchResult> candidates, 
        float[] queryVector, 
        int topK, 
        float diversityWeight)
    {
        // Maximal Marginal Relevance for result diversity
        var selected = new List<SearchResult>();
        var remaining = candidates.ToList();

        while (selected.Count < topK && remaining.Count > 0)
        {
            SearchResult? best = null;
            float bestScore = float.MinValue;

            foreach (var candidate in remaining)
            {
                float relevance = candidate.Score;
                float redundancy = selected.Count > 0
                    ? selected.Max(s => ComputeSimilarity(s.Content, candidate.Content))
                    : 0;

                float mmrScore = (1 - diversityWeight) * relevance - diversityWeight * redundancy;

                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    best = candidate;
                }
            }

            if (best != null)
            {
                selected.Add(best);
                remaining.Remove(best);
            }
        }

        return selected;
    }

    private float ComputeSimilarity(string text1, string text2)
    {
        // Simple Jaccard similarity for redundancy check
        var words1 = text1.ToLower().Split(' ').ToHashSet();
        var words2 = text2.ToLower().Split(' ').ToHashSet();
        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();
        return union > 0 ? (float)intersection / union : 0;
    }
}
```

### 5.3 Collection Schema Summary

```
Collection: policy_documents
├── Vector: float[1536], Cosine distance
└── Payload:
    ├── document_id: keyword (indexed)
    ├── content: text
    ├── chunk_index: integer
    ├── page_number: integer
    ├── document_title: text
    ├── section: text
    ├── department: keyword (indexed)
    ├── upload_date: datetime (indexed)
    └── token_count: integer
```

---

## Phase 6: Query-Time Retrieval Logic

### 6.1 Retrieval Service

```csharp
// Services/RetrievalService.cs
public class RetrievalService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStoreService _vectorStore;

    public RetrievalService(IEmbeddingService embeddingService, IVectorStoreService vectorStore)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
    }

    public async Task<List<SearchResult>> RetrieveContextAsync(
        string query, 
        RetrievalOptions? options = null)
    {
        options ??= new RetrievalOptions();

        // 1. Generate query embedding
        var queryVector = await _embeddingService.GenerateEmbeddingAsync(query);

        // 2. Search vector store with MMR
        var searchOptions = new SearchOptions
        {
            TopK = options.TopK,
            ScoreThreshold = options.ScoreThreshold,
            DepartmentFilter = options.DepartmentFilter,
            UseMmr = true,
            MmrDiversity = 0.3f
        };

        var results = await _vectorStore.SearchAsync(queryVector, searchOptions);

        // 3. Filter and rank results
        return results
            .Where(r => r.Score >= options.ScoreThreshold)
            .OrderByDescending(r => r.Score)
            .Take(options.TopK)
            .ToList();
    }
}

public class RetrievalOptions
{
    public int TopK { get; set; } = 5;
    public float ScoreThreshold { get; set; } = 0.7f;
    public string? DepartmentFilter { get; set; }
}
```

---

## Phase 7: Prompt Construction & LLM Integration

### 7.1 Prompt Builder

```csharp
// Services/PromptBuilder.cs
public class PromptBuilder
{
    private const int MaxContextTokens = 8000;  // Leave room for response
    private readonly ITokenizer _tokenizer;

    public PromptBuilder(ITokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }

    public (string SystemPrompt, string UserPrompt) BuildRAGPrompt(
        string query, 
        List<SearchResult> contexts)
    {
        var systemPrompt = @"You are a helpful assistant that answers questions about company policies.

IMPORTANT RULES:
1. Answer ONLY based on the provided context from policy documents
2. If the context doesn't contain enough information to answer, say: ""I couldn't find specific information about this in the policy documents.""
3. Always cite your sources using [Source N] format
4. Be precise and professional
5. If multiple policies conflict, mention all relevant policies
6. Do not make up information or policies that aren't in the context";

        // Build context string with token budget
        var contextBuilder = new StringBuilder();
        int currentTokens = 0;

        for (int i = 0; i < contexts.Count; i++)
        {
            var source = contexts[i];
            var sourceText = $"\n[Source {i + 1}: {source.DocumentTitle}, Page {source.PageNumber}]\n{source.Content}\n";
            var sourceTokens = _tokenizer.CountTokens(sourceText);

            if (currentTokens + sourceTokens > MaxContextTokens)
                break;

            contextBuilder.Append(sourceText);
            currentTokens += sourceTokens;
        }

        var userPrompt = $@"Context from Policy Documents:
{contextBuilder}

Question: {query}

Please provide a comprehensive answer based on the context above, citing sources where applicable.";

        return (systemPrompt, userPrompt);
    }
}
```

### 7.2 Chat Completion Service

```csharp
// Services/ChatCompletionService.cs
using OpenAI.Chat;

public class ChatCompletionService
{
    private readonly ChatClient _client;
    private readonly PromptBuilder _promptBuilder;
    private readonly RetrievalService _retrievalService;

    public ChatCompletionService(
        IConfiguration configuration,
        PromptBuilder promptBuilder,
        RetrievalService retrievalService)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        var model = configuration["OpenAI:ChatModel"] ?? "gpt-4o";
        _client = new ChatClient(model, apiKey);
        _promptBuilder = promptBuilder;
        _retrievalService = retrievalService;
    }

    public async Task<ChatResponse> GetResponseAsync(string query, string? departmentFilter = null)
    {
        // 1. Retrieve relevant context
        var contexts = await _retrievalService.RetrieveContextAsync(query, new RetrievalOptions
        {
            TopK = 5,
            DepartmentFilter = departmentFilter
        });

        if (contexts.Count == 0)
        {
            return new ChatResponse
            {
                Answer = "I couldn't find any relevant policy documents to answer your question.",
                Sources = new List<SourceReference>()
            };
        }

        // 2. Build prompt
        var (systemPrompt, userPrompt) = _promptBuilder.BuildRAGPrompt(query, contexts);

        // 3. Get LLM response
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var completion = await _client.CompleteChatAsync(messages);
        var answer = completion.Value.Content[0].Text;

        return new ChatResponse
        {
            Answer = answer,
            Sources = contexts.Select((c, i) => new SourceReference
            {
                Index = i + 1,
                DocumentTitle = c.DocumentTitle,
                PageNumber = c.PageNumber,
                Content = c.Content,
                Score = c.Score
            }).ToList()
        };
    }

    public async IAsyncEnumerable<string> GetStreamingResponseAsync(
        string query, 
        string? departmentFilter = null)
    {
        var contexts = await _retrievalService.RetrieveContextAsync(query, new RetrievalOptions
        {
            TopK = 5,
            DepartmentFilter = departmentFilter
        });

        var (systemPrompt, userPrompt) = _promptBuilder.BuildRAGPrompt(query, contexts);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        await foreach (var update in _client.CompleteChatStreamingAsync(messages))
        {
            if (update.ContentUpdate.Count > 0)
            {
                yield return update.ContentUpdate[0].Text;
            }
        }
    }
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<SourceReference> Sources { get; set; } = new();
}

public class SourceReference
{
    public int Index { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
}
```

---

## Phase 8: Backend API Design

### 8.1 Document Controller

```csharp
// Controllers/DocumentsController.cs
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestionService;
    private readonly IDocumentRepository _documentRepository;

    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]  // 50MB limit
    public async Task<IActionResult> Upload([FromForm] List<IFormFile> files)
    {
        var results = new List<DocumentUploadResult>();

        foreach (var file in files)
        {
            var allowedExtensions = new[] { ".pdf", ".docx", ".txt" };
            var extension = Path.GetExtension(file.FileName).ToLower();
            
            if (!allowedExtensions.Contains(extension))
            {
                results.Add(new DocumentUploadResult
                {
                    FileName = file.FileName,
                    Success = false,
                    Error = $"File type {extension} not supported"
                });
                continue;
            }

            try
            {
                var documentId = await _ingestionService.IngestDocumentAsync(
                    file.OpenReadStream(), 
                    file.FileName);

                results.Add(new DocumentUploadResult
                {
                    FileName = file.FileName,
                    Success = true,
                    DocumentId = documentId
                });
            }
            catch (Exception ex)
            {
                results.Add(new DocumentUploadResult
                {
                    FileName = file.FileName,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return Ok(results);
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var documents = await _documentRepository.GetDocumentsAsync(page, pageSize);
        return Ok(documents);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDocument(Guid id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        if (document == null)
            return NotFound();
        return Ok(document);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        await _ingestionService.DeleteDocumentAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/reprocess")]
    public async Task<IActionResult> ReprocessDocument(Guid id)
    {
        await _ingestionService.ReprocessDocumentAsync(id);
        return Accepted();
    }
}
```

### 8.2 Chat Controller

```csharp
// Controllers/ChatController.cs
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ChatCompletionService _chatService;

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required");

        var response = await _chatService.GetResponseAsync(
            request.Message, 
            request.DepartmentFilter);

        return Ok(response);
    }

    [HttpGet("stream")]
    public async Task StreamChat([FromQuery] string message, [FromQuery] string? department = null)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Add("Cache-Control", "no-cache");
        Response.Headers.Add("Connection", "keep-alive");

        await foreach (var chunk in _chatService.GetStreamingResponseAsync(message, department))
        {
            var data = $"data: {JsonSerializer.Serialize(new { content = chunk })}\n\n";
            await Response.WriteAsync(data);
            await Response.Body.FlushAsync();
        }

        await Response.WriteAsync("event: done\ndata: {}\n\n");
    }
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? DepartmentFilter { get; set; }
}
```

### 8.3 Search Controller

```csharp
// Controllers/SearchController.cs
[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly RetrievalService _retrievalService;

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        var results = await _retrievalService.RetrieveContextAsync(
            request.Query,
            new RetrievalOptions
            {
                TopK = request.TopK ?? 10,
                ScoreThreshold = request.ScoreThreshold ?? 0.5f,
                DepartmentFilter = request.DepartmentFilter
            });

        return Ok(new SearchResponse
        {
            Results = results,
            TotalCount = results.Count
        });
    }
}

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TopK { get; set; }
    public float? ScoreThreshold { get; set; }
    public string? DepartmentFilter { get; set; }
}
```

### 8.4 API Request/Response Models Summary

```
POST /api/documents/upload
├── Request: multipart/form-data { files: File[] }
└── Response: DocumentUploadResult[]

GET /api/documents
├── Query: ?page=1&pageSize=20
└── Response: { documents: Document[], totalCount: number }

DELETE /api/documents/{id}
└── Response: 204 No Content

POST /api/chat
├── Request: { message: string, departmentFilter?: string }
└── Response: { answer: string, sources: SourceReference[] }

GET /api/chat/stream
├── Query: ?message={query}&department={filter}
└── Response: SSE stream with { content: string } chunks

POST /api/search
├── Request: { query: string, topK?: number, scoreThreshold?: number }
└── Response: { results: SearchResult[], totalCount: number }
```

---

## Phase 9: Angular Frontend Implementation

### 9.1 Project Structure

```
src/app/
├── core/
│   ├── services/
│   │   ├── chat.service.ts
│   │   ├── document.service.ts
│   │   └── api.interceptor.ts
│   └── models/
│       ├── chat.model.ts
│       └── document.model.ts
├── features/
│   ├── chat/
│   │   ├── chat.component.ts
│   │   ├── chat.component.html
│   │   ├── message-list/
│   │   └── chat-input/
│   └── documents/
│       ├── document-list/
│       ├── document-upload/
│       └── document-viewer/
└── shared/
    ├── components/
    └── pipes/
```

### 9.2 Chat Service with Streaming

```typescript
// core/services/chat.service.ts
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, Subject } from 'rxjs';
import { ChatResponse, ChatMessage } from '../models/chat.model';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private http = inject(HttpClient);
  private apiUrl = '/api/chat';

  // Standard HTTP request
  sendMessage(message: string, department?: string): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(this.apiUrl, {
      message,
      departmentFilter: department
    });
  }

  // Server-Sent Events for streaming
  streamMessage(message: string, department?: string): Observable<string> {
    return new Observable(observer => {
      const params = new URLSearchParams({ message });
      if (department) params.append('department', department);
      
      const eventSource = new EventSource(`${this.apiUrl}/stream?${params}`);
      
      eventSource.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          observer.next(data.content);
        } catch {
          observer.next(event.data);
        }
      };

      eventSource.addEventListener('done', () => {
        eventSource.close();
        observer.complete();
      });

      eventSource.onerror = (error) => {
        eventSource.close();
        observer.error(error);
      };

      return () => eventSource.close();
    });
  }
}
```

### 9.3 Document Service

```typescript
// core/services/document.service.ts
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEventType, HttpProgressEvent } from '@angular/common/http';
import { Observable, filter, map } from 'rxjs';
import { Document, UploadProgress } from '../models/document.model';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private http = inject(HttpClient);
  private apiUrl = '/api/documents';

  uploadDocuments(files: File[]): Observable<UploadProgress> {
    const formData = new FormData();
    files.forEach(file => formData.append('files', file, file.name));

    return this.http.post(this.apiUrl + '/upload', formData, {
      reportProgress: true,
      observe: 'events'
    }).pipe(
      filter(event => 
        event.type === HttpEventType.UploadProgress || 
        event.type === HttpEventType.Response
      ),
      map(event => {
        if (event.type === HttpEventType.UploadProgress) {
          const progress = event as HttpProgressEvent;
          return {
            status: 'uploading' as const,
            progress: Math.round((progress.loaded / (progress.total || 1)) * 100)
          };
        }
        return {
          status: 'complete' as const,
          progress: 100,
          results: (event as any).body
        };
      })
    );
  }

  getDocuments(page = 1, pageSize = 20): Observable<{ documents: Document[], totalCount: number }> {
    return this.http.get<{ documents: Document[], totalCount: number }>(
      this.apiUrl,
      { params: { page, pageSize } }
    );
  }

  deleteDocument(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  reprocessDocument(id: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${id}/reprocess`, {});
  }
}
```

### 9.4 Chat Component

```typescript
// features/chat/chat.component.ts
import { Component, inject, signal } from '@angular/core';
import { ChatService } from '../../core/services/chat.service';
import { ChatMessage, SourceReference } from '../../core/models/chat.model';

@Component({
  selector: 'app-chat',
  templateUrl: './chat.component.html',
  styleUrls: ['./chat.component.scss']
})
export class ChatComponent {
  private chatService = inject(ChatService);

  messages = signal<ChatMessage[]>([]);
  currentStreamedResponse = signal('');
  isLoading = signal(false);
  selectedDepartment = signal<string | undefined>(undefined);

  sendMessage(content: string): void {
    if (!content.trim() || this.isLoading()) return;

    // Add user message
    this.messages.update(msgs => [...msgs, {
      role: 'user',
      content,
      timestamp: new Date()
    }]);

    this.isLoading.set(true);
    this.currentStreamedResponse.set('');

    // Use streaming for real-time response
    this.chatService.streamMessage(content, this.selectedDepartment()).subscribe({
      next: (chunk) => {
        this.currentStreamedResponse.update(current => current + chunk);
      },
      error: (error) => {
        console.error('Stream error:', error);
        this.messages.update(msgs => [...msgs, {
          role: 'assistant',
          content: 'Sorry, an error occurred. Please try again.',
          timestamp: new Date()
        }]);
        this.isLoading.set(false);
      },
      complete: () => {
        // Move streamed response to messages
        const finalResponse = this.currentStreamedResponse();
        this.messages.update(msgs => [...msgs, {
          role: 'assistant',
          content: finalResponse,
          timestamp: new Date()
        }]);
        this.currentStreamedResponse.set('');
        this.isLoading.set(false);
      }
    });
  }
}
```

### 9.5 Chat Component Template

```html
<!-- features/chat/chat.component.html -->
<div class="chat-container">
  <!-- Header with department filter -->
  <div class="chat-header">
    <h2>Policy Assistant</h2>
    <mat-form-field appearance="outline">
      <mat-label>Department Filter</mat-label>
      <mat-select [(value)]="selectedDepartment">
        <mat-option [value]="undefined">All Departments</mat-option>
        <mat-option value="HR">HR</mat-option>
        <mat-option value="Legal">Legal</mat-option>
        <mat-option value="Finance">Finance</mat-option>
        <mat-option value="IT">IT</mat-option>
      </mat-select>
    </mat-form-field>
  </div>

  <!-- Message list -->
  <div class="message-list" #messageContainer>
    @for (message of messages(); track $index) {
      <div class="message" [class.user]="message.role === 'user'" [class.assistant]="message.role === 'assistant'">
        <div class="message-content" [innerHTML]="message.content | markdown"></div>
        @if (message.sources?.length) {
          <div class="sources">
            <span class="sources-label">Sources:</span>
            @for (source of message.sources; track source.index) {
              <button mat-stroked-button (click)="showSource(source)">
                [{{ source.index }}] {{ source.documentTitle }} (p.{{ source.pageNumber }})
              </button>
            }
          </div>
        }
      </div>
    }
    
    <!-- Streaming response -->
    @if (currentStreamedResponse()) {
      <div class="message assistant streaming">
        <div class="message-content" [innerHTML]="currentStreamedResponse() | markdown"></div>
        <span class="typing-indicator">●</span>
      </div>
    }

    <!-- Loading indicator -->
    @if (isLoading() && !currentStreamedResponse()) {
      <div class="message assistant loading">
        <mat-spinner diameter="20"></mat-spinner>
        <span>Searching policies...</span>
      </div>
    }
  </div>

  <!-- Input area -->
  <div class="chat-input">
    <mat-form-field appearance="outline" class="full-width">
      <input matInput 
             placeholder="Ask about company policies..." 
             [(ngModel)]="inputMessage"
             (keyup.enter)="sendMessage(inputMessage)"
             [disabled]="isLoading()">
    </mat-form-field>
    <button mat-fab color="primary" 
            (click)="sendMessage(inputMessage)" 
            [disabled]="isLoading() || !inputMessage.trim()">
      <mat-icon>send</mat-icon>
    </button>
  </div>
</div>
```

### 9.6 Document Upload Component

```typescript
// features/documents/document-upload/document-upload.component.ts
import { Component, inject, signal, output } from '@angular/core';
import { DocumentService } from '../../../core/services/document.service';

@Component({
  selector: 'app-document-upload',
  template: `
    <div class="upload-zone" 
         (drop)="onDrop($event)" 
         (dragover)="onDragOver($event)"
         [class.drag-over]="isDragOver()">
      
      <mat-icon>cloud_upload</mat-icon>
      <p>Drag and drop files here or</p>
      <button mat-raised-button color="primary" (click)="fileInput.click()">
        Browse Files
      </button>
      <input #fileInput type="file" multiple 
             accept=".pdf,.docx,.txt" 
             (change)="onFileSelected($event)" 
             hidden>
      <p class="hint">Supported: PDF, DOCX, TXT (Max 50MB)</p>
    </div>

    @if (uploadProgress() > 0) {
      <mat-progress-bar mode="determinate" [value]="uploadProgress()"></mat-progress-bar>
      <p>Uploading: {{ uploadProgress() }}%</p>
    }

    @if (uploadResults().length > 0) {
      <div class="upload-results">
        @for (result of uploadResults(); track result.fileName) {
          <div class="result-item" [class.success]="result.success" [class.error]="!result.success">
            <mat-icon>{{ result.success ? 'check_circle' : 'error' }}</mat-icon>
            <span>{{ result.fileName }}</span>
            @if (result.error) {
              <span class="error-message">{{ result.error }}</span>
            }
          </div>
        }
      </div>
    }
  `
})
export class DocumentUploadComponent {
  private documentService = inject(DocumentService);

  isDragOver = signal(false);
  uploadProgress = signal(0);
  uploadResults = signal<any[]>([]);
  uploadComplete = output<void>();

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver.set(false);
    const files = Array.from(event.dataTransfer?.files || []);
    this.uploadFiles(files);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files || []);
    this.uploadFiles(files);
  }

  private uploadFiles(files: File[]): void {
    if (files.length === 0) return;

    this.uploadProgress.set(0);
    this.uploadResults.set([]);

    this.documentService.uploadDocuments(files).subscribe({
      next: (progress) => {
        this.uploadProgress.set(progress.progress);
        if (progress.status === 'complete' && progress.results) {
          this.uploadResults.set(progress.results);
          this.uploadComplete.emit();
        }
      },
      error: (error) => {
        console.error('Upload error:', error);
        this.uploadProgress.set(0);
      }
    });
  }
}
```

---

## Phase 10: Production Hardening

### 10.1 Authentication & Authorization

```csharp
// Program.cs - Add Azure AD authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PolicyReader", policy => 
        policy.RequireClaim("department"));
    options.AddPolicy("PolicyAdmin", policy => 
        policy.RequireRole("Admin"));
});

// Controller attribute
[Authorize(Policy = "PolicyReader")]
public class ChatController : ControllerBase { }
```

### 10.2 Rate Limiting

```csharp
// Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("chat", config =>
    {
        config.Window = TimeSpan.FromMinutes(1);
        config.PermitLimit = 20;
        config.QueueLimit = 5;
    });
});

// Controller
[EnableRateLimiting("chat")]
public async Task<IActionResult> Chat([FromBody] ChatRequest request) { }
```

### 10.3 Input Validation

```csharp
// Models/ChatRequest.cs
public class ChatRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;
    
    [StringLength(100)]
    public string? DepartmentFilter { get; set; }
}
```

### 10.4 Observability (Serilog + OpenTelemetry)

```csharp
// Program.cs
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.WithProperty("Application", "PolicyRAG")
    .WriteTo.Console()
    .WriteTo.Seq("http://localhost:5341"));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("PolicyRAG.Qdrant")
        .AddOtlpExporter());
```

### 10.5 Health Checks

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<QdrantHealthCheck>("qdrant")
    .AddCheck<OpenAIHealthCheck>("openai");

// Health check implementation
public class QdrantHealthCheck : IHealthCheck
{
    private readonly QdrantClient _client;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ListCollectionsAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Qdrant connection failed", ex);
        }
    }
}
```

---

## Data Flow Summary

```
1. DOCUMENT INGESTION FLOW
   ┌────────────────────────────────────────────────────────────────────────┐
   │  Upload Files → Parse (PDF/DOCX/TXT) → Clean Text → Chunk (512 tokens) │
   │       ↓                                                                 │
   │  Generate Embeddings (batch 100) → Store in Qdrant with Metadata        │
   └────────────────────────────────────────────────────────────────────────┘

2. QUERY FLOW
   ┌────────────────────────────────────────────────────────────────────────┐
   │  User Query → Embed Query → Vector Search (top 50) → MMR Re-rank (top 5)│
   │       ↓                                                                 │
   │  Build Prompt (System + Context + Query) → LLM Response → Stream to UI  │
   └────────────────────────────────────────────────────────────────────────┘
```

---

## Key Configuration Parameters

| Parameter | Recommended Value | Notes |
|-----------|-------------------|-------|
| Chunk Size | 512-1024 tokens | Larger for policy docs with long sections |
| Chunk Overlap | 50-100 tokens | Prevents context loss at boundaries |
| Embedding Model | text-embedding-3-small | Cost-effective, 1536 dimensions |
| Vector Distance | Cosine | Standard for text embeddings |
| Initial Candidates | 50 | For MMR diversity selection |
| Final Top-K | 5 | Balance between context and token budget |
| Score Threshold | 0.7 | Filter low-relevance results |
| MMR Diversity | 0.3 | Slight preference for relevance over diversity |
| Max Context Tokens | 8000 | Leave room for response in GPT-4o |
| Batch Size (Embeddings) | 100 | Balance throughput and rate limits |
| Batch Size (Qdrant Upsert) | 100 | Optimal for network efficiency |

---

## Next Steps

1. **Phase 1-2**: Set up infrastructure and implement document parsing
2. **Phase 3-5**: Implement chunking, embedding, and Qdrant storage
3. **Phase 6-7**: Build retrieval and LLM integration
4. **Phase 8-9**: Complete API and Angular frontend
5. **Phase 10**: Add authentication, rate limiting, and monitoring
6. **Testing**: Integration tests for full RAG pipeline
7. **Deployment**: Containerize with Docker, deploy to Azure/AWS
