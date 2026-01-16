namespace PolicyRAG.Api.Configuration;

public class OpenAIOptions
{
    public const string SectionName = "OpenAI";
    
    public string? ApiKey { get; set; }
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o";
}

public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";
    
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public string ChatDeployment { get; set; } = "gpt-4o";
}
