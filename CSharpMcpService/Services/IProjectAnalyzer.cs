using CSharpMcpService.Models;

namespace CSharpMcpService.Services;

public interface IProjectAnalyzer
{
    Task<ProjectInfo> AnalyzeProjectAsync(string projectPath);
    Task<List<CodeSymbol>> ExtractSymbolsAsync(ProjectInfo projectInfo);
    Task<List<CodeSymbol>> SearchAsync(List<CodeSymbol> symbols, string query, int topK = 5);
    Task InitializeEmbeddingModelAsync();
}