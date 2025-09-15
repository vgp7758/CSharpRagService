using Microsoft.Extensions.Logging;
using CSharpMcpService.Models;

namespace CSharpMcpService.Services;

public class VectorDatabase
{
    private readonly ILogger<VectorDatabase> _logger;
    private readonly Dictionary<string, (CodeSymbol Symbol, float[] Embedding)> _vectors = new();
    private readonly object _lock = new();

    public VectorDatabase(ILogger<VectorDatabase> logger)
    {
        _logger = logger;
    }

    public void AddVector(CodeSymbol symbol, float[] embedding)
    {
        lock (_lock)
        {
            _vectors[symbol.Id] = (symbol, embedding);
        }

        _logger.LogDebug("Added vector for symbol: {SymbolId}", symbol.Id);
    }

    public void AddVectors(List<(CodeSymbol Symbol, float[] Embedding)> vectors)
    {
        lock (_lock)
        {
            foreach (var (symbol, embedding) in vectors)
            {
                _vectors[symbol.Id] = (symbol, embedding);
            }
        }

        _logger.LogInformation("Added {Count} vectors to database", vectors.Count);
    }

    public List<SearchResult> Search(string query, float[] queryEmbedding, int topK = 5)
    {
        lock (_lock)
        {
            var results = _vectors.Values
                .Select(item => new SearchResult
                {
                    Symbol = item.Symbol,
                    SimilarityScore = CalculateCosineSimilarity(queryEmbedding, item.Embedding)
                })
                .OrderByDescending(r => r.SimilarityScore)
                .Take(topK)
                .ToList();

            return results;
        }
    }

    public List<SearchResult> SearchByProject(string projectId, float[] queryEmbedding, int topK = 5)
    {
        lock (_lock)
        {
            var results = _vectors.Values
                .Where(item => item.Symbol.Metadata.ContainsKey("project") &&
                             item.Symbol.Metadata["project"].ToString() == projectId)
                .Select(item => new SearchResult
                {
                    Symbol = item.Symbol,
                    SimilarityScore = CalculateCosineSimilarity(queryEmbedding, item.Embedding)
                })
                .OrderByDescending(r => r.SimilarityScore)
                .Take(topK)
                .ToList();

            return results;
        }
    }

    public List<SearchResult> SearchBySymbolType(SymbolKind symbolKind, float[] queryEmbedding, int topK = 5)
    {
        lock (_lock)
        {
            var results = _vectors.Values
                .Where(item => item.Symbol.Kind == symbolKind)
                .Select(item => new SearchResult
                {
                    Symbol = item.Symbol,
                    SimilarityScore = CalculateCosineSimilarity(queryEmbedding, item.Embedding)
                })
                .OrderByDescending(r => r.SimilarityScore)
                .Take(topK)
                .ToList();

            return results;
        }
    }

    public List<SearchResult> AdvancedSearch(
        float[] queryEmbedding,
        string? projectId = null,
        SymbolKind? symbolKind = null,
        string? namespaceFilter = null,
        string? accessibilityFilter = null,
        int topK = 5)
    {
        lock (_lock)
        {
            var query = _vectors.Values.AsQueryable();

            if (projectId != null)
            {
                query = query.Where(item => item.Symbol.Metadata.ContainsKey("project") &&
                                          item.Symbol.Metadata["project"].ToString() == projectId);
            }

            if (symbolKind != null)
            {
                query = query.Where(item => item.Symbol.Kind == symbolKind);
            }

            if (namespaceFilter != null)
            {
                query = query.Where(item => item.Symbol.Namespace.Contains(namespaceFilter));
            }

            if (accessibilityFilter != null)
            {
                query = query.Where(item => item.Symbol.Accessibility == accessibilityFilter);
            }

            var results = query
                .Select(item => new SearchResult
                {
                    Symbol = item.Symbol,
                    SimilarityScore = CalculateCosineSimilarity(queryEmbedding, item.Embedding)
                })
                .OrderByDescending(r => r.SimilarityScore)
                .Take(topK)
                .ToList();

            return results;
        }
    }

    public virtual bool RemoveVector(string symbolId)
    {
        lock (_lock)
        {
            return _vectors.Remove(symbolId);
        }
    }

    public virtual void Clear()
    {
        lock (_lock)
        {
            _vectors.Clear();
        }

        _logger.LogInformation("Vector database cleared");
    }

    public virtual int Count => _vectors.Count;

    public virtual List<CodeSymbol> GetAllSymbols()
    {
        lock (_lock)
        {
            return _vectors.Values.Select(item => item.Symbol).ToList();
        }
    }

    public virtual bool ContainsSymbol(string symbolId)
    {
        lock (_lock)
        {
            return _vectors.ContainsKey(symbolId);
        }
    }

    public virtual CodeSymbol? GetSymbol(string symbolId)
    {
        lock (_lock)
        {
            return _vectors.TryGetValue(symbolId, out var item) ? item.Symbol : null;
        }
    }

    private double CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            return 0.0;

        var dotProduct = 0.0;
        var magnitude1 = 0.0;
        var magnitude2 = 0.0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0.0;

        return dotProduct / (magnitude1 * magnitude2);
    }

    public Dictionary<string, int> GetProjectStatistics()
    {
        lock (_lock)
        {
            var stats = new Dictionary<string, int>();

            foreach (var item in _vectors.Values)
            {
                if (item.Symbol.Metadata.TryGetValue("project", out var projectObj))
                {
                    var projectName = projectObj.ToString() ?? "unknown";
                    stats[projectName] = stats.GetValueOrDefault(projectName) + 1;
                }
            }

            return stats;
        }
    }

    public Dictionary<SymbolKind, int> GetSymbolTypeStatistics()
    {
        lock (_lock)
        {
            var stats = new Dictionary<SymbolKind, int>();

            foreach (var item in _vectors.Values)
            {
                stats[item.Symbol.Kind] = stats.GetValueOrDefault(item.Symbol.Kind) + 1;
            }

            return stats;
        }
    }
}