using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace CSharpMcpService.Services;

public class CachingEmbeddingService : IEmbeddingService
{
    private readonly ILogger<CachingEmbeddingService> _logger;
    private readonly IEmbeddingService _innerService;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration;

    public CachingEmbeddingService(
        ILogger<CachingEmbeddingService> logger,
        IEmbeddingService innerService,
        IMemoryCache cache,
        TimeSpan? cacheDuration = null)
    {
        _logger = logger;
        _innerService = innerService;
        _cache = cache;
        _cacheDuration = cacheDuration ?? TimeSpan.FromHours(1);
    }

    public async Task InitializeAsync()
    {
        await _innerService.InitializeAsync();
        _logger.LogInformation("Caching embedding service initialized");
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        try
        {
            // 生成缓存键
            var cacheKey = $"embedding_{GetHash(text)}";

            // 尝试从缓存获取
            if (_cache.TryGetValue(cacheKey, out float[]? cachedEmbedding) && cachedEmbedding != null)
            {
                _logger.LogDebug("Cache hit for text of length {Length}", text.Length);
                return cachedEmbedding;
            }

            // 缓存未命中，生成新的嵌入
            _logger.LogDebug("Cache miss for text of length {Length}", text.Length);
            var embedding = await _innerService.GenerateEmbeddingAsync(text);

            // 缓存结果
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration,
                Size = 1 // 用于缓存大小管理
            };

            _cache.Set(cacheKey, embedding, cacheOptions);

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating cached embedding");
            throw;
        }
    }

    public async Task<List<float[]>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        try
        {
            var results = new List<float[]>(texts.Count);
            var uncachedTexts = new List<string>();
            var uncachedIndices = new List<int>();

            // 检查缓存
            for (int i = 0; i < texts.Count; i++)
            {
                var text = texts[i];
                var cacheKey = $"embedding_{GetHash(text)}";

                if (_cache.TryGetValue(cacheKey, out float[]? cachedEmbedding) && cachedEmbedding != null)
                {
                    results.Add(cachedEmbedding);
                }
                else
                {
                    results.Add(null!); // 占位符
                    uncachedTexts.Add(text);
                    uncachedIndices.Add(i);
                }
            }

            // 批量生成未缓存的嵌入
            if (uncachedTexts.Any())
            {
                _logger.LogDebug("Generating {Count} uncached embeddings", uncachedTexts.Count);
                var uncachedEmbeddings = await _innerService.GenerateEmbeddingsBatchAsync(uncachedTexts);

                // 更新结果和缓存
                for (int i = 0; i < uncachedEmbeddings.Count; i++)
                {
                    var embedding = uncachedEmbeddings[i];
                    var originalIndex = uncachedIndices[i];
                    var text = uncachedTexts[i];

                    results[originalIndex] = embedding;

                    // 缓存新结果
                    var cacheKey = $"embedding_{GetHash(text)}";
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _cacheDuration,
                        Size = 1
                    };

                    _cache.Set(cacheKey, embedding, cacheOptions);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating cached batch embeddings");
            throw;
        }
    }

    // 获取缓存统计
    public CacheStatistics GetCacheStatistics()
    {
        // 这是一个简化的统计方法，实际应用中可能需要更复杂的跟踪
        return new CacheStatistics
        {
            TotalRequests = 0, // 需要实现请求计数
            CacheHits = 0,      // 需要实现命中计数
            CacheMisses = 0     // 需要实现未命中计数
            // HitRate 是计算属性，不需要设置
        };
    }

    // 清除缓存
    public void ClearCache()
    {
        _cache.Remove("embedding_*");
        _logger.LogInformation("Embedding cache cleared");
    }

    private string GetHash(string text)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(hash);
    }
}

public class CacheStatistics
{
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double HitRate => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0.0;
}