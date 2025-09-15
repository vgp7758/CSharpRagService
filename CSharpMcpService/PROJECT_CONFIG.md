# C# MCP Service - ��Ŀ����˵��

## ���÷�ʽ

����������ͨ�����¼��ַ�ʽ����Ĭ�ϵ���Ŀ���ƣ�

### 1. ����������ʽ

```bash
export DEFAULT_PROJECT_NAME="MyProject.csproj"
dotnet run
```

### 2. �����в�����ʽ

```bash
dotnet run --project=MyProject.csproj
```

### 3. MCP JSON�����ļ�

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

## ʹ�÷�ʽ

### 1. ʹ��Ĭ����Ŀ����

���������Ĭ����Ŀ���ƣ�ֻ�贫�빤��Ŀ¼��

```json
{
  "tool": "analyze_csharp_project",
  "parameters": {
    "workingDirectory": "/path/to/your/project"
  }
}
```

### 2. ָ���ض���Ŀ����

```json
{
  "tool": "analyze_csharp_project",
  "parameters": {
    "workingDirectory": "/path/to/your/project",
    "projectName": "SpecificProject.csproj"
  }
}
```

### 3. �Զ�������Ŀ

�������Ŀ¼��ֻ��һ��.csproj�ļ���ϵͳ���Զ����֣�

```json
{
  "tool": "analyze_csharp_project",
  "parameters": {
    "workingDirectory": "/path/to/your/project"
  }
}
```

## ���ȼ�

���������ȼ�˳��Ϊ��

1. `projectName` ������������ȼ���
2. `DEFAULT_PROJECT_NAME` ��������
3. `--project=` �����в���
4. �Զ����ֵ���.csproj�ļ���������ȼ���

## ������

- ���ָ������Ŀ�ļ������ڣ��᷵�ش�����Ϣ
- �������Ŀ¼���ж��.csproj�ļ���δָ����Ŀ���ƣ����г����п��õ���Ŀ�ļ�
- �������Ŀ¼��û��.csproj�ļ����᷵����Ӧ�Ĵ�����Ϣ