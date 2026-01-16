public record ParsedDocument(
    string Content,
    string FileName,
    int PageCount,
    Dictionary<int, string> PageContents,  // Page number -> content
    Dictionary<string, string> Metadata
);
