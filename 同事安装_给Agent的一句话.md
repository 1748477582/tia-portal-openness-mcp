---
summary: 给同事的安装指令：前提条件、一条可复制的一行命令、装前自检、换版本与卸载、公司网络注意、离线安装
kind: entry
when: 别人的机器要装这套 MCP 时（把「一行安装」整段发过去即可）
env: none
---

# 给同事：复制一条命令就能装

## 一、前提条件（先确认，省得装到一半卡住）

| 项 | 要求 | 说明 |
|---|---|---|
| 系统 | Windows 10 1903+ / 11（x64） | 引擎基于 .NET Framework 4.8 |
| **TIA Portal** | **V18 / V20 / V21，安装时勾了 Openness** | V16/V17 不支持；没勾 Openness 也装不了 |
| .NET Framework | 4.8（系统自带） | — |
| 磁盘 | 约 40 MB | 包 17 MB + 解压 |
| 网络 | 能访问 `codeload.github.com` 或 `api.github.com` | 全不通就看第六节离线安装 |
| 管理员权限 | **安装不需要** | 只有 `doctor --fix` 补 Openness 用户组时需要 |

**会改动什么**：只改 5 个客户端配置里的 `mcpServers["tia-portal"]` 一项（WorkBuddy / Claude Desktop / Claude Code / Cursor / VS Code，
只处理已存在的那几个），改前自动备份 `*.bak`；下载时另外建 `%USERPROFILE%\tia-mcp`。都能回滚（见第五节）。

## 二、一行安装（首选）

把下面**整段**复制给对方，粘给他的 AI 或直接在 PowerShell 里执行。
**不依赖 `raw.githubusercontent.com`**（很多网络封它），**也不用 `iex`**（企业组策略常禁）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing 'https://codeload.github.com/1748477582/tia-portal-openness-mcp/zip/refs/heads/master' -OutFile .\tia-mcp.zip; Expand-Archive .\tia-mcp.zip -DestinationPath .\tia-mcp-pkg -Force; & .\tia-mcp-pkg\tia-portal-openness-mcp-master\install-mcp.ps1 -TiaMajorVersion 20"
```

把末尾的 `20` 换成 18 / 20 / 21。**去掉 `-TiaMajorVersion 20`**，它会在终端里让你确认探测结果并选版本——不替你选。

装完**必须重启 AI 客户端**（配置在启动时读取）。

## 三、装之前建议先自检（只读，不改任何东西）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing 'https://codeload.github.com/1748477582/tia-portal-openness-mcp/zip/refs/heads/master' -OutFile .\tia-mcp.zip; Expand-Archive .\tia-mcp.zip -DestinationPath .\tia-mcp-pkg -Force; & .\tia-mcp-pkg\tia-portal-openness-mcp-master\bootstrap.ps1 -Check"
```

它会报告：PowerShell 版本、.NET 4.8、执行策略、TIA 版本、磁盘、**四条下载通道哪条能通**、哪些客户端会被改，并给结论。

## 四、给 AI 的自然语言版（不想贴命令时）

```
帮我装 TIA Portal MCP，执行：
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing 'https://codeload.github.com/1748477582/tia-portal-openness-mcp/zip/refs/heads/master' -OutFile .\tia-mcp.zip; Expand-Archive .\tia-mcp.zip -DestinationPath .\tia-mcp-pkg -Force; & .\tia-mcp-pkg\tia-portal-openness-mcp-master\install-mcp.ps1"

它会先列出本机装了哪些 TIA —— 把列表贴给我，我告诉你用哪个版本，然后你加 -TiaMajorVersion <版本> 再跑一次。
装完提醒我重启客户端。
```

## 五、装完以后 / 回滚

| 我要… | 怎么做 |
|---|---|
| 换 TIA 版本 | `%USERPROFILE%\tia-mcp` 里运行 `switch-version.ps1`（列出本机版本让你选，只改 `TIA_MCP_VERSION`） |
| 只看装了什么 | `switch-version.ps1 -Show` |
| 只暴露约 40 个核心工具 | 安装时加 `-Lite` |
| **卸载** | `install-mcp.ps1 -Remove`（只删 `tia-portal`，其它 MCP 配置不动），再删 `%USERPROFILE%\tia-mcp` |
| 体检 | `...\tia-mcp\tools\tiaportal-mcp\src\TiaMcpServer\bin-vXX\Release\net48\TiaMcpServer.exe doctor` |

## 六、公司网络 / 受管机器（这些不是脚本问题）

| 现象 | 原因 | 处理 |
|---|---|---|
| 下载的 `.ps1` 打不开、内容乱码 | 终端 DLP 把脚本加密了（亿赛通一类，按内容/文件名触发） | 换未受管机器下载后拷过来；或让 IT 加白。**别怀疑脚本坏了** |
| `raw.githubusercontent.com` 超时 | 该域名常被墙（`codeload`/`api`/`github.com` 可能仍通） | 用第二节那种整包写法，不碰 raw |
| `iex` / `Invoke-Expression` 被拦 | 组策略禁止动态执行 | 本仓库的一行命令**不用** iex，直接下载成文件再执行 |
| 要走代理 | — | 先 `set HTTPS_PROXY=http://host:port` 再跑；`bootstrap.ps1` 会自动带上 |
| **完全不能上网** | 内网隔离 | 见下 |

**离线安装**：在能上网的机器下 `https://github.com/1748477582/tia-portal-openness-mcp/archive/refs/heads/master.zip`，
拷到目标机器解压到 `%USERPROFILE%\tia-mcp`，然后运行 `powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -TiaMajorVersion 20`。

## 七、各客户端的配置文件位置（脚本自动找，一般不用管）

| 客户端 | 配置文件 |
|---|---|
| WorkBuddy | `%USERPROFILE%\.workbuddy\mcp.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Claude Code | `%USERPROFILE%\.claude.json` |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` |
| VS Code | `%APPDATA%\Code\User\mcp.json` |
