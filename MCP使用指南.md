# C# MCP 服务使用指南

## 概述

C# MCP 服务是一个专门用于分析C#项目的Model Context Protocol (MCP)服务。它能够：
- 分析C#项目文件(.csproj)
- 提取代码符号（类、方法、属性等）
- 构建向量数据库用于语义搜索
- 提供智能代码搜索功能

## 启动服务

### 1. 直接运行DLL文件

```bash
cd D:\Projects\CSharpRagService\CSharpMcpService\bin\Debug\net8.0
dotnet CSharpMcpService.dll
```

### 2. 使用启动脚本

```bash
# 运行启动脚本
cd D:\Projects\CSharpRagService
start-mcp-server.bat
```

### 3. 使用命令行参数

```bash
# 启用高级嵌入模型（需要Python推理服务器）
dotnet CSharpMcpService.dll --advanced-embedding

# 设置默认项目名称
dotnet CSharpMcpService.dll --project=MyProject.csproj

# 或使用环境变量
set USE_ADVANCED_EMBEDDING=true
dotnet CSharpMcpService.dll
```

## Agent配置

### Claude Desktop配置

将以下配置添加到Claude Desktop的配置文件中：

```json
{
  "mcpServers": {
    "csharp-rag": {
      "command": "dotnet",
      "args": ["D:\\Projects\\CSharpRagService\\CSharpMcpService\\bin\\Debug\\net8.0\\CSharpMcpService.dll"],
      "env": {
        "USE_ADVANCED_EMBEDDING": "true"
      }
    }
  }
}
```

### VS Code配置

对于VS Code的MCP客户端，配置类似：

```json
{
  "mcp.servers": {
    "csharp-rag": {
      "command": "dotnet",
      "args": ["D:\\Projects\\CSharpRagService\\CSharpMcpService\\bin\\Debug\\net8.0\\CSharpMcpService.dll"]
    }
  }
}
```

## 可用工具

### 1. analyze_csharp_project

分析C#项目并提取符号。支持灵活的项目路径解析。

**参数：**
- `workingDirectory` (必需): 包含项目的工作目录路径
- `projectName` (可选): 项目名称（如 'MyProject.csproj'）。如果未提供，会自动搜索目录中的.csproj文件

**示例：**
```json
{
  "name": "analyze_csharp_project",
  "arguments": {
    "workingDirectory": "D:\\MyProject"
  }
}
```

或指定项目名称：
```json
{
  "name": "analyze_csharp_project",
  "arguments": {
    "workingDirectory": "D:\\MyProject",
    "projectName": "MyProject.csproj"
  }
}
```

### 2. search_csharp_code

在已分析的C#项目中搜索代码符号。

**参数：**
- `query` (必需): 搜索查询
- `projectId` (可选): 项目ID，限制搜索范围
- `topK` (可选): 返回结果数量，默认为5

**示例：**
```json
{
  "name": "search_csharp_code",
  "arguments": {
    "query": "database connection",
    "topK": 10
  }
}
```

### 3. compile_csharp_project

编译C#项目并检查编译错误和警告。

**参数：**
- `projectPath` (必需): 要编译的 .csproj 文件的完整路径
- `configuration` (可选): 构建配置（Debug 或 Release），默认为 Debug

**示例：**
```json
{
  "name": "compile_csharp_project",
  "arguments": {
    "projectPath": "D:\\MyProject\\MyProject.csproj",
    "configuration": "Debug"
  }
}
```

## 高级功能

### 1. 嵌入模型支持

- **简单嵌入**: 基于哈希的轻量级嵌入（默认）
- **高级嵌入**: 使用EmbeddingGemma-300m模型（需要Python推理服务器）

### 2. Python推理服务器

如需使用高级嵌入模型，请启动Python推理服务器：

```bash
cd D:\Projects\CSharpRagService\CSharpMcpService\PythonInference
pip install -r requirements.txt
python embedding_server.py
```

### 3. 性能优化

- **并行处理**: 支持多文件并行分析
- **缓存机制**: 嵌入结果缓存，提升性能
- **增量更新**: 仅更新修改的文件

## 使用示例

### 工作流程

1. **分析项目**:
   ```json
   {
     "name": "analyze_csharp_project",
     "arguments": {
       "workingDirectory": "D:\\MyProject"
     }
   }
   ```

2. **搜索代码**:
   ```json
   {
     "name": "search_csharp_code",
     "arguments": {
       "query": "用户认证方法"
     }
   }
   ```

3. **编译项目**:
   ```json
   {
     "name": "compile_csharp_project",
     "arguments": {
       "projectPath": "D:\\MyProject\\MyProject.csproj"
     }
   }
   ```

3. **获取结果**:
   服务将返回匹配的代码符号，包括：
   - 符号名称和类型
   - 命名空间
   - 文件路径和行号
   - 相似度评分
   - 代码摘要

## 故障排除

### 常见问题

1. **项目文件不存在**: 确保提供的目录路径正确且包含.csproj文件
2. **多项目冲突**: 如果目录中有多个.csproj文件，请明确指定projectName参数
3. **嵌入服务不可用**: 检查Python推理服务器是否运行
4. **权限问题**: 确保对项目文件有读取权限
5. **DLL文件锁定**: 如果遇到文件锁定问题，可以使用临时版本的DLL

### 日志查看

服务会输出详细的日志信息到stderr，包括：
- 服务启动状态
- 项目分析进度
- 搜索结果统计
- 错误信息
- 时间戳和日志级别

**注意**: 日志输出到stderr以避免与stdout的JSON-RPC通信冲突

## 性能建议

1. **首次分析**: 可能需要较长时间，因为需要生成嵌入向量
2. **缓存利用**: 重复查询相同内容会更快
3. **内存使用**: 大型项目可能需要更多内存
4. **磁盘空间**: 嵌入向量会存储在内存中

## 扩展功能

服务支持以下扩展功能：

- 并行文件分析
- 智能缓存机制
- 模式匹配搜索
- 依赖关系分析
- 调用链追踪
- 增量数据库更新

## 技术架构

- **协议**: MCP (Model Context Protocol)
- **框架**: .NET 8.0
- **代码分析**: Roslyn
- **嵌入服务**: 支持多种模型
- **向量数据库**: 内存存储，支持持久化
- **搜索算法**: 余弦相似度计算