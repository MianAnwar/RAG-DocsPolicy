namespace PolicyRAG.Api.Interfaces;

public interface IDocumentParser
{
    bool CanParse(string fileExtension);
    Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName);
}
