using System.Text.Json;
using System.Linq;
using CSharpMcpService.Models;
using CSharpMcpService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace CSharpMcpService;

public class McpTools
{
    private readonly ILogger<McpTools> _logger;
    private readonly IProjectAnalyzer _projectAnalyzer;
    private readonly VectorDatabase _vectorDatabase;
    private readonly IEmbeddingService _embeddingService;
    private readonly ProjectMonitoringService? _monitoringService;

    public McpTools(
        ILogger<McpTools> logger,
        IProjectAnalyzer projectAnalyzer,
        VectorDatabase vectorDatabase,
        IEmbeddingService embeddingService,
        ProjectMonitoringService? monitoringService = null)
    {
        _logger = logger;
        _projectAnalyzer = projectAnalyzer;
        _vectorDatabase = vectorDatabase;
        _embeddingService = embeddingService;
        _monitoringService = monitoringService;
    }

    [McpTool("analyze_csharp_project", "Analyze a C# project and extract symbols")]
    public async Task<string> AnalyzeCSharpProjectAsync(string projectPath)
    {
        try
        {
            _logger.LogInformation("Analyzing C# project: {ProjectPath}", projectPath);

            if (!File.Exists(projectPath))
            {
                return JsonSerializer.Serialize(new { error = $"Project file not found: {projectPath}" });
            }

            if (!projectPath.EndsWith(".csproj"))
            {
                return JsonSerializer.Serialize(new { error = "File must be a .csproj file" });
            }

            var projectInfo = await _projectAnalyzer.AnalyzeProjectAsync(projectPath);
            var symbols = await _projectAnalyzer.ExtractSymbolsAsync(projectInfo);

            // Add symbols to vector database
            foreach (var symbol in symbols.Where(s => s.Embedding != null))
            {
                _vectorDatabase.AddVector(symbol, symbol.Embedding!);
            }

            var result = new
            {
                project = projectInfo,
                symbolsCount = symbols.Count,
                classes = symbols.Count(s => s.Kind == SymbolKind.Class),
                methods = symbols.Count(s => s.Kind == SymbolKind.Method),
                properties = symbols.Count(s => s.Kind == SymbolKind.Property),
                databaseSize = _vectorDatabase.Count
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing C# project");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("search_csharp_code", "Search C# code using semantic similarity")]
    public async Task<string> SearchCSharpCodeAsync(
        string query,
        int topK = 5,
        string? projectId = null,
        string? symbolType = null,
        string? namespaceFilter = null,
        string? accessibilityFilter = null)
    {
        try
        {
            _logger.LogInformation("Searching C# code: {Query}", query);

            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

            var symbolKind = symbolType != null ? Enum.Parse<SymbolKind>(symbolType, true) : (SymbolKind?)null;

            var results = _vectorDatabase.AdvancedSearch(
                queryEmbedding,
                projectId,
                symbolKind,
                namespaceFilter,
                accessibilityFilter,
                topK);

            var searchResults = results.Select(r => new
            {
                r.Symbol.Name,
                r.Symbol.Kind,
                r.Symbol.Namespace,
                r.Symbol.Accessibility,
                r.Symbol.Signature,
                r.Symbol.FilePath,
                r.Symbol.LineNumber,
                SimilarityScore = Math.Round(r.SimilarityScore, 4),
                Parameters = r.Symbol.Parameters.Select(p => new
                {
                    p.Name,
                    p.Type,
                    p.DefaultValue
                }),
                r.Symbol.Documentation
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                query,
                results = searchResults,
                totalResults = searchResults.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching C# code");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("get_project_info", "Get detailed information about a C# project")]
    public async Task<string> GetProjectInfoAsync(string projectPath)
    {
        try
        {
            _logger.LogInformation("Getting project info: {ProjectPath}", projectPath);

            if (!File.Exists(projectPath))
            {
                return JsonSerializer.Serialize(new { error = $"Project file not found: {projectPath}" });
            }

            var projectInfo = await _projectAnalyzer.AnalyzeProjectAsync(projectPath);

            return JsonSerializer.Serialize(projectInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project info");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("get_symbol_details", "Get detailed information about a specific symbol")]
    public async Task<string> GetSymbolDetailsAsync(string symbolId)
    {
        try
        {
            _logger.LogInformation("Getting symbol details: {SymbolId}", symbolId);

            var symbol = _vectorDatabase.GetSymbol(symbolId);

            if (symbol == null)
            {
                return JsonSerializer.Serialize(new { error = $"Symbol not found: {symbolId}" });
            }

            return JsonSerializer.Serialize(symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbol details");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("get_database_stats", "Get statistics about the vector database")]
    public async Task<string> GetDatabaseStatsAsync()
    {
        try
        {
            _logger.LogInformation("Getting database statistics");

            var projectStats = _vectorDatabase.GetProjectStatistics();
            var symbolTypeStats = _vectorDatabase.GetSymbolTypeStatistics();

            return JsonSerializer.Serialize(new
            {
                totalSymbols = _vectorDatabase.Count,
                projectStats,
                symbolTypeStats
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting database stats");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("clear_database", "Clear all data from the vector database")]
    public async Task<string> ClearDatabaseAsync()
    {
        try
        {
            _logger.LogInformation("Clearing vector database");

            _vectorDatabase.Clear();

            return JsonSerializer.Serialize(new { success = true, message = "Database cleared successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing database");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    public async Task<string> SearchCSharpCodeAsync(
        string query,
        string? projectId = null,
        int topK = 5)
    {
        try
        {
            _logger.LogInformation("Searching for: {Query}, Project: {ProjectId}, TopK: {TopK}",
                query, projectId ?? "all", topK);

            var allSymbols = _vectorDatabase.GetAllSymbols();

            // Filter by project if specified
            var filteredSymbols = string.IsNullOrEmpty(projectId)
                ? allSymbols
                : allSymbols.Where(s => s.Metadata.TryGetValue("project", out var projectObj) &&
                                        projectObj.ToString() == projectId).ToList();

            if (!filteredSymbols.Any())
            {
                return JsonSerializer.Serialize(new {
                    results = new string[0],
                    message = "No symbols found to search"
                });
            }

            // Generate query embedding
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

            // Calculate similarities and get top results
            var results = filteredSymbols
                .Where(s => s.Embedding != null)
                .Select(symbol => new
                {
                    Symbol = symbol,
                    SimilarityScore = CalculateCosineSimilarity(queryEmbedding, symbol.Embedding!)
                })
                .OrderByDescending(r => r.SimilarityScore)
                .Take(topK)
                .Select(r => new
                {
                    r.Symbol.Id,
                    r.Symbol.Name,
                    r.Symbol.Kind,
                    r.Symbol.Namespace,
                    r.Symbol.Accessibility,
                    r.Symbol.FilePath,
                    r.Symbol.LineNumber,
                    r.Symbol.Signature,
                    SimilarityScore = Math.Round(r.SimilarityScore, 4),
                    Summary = GenerateSymbolSummary(r.Symbol)
                })
                .ToList();

            _logger.LogInformation("Search completed. Found {Count} results", results.Count);

            return JsonSerializer.Serialize(new { results });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching code");
            return JsonSerializer.Serialize(new { error = ex.Message });
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

    private string GenerateSymbolSummary(CodeSymbol symbol)
    {
        var parts = new List<string>
        {
            symbol.Kind.ToString(),
            symbol.Name
        };

        if (!string.IsNullOrEmpty(symbol.Namespace))
        {
            parts.Add($"in {symbol.Namespace}");
        }

        if (!string.IsNullOrEmpty(symbol.ReturnType) && symbol.Kind == SymbolKind.Method)
        {
            parts.Add($"returns {symbol.ReturnType}");
        }

        return string.Join(" ", parts);
    }

    [McpTool("list_symbols", "List all symbols in the database with optional filtering")]
    public async Task<string> ListSymbolsAsync(
        string? projectId = null,
        string? symbolType = null,
        string? namespaceFilter = null,
        int limit = 100)
    {
        try
        {
            _logger.LogInformation("Listing symbols");

            var symbols = _vectorDatabase.GetAllSymbols();

            if (projectId != null)
            {
                symbols = symbols.Where(s => s.Metadata.TryGetValue("project", out var projectObj) &&
                                            projectObj.ToString() == projectId).ToList();
            }

            if (symbolType != null)
            {
                var kind = Enum.Parse<SymbolKind>(symbolType, true);
                symbols = symbols.Where(s => s.Kind == kind).ToList();
            }

            if (namespaceFilter != null)
            {
                symbols = symbols.Where(s => s.Namespace.Contains(namespaceFilter)).ToList();
            }

            var limitedSymbols = symbols.Take(limit).ToList();

            var result = limitedSymbols.Select(s => new
            {
                s.Id,
                s.Name,
                s.Kind,
                s.Namespace,
                s.Accessibility,
                s.FilePath,
                s.LineNumber
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                symbols = result,
                totalCount = symbols.Count,
                returnedCount = result.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing symbols");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("compile_csharp_project", "Compile a C# project and check for compilation errors")]
    public async Task<string> CompileCSharpProjectAsync(string projectPath, string? configuration = "Debug")
    {
        try
        {
            _logger.LogInformation("Compiling C# project: {ProjectPath}", projectPath);

            if (!File.Exists(projectPath))
            {
                return JsonSerializer.Serialize(new { error = $"Project file not found: {projectPath}" });
            }

            if (!projectPath.EndsWith(".csproj"))
            {
                return JsonSerializer.Serialize(new { error = "File must be a .csproj file" });
            }

            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            // Start the dotnet build process
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" --configuration {configuration} --verbosity normal",
                WorkingDirectory = projectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var output = new StringBuilder();
            var errorOutput = new StringBuilder();
            var compilationErrors = new List<object>();
            var compilationWarnings = new List<object>();

            using (var process = new Process())
            {
                process.StartInfo = processStartInfo;
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.AppendLine(e.Data);
                        
                        // Parse compilation errors and warnings
                        if (e.Data.Contains("error CS") || e.Data.Contains("error MSB"))
                        {
                            var errorInfo = ParseCompilationMessage(e.Data, "error");
                            if (errorInfo != null)
                                compilationErrors.Add(errorInfo);
                        }
                        else if (e.Data.Contains("warning CS") || e.Data.Contains("warning MSB"))
                        {
                            var warningInfo = ParseCompilationMessage(e.Data, "warning");
                            if (warningInfo != null)
                                compilationWarnings.Add(warningInfo);
                        }
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for the process to complete with a timeout
                var completed = await Task.Run(() => process.WaitForExit(80000)); // 60 second timeout

                if (!completed)
                {
                    process.Kill();
                    return JsonSerializer.Serialize(new
                    {
                        error = "Compilation timeout after 60 seconds",
                        projectPath,
                        configuration
                    });
                }

                var exitCode = process.ExitCode;
                var buildOutput = output.ToString();
                var buildErrors = errorOutput.ToString();

                // Determine compilation status
                var isSuccess = exitCode == 0 && compilationErrors.Count == 0;
                var status = isSuccess ? "Success" : "Failed";

                var result = new
                {
                    projectPath,
                    projectName,
                    configuration,
                    status,
                    exitCode,
                    isSuccess,
                    errorCount = compilationErrors.Count,
                    warningCount = compilationWarnings.Count,
                    errors = compilationErrors,
                    buildTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                _logger.LogInformation("Compilation completed for {ProjectPath}: {Status}", projectPath, status);

                return JsonSerializer.Serialize(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compiling C# project");
            return JsonSerializer.Serialize(new 
            { 
                error = ex.Message,
                projectPath,
                configuration = configuration ?? "Debug"
            });
        }
    }

    private object? ParseCompilationMessage(string message, string messageType)
    {
        try
        {
            // Example format: "Program.cs(10,5): error CS0103: The name 'xyz' does not exist in the current context"
            var parts = message.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;

            var fileAndLocation = parts[0].Trim();
            var errorCode = parts[1].Trim().Split(' ').LastOrDefault();
            var description = string.Join(":", parts.Skip(2)).Trim();

            // Extract file path and line/column info
            var fileMatch = System.Text.RegularExpressions.Regex.Match(fileAndLocation, @"^(.+?)\((\d+),(\d+)\)");
            
            if (fileMatch.Success)
            {
                return new
                {
                    type = messageType,
                    code = errorCode,
                    description,
                    file = fileMatch.Groups[1].Value,
                    line = int.Parse(fileMatch.Groups[2].Value),
                    column = int.Parse(fileMatch.Groups[3].Value),
                    fullMessage = message
                };
            }
            else
            {
                // Fallback for messages without file location
                return new
                {
                    type = messageType,
                    code = errorCode,
                    description,
                    fullMessage = message
                };
            }
        }
        catch
        {
            // If parsing fails, return the raw message
            return new
            {
                type = messageType,
                fullMessage = message
            };
        }
    }

    [McpTool("monitor_csharp_project", "Start monitoring a C# project for automatic incremental updates")]
    public async Task<string> MonitorCSharpProjectAsync(string projectPath)
    {
        try
        {
            if (_monitoringService == null)
            {
                return JsonSerializer.Serialize(new {
                    error = "Project monitoring service is not available. Please ensure IncrementalVectorDatabase is configured."
                });
            }

            if (!File.Exists(projectPath))
            {
                return JsonSerializer.Serialize(new { error = $"Project file not found: {projectPath}" });
            }

            if (!projectPath.EndsWith(".csproj"))
            {
                return JsonSerializer.Serialize(new { error = "File must be a .csproj file" });
            }

            await _monitoringService.AddProjectAsync(projectPath);

            var result = new
            {
                success = true,
                message = $"Project monitoring started for: {projectPath}",
                projectPath,
                monitoringEnabled = true
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting project monitoring");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("stop_monitoring_csharp_project", "Stop monitoring a C# project")]
    public async Task<string> StopMonitoringCSharpProjectAsync(string projectId)
    {
        try
        {
            if (_monitoringService == null)
            {
                return JsonSerializer.Serialize(new {
                    error = "Project monitoring service is not available."
                });
            }

            await _monitoringService.RemoveProjectAsync(projectId);

            var result = new
            {
                success = true,
                message = $"Project monitoring stopped for: {projectId}",
                projectId,
                monitoringEnabled = false
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping project monitoring");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("get_monitoring_status", "Get status of all monitored projects")]
    public async Task<string> GetMonitoringStatusAsync()
    {
        try
        {
            if (_monitoringService == null)
            {
                return JsonSerializer.Serialize(new {
                    error = "Project monitoring service is not available."
                });
            }

            var status = _monitoringService.GetMonitoringStatus();

            var result = new
            {
                monitoringServiceActive = true,
                monitoredProjects = status,
                totalProjects = status.Count,
                activeProjects = status.Values.Count(p => p.IsActive),
                totalPendingChanges = status.Values.Sum(p => p.PendingChangesCount)
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting monitoring status");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpTool("update_csharp_project", "Manually trigger incremental update for a monitored project")]
    public async Task<string> UpdateCSharpProjectAsync(string projectPath)
    {
        try
        {
            if (_vectorDatabase is not IncrementalVectorDatabase incrementalDb)
            {
                return JsonSerializer.Serialize(new {
                    error = "Incremental update not enabled. Please configure IncrementalVectorDatabase."
                });
            }

            if (!File.Exists(projectPath))
            {
                return JsonSerializer.Serialize(new { error = $"Project file not found: {projectPath}" });
            }

            _logger.LogInformation("Manually triggering incremental update for C# project: {ProjectPath}", projectPath);

            // 重新分析项目
            var projectInfo = await _projectAnalyzer.AnalyzeProjectAsync(projectPath);
            var newSymbols = await _projectAnalyzer.ExtractSymbolsAsync(projectInfo);

            // 执行增量更新
            var summary = await incrementalDb.UpdateProjectAsync(projectInfo.Id, newSymbols, _embeddingService);

            var result = new
            {
                projectId = summary.ProjectId,
                success = summary.Success,
                duration = summary.Duration.TotalMilliseconds,
                addedSymbols = summary.AddedSymbols,
                updatedSymbols = summary.UpdatedSymbols,
                removedSymbols = summary.RemovedSymbols,
                totalSymbols = incrementalDb.Count,
                errorMessage = summary.ErrorMessage
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error manually updating C# project");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}