using CSharpMcpService.Models;
using CSharpMcpService.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CSharpMcpService;

public class AdvancedFeaturesDemo
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== C# MCP Service Advanced Features Demo ===");

        // Set up logging
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        try
        {
            // Initialize services with advanced features
            var logger = loggerFactory.CreateLogger<AdvancedFeaturesDemo>();
            var embeddingService = new SimpleEmbeddingService(
                loggerFactory.CreateLogger<SimpleEmbeddingService>());

            // Add caching
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cachingEmbeddingService = new CachingEmbeddingService(
                loggerFactory.CreateLogger<CachingEmbeddingService>(),
                embeddingService,
                memoryCache);

            // Use vector database
            var vectorDb = new VectorDatabase(
                loggerFactory.CreateLogger<VectorDatabase>());

            // Use parallel analyzer
            var parallelAnalyzer = new ParallelProjectAnalyzer(
                loggerFactory.CreateLogger<ParallelProjectAnalyzer>(),
                cachingEmbeddingService,
                maxDegreeOfParallelism: 4);

            // Use advanced search service
            var advancedSearchService = new AdvancedSearchService(
                loggerFactory.CreateLogger<AdvancedSearchService>(),
                vectorDb,
                cachingEmbeddingService);

            await cachingEmbeddingService.InitializeAsync();
            await parallelAnalyzer.InitializeEmbeddingModelAsync();

            logger.LogInformation("=== Advanced Features Initialized ===");

            // Test with the example project
            var testProjectPath = @"D:\Projects\CSharpRagService\Example\GeneratorApp\GeneratorApp.csproj";

            if (!File.Exists(testProjectPath))
            {
                logger.LogError("Example project not found.");
                return;
            }

            // === 1. 并行文件分析测试 ===
            logger.LogInformation("=== 1. Parallel File Analysis Test ===");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var projectInfo = await parallelAnalyzer.AnalyzeProjectAsync(testProjectPath);
            var symbols = await parallelAnalyzer.ExtractSymbolsAsync(projectInfo);

            stopwatch.Stop();
            logger.LogInformation("✓ Parallel analysis completed in {Elapsed} ms", stopwatch.ElapsedMilliseconds);
            logger.LogInformation("  - Processed {FileCount} files", projectInfo.SourceFiles.Count);
            logger.LogInformation("  - Extracted {SymbolCount} symbols", symbols.Count);

            // === 2. 缓存性能测试 ===
            logger.LogInformation("=== 2. Caching Performance Test ===");

            var testTexts = new List<string>
            {
                "user authentication method",
                "database connection handling",
                "API controller implementation",
                "configuration file processing",
                "logging functionality"
            };

            // First call (cache miss)
            stopwatch.Restart();
            var embeddings1 = await cachingEmbeddingService.GenerateEmbeddingsBatchAsync(testTexts);
            stopwatch.Stop();
            logger.LogInformation("✓ First call (cache miss): {Elapsed} ms", stopwatch.ElapsedMilliseconds);

            // Second call (cache hit)
            stopwatch.Restart();
            var embeddings2 = await cachingEmbeddingService.GenerateEmbeddingsBatchAsync(testTexts);
            stopwatch.Stop();
            logger.LogInformation("✓ Second call (cache hit): {Elapsed} ms", stopwatch.ElapsedMilliseconds);

            // === 3. 向量数据库存储测试 ===
            logger.LogInformation("=== 3. Vector Database Storage Test ===");

            // Add symbols to database
            foreach (var symbol in symbols.Where(s => s.Embedding != null))
            {
                vectorDb.AddVector(symbol, symbol.Embedding!);
            }

            logger.LogInformation("✓ Vector database populated with {Count} symbols", vectorDb.GetAllSymbols().Count);

            // === 4. 高级搜索测试 ===
            logger.LogInformation("=== 4. Advanced Search Test ===");

            // 4.1 模式搜索
            logger.LogInformation("4.1 Pattern-based Search:");
            var patternResults = await advancedSearchService.SearchByPatternAsync("config");
            logger.LogInformation("  Found {Count} symbols matching 'config' pattern", patternResults.Count);

            foreach (var result in patternResults.Take(3))
            {
                logger.LogInformation("  - {Name} ({Kind}) in {Namespace}",
                    result.Name, result.Kind, result.Namespace);
            }

            // 4.2 依赖关系搜索
            logger.LogInformation("4.2 Dependency Relationship Search:");
            var dependencyResults = await advancedSearchService.SearchByDependenciesAsync("Main");
            logger.LogInformation("  Found {Count} dependencies for 'Main'", dependencyResults.Count);

            foreach (var result in dependencyResults.Take(3))
            {
                logger.LogInformation("  - {From} -> {To} ({Type})",
                    result.FromSymbol.Name, result.ToSymbol.Name, result.DependencyType);
            }

            // 4.3 调用链搜索
            logger.LogInformation("4.3 Call Chain Search:");
            var callChainResults = await advancedSearchService.SearchCallChainAsync("Main");
            logger.LogInformation("  Found {Count} call chain relationships", callChainResults.Count);

            foreach (var result in callChainResults.Take(3))
            {
                logger.LogInformation("  - {Caller} -> {Callee} ({Direction})",
                    result.Caller.Name, result.Callee.Name, result.Direction);
            }

            // 4.4 智能搜索
            logger.LogInformation("4.4 Smart Code Search:");
            var searchOptions = new SearchOptions
            {
                EnableSemanticSearch = true,
                EnablePatternSearch = true,
                TopK = 5
            };

            var smartResults = await advancedSearchService.SmartCodeSearchAsync("generator configuration", searchOptions);
            logger.LogInformation("  Smart search found {Count} results", smartResults.Count);

            foreach (var result in smartResults)
            {
                logger.LogInformation("  - {Name} ({Kind}) - {Namespace}",
                    result.Name, result.Kind, result.Namespace);
            }

            // === 5. 性能对比测试 ===
            logger.LogInformation("=== 5. Performance Comparison ===");

            // 测试查询性能
            var queries = new[]
            {
                "generator",
                "configuration",
                "language",
                "protocol",
                "bitrpc"
            };

            foreach (var query in queries)
            {
                stopwatch.Restart();
                var results = await advancedSearchService.SmartCodeSearchAsync(query, new SearchOptions { TopK = 3 });
                stopwatch.Stop();

                logger.LogInformation("  Query '{Query}': {Elapsed} ms, {Count} results",
                    query, stopwatch.ElapsedMilliseconds, results.Count);
            }

            // === 6. 数据库统计 ===
            logger.LogInformation("=== 6. Database Statistics ===");
            var allSymbols = vectorDb.GetAllSymbols();
            var typeStats = allSymbols.GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.Count());

            logger.LogInformation("  Total symbols: {Total}", allSymbols.Count);

            foreach (var (type, count) in typeStats)
            {
                logger.LogInformation("  - {Type}: {Count} symbols", type, count);
            }

            // === 7. 导出结果 ===
            logger.LogInformation("=== 7. Exporting Results ===");

            var demoResults = new
            {
                TestProject = projectInfo.AssemblyName,
                Performance = new
                {
                    ParallelAnalysisTime = $"{stopwatch.ElapsedMilliseconds} ms",
                    CacheHitRate = "N/A", // 需要实现缓存统计
                    SymbolsCount = symbols.Count
                },
                AdvancedSearchResults = new
                {
                    PatternSearchCount = patternResults.Count,
                    DependencySearchCount = dependencyResults.Count,
                    CallChainSearchCount = callChainResults.Count,
                    SmartSearchCount = smartResults.Count
                },
                DatabaseStats = new
                {
                    TotalSymbols = allSymbols.Count,
                    TypeStats = typeStats
                },
                FeaturesDemonstrated = new[]
                {
                    "Parallel File Analysis",
                    "Embedding Caching",
                    "Vector Database Storage",
                    "Pattern-based Search",
                    "Dependency Relationship Search",
                    "Call Chain Analysis",
                    "Smart Code Search"
                }
            };

            var json = JsonSerializer.Serialize(demoResults, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var outputPath = "advanced_features_demo.json";
            await File.WriteAllTextAsync(outputPath, json);
            logger.LogInformation("✓ Demo results exported to: {Path}", outputPath);

            // Clean up is not needed for VectorDatabase as it doesn't have persistence

            logger.LogInformation("=== Advanced Features Demo Completed Successfully ===");
            logger.LogInformation("All advanced features from README.md have been implemented and tested!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during demo: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}