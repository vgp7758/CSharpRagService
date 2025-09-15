using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using CSharpMcpService.Models;
using ProjectInfo = CSharpMcpService.Models.ProjectInfo;
using SymbolKind = CSharpMcpService.Models.SymbolKind;

namespace CSharpMcpService.Services;

public class SimpleProjectAnalyzer : IProjectAnalyzer
{
    private readonly ILogger<SimpleProjectAnalyzer> _logger;
    private readonly IEmbeddingService _embeddingService;

    public SimpleProjectAnalyzer(ILogger<SimpleProjectAnalyzer> logger, IEmbeddingService embeddingService)
    {
        _logger = logger;
        _embeddingService = embeddingService;
    }

    public async Task InitializeEmbeddingModelAsync()
    {
        await _embeddingService.InitializeAsync();
    }

    public async Task<ProjectInfo> AnalyzeProjectAsync(string projectPath)
    {
        try
        {
            _logger.LogInformation("Analyzing project: {ProjectPath}", projectPath);

            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"Project file not found: {projectPath}");
            }

            var projectInfo = new ProjectInfo
            {
                Id = Path.GetFileNameWithoutExtension(projectPath),
                Name = Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath = projectPath,
                AssemblyName = Path.GetFileNameWithoutExtension(projectPath),
                TargetFramework = "net8.0",
                OutputPath = Path.Combine(Path.GetDirectoryName(projectPath) ?? "", "bin", "Debug"),
                RootNamespace = Path.GetFileNameWithoutExtension(projectPath)
            };

            // Extract source files from project directory
            var projectDir = Path.GetDirectoryName(projectPath) ?? "";
            var sourceFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains("obj") && !file.Contains("bin"))
                .ToList();

            projectInfo.SourceFiles = sourceFiles;

            // Try to extract package references from .csproj file
            var projectContent = await File.ReadAllTextAsync(projectPath);
            var packageReferences = ExtractPackageReferences(projectContent);
            projectInfo.Packages = packageReferences;

            _logger.LogInformation("Project analysis completed. Found {FileCount} source files.",
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
            _logger.LogInformation("Extracting symbols from project: {ProjectPath}", projectInfo.ProjectPath);

            var symbols = new List<CodeSymbol>();

            foreach (var sourceFile in projectInfo.SourceFiles)
            {
                if (!File.Exists(sourceFile))
                {
                    _logger.LogWarning("Source file not found: {SourceFile}", sourceFile);
                    continue;
                }

                var fileSymbols = await ExtractSymbolsFromFileAsync(sourceFile, projectInfo);
                symbols.AddRange(fileSymbols);
            }

            _logger.LogInformation("Extracted {SymbolCount} symbols from project", symbols.Count);
            return symbols;
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

            // Extract classes
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDecl in classDeclarations)
            {
                var symbol = new CodeSymbol
                {
                    Id = $"{projectInfo.AssemblyName}.{classDecl.Identifier.Text}",
                    Name = classDecl.Identifier.Text,
                    Kind = SymbolKind.Class,
                    Namespace = ExtractNamespace(root, classDecl.Span.Start),
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

                symbols.Add(symbol);
            }

            // Extract methods
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDecl in methodDeclarations)
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
                    Namespace = ExtractNamespace(root, methodDecl.Span.Start),
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

                symbols.Add(symbol);
            }

            // Generate embeddings for symbols
            foreach (var symbol in symbols)
            {
                symbol.Embedding = await _embeddingService.GenerateEmbeddingAsync(
                    $"{symbol.Name} {symbol.Kind} {symbol.Signature}");
            }

            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting symbols from file {FilePath}", filePath);
            return new List<CodeSymbol>();
        }
    }

    private string ExtractNamespace(SyntaxNode root, int position)
    {
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
        var matches = Regex.Matches(projectContent, packagePattern);

        foreach (Match match in matches)
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

            var results = symbols
                .Where(s => s.Embedding != null)
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