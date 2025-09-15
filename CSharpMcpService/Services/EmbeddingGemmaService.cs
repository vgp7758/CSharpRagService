using System.Text.Json;
using Microsoft.Extensions.Logging;
using CSharpMcpService.Models;

namespace CSharpMcpService.Services;

public class EmbeddingGemmaService : IEmbeddingService
{
    private readonly ILogger<EmbeddingGemmaService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _modelEndpoint;
    private readonly int _embeddingDimension = 384;
    private bool _isInitialized = false;

    public EmbeddingGemmaService(ILogger<EmbeddingGemmaService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _modelEndpoint = "http://localhost:8000/embed"; // Python inference server
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing EmbeddingGemma service...");

            // Check if Python inference server is available
            try
            {
                var response = await _httpClient.GetAsync($"{_modelEndpoint}/health");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("EmbeddingGemma server not available, falling back to simple embedding");
                    return;
                }
            }
            catch
            {
                _logger.LogWarning("Could not connect to EmbeddingGemma server, falling back to simple embedding");
                return;
            }

            _isInitialized = true;
            _logger.LogInformation("EmbeddingGemma service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing EmbeddingGemma service");
            _isInitialized = false;
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (!_isInitialized)
        {
            // Fallback to simple embedding
            return await GenerateSimpleEmbeddingAsync(text);
        }

        try
        {
            var request = new
            {
                texts = new[] { text },
                normalize = true
            };

            var content = JsonSerializer.Serialize(request);
            var response = await _httpClient.PostAsync(_modelEndpoint,
                new StringContent(content, System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"EmbeddingGemma server returned {response.StatusCode}, falling back to simple embedding");
                return await GenerateSimpleEmbeddingAsync(text);
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseContent);

            if (result?.Embeddings?.FirstOrDefault() == null)
            {
                _logger.LogWarning("No embeddings returned from EmbeddingGemma, falling back to simple embedding");
                return await GenerateSimpleEmbeddingAsync(text);
            }

            return result.Embeddings.First().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding with EmbeddingGemma, falling back to simple embedding");
            return await GenerateSimpleEmbeddingAsync(text);
        }
    }

    public async Task<List<float[]>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        if (!_isInitialized)
        {
            // Fallback to simple embedding
            var fallbackResults = await Task.WhenAll(texts.Select(GenerateSimpleEmbeddingAsync));
            return fallbackResults.ToList();
        }

        try
        {
            var request = new
            {
                texts = texts,
                normalize = true
            };

            var content = JsonSerializer.Serialize(request);
            var response = await _httpClient.PostAsync(_modelEndpoint,
                new StringContent(content, System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"EmbeddingGemma batch server returned {response.StatusCode}, falling back to simple embedding");
                var fallbackResults = await Task.WhenAll(texts.Select(GenerateSimpleEmbeddingAsync));
                return fallbackResults.ToList();
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseContent);

            if (result?.Embeddings == null || result.Embeddings.Count != texts.Count)
            {
                _logger.LogWarning("Invalid batch embedding response from EmbeddingGemma, falling back to simple embedding");
                var fallbackResults = await Task.WhenAll(texts.Select(GenerateSimpleEmbeddingAsync));
                return fallbackResults.ToList();
            }

            return result.Embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating batch embeddings with EmbeddingGemma, falling back to simple embedding");
            var fallbackResults = await Task.WhenAll(texts.Select(GenerateSimpleEmbeddingAsync));
            return fallbackResults.ToList();
        }
    }

    private async Task<float[]> GenerateSimpleEmbeddingAsync(string text)
    {
        // Fallback to the simple hash-based embedding
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));

        var embedding = new float[_embeddingDimension];

        for (int i = 0; i < _embeddingDimension; i++)
        {
            int hashIndex = i % hash.Length;
            embedding[i] = (float)hash[hashIndex] / 255.0f * 2.0f - 1.0f;
        }

        // Normalize
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }

    private class EmbeddingResponse
    {
        public List<float[]>? Embeddings { get; set; }
        public string? Model { get; set; }
        public double? InferenceTime { get; set; }
    }
}