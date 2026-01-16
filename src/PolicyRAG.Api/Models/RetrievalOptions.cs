namespace PolicyRAG.Api.Models;

public class RetrievalOptions
{
    public int TopK { get; set; } = 5;
    public float ScoreThreshold { get; set; } = 0.7f;
    public string? DepartmentFilter { get; set; }
}
