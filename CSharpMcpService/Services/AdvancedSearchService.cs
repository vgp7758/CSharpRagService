using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using CSharpMcpService.Models;
using SymbolKind = CSharpMcpService.Models.SymbolKind;

namespace CSharpMcpService.Services;

public class AdvancedSearchService
{
    private readonly ILogger<AdvancedSearchService> _logger;
    private readonly VectorDatabase _vectorDatabase;
    private readonly IEmbeddingService _embeddingService;

    public AdvancedSearchService(
        ILogger<AdvancedSearchService> logger,
        VectorDatabase vectorDatabase,
        IEmbeddingService embeddingService)
    {
        _logger = logger;
        _vectorDatabase = vectorDatabase;
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// 基于代码模式搜索
    /// </summary>
    public async Task<List<CodeSymbol>> SearchByPatternAsync(
        string pattern,
        string? projectId = null,
        int topK = 10)
    {
        try
        {
            _logger.LogInformation("Searching by pattern: {Pattern}", pattern);

            var allSymbols = _vectorDatabase.GetAllSymbols();
            var patternSymbols = allSymbols.Where(s => MatchesPattern(s, pattern)).ToList();

            // 按相关性和重要性排序
            var results = patternSymbols
                .OrderByDescending(s => CalculatePatternRelevance(s, pattern))
                .Take(topK)
                .ToList();

            _logger.LogInformation("Pattern search found {Count} results", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in pattern search");
            return new List<CodeSymbol>();
        }
    }

    /// <summary>
    /// 基于依赖关系搜索
    /// </summary>
    public async Task<List<DependencySearchResult>> SearchByDependenciesAsync(
        string symbolName,
        DependencyType dependencyType = DependencyType.All,
        int depth = 2)
    {
        try
        {
            _logger.LogInformation("Searching dependencies for: {SymbolName}, Type: {Type}, Depth: {Depth}",
                symbolName, dependencyType, depth);

            var allSymbols = _vectorDatabase.GetAllSymbols();
            var targetSymbol = allSymbols.FirstOrDefault(s => s.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase));

            if (targetSymbol == null)
            {
                _logger.LogWarning("Target symbol not found: {SymbolName}", symbolName);
                return new List<DependencySearchResult>();
            }

            var dependencies = new List<DependencySearchResult>();
            var visited = new HashSet<string>();

            // 查找依赖关系
            await FindDependenciesRecursive(targetSymbol, dependencyType, depth, dependencies, visited, allSymbols);

            _logger.LogInformation("Dependency search found {Count} results", dependencies.Count);
            return dependencies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in dependency search");
            return new List<DependencySearchResult>();
        }
    }

    /// <summary>
    /// 基于调用链搜索
    /// </summary>
    public async Task<List<CallChainResult>> SearchCallChainAsync(
        string methodName,
        CallChainDirection direction = CallChainDirection.Both,
        int maxDepth = 3)
    {
        try
        {
            _logger.LogInformation("Searching call chain for: {MethodName}, Direction: {Direction}, MaxDepth: {Depth}",
                methodName, direction, maxDepth);

            var allSymbols = _vectorDatabase.GetAllSymbols();
            var methodSymbols = allSymbols.Where(s => s.Kind == SymbolKind.Method).ToList();

            var results = new List<CallChainResult>();

            if (direction == CallChainDirection.Callees || direction == CallChainDirection.Both)
            {
                var callees = await FindCalleesRecursive(methodName, maxDepth, methodSymbols);
                results.AddRange(callees);
            }

            if (direction == CallChainDirection.Callers || direction == CallChainDirection.Both)
            {
                var callers = await FindCallersRecursive(methodName, maxDepth, methodSymbols);
                results.AddRange(callers);
            }

            _logger.LogInformation("Call chain search found {Count} results", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in call chain search");
            return new List<CallChainResult>();
        }
    }

    /// <summary>
    /// 智能代码搜索（结合语义和模式）
    /// </summary>
    public async Task<List<CodeSymbol>> SmartCodeSearchAsync(
        string query,
        SearchOptions options)
    {
        try
        {
            _logger.LogInformation("Smart code search: {Query}", query);

            var results = new List<CodeSymbol>();

            // 1. 语义搜索
            if (options.EnableSemanticSearch)
            {
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                var semanticResults = _vectorDatabase.AdvancedSearch(
                    queryEmbedding,
                    options.ProjectId,
                    options.SymbolType,
                    options.NamespaceFilter,
                    options.AccessibilityFilter,
                    options.TopK);

                results.AddRange(semanticResults.Select(r => r.Symbol));
            }

            // 2. 模式搜索
            if (options.EnablePatternSearch)
            {
                var patternResults = await SearchByPatternAsync(query, options.ProjectId, options.TopK);
                results.AddRange(patternResults);
            }

            // 3. 去重和排序
            var uniqueResults = results
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .OrderByDescending(s => CalculateSearchRelevance(s, query))
                .Take(options.TopK)
                .ToList();

            _logger.LogInformation("Smart search found {Count} unique results", uniqueResults.Count);
            return uniqueResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in smart code search");
            return new List<CodeSymbol>();
        }
    }

    private bool MatchesPattern(CodeSymbol symbol, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;

        var patternLower = pattern.ToLower();

        // 1. 名称匹配
        if (symbol.Name.ToLower().Contains(patternLower))
            return true;

        // 2. 签名匹配
        if (!string.IsNullOrEmpty(symbol.Signature) &&
            symbol.Signature.ToLower().Contains(patternLower))
            return true;

        // 3. 源代码匹配
        if (!string.IsNullOrEmpty(symbol.SourceCode) &&
            symbol.SourceCode.ToLower().Contains(patternLower))
            return true;

        // 4. 命名空间匹配
        if (!string.IsNullOrEmpty(symbol.Namespace) &&
            symbol.Namespace.ToLower().Contains(patternLower))
            return true;

        // 5. 参数类型匹配
        if (symbol.Parameters.Any(p => p.Type.ToLower().Contains(patternLower)))
            return true;

        // 6. 返回类型匹配
        if (!string.IsNullOrEmpty(symbol.ReturnType) &&
            symbol.ReturnType.ToLower().Contains(patternLower))
            return true;

        return false;
    }

    private double CalculatePatternRelevance(CodeSymbol symbol, string pattern)
    {
        var relevance = 0.0;

        var patternLower = pattern.ToLower();

        // 名称完全匹配
        if (symbol.Name.ToLower() == patternLower)
            relevance += 10.0;

        // 名称部分匹配
        if (symbol.Name.ToLower().Contains(patternLower))
            relevance += 5.0;

        // 签名匹配
        if (!string.IsNullOrEmpty(symbol.Signature) &&
            symbol.Signature.ToLower().Contains(patternLower))
            relevance += 3.0;

        // 源代码匹配
        if (!string.IsNullOrEmpty(symbol.SourceCode) &&
            symbol.SourceCode.ToLower().Contains(patternLower))
            relevance += 2.0;

        // 类型权重
        relevance += GetTypeWeight(symbol.Kind);

        return relevance;
    }

    private double GetTypeWeight(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Method => 3.0,
            SymbolKind.Class => 2.5,
            SymbolKind.Interface => 2.0,
            SymbolKind.Property => 1.5,
            SymbolKind.Field => 1.0,
            SymbolKind.Constructor => 2.0,
            _ => 1.0
        };
    }

    private async Task FindDependenciesRecursive(
        CodeSymbol currentSymbol,
        DependencyType dependencyType,
        int remainingDepth,
        List<DependencySearchResult> results,
        HashSet<string> visited,
        List<CodeSymbol> allSymbols)
    {
        if (remainingDepth <= 0 || visited.Contains(currentSymbol.Id))
            return;

        visited.Add(currentSymbol.Id);

        var dependencies = FindDirectDependencies(currentSymbol, dependencyType, allSymbols);

        foreach (var dep in dependencies)
        {
            var result = new DependencySearchResult
            {
                FromSymbol = currentSymbol,
                ToSymbol = dep.Symbol,
                DependencyType = dep.Type,
                Depth = 2 - remainingDepth
            };

            results.Add(result);

            // 递归查找深层依赖
            await FindDependenciesRecursive(dep.Symbol, dependencyType, remainingDepth - 1, results, visited, allSymbols);
        }
    }

    private List<DependencyInfo> FindDirectDependencies(
        CodeSymbol symbol,
        DependencyType dependencyType,
        List<CodeSymbol> allSymbols)
    {
        var dependencies = new List<DependencyInfo>();

        if (string.IsNullOrEmpty(symbol.SourceCode))
            return dependencies;

        var sourceCode = symbol.SourceCode;

        foreach (var otherSymbol in allSymbols)
        {
            if (otherSymbol.Id == symbol.Id)
                continue;

            // 检查类型引用
            if ((dependencyType == DependencyType.All || dependencyType == DependencyType.TypeReference) &&
                sourceCode.Contains(otherSymbol.Name))
            {
                dependencies.Add(new DependencyInfo
                {
                    Symbol = otherSymbol,
                    Type = DependencyType.TypeReference
                });
            }

            // 检查方法调用
            if ((dependencyType == DependencyType.All || dependencyType == DependencyType.MethodCall) &&
                otherSymbol.Kind == SymbolKind.Method &&
                sourceCode.Contains($"{otherSymbol.Name}("))
            {
                dependencies.Add(new DependencyInfo
                {
                    Symbol = otherSymbol,
                    Type = DependencyType.MethodCall
                });
            }

            // 检查继承关系
            if ((dependencyType == DependencyType.All || dependencyType == DependencyType.Inheritance) &&
                symbol.Kind == SymbolKind.Class &&
                symbol.ParentClass == otherSymbol.Name)
            {
                dependencies.Add(new DependencyInfo
                {
                    Symbol = otherSymbol,
                    Type = DependencyType.Inheritance
                });
            }
        }

        return dependencies;
    }

    private async Task<List<CallChainResult>> FindCalleesRecursive(
        string methodName,
        int remainingDepth,
        List<CodeSymbol> allSymbols)
    {
        var results = new List<CallChainResult>();

        if (remainingDepth <= 0)
            return results;

        var methodSymbol = allSymbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Method &&
            s.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

        if (methodSymbol == null || string.IsNullOrEmpty(methodSymbol.SourceCode))
            return results;

        // 查找被调用的方法
        var callees = FindMethodCalls(methodSymbol.SourceCode, allSymbols);

        foreach (var callee in callees)
        {
            var result = new CallChainResult
            {
                Caller = methodSymbol,
                Callee = callee,
                Depth = 3 - remainingDepth,
                Direction = CallChainDirection.Callees
            };

            results.Add(result);

            // 递归查找更深层的调用
            var deeperCallees = await FindCalleesRecursive(callee.Name, remainingDepth - 1, allSymbols);
            results.AddRange(deeperCallees);
        }

        return results;
    }

    private async Task<List<CallChainResult>> FindCallersRecursive(
        string methodName,
        int remainingDepth,
        List<CodeSymbol> allSymbols)
    {
        var results = new List<CallChainResult>();

        if (remainingDepth <= 0)
            return results;

        var targetMethod = allSymbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Method &&
            s.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

        if (targetMethod == null)
            return results;

        // 查找调用该方法的其他方法
        var callers = FindMethodCallers(targetMethod.Name, allSymbols);

        foreach (var caller in callers)
        {
            var result = new CallChainResult
            {
                Caller = caller,
                Callee = targetMethod,
                Depth = 3 - remainingDepth,
                Direction = CallChainDirection.Callers
            };

            results.Add(result);

            // 递归查找更上层的调用者
            var deeperCallers = await FindCallersRecursive(caller.Name, remainingDepth - 1, allSymbols);
            results.AddRange(deeperCallers);
        }

        return results;
    }

    private List<CodeSymbol> FindMethodCalls(string sourceCode, List<CodeSymbol> allSymbols)
    {
        var calls = new List<CodeSymbol>();

        // 使用正则表达式匹配方法调用
        var methodCallPattern = @"(\w+)\s*\(";
        var matches = Regex.Matches(sourceCode, methodCallPattern);

        foreach (Match match in matches)
        {
            var methodName = match.Groups[1].Value;
            var methodSymbol = allSymbols.FirstOrDefault(s =>
                s.Kind == SymbolKind.Method &&
                s.Name == methodName);

            if (methodSymbol != null)
            {
                calls.Add(methodSymbol);
            }
        }

        return calls;
    }

    private List<CodeSymbol> FindMethodCallers(string methodName, List<CodeSymbol> allSymbols)
    {
        var callers = new List<CodeSymbol>();

        foreach (var symbol in allSymbols)
        {
            if (symbol.Kind == SymbolKind.Method && !string.IsNullOrEmpty(symbol.SourceCode))
            {
                if (symbol.SourceCode.Contains($"{methodName}("))
                {
                    callers.Add(symbol);
                }
            }
        }

        return callers;
    }

    private double CalculateSearchRelevance(CodeSymbol symbol, string query)
    {
        var relevance = 0.0;
        var queryLower = query.ToLower();

        // 名称匹配权重
        if (symbol.Name.ToLower().Contains(queryLower))
            relevance += 5.0;

        // 类型权重
        relevance += GetTypeWeight(symbol.Kind);

        // 访问级别权重
        if (symbol.Accessibility == "Public")
            relevance += 1.0;

        return relevance;
    }
}

// 辅助类和枚举
public enum DependencyType
{
    All,
    TypeReference,
    MethodCall,
    Inheritance,
    PropertyAccess
}

public enum CallChainDirection
{
    Callers,    // 调用者
    Callees,    // 被调用者
    Both        // 双向
}

public class DependencySearchResult
{
    public CodeSymbol FromSymbol { get; set; } = null!;
    public CodeSymbol ToSymbol { get; set; } = null!;
    public DependencyType DependencyType { get; set; }
    public int Depth { get; set; }
}

public class CallChainResult
{
    public CodeSymbol Caller { get; set; } = null!;
    public CodeSymbol Callee { get; set; } = null!;
    public int Depth { get; set; }
    public CallChainDirection Direction { get; set; }
}

public class SearchOptions
{
    public bool EnableSemanticSearch { get; set; } = true;
    public bool EnablePatternSearch { get; set; } = true;
    public string? ProjectId { get; set; }
    public SymbolKind? SymbolType { get; set; }
    public string? NamespaceFilter { get; set; }
    public string? AccessibilityFilter { get; set; }
    public int TopK { get; set; } = 10;
}

internal class DependencyInfo
{
    public CodeSymbol Symbol { get; set; } = null!;
    public DependencyType Type { get; set; }
}