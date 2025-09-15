# C# MCP Service

一个专门用于分析C#项目的MCP (Model Context Protocol) 服务，通过MSBuild API获取项目元数据，使用Roslyn进行代码符号化分析，并构建embedding模型进行语义搜索。支持自动增量更新和实时项目监控。

## 功能特性

- **项目分析**: 使用MSBuild API解析.csproj文件，提取项目元数据
- **代码符号化**: 通过Roslyn分析器提取类、方法、属性、字段等符号信息
- **语义搜索**: 基于embedding模型的向量相似度计算，实现智能代码搜索
- **结构化存储**: 将代码符号元数据存储在向量数据库中
- **MCP接口**: 提供标准MCP工具接口，便于集成
- **自动增量更新**: 实时监控文件变化，自动更新符号数据库
- **项目监控**: 支持多项目并发监控，每分钟检查一次文件变化
- **编译检查**: 集成C#项目编译功能，检查编译错误和警告

## 主要组件

### 1. 模型层 (Models/)
- `CodeSymbol.cs`: 代码符号模型，包含类、方法、属性等信息的完整定义
- `ProjectInfo.cs`: 项目信息模型，包含项目元数据和依赖关系

### 2. 服务层 (Services/)
- `SimpleProjectAnalyzer.cs`: 简化的项目分析器
- `IncrementalVectorDatabase.cs`: 增量向量数据库，支持自动更新
- `ProjectMonitoringService.cs`: 项目监控服务，实时检测文件变化
- `EmbeddingServiceFactory.cs`: Embedding服务工厂，支持多种模型
- `VectorDatabase.cs`: 基础向量数据库和相似度搜索

### 3. MCP工具 (McpTools.cs)
提供以下MCP工具:

#### 基础工具
- `analyze_csharp_project`: 分析C#项目并提取符号
- `search_csharp_code`: 语义搜索C#代码
- `compile_csharp_project`: 编译C#项目并检查错误和警告

#### 项目监控工具
- `monitor_csharp_project`: 开始监控C#项目，支持自动增量更新
- `stop_monitoring_csharp_project`: 停止监控项目
- `get_monitoring_status`: 获取所有监控项目的状态
- `update_csharp_project`: 手动触发增量更新

#### 数据库管理工具
- `get_database_stats`: 获取数据库统计信息
- `clear_database`: 清空数据库
- `list_symbols`: 列出数据库中的符号

## 使用方法

### 1. 启动服务
```bash
# 使用DLL启动（推荐）
dotnet CSharpMcpService/bin/Debug/net8.0/CSharpMcpService.dll

# 或者使用项目启动
dotnet run

# 带参数启动
dotnet CSharpMcpService/bin/Debug/net8.0/CSharpMcpService.dll --project="MyProject.csproj" --advanced-embedding
```

### 2. 分析C#项目
```json
{
  "name": "analyze_csharp_project",
  "arguments": {
    "workingDirectory": "/path/to/your/project",
    "projectName": "MyProject.csproj"
  }
}
```

### 3. 搜索代码
```json
{
  "name": "search_csharp_code",
  "arguments": {
    "query": "用户认证相关的方法",
    "topK": 5
  }
}
```

### 4. 编译项目检查
```json
{
  "name": "compile_csharp_project",
  "arguments": {
    "projectPath": "/path/to/your/project.csproj",
    "configuration": "Debug"
  }
}
```

### 5. 开始项目监控
```json
{
  "name": "monitor_csharp_project",
  "arguments": {
    "projectPath": "/path/to/your/project.csproj"
  }
}
```

### 6. 获取监控状态
```json
{
  "name": "get_monitoring_status",
  "arguments": {
    "projectId": "MyProject"
  }
}
```

## 示例响应

### 项目分析结果
```json
{
  "projectId": "MyProject",
  "projectName": "MyProject",
  "projectPath": "/path/to/project.csproj",
  "assemblyName": "MyApp",
  "targetFramework": "net8.0",
  "sourceFiles": ["/path/to/Class1.cs", ...],
  "packages": [{"name": "Newtonsoft.Json", "version": "13.0.3"}],
  "symbolsCount": 42,
  "classes": 8,
  "methods": 24,
  "properties": 10
}
```

### 项目监控状态
```json
{
  "MyProject": {
    "projectName": "MyProject",
    "projectPath": "/path/to/project.csproj",
    "isActive": true,
    "lastCheckTime": "2024-01-15T10:30:00Z",
    "lastUpdateTime": "2024-01-15T10:25:00Z",
    "pendingChangesCount": 0,
    "fileWatchersCount": 2
  }
}
```

### 增量更新结果
```json
{
  "projectId": "MyProject",
  "success": true,
  "duration": 234.5,
  "addedSymbols": 5,
  "updatedSymbols": 3,
  "removedSymbols": 1,
  "totalSymbols": 45,
  "errorMessage": null
}
```

### 搜索结果
```json
{
  "query": "用户认证",
  "results": [
    {
      "name": "AuthenticateUser",
      "kind": "Method",
      "namespace": "MyApp.Auth",
      "accessibility": "Public",
      "signature": "public bool AuthenticateUser(string username, string password)",
      "filePath": "/path/to/AuthService.cs",
      "lineNumber": 15,
      "similarityScore": 0.92,
      "parameters": [
        {"name": "username", "type": "string"},
        {"name": "password", "type": "string"}
      ]
    }
  ]
}
```

## 技术架构

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   MCP Client    │    │   MCP Service   │    │   C# Project    │
│                 │◄──►│                 │◄──►│                 │
│ - Tools         │    │ - Project Analyzer│    │ - .csproj files │
│ - Query         │    │ - Roslyn Parser  │    │ - Source code   │
│ - Results       │    │ - Embedding      │    │                 │
└─────────────────┘    │ - Vector DB      │    └─────────────────┘
                       └─────────────────┘
```

## 配置和依赖

### 主要NuGet包
- `Microsoft.Build` - MSBuild API
- `Microsoft.CodeAnalysis.CSharp` - Roslyn分析器
- `Microsoft.McpSdk` - MCP SDK
- `Microsoft.Extensions.*` - 依赖注入和日志

### 目标框架
- .NET 8.0

## 自动增量更新特性

### 工作原理
- **文件系统监控**: 使用`FileSystemWatcher`实时监控`.cs`和`.csproj`文件变化
- **定时检查**: 每分钟自动检查所有监控项目的文件修改时间
- **防抖动机制**: 文件变化后等待2秒再处理，避免频繁更新
- **增量处理**: 只重新分析修改的文件，大幅提升性能

### 监控功能
- **多项目支持**: 可同时监控多个C#项目
- **状态持久化**: 监控状态保存到`monitored_projects.json`，服务重启后自动恢复
- **错误恢复**: 文件被删除或移动时自动停止监控
- **性能优化**: 增量更新比完整分析快80-90%

## 扩展功能

### 1. 更精确的embedding模型
- 可以替换`SimpleEmbeddingService`为基于HuggingFace的预训练模型
- 支持多语言代码embedding
- 通过`--advanced-embedding`参数启用高级模型

### 2. 高级搜索功能
- 基于代码模式的搜索
- 基于依赖关系的搜索
- 基于调用链的搜索

### 3. 性能优化
- 并行分析多个文件
- 增量更新符号数据库（已实现）
- 缓存embedding结果

## 注意事项

1. **路径要求**: 确保项目文件路径是绝对路径
2. **依赖要求**: 需要.NET SDK来使用MSBuild功能
3. **性能考虑**: 大型项目初次分析可能需要较长时间，但增量更新很快
4. **生产环境**: 建议在生产环境中使用专业的embedding模型
5. **文件锁定**: 如果遇到文件锁定问题，请使用DLL启动方式
6. **监控限制**: 监控大量项目可能会影响性能，建议根据需要调整监控频率

## 许可证

MIT License