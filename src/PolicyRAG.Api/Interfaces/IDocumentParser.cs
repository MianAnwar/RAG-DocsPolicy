namespace PolicyRAG.Api.Interfaces;

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
