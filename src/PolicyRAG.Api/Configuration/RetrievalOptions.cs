namespace PolicyRAG.Api.Configuration;

public class RetrievalOptions
{
    public const string SectionName = "Retrieval";
    
    public int DefaultTopK { get; set; } = 5;
    public float ScoreThreshold { get; set; } = 0.7f;
    public float MmrDiversity { get; set; } = 0.3f;
}
