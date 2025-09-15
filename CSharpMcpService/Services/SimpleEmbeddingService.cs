using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CSharpMcpService.Services;

public class SimpleEmbeddingService : IEmbeddingService
{
    private readonly ILogger<SimpleEmbeddingService> _logger;
    private const int EmbeddingDimension = 384;

    public SimpleEmbeddingService(ILogger<SimpleEmbeddingService> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        _logger.LogInformation("Initializing simple embedding service");
        return Task.CompletedTask;
    }

    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var embedding = CreateSimpleEmbedding(text);
        return Task.FromResult(embedding);
    }

    public Task<List<float[]>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        var embeddings = texts.Select(CreateSimpleEmbedding).ToList();
        return Task.FromResult(embeddings);
    }

    private float[] CreateSimpleEmbedding(string text)
    {
        // Simple hash-based embedding generation
        // In production, replace with a proper embedding model
        var embedding = new float[EmbeddingDimension];

        if (string.IsNullOrWhiteSpace(text))
            return embedding;

        // Use hash function to generate deterministic embeddings
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));

        // Convert hash to normalized float array
        for (int i = 0; i < EmbeddingDimension; i++)
        {
            int hashIndex = i % hash.Length;
            embedding[i] = (float)hash[hashIndex] / 255.0f * 2.0f - 1.0f; // Normalize to [-1, 1]
        }

        // Add some text-based features
        var words = text.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordHash = ComputeWordHash(words);

        for (int i = 0; i < Math.Min(EmbeddingDimension, wordHash.Length); i++)
        {
            embedding[i] = (embedding[i] + wordHash[i]) / 2.0f;
        }

        // Normalize the embedding
        NormalizeEmbedding(embedding);

        return embedding;
    }

    private byte[] ComputeWordHash(string[] words)
    {
        using var sha256 = SHA256.Create();
        var combinedWords = string.Join(" ", words);
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(combinedWords));
    }

    private void NormalizeEmbedding(float[] embedding)
    {
        var magnitude = 0.0f;

        for (int i = 0; i < embedding.Length; i++)
        {
            magnitude += embedding[i] * embedding[i];
        }

        magnitude = MathF.Sqrt(magnitude);

        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }
    }
}