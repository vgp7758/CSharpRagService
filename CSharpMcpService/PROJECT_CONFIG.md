# C# MCP Service - 项目配置说明

## 配置方式

现在您可以通过以下几种方式配置默认的项目名称：

### 1. 环境变量方式

```bash
export DEFAULT_PROJECT_NAME="MyProject.csproj"
dotnet run
```

### 2. 命令行参数方式

```bash
dotnet run --project=MyProject.csproj
```

### 3. MCP JSON配置文件

```json
{
  "mcpServers": {
    "csharp-rag": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "path/to/CSharpMcpService.csproj",
        "--project=MyProject.csproj"
      ],
      "env": {
        "DEFAULT_PROJECT_NAME": "MyProject.csproj",
        "USE_ADVANCED_EMBEDDING": "false"
      }
    }
  }
}
```

## 使用方式

### 1. 使用默认项目名称

如果已配置默认项目名称，只需传入工作目录：

```json
{
  "tool": "analyze_csharp_project",
  "parameters": {
    "workingDirectory": "/path/to/your/project"
  }
}
```

### 2. 指定特定项目名称

```json
{
  "tool": "analyze_csharp_project",
  "parameters": {
    "workingDirectory": "/path/to/your/project",
    "projectName": "SpecificProject.csproj"
  }
}
```

### 3. 自动发现项目

如果工作目录中只有一个.csproj文件，系统会自动发现：

```json
{
  "tool": "analyze_csharp_project",
  "parameters": {
    "workingDirectory": "/path/to/your/project"
  }
}
```

## 优先级

参数的优先级顺序为：

1. `projectName` 参数（最高优先级）
2. `DEFAULT_PROJECT_NAME` 环境变量
3. `--project=` 命令行参数
4. 自动发现单个.csproj文件（最低优先级）

## 错误处理

- 如果指定的项目文件不存在，会返回错误信息
- 如果工作目录中有多个.csproj文件且未指定项目名称，会列出所有可用的项目文件
- 如果工作目录中没有.csproj文件，会返回相应的错误信息