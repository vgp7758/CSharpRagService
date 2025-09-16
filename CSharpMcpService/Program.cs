using CSharpMcpService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CSharpMcpService;

public class Program
{
    private static string? _defaultProjectName;

    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure logging to stderr to avoid interfering with JSON-RPC on stdout
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StderrLoggerProvider());
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Get configuration
        var useAdvancedEmbedding = args.Contains("advanced-embedding") ||
                                   Environment.GetEnvironmentVariable("USE_ADVANCED_EMBEDDING") == "true";

        // Get default project name from environment or args
        _defaultProjectName = args.FirstOrDefault(a => a.StartsWith("project="))?.Substring("project=".Length);

        // Register services with factory
        builder.Services.AddSingleton<IProjectAnalyzer, SimpleProjectAnalyzer>();
        builder.Services.AddSingleton<IncrementalVectorDatabase>();
        builder.Services.AddSingleton<VectorDatabase>(sp => sp.GetRequiredService<IncrementalVectorDatabase>());
        builder.Services.AddSingleton<McpTools>();

        // Register project monitoring service
        builder.Services.AddSingleton<ProjectMonitoringService>();

        // Register embedding service using factory
        builder.Services.AddSingleton(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return EmbeddingServiceFactory.CreateEmbeddingService(loggerFactory, useAdvancedEmbedding);
        });

        var host = builder.Build();

        // Initialize services
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var embeddingService = host.Services.GetRequiredService<IEmbeddingService>();
        var projectAnalyzer = host.Services.GetRequiredService<IProjectAnalyzer>();

        logger.LogInformation("Starting C# MCP Service");
        logger.LogInformation("Advanced embedding enabled: {Enabled}", useAdvancedEmbedding);
        logger.LogInformation("Default project name: {ProjectName}", _defaultProjectName ?? "Not configured");

        try
        {
            // Initialize embedding service
            await embeddingService.InitializeAsync();
            await projectAnalyzer.InitializeEmbeddingModelAsync();

            logger.LogInformation("C# MCP Service initialized successfully");

            // Start project monitoring service
            var monitoringService = host.Services.GetRequiredService<ProjectMonitoringService>();
            _ = monitoringService.StartMonitoringAsync();

            // Start MCP server
            await RunMcpServerAsync(host.Services, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting C# MCP Service");
            throw;
        }
    }

    private static async Task RunMcpServerAsync(IServiceProvider services, ILogger<Program> logger)
    {
        var mcpTools = services.GetRequiredService<McpTools>();

        logger.LogInformation("C# MCP Server running. Press Ctrl+C to exit.");

        // Simple MCP server implementation using stdin/stdout
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        while (true)
        {
            try
            {
                // Read JSON-RPC request from stdin
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                {
                    await Task.Delay(100);
                    continue;
                }
                logger.LogDebug("Received request: {Request}", line);

                var request = JsonSerializer.Deserialize<JsonElement>(line);
                var method = request.GetProperty("method").GetString();

                object? id = null;
                if (request.TryGetProperty("id", out var idProp))
                {
                    id = idProp.ValueKind switch
                    {
                        JsonValueKind.String => idProp.GetString(),
                        JsonValueKind.Number => idProp.GetInt32(),
                        _ => null
                    };
                }

                object? result = null;
                string? error = null;

                try
                {
                    // Route to appropriate method
                    result = method switch
                    {
                        "initialize" => await InitializeAsync(),
                        "tools/list" => await ListToolsAsync(),
                        "tools/call" => await CallToolAsync(request, mcpTools),
                        _ => throw new NotSupportedException($"Method not supported: {method}")
                    };
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing request");
                    error = ex.Message;
                }

                // Send response - only include result OR error, not both
                object response;
                if (error != null)
                {
                    response = new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -1, message = error }
                    };
                }
                else
                {
                    response = new
                    {
                        jsonrpc = "2.0",
                        id,
                        result
                    };
                }

                var responseJson = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson);

                logger.LogDebug("Sent response: {Response}", responseJson);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in MCP server loop");
                break;
            }
        }
    }

    private static async Task<object> InitializeAsync()
    {
        return new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new
                {
                    listChanged = false
                }
            },
            serverInfo = new
            {
                name = "csharp-rag-service",
                version = "1.0.0",
                defaultProject = _defaultProjectName
            }
        };
    }

    private static async Task<object> ListToolsAsync()
    {
        return new
        {
            tools = new object[]
            {
                new
                {
                    name = "analyze_csharp_project",
                    description = "Analyze a C# project and extract symbols for code search. Pass working directory path, optionally with project name.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            workingDirectory = new
                            {
                                type = "string",
                                description = "Working directory path containing the project"
                            },
                            projectName = new
                            {
                                type = "string",
                                description = "Optional project name (e.g., 'MyProject.csproj'). If not provided, uses default configured project name or searches for .csproj files."
                            }
                        },
                        required = new[] { "workingDirectory" }
                    }
                },
                new
                {
                    name = "search_csharp_code",
                    description = "Search for code symbols in analyzed C# projects",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new
                            {
                                type = "string",
                                description = "Search query for code symbols"
                            },
                            projectId = new
                            {
                                type = "string",
                                description = "Optional project ID to search within"
                            },
                            topK = new
                            {
                                type = "integer",
                                description = "Maximum number of results to return"
                            }
                        },
                        required = new[] { "query" }
                    }
                },
                new
                {
                    name = "compile_csharp_project",
                    description = "Compile a C# project and check for compilation errors and warnings",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            projectPath = new
                            {
                                type = "string",
                                description = "Full path to the .csproj file to compile"
                            },
                            configuration = new
                            {
                                type = "string",
                                description = "Build configuration (Debug or Release). Defaults to Debug if not specified."
                            }
                        },
                        required = new[] { "projectPath" }
                    }
                },
                new
                {
                    name = "monitor_csharp_project",
                    description = "Start monitoring a C# project for automatic incremental updates",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            projectPath = new
                            {
                                type = "string",
                                description = "Full path to the .csproj file to monitor"
                            }
                        },
                        required = new[] { "projectPath" }
                    }
                },
                new
                {
                    name = "stop_monitoring_csharp_project",
                    description = "Stop monitoring a C# project",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            projectId = new
                            {
                                type = "string",
                                description = "Project ID to stop monitoring"
                            }
                        },
                        required = new[] { "projectId" }
                    }
                },
                new
                {
                    name = "get_monitoring_status",
                    description = "Get status of all monitored projects",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            projectId = new
                            {
                                type = "string",
                                description = "Optional project ID to get status for specific project"
                            }
                        }
                    }
                },
                new
                {
                    name = "update_csharp_project",
                    description = "Manually trigger incremental update for a monitored project",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            projectPath = new
                            {
                                type = "string",
                                description = "Full path to the .csproj file to update"
                            }
                        },
                        required = new[] { "projectPath" }
                    }
                }
            }
        };
    }

    private static async Task<object> CallToolAsync(JsonElement request, McpTools mcpTools)
    {
        var paramsElement = request.GetProperty("params");
        var name = paramsElement.GetProperty("name").GetString();
        var arguments = paramsElement.GetProperty("arguments");
        if(arguments.TryGetProperty("server_name", out var sn) && 
            arguments.TryGetProperty("tool_name", out var tn) &&
            arguments.TryGetProperty("args",out var args))
        {
            arguments = args;
        }

        var resultJson = name switch
        {
            "analyze_csharp_project" => await HandleAnalyzeProjectAsync(arguments, mcpTools),
            "search_csharp_code" => await mcpTools.SearchCSharpCodeAsync(
                query: arguments.GetProperty("query").GetString()!,
                projectId: arguments.TryGetProperty("projectId", out var projectId) ? projectId.GetString() : null,
                topK: arguments.TryGetProperty("topK", out var topK) ? topK.GetInt32() : 5),
            "compile_csharp_project" => await mcpTools.CompileCSharpProjectAsync(
                projectPath: arguments.GetProperty("projectPath").GetString()!,
                configuration: arguments.TryGetProperty("configuration", out var config) ? config.GetString() : "Debug"),
            "monitor_csharp_project" => await mcpTools.MonitorCSharpProjectAsync(
                projectPath: arguments.GetProperty("projectPath").GetString()!),
            "stop_monitoring_csharp_project" => await mcpTools.StopMonitoringCSharpProjectAsync(
                projectId: arguments.GetProperty("projectId").GetString()!),
            "get_monitoring_status" => await mcpTools.GetMonitoringStatusAsync(),
            "update_csharp_project" => await mcpTools.UpdateCSharpProjectAsync(
                projectPath: arguments.GetProperty("projectPath").GetString()!),
            _ => throw new NotSupportedException($"Tool not supported: {name}")
        };

        // Parse the JSON string result into an object for proper MCP response
        var resultObject = JsonSerializer.Deserialize<object>(resultJson);
        
        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = resultJson
                }
            }
        };
    }

    private static async Task<string> HandleAnalyzeProjectAsync(JsonElement arguments, McpTools mcpTools)
    {
        try
        {
            var workingDirectory = arguments.GetProperty("workingDirectory").GetString()!;
            var projectName = arguments.TryGetProperty("projectName", out var projName) 
                ? projName.GetString() 
                : _defaultProjectName;

            // Resolve project path
            string projectPath;
            
            if (!string.IsNullOrEmpty(projectName))
            {
                // Combine working directory with project name
                projectPath = Path.Combine(workingDirectory, projectName);
                
                // Add .csproj extension if not present
                if (!projectName.EndsWith(".csproj"))
                {
                    projectPath += ".csproj";
                }
            }
            else
            {
                // Search for .csproj files in the working directory
                var csprojFiles = Directory.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
                
                if (csprojFiles.Length == 0)
                {
                    return JsonSerializer.Serialize(new { error = $"No .csproj files found in directory: {workingDirectory}" });
                }
                
                if (csprojFiles.Length > 1)
                {
                    return JsonSerializer.Serialize(new { 
                        error = $"Multiple .csproj files found in directory: {workingDirectory}. Please specify projectName parameter.",
                        availableProjects = csprojFiles.Select(Path.GetFileName).ToArray()
                    });
                }
                
                projectPath = csprojFiles[0];
            }

            // Ensure the project file exists
            if (!File.Exists(projectPath))
            {
                return JsonSerializer.Serialize(new { error = $"Project file not found: {projectPath}" });
            }

            // Call the actual analysis method
            return await mcpTools.AnalyzeCSharpProjectAsync(projectPath);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Error handling project analysis: {ex.Message}" });
        }
    }
}

// Custom logger provider that writes to stderr instead of stdout
public class StderrLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new StderrLogger(categoryName);
    }

    public void Dispose()
    {
    }
}

public class StderrLogger : ILogger
{
    private readonly string _categoryName;

    public StderrLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) => new NoOpDisposable();

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        var logEntry = $"{timestamp} [{logLevel.ToString().ToLower()}] [{_categoryName}] {message}";
        
        if (exception != null)
        {
            logEntry += Environment.NewLine + exception.ToString();
        }

        Console.Error.WriteLine(logEntry);
    }

    private class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}