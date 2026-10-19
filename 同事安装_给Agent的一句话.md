# 给同事的 agent：一句话安装 TIA Portal MCP

> 下面每一段都是**可以整段复制给他人 agent**的指令。先复制「一句话版」，不够用再用「完整版」。

---

## 一句话版（推荐，最短）

```
帮我装 TIA Portal MCP：下载 https://github.com/1748477582/tia-portal-openness-mcp/archive/refs/heads/master.zip
解压到 %USERPROFILE%\Downloads\tia-mcp，然后在解压目录里运行
powershell -ExecutionPolicy Bypass -File install-mcp.ps1
装完把脚本输出贴给我，并告诉我应该重启哪个客户端。
```

**如果他的 agent 已经在终端里、能自己下载解压**，用这条更短的：

```
用 PowerShell 下载并解压 https://github.com/1748477582/tia-portal-openness-mcp/archive/refs/heads/master.zip 到 %USERPROFILE%\Downloads\tia-mcp，
然后运行其中的 install-mcp.ps1（powershell -ExecutionPolicy Bypass -File），把输出总结给我。
```

---

## 完整版（需要指定客户端 / 精简工具集 / 只看计划）

```
帮我安装 TIA Portal MCP，做这几步：

1. 下载 https://github.com/1748477582/tia-portal-openness-mcp/archive/refs/heads/master.zip
   解压到 %USERPROFILE%\Downloads\tia-mcp（仓库是公开的，不需要 token）。
2. 先跑一次干跑确认它会改什么：
   powershell -ExecutionPolicy Bypass -File %USERPROFILE%\Downloads\tia-mcp\install-mcp.ps1 -DryRun
   把输出贴给我看。
3. 确认无误后正式安装（把 <client> 换成他实际用的客户端）：
   powershell -ExecutionPolicy Bypass -File %USERPROFILE%\Downloads\tia-mcp\install-mcp.ps1 -Client <client>
   <client> 可选：workbuddy | claude | claude-code | cursor | vscode | all（默认 all＝所有已装的）
4. 如果他的模型较弱或客户端限制工具数量（VS Code 上限 128 个工具），加 -Lite，只暴露约 40 个核心工具。
5. 安装完成后提醒他重启 AI 客户端（配置在客户端启动时读取）。
6. 验收：重启后让 agent 调用 tia-portal 的 Bootstrap 工具，应返回连接状态；
   若报连不上，让他跑 <解压目录>\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48\TiaMcpServer.exe doctor
   （V18 用户换成 bin-v18 目录）。

注意：
- 需要 Windows + 已安装 TIA Portal V18/V20/V21（含 Openness 选项），不需要管理员权限。
- 脚本会自动挑选与本机 TIA 版本匹配的构建；装了多个版本时默认用最高版本，
  想指定就加 -TiaMajorVersion 18。
- 脚本改配置前会自动把原文件备份成 *.bak。
```

---

## 卸载 / 回退

```
powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -Client <client> -Remove
```
只删 `tia-portal` 条目，同文件里其它 MCP 配置保持不变（备份在 `*.bak`）。

---

## 各客户端的配置文件位置（脚本会自动找，一般不用管）

| 客户端 | 配置文件 |
|---|---|
| WorkBuddy | `%USERPROFILE%\.workbuddy\mcp.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Claude Code | `%USERPROFILE%\.claude.json` |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` |
| VS Code | `%APPDATA%\Code\User\mcp.json` |

---

## 常见卡点

| 现象 | 原因与处理 |
|---|---|
| 提示"未找到 TIA Portal" | 装的是 V16/V17（本 MCP 需要 **V18+**，V16/V17 没有 Openness）。装 V18+ 或设 `setx TiaPortalLocation "C:\Program Files\Siemens\Automation\Portal V18"` |
| 提示"命令无法识别"类乱码 | 让 agent 用 `powershell -ExecutionPolicy Bypass -File <路径>\install-mcp.ps1` 而不是双击 |
| 装完客户端里看不到工具 | **必须重启客户端**；配置只在启动时读取 |
| 工具太多导致模型不加载 | 加 `-Lite`（约 40 个核心工具） |
| 连不上 TIA | 跑 `TiaMcpServer.exe doctor`，加 `--fix` 可自动修 Openness 用户组权限 |

---

## 给 agent 的补充约束（可选，粘上更稳）

```
安装过程中：
- 不要跳过 -DryRun 先看计划；
- 不要用 git clone（仓库里有二进制 exe，ZIP 更稳）；
- 不要把 GitHub token、API key 之类写进任何配置文件；
- 安装失败时把**完整输出**贴给我，不要只说"失败了"。
```