using System.Text.Json;
using Microsoft.Extensions.Logging;
using CSharpMcpService.Models;

namespace CSharpMcpService.Services;

public class IncrementalVectorDatabase : VectorDatabase
{
    private readonly ILogger<IncrementalVectorDatabase> _logger;
    private readonly Dictionary<string, (CodeSymbol Symbol, float[] Embedding, DateTime LastModified)> _vectors = new();
    private readonly object _lock = new();
    private readonly string _persistencePath;
    private bool _isDirty = false;

    public IncrementalVectorDatabase(ILogger<IncrementalVectorDatabase> logger, string? persistencePath = null) : base(logger)
    {
        _logger = logger;
        _persistencePath = persistencePath ?? Path.Combine(Directory.GetCurrentDirectory(), "vector_db.json");
    }

    /// <summary>
    /// 添加或更新向量
    /// </summary>
    public void AddOrUpdateVector(CodeSymbol symbol, float[] embedding)
    {
        lock (_lock)
        {
            var fileLastModified = GetFileLastModified(symbol.FilePath);
            var existing = _vectors.TryGetValue(symbol.Id, out var existingValue);

            if (existing)
            {
                // 检查是否需要更新
                if (ShouldUpdateSymbol(existingValue, fileLastModified))
                {
                    _vectors[symbol.Id] = (symbol, embedding, fileLastModified);
                    _isDirty = true;
                    _logger.LogDebug("Updated vector for symbol: {SymbolId}", symbol.Id);
                }
            }
            else
            {
                // 新符号
                _vectors[symbol.Id] = (symbol, embedding, fileLastModified);
                _isDirty = true;
                _logger.LogDebug("Added new vector for symbol: {SymbolId}", symbol.Id);
            }
        }
    }

    /// <summary>
    /// 批量添加或更新向量
    /// </summary>
    public void AddOrUpdateVectors(List<(CodeSymbol Symbol, float[] Embedding)> vectors)
    {
        lock (_lock)
        {
            foreach (var (symbol, embedding) in vectors)
            {
                AddOrUpdateVector(symbol, embedding);
            }

            if (_isDirty)
            {
                _logger.LogInformation("Batch processed {Count} vectors, database now contains {Total} symbols",
                    vectors.Count, _vectors.Count);
            }
        }
    }

    /// <summary>
    /// 增量更新项目
    /// </summary>
    public async Task<UpdateSummary> UpdateProjectAsync(
        string projectId,
        List<CodeSymbol> newSymbols,
        IEmbeddingService embeddingService)
    {
        var summary = new UpdateSummary
        {
            ProjectId = projectId,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // 1. 检查文件修改
            var modifiedFiles = await GetModifiedFilesAsync(projectId);

            // 2. 删除已不存在的文件的符号
            var symbolsToRemove = _vectors.Values
                .Where(v => v.Symbol.Metadata.TryGetValue("project", out var projectObj) &&
                           projectObj.ToString() == projectId &&
                           (modifiedFiles.Contains(v.Symbol.FilePath) || !File.Exists(v.Symbol.FilePath)))
                .Select(v => v.Symbol.Id)
                .ToList();

            foreach (var symbolId in symbolsToRemove)
            {
                RemoveVector(symbolId);
                summary.RemovedSymbols++;
            }

            // 3. 更新修改的文件
            foreach (var filePath in modifiedFiles)
            {
                if (File.Exists(filePath))
                {
                    var fileSymbols = newSymbols.Where(s => s.FilePath == filePath).ToList();
                    var fileEmbeddings = await embeddingService.GenerateEmbeddingsBatchAsync(
                        fileSymbols.Select(s => $"{s.Name} {s.Kind} {s.Signature}").ToList());

                    for (int i = 0; i < fileSymbols.Count; i++)
                    {
                        AddOrUpdateVector(fileSymbols[i], fileEmbeddings[i]);
                        summary.UpdatedSymbols++;
                    }
                }
            }

            // 4. 添加新符号
            var existingSymbolIds = _vectors.Keys
                .Where(id => _vectors[id].Symbol.Metadata.TryGetValue("project", out var projectObj) &&
                             projectObj.ToString() == projectId)
                .ToHashSet();

            var newSymbolsForProject = newSymbols
                .Where(s => !existingSymbolIds.Contains(s.Id))
                .ToList();

            if (newSymbolsForProject.Any())
            {
                var newEmbeddings = await embeddingService.GenerateEmbeddingsBatchAsync(
                    newSymbolsForProject.Select(s => $"{s.Name} {s.Kind} {s.Signature}").ToList());

                for (int i = 0; i < newSymbolsForProject.Count; i++)
                {
                    AddOrUpdateVector(newSymbolsForProject[i], newEmbeddings[i]);
                    summary.AddedSymbols++;
                }
            }

            summary.EndTime = DateTime.UtcNow;
            summary.Success = true;

            _logger.LogInformation("Project update completed: {Added} added, {Updated} updated, {Removed} removed",
                summary.AddedSymbols, summary.UpdatedSymbols, summary.RemovedSymbols);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating project {ProjectId}", projectId);
            summary.Success = false;
            summary.ErrorMessage = ex.Message;
            summary.EndTime = DateTime.UtcNow;
            return summary;
        }
    }

    
    /// <summary>
    /// 获取需要更新的文件列表
    /// </summary>
    private async Task<List<string>> GetModifiedFilesAsync(string projectId)
    {
        var modifiedFiles = new List<string>();

        foreach (var item in _vectors.Values)
        {
            if (item.Symbol.Metadata.TryGetValue("project", out var projectObj) &&
                projectObj.ToString() == projectId)
            {
                var currentLastModified = GetFileLastModified(item.Symbol.FilePath);
                if (currentLastModified > item.LastModified)
                {
                    modifiedFiles.Add(item.Symbol.FilePath);
                }
            }
        }

        return modifiedFiles.Distinct().ToList();
    }

    /// <summary>
    /// 检查符号是否需要更新
    /// </summary>
    private bool ShouldUpdateSymbol(
        (CodeSymbol Symbol, float[] Embedding, DateTime LastModified) existing,
        DateTime fileLastModified)
    {
        // 如果文件被修改，需要更新
        if (fileLastModified > existing.LastModified)
            return true;

        // 检查符号元数据是否变化
        // 这里可以添加更多的变化检测逻辑

        return false;
    }

    /// <summary>
    /// 获取文件最后修改时间
    /// </summary>
    private DateTime GetFileLastModified(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                return File.GetLastWriteTimeUtc(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting last modified time for file: {FilePath}", filePath);
        }

        return DateTime.MinValue;
    }

    /// <summary>
    /// 保存到持久化存储
    /// </summary>
    public async Task SaveAsync()
    {
        if (!_isDirty) return;

        try
        {
            var data = new
            {
                Version = 1,
                LastUpdated = DateTime.UtcNow,
                Vectors = _vectors.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        Symbol = kvp.Value.Symbol,
                        Embedding = kvp.Value.Embedding,
                        LastModified = kvp.Value.LastModified
                    })
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var directory = Path.GetDirectoryName(_persistencePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_persistencePath, json);
            _isDirty = false;

            _logger.LogDebug("Database saved to {Path}", _persistencePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving database to {Path}", _persistencePath);
            throw;
        }
    }

    /// <summary>
    /// 从持久化存储加载
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_persistencePath))
            {
                _logger.LogInformation("No existing database found at {Path}", _persistencePath);
                return;
            }

            var json = await File.ReadAllTextAsync(_persistencePath);
            var data = JsonSerializer.Deserialize<DatabaseData>(json);

            if (data?.Vectors != null)
            {
                _vectors.Clear();
                foreach (var kvp in data.Vectors)
                {
                    _vectors[kvp.Key] = (
                        kvp.Value.Symbol,
                        kvp.Value.Embedding,
                        kvp.Value.LastModified
                    );
                }

                _logger.LogInformation("Loaded {Count} vectors from {Path}", _vectors.Count, _persistencePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading database from {Path}", _persistencePath);
            throw;
        }
    }

    // 重写基础方法以支持增量功能
    public override bool RemoveVector(string symbolId)
    {
        lock (_lock)
        {
            var removed = _vectors.Remove(symbolId);
            if (removed) _isDirty = true;
            return removed;
        }
    }

    public override void Clear()
    {
        lock (_lock)
        {
            _vectors.Clear();
            _isDirty = true;
            _logger.LogInformation("Incremental vector database cleared");
        }
    }

    public override int Count => _vectors.Count;

    public override List<CodeSymbol> GetAllSymbols()
    {
        lock (_lock)
        {
            return _vectors.Values.Select(item => item.Symbol).ToList();
        }
    }

    public override CodeSymbol? GetSymbol(string symbolId)
    {
        lock (_lock)
        {
            return _vectors.TryGetValue(symbolId, out var item) ? item.Symbol : null;
        }
    }

    public override bool ContainsSymbol(string symbolId)
    {
        lock (_lock)
        {
            return _vectors.ContainsKey(symbolId);
        }
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

// 辅助类
public class UpdateSummary
{
    public string ProjectId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int AddedSymbols { get; set; }
    public int UpdatedSymbols { get; set; }
    public int RemovedSymbols { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

internal class DatabaseData
{
    public int Version { get; set; }
    public DateTime LastUpdated { get; set; }
    public Dictionary<string, VectorData>? Vectors { get; set; }
}

internal class VectorData
{
    public CodeSymbol Symbol { get; set; } = null!;
    public float[] Embedding { get; set; } = null!;
    public DateTime LastModified { get; set; }
}