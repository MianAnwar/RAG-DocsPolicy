namespace PolicyRAG.Api.Interfaces;

public interface ITokenizer
{
    int CountTokens(string text);
    List<int> Encode(string text);
    string Decode(List<int> tokens);
}
