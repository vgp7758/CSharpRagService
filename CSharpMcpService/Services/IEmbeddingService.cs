namespace CSharpMcpService.Services;

public interface IEmbeddingService
{
    Task InitializeAsync();
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<List<float[]>> GenerateEmbeddingsBatchAsync(List<string> texts);
}