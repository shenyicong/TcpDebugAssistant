@echo off
chcp 65001 >nul 2>&1
cd /d "%~dp0"

set "DOTNET=C:\Users\零柒\.dotnet8\dotnet.exe"
set "DLL=bin\Debug\net8.0-windows\TcpDebugAssistant.dll"

if not exist "%DOTNET%" (
    echo [错误] 未找到本地 .NET 运行环境：
    echo   %DOTNET%
    echo 请先安装 .NET 8 SDK，或让 WorkBuddy 助手重新配置。
    pause
    exit /b 1
)

if not exist "%DLL%" (
    echo [信息] 首次编译中（请稍候十几秒）...
    "%DOTNET%" build -c Debug
    if errorlevel 1 (
        echo [错误] 编译失败，请查看上方输出。
        pause
        exit /b 1
    )
)

echo [信息] 正在启动 TCP 调试助手...
rem 用 start 独立启动 GUI：关闭本 CMD 窗口不会杀死程序，也不会出现黑窗口长时间停留
start "" "%DOTNET%" "%DLL%"
