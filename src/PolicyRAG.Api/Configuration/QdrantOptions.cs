namespace PolicyRAG.Api.Configuration;

public class QdrantOptions
{
    public const string SectionName = "Qdrant";
    
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public string CollectionName { get; set; } = "policy_documents";
    public ulong VectorSize { get; set; } = 1536;
    public string? ApiKey { get; set; }
}
