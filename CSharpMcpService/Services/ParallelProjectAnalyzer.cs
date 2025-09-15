using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using CSharpMcpService.Models;
using ProjectInfo = CSharpMcpService.Models.ProjectInfo;
using SymbolKind = CSharpMcpService.Models.SymbolKind;

namespace CSharpMcpService.Services;

public class ParallelProjectAnalyzer : IProjectAnalyzer
{
    private readonly ILogger<ParallelProjectAnalyzer> _logger;
    private readonly IEmbeddingService _embeddingService;
    private readonly int _maxDegreeOfParallelism;

    public ParallelProjectAnalyzer(
        ILogger<ParallelProjectAnalyzer> logger,
        IEmbeddingService embeddingService,
        int maxDegreeOfParallelism = 4)
    {
        _logger = logger;
        _embeddingService = embeddingService;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    public async Task InitializeEmbeddingModelAsync()
    {
        await _embeddingService.InitializeAsync();
    }

    public async Task<ProjectInfo> AnalyzeProjectAsync(string projectPath)
    {
        try
        {
            _logger.LogInformation("Analyzing project with parallel processing: {ProjectPath}", projectPath);

            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"Project file not found: {projectPath}");
            }

            var projectInfo = new ProjectInfo
            {
                ProjectPath = projectPath,
                AssemblyName = Path.GetFileNameWithoutExtension(projectPath),
                TargetFramework = "net8.0",
                OutputPath = Path.Combine(Path.GetDirectoryName(projectPath) ?? "", "bin", "Debug"),
                RootNamespace = Path.GetFileNameWithoutExtension(projectPath)
            };

            // 并行提取源文件
            var projectDir = Path.GetDirectoryName(projectPath) ?? "";
            var sourceFilesTask = Task.Run(() =>
            {
                var files = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                    .Where(file => !file.Contains("obj") && !file.Contains("bin"))
                    .ToList();
                return files;
            });

            // 并行解析项目文件
            var projectContentTask = File.ReadAllTextAsync(projectPath);
            await Task.WhenAll(sourceFilesTask, projectContentTask);

            projectInfo.SourceFiles = await sourceFilesTask;
            var projectContent = await projectContentTask;

            // 并行提取包引用
            var packages = await Task.Run(() => ExtractPackageReferences(projectContent));
            projectInfo.Packages = packages;

            _logger.LogInformation("Parallel project analysis completed. Found {FileCount} source files.",
                projectInfo.SourceFiles.Count);

            return projectInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project {ProjectPath}", projectPath);
            throw;
        }
    }

    public async Task<List<CodeSymbol>> ExtractSymbolsAsync(ProjectInfo projectInfo)
    {
        try
        {
            _logger.LogInformation("Extracting symbols with parallel processing from project: {ProjectPath}",
                projectInfo.ProjectPath);

            var symbols = new ConcurrentBag<CodeSymbol>();
            var failedFiles = new ConcurrentBag<string>();

            // 使用并行处理每个文件
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxDegreeOfParallelism
            };

            await Parallel.ForEachAsync(projectInfo.SourceFiles, parallelOptions, async (sourceFile, cancellationToken) =>
            {
                try
                {
                    var fileSymbols = await ExtractSymbolsFromFileAsync(sourceFile, projectInfo);
                    foreach (var symbol in fileSymbols)
                    {
                        symbols.Add(symbol);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file {FilePath}", sourceFile);
                    failedFiles.Add(sourceFile);
                }
            });

            _logger.LogInformation("Parallel symbol extraction completed. Found {SymbolCount} symbols from {FileCount} files.",
                symbols.Count, projectInfo.SourceFiles.Count);

            if (failedFiles.Any())
            {
                _logger.LogWarning("Failed to process {Count} files: {Files}",
                    failedFiles.Count, string.Join(", ", failedFiles));
            }

            return symbols.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting symbols from project {ProjectPath}", projectInfo.ProjectPath);
            throw;
        }
    }

    private async Task<List<CodeSymbol>> ExtractSymbolsFromFileAsync(string filePath, ProjectInfo projectInfo)
    {
        var symbols = new List<CodeSymbol>();

        try
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = syntaxTree.GetRoot();

            // 并行提取不同类型的符号
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            var propertyDeclarations = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().ToList();
            var fieldDeclarations = root.DescendantNodes().OfType<FieldDeclarationSyntax>().ToList();

            // 并行处理不同类型的声明
            var classTasks = classDeclarations.Select(decl => Task.Run(() =>
                CreateClassSymbol(decl, filePath, syntaxTree, projectInfo)));

            var methodTasks = methodDeclarations.Select(decl => Task.Run(() =>
                CreateMethodSymbol(decl, filePath, syntaxTree, projectInfo)));

            var propertyTasks = propertyDeclarations.Select(decl => Task.Run(() =>
                CreatePropertySymbol(decl, filePath, syntaxTree, projectInfo)));

            var fieldTasks = fieldDeclarations.Select(decl => Task.Run(() =>
                CreateFieldSymbol(decl, filePath, syntaxTree, projectInfo)));

            // 等待所有任务完成
            var allTasks = classTasks.Concat(methodTasks).Concat(propertyTasks).Concat(fieldTasks);
            var results = await Task.WhenAll(allTasks);

            symbols.AddRange(results.Where(s => s != null).OfType<CodeSymbol>());

            // 批量生成 embeddings
            var textsToEmbed = symbols.Select(s => $"{s.Name} {s.Kind} {s.Signature}").ToList();
            var embeddings = await _embeddingService.GenerateEmbeddingsBatchAsync(textsToEmbed);

            // 分配 embeddings
            for (int i = 0; i < symbols.Count && i < embeddings.Count; i++)
            {
                symbols[i].Embedding = embeddings[i];
            }

            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting symbols from file {FilePath}", filePath);
            return new List<CodeSymbol>();
        }
    }

    private CodeSymbol? CreateClassSymbol(
        ClassDeclarationSyntax classDecl,
        string filePath,
        SyntaxTree syntaxTree,
        ProjectInfo projectInfo)
    {
        try
        {
            var symbol = new CodeSymbol
            {
                Id = $"{projectInfo.AssemblyName}.{classDecl.Identifier.Text}",
                Name = classDecl.Identifier.Text,
                Kind = SymbolKind.Class,
                Namespace = ExtractNamespace(syntaxTree, classDecl.Span.Start),
                Accessibility = GetAccessibility(classDecl.Modifiers),
                FilePath = filePath,
                LineNumber = syntaxTree.GetLineSpan(classDecl.Span).StartLinePosition.Line + 1,
                Signature = classDecl.ToString(),
                SourceCode = classDecl.ToFullString(),
                Metadata = new Dictionary<string, object>
                {
                    ["project"] = projectInfo.AssemblyName,
                    ["isStatic"] = classDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)),
                    ["isAbstract"] = classDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AbstractKeyword)),
                    ["isSealed"] = classDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SealedKeyword))
                }
            };

            return symbol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating class symbol");
            return null;
        }
    }

    private CodeSymbol? CreateMethodSymbol(
        MethodDeclarationSyntax methodDecl,
        string filePath,
        SyntaxTree syntaxTree,
        ProjectInfo projectInfo)
    {
        try
        {
            var parameters = methodDecl.ParameterList.Parameters.Select(p => new ParameterInfo
            {
                Name = p.Identifier.Text,
                Type = p.Type?.ToString() ?? "",
                DefaultValue = p.Default?.Value?.ToString()
            }).ToList();

            var symbol = new CodeSymbol
            {
                Id = $"{projectInfo.AssemblyName}.{methodDecl.Identifier.Text}",
                Name = methodDecl.Identifier.Text,
                Kind = SymbolKind.Method,
                Namespace = ExtractNamespace(syntaxTree, methodDecl.Span.Start),
                Accessibility = GetAccessibility(methodDecl.Modifiers),
                FilePath = filePath,
                LineNumber = syntaxTree.GetLineSpan(methodDecl.Span).StartLinePosition.Line + 1,
                Signature = methodDecl.ToString(),
                ReturnType = methodDecl.ReturnType?.ToString(),
                Parameters = parameters,
                SourceCode = methodDecl.ToFullString(),
                Metadata = new Dictionary<string, object>
                {
                    ["project"] = projectInfo.AssemblyName,
                    ["isStatic"] = methodDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)),
                    ["isVirtual"] = methodDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VirtualKeyword)),
                    ["isOverride"] = methodDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.OverrideKeyword)),
                    ["isAsync"] = methodDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword))
                }
            };

            return symbol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating method symbol");
            return null;
        }
    }

    private CodeSymbol? CreatePropertySymbol(
        PropertyDeclarationSyntax propDecl,
        string filePath,
        SyntaxTree syntaxTree,
        ProjectInfo projectInfo)
    {
        try
        {
            var symbol = new CodeSymbol
            {
                Id = $"{projectInfo.AssemblyName}.{propDecl.Identifier.Text}",
                Name = propDecl.Identifier.Text,
                Kind = SymbolKind.Property,
                Namespace = ExtractNamespace(syntaxTree, propDecl.Span.Start),
                Accessibility = GetAccessibility(propDecl.Modifiers),
                FilePath = filePath,
                LineNumber = syntaxTree.GetLineSpan(propDecl.Span).StartLinePosition.Line + 1,
                Signature = propDecl.ToString(),
                ReturnType = propDecl.Type?.ToString(),
                SourceCode = propDecl.ToFullString(),
                Metadata = new Dictionary<string, object>
                {
                    ["project"] = projectInfo.AssemblyName,
                    ["isStatic"] = propDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)),
                    ["isVirtual"] = propDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VirtualKeyword))
                }
            };

            return symbol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating property symbol");
            return null;
        }
    }

    private CodeSymbol? CreateFieldSymbol(
        FieldDeclarationSyntax fieldDecl,
        string filePath,
        SyntaxTree syntaxTree,
        ProjectInfo projectInfo)
    {
        try
        {
            var variable = fieldDecl.Declaration.Variables.FirstOrDefault();
            if (variable == null) return null;

            var symbol = new CodeSymbol
            {
                Id = $"{projectInfo.AssemblyName}.{variable.Identifier.Text}",
                Name = variable.Identifier.Text,
                Kind = SymbolKind.Field,
                Namespace = ExtractNamespace(syntaxTree, fieldDecl.Span.Start),
                Accessibility = GetAccessibility(fieldDecl.Modifiers),
                FilePath = filePath,
                LineNumber = syntaxTree.GetLineSpan(fieldDecl.Span).StartLinePosition.Line + 1,
                Signature = fieldDecl.ToString(),
                ReturnType = fieldDecl.Declaration.Type?.ToString(),
                SourceCode = fieldDecl.ToFullString(),
                Metadata = new Dictionary<string, object>
                {
                    ["project"] = projectInfo.AssemblyName,
                    ["isStatic"] = fieldDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)),
                    ["isReadOnly"] = fieldDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword))
                }
            };

            return symbol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating field symbol");
            return null;
        }
    }

    private string ExtractNamespace(SyntaxTree syntaxTree, int position)
    {
        var root = syntaxTree.GetRoot();
        var namespaceDecl = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault(ns => ns.Span.Contains(position));

        return namespaceDecl?.Name.ToString() ?? "";
    }

    private string GetAccessibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
            return "Public";
        if (modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword)))
            return "Private";
        if (modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ProtectedKeyword)))
            return "Protected";
        if (modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InternalKeyword)))
            return "Internal";
        return "Internal";
    }

    private List<PackageReference> ExtractPackageReferences(string projectContent)
    {
        var packages = new List<PackageReference>();
        var packagePattern = @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""\s*/>";
        var matches = System.Text.RegularExpressions.Regex.Matches(projectContent, packagePattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Success)
            {
                packages.Add(new PackageReference
                {
                    Name = match.Groups[1].Value,
                    Version = match.Groups[2].Value
                });
            }
        }

        return packages;
    }

    public async Task<List<CodeSymbol>> SearchAsync(List<CodeSymbol> symbols, string query, int topK = 5)
    {
        try
        {
            _logger.LogInformation("Searching for: {Query}", query);

            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

            // 并行计算相似度
            var results = symbols
                .Where(s => s.Embedding != null)
                .AsParallel()
                .Select(symbol => new SearchResult
                {
                    Symbol = symbol,
                    SimilarityScore = CalculateSimilarity(queryEmbedding, symbol.Embedding!)
                })
                .OrderByDescending(r => r.SimilarityScore)
                .Take(topK)
                .ToList();

            return results.Select(r => r.Symbol).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching symbols");
            throw;
        }
    }

    private double CalculateSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length)
            return 0;

        var dotProduct = 0.0;
        var magnitude1 = 0.0;
        var magnitude2 = 0.0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }
}