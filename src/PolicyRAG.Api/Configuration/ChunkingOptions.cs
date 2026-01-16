namespace PolicyRAG.Api.Configuration;

public class ChunkingOptions
{
    public const string SectionName = "Chunking";
    
    public int MaxChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;
    public int MinChunkSize { get; set; } = 100;
    public string[] Separators { get; set; } = new[] { "\n\n", "\n", ". ", " " };
}
