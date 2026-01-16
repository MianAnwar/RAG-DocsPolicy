using PolicyRAG.Api.Interfaces;
using TiktokenSharp;

namespace PolicyRAG.Api.Services;

public class TiktokenTokenizer : ITokenizer
{
    private readonly TikToken _tikToken;

    public TiktokenTokenizer()
    {
        // Use cl100k_base encoding (used by text-embedding-3-small and GPT-4)
        _tikToken = TikToken.GetEncoding("cl100k_base");
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        return _tikToken.Encode(text).Count;
    }

    public List<int> Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<int>();
            
        return _tikToken.Encode(text);
    }

    public string Decode(List<int> tokens)
    {
        if (tokens == null || tokens.Count == 0)
            return string.Empty;
            
        return _tikToken.Decode(tokens);
    }
}
