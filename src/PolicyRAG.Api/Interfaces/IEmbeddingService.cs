namespace PolicyRAG.Api.Interfaces;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, IProgress<int>? progress = null);
}
