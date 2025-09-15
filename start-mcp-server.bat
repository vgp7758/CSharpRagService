@echo off
echo 启动 C# MCP 服务...
echo.

REM 设置工作目录
cd /d "%~dp0\CSharpMcpService\bin\Debug\net8.0"

REM 检查DLL文件是否存在
if not exist "CSharpMcpService.dll" (
    echo 错误: 找不到 CSharpMcpService.dll
    echo 请先编译项目: cd CSharpMcpService && dotnet build
    pause
    exit /b 1
)

REM 设置环境变量
set USE_ADVANCED_EMBEDDING=true

REM 获取命令行参数
set PROJECT_NAME=
set ADVANCED_EMBEDDING=
for %%a in (%*) do (
    if "%%a"=="--advanced-embedding" set ADVANCED_EMBEDDING=1
    if "%%a"=="--simple-embedding" set USE_ADVANCED_EMBEDDING=false
    echo %%a | findstr /c:"--project=" >nul
    if not errorlevel 1 set PROJECT_NAME=%%a
)

REM 处理项目名称参数
if not "%PROJECT_NAME%"=="" (
    set PROJECT_NAME=%PROJECT_NAME:--project=%
    echo 将使用默认项目: %PROJECT_NAME%
)

REM 处理高级嵌入参数
if not "%ADVANCED_EMBEDDING%"=="" (
    set USE_ADVANCED_EMBEDDING=true
    echo 启用高级嵌入模式
)

echo 启动参数:
echo   - 高级嵌入: %USE_ADVANCED_EMBEDDING%
if not "%PROJECT_NAME%"=="" (
    echo   - 默认项目: %PROJECT_NAME%
)
echo.
echo C# MCP 服务正在启动...
echo 按 Ctrl+C 退出服务
echo.

REM 构建启动命令
set CMD_LINE=dotnet CSharpMcpService.dll
if not "%PROJECT_NAME%"=="" (
    set CMD_LINE=%CMD_LINE% --project=%PROJECT_NAME%
)
if "%USE_ADVANCED_EMBEDDING%"=="true" (
    set CMD_LINE=%CMD_LINE% --advanced-embedding
)

REM 启动服务
%CMD_LINE%

echo.
echo 服务已停止
pause