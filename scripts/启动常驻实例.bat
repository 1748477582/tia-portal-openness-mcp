@echo off
chcp 65001 >nul
setlocal
rem 预热一个常驻 headless TIA 实例：跑一次留着，之后每次 tia 命令约 1 秒连上。
rem 按 Ctrl+C 停止并关闭该实例。
rem ⚠️ 单实例锁冲突（2026-09-17 起）：本脚本运行期间持有 TiaMcpServer.SingleAttach.V<n> 互斥体，
rem    期间 WorkBuddy/Cursor 等 AI 客户端的 MCP Connect 会被拒绝（报"另一个实例已占用"）。
rem    用 AI 连接器干活时不要跑本脚本；TIA 已开着时服务器本来就会 ~1s 自动 attach，无需预热。
set "EXE=%~dp0..\tools\tiaportal-mcp\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
if not exist "%EXE%" set "EXE=%~dp0..\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48\TiaMcpServer.exe"
if not exist "%EXE%" (
  echo 找不到 tia 可执行文件（V21/V20 均不存在）。
  pause
  exit /b 2
)
echo 正在冷启动 headless TIA 并保活，按 Ctrl+C 停止...
echo 注意：保活期间 AI 客户端的 MCP Connect 会被单实例锁拒绝。
"%EXE%" prewarm
