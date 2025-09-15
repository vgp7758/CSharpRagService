@echo off
echo 启动 C# MCP 服务...
echo.

REM 设置工作目录
cd /d "%~dp0\CSharpMcpService\bin\Debug\net8.0-temp"

REM 检查可执行文件是否存在
if not exist "CSharpMcpService.dll" (
    echo 错误: 找不到 CSharpMcpService.dll
    echo 请先编译项目: cd CSharpMcpService && dotnet build --configuration Debug --output ./bin/Debug/net8.0-temp
    pause
    exit /b 1
)

REM 设置环境变量
set USE_ADVANCED_EMBEDDING=true

echo 启动参数:
echo   - 高级嵌入: %USE_ADVANCED_EMBEDDING%
echo.
echo C# MCP 服务正在启动...
echo 按 Ctrl+C 退出服务
echo.

REM 启动服务
dotnet CSharpMcpService.dll

echo.
echo 服务已停止
pause