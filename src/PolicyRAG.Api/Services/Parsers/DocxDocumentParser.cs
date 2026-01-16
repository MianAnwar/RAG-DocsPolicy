using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PolicyRAG.Api.Interfaces;

namespace PolicyRAG.Api.Services.Parsers;

public class DocxDocumentParser : IDocumentParser
{
    public bool CanParse(string fileExtension) 
        => fileExtension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName)
    {
        await Task.CompletedTask; // DocumentFormat.OpenXml is synchronous
        
        using var document = WordprocessingDocument.Open(fileStream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        
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
