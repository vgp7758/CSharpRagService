using System.Text.Json.Serialization;

namespace CSharpMcpService.Models;

public class ProjectInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;

    [JsonPropertyName("assemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    [JsonPropertyName("targetFramework")]
    public string TargetFramework { get; set; } = string.Empty;

    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("rootNamespace")]
    public string RootNamespace { get; set; } = string.Empty;

    [JsonPropertyName("sourceFiles")]
    public List<string> SourceFiles { get; set; } = new();

    [JsonPropertyName("references")]
    public List<string> References { get; set; } = new();

    [JsonPropertyName("packages")]
    public List<PackageReference> Packages { get; set; } = new();

    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();
}

public class PackageReference
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public class SearchResult
{
    [JsonPropertyName("symbol")]
    public CodeSymbol Symbol { get; set; } = new();

    [JsonPropertyName("similarityScore")]
    public double SimilarityScore { get; set; }

    [JsonPropertyName("highlightedCode")]
    public string? HighlightedCode { get; set; }
}