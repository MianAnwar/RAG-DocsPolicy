using PolicyRAG.Api.Interfaces;

namespace PolicyRAG.Api.Services;

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
