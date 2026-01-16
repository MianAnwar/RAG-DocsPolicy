namespace PolicyRAG.Api.Models;

public class DocumentMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }
}
