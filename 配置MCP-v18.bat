@echo off
chcp 65001 >nul
rem 一键把本 MCP 注册进已安装的 AI 客户端（WorkBuddy / Claude Desktop / Claude Code / Cursor / VS Code），**V18**。
rem V20 用户改用 配置MCP-v20.bat，V21 用户用 配置MCP.bat。
rem 自动写入正确的 exe 路径并合并到现有配置（保留你已有的其它 MCP server，原配置自动备份为 *.bak）。
rem 不需要管理员权限；改完请重启客户端。
if not exist "%~dp0install-mcp.ps1" (
    echo [错误] 找不到 install-mcp.ps1。请整包解压，不要单拷 bat。
    pause
    exit /b 1
)
if not exist "%~dp0tools\tiaportal-mcp\src\TiaMcpServer\bin-v18\Release\net48\TiaMcpServer.exe" (
    echo [错误] 找不到 V18 引擎 exe。请确认本脚本和 tools 文件夹在同一目录（整包解压，不要单拷 bat）。
    pause
    exit /b 1
)

rem 安装器自己会探测本机装了哪些 TIA 版本并选对应构建；这里固定 V18 以免选错。
powershell -ExecutionPolicy Bypass -File "%~dp0install-mcp.ps1" -TiaMajorVersion 18 %*

echo.
echo 完成后请重启对应 AI 客户端。
echo 提示：模型较弱或客户端限工具数（如 VS Code）时，上面加 -- 之后的参数改成 -Lite
echo 提示：想先看它要改什么，加 -DryRun
echo 提示：其它未自动写入的宿主，用 install-mcp.ps1 -Client cursor 指定后再跑一次即可。
pause