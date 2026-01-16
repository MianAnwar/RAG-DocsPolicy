using System.Text;
using System.Text.RegularExpressions;
using PolicyRAG.Api.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PolicyRAG.Api.Services.Parsers;

public class PdfDocumentParser : IDocumentParser
{
    public bool CanParse(string fileExtension) 
        => fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName)
    {
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);
        var pageContents = new Dictionary<int, string>();
        var fullContent = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            // ContentOrderTextExtractor preserves reading order - best for RAG
            var text = ContentOrderTextExtractor.GetText(page);
            var cleanedText = CleanText(text);
            
            pageContents[page.Number] = cleanedText;
            fullContent.AppendLine(cleanedText);
        }

        return new ParsedDocument(
            Content: fullContent.ToString(),
            FileName: fileName,
            PageCount: document.NumberOfPages,
            PageContents: pageContents,
            Metadata: new Dictionary<string, string>
            {
                ["Author"] = document.Information?.Author ?? "",
                ["Title"] = document.Information?.Title ?? fileName,
                ["CreatedDate"] = document.Information?.CreationDate?.ToString() ?? ""
            }
        );
    }

    private string CleanText(string text)
    {
        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ");
        // Remove page numbers/headers (customize based on your documents)
        text = Regex.Replace(text, @"Page \d+ of \d+", "");
        return text.Trim();
    }
}
