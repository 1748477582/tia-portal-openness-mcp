---
summary: 给同事的安装指令：一条可复制的一行命令、给 AI 的自然语言版、换版本与卸载、常见卡点
kind: entry
when: 别人的机器要装这套 MCP 时（把「一行安装」整段发过去即可）
env: none
---

# 给同事：复制一条命令就能装

## 一、一行安装（首选）

把下面**整段**复制给对方，粘给他的 AI 或直接在 PowerShell 里执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/1748477582/tia-portal-openness-mcp/master/bootstrap.ps1' -OutFile .\tia-bootstrap.ps1; & .\tia-bootstrap.ps1"
```

它会：下载交付包 → 探测本机装了哪些 TIA → **把版本列表打印出来**，并给出"填好版本号的下一行"。
**它不会替你选版本**——这是刻意的。

想一步到位（你已知道用哪个版本），在末尾加 `-Version 20`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/1748477582/tia-portal-openness-mcp/master/bootstrap.ps1' -OutFile .\tia-bootstrap.ps1; & .\tia-bootstrap.ps1 -Version 20"
```

不钉版本、跟随当前打开的 TIA：把 `-Version 20` 换成 `-FollowOpenTia`。

装完**必须重启 AI 客户端**（配置在启动时读取）。

## 二、两步走（想知道本机有哪些版本时）

第一步跑上面那条**不带版本**的命令，它会打印类似：

```
  本机探测到的 TIA Portal：
    V18  C:\Program Files\Siemens\Automation\Portal V18   [可用]
    V20  D:\Program Files\Siemens\Automation\Portal V20   [可用]

  下一步：从下面选一条复制执行（把版本换成你要用的那个）
    V18: powershell ... & .\tia-bootstrap.ps1 -Version 18
    V20: powershell ... & .\tia-bootstrap.ps1 -Version 20
```

第二步：把要用的那条复制执行即可。

## 三、给 AI 的自然语言版（不想贴命令时）

```
帮我装 TIA Portal MCP，执行：
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/1748477582/tia-portal-openness-mcp/master/bootstrap.ps1' -OutFile .\tia-bootstrap.ps1; & .\tia-bootstrap.ps1"

它会先列出本机装了哪些 TIA —— 把列表贴给我，我告诉你用哪个版本，然后你用 -Version <版本> 再跑一次。
装完提醒我重启客户端。
```

## 四、装完以后

| 我要… | 怎么做 |
|---|---|
| 换 TIA 版本 | 在 `%USERPROFILE%\tia-mcp` 里运行 `switch-version.ps1`（列出本机版本让你选，只改 `TIA_MCP_VERSION`） |
| 只看装了什么 | `switch-version.ps1 -Show` |
| 只暴露约 40 个核心工具 | 安装时加 `-Lite` |
| 卸载 | `install-mcp.ps1 -Remove`（只删 `tia-portal`，其它 MCP 配置不动） |
| 体检 | `%USERPROFILE%\tia-mcp\tools\tiaportal-mcp\src\TiaMcpServer\bin-vXX\Release\net48\TiaMcpServer.exe doctor` |

## 五、常见卡点

| 现象 | 处理 |
|---|---|
| 报"未找到 TIA Portal" | 多半装在非标准目录：`setx TiaPortalLocation "D:\Siemens\Automation\Portal V20"`，**重开终端**再跑 |
| 下载不了 `raw.githubusercontent.com` | 改用 ZIP 直链 `https://github.com/1748477582/tia-portal-openness-mcp/archive/refs/heads/master.zip`，解压后跑 `install-mcp.ps1` |
| 装完客户端里看不到工具 | **必须重启客户端**；配置只在启动时读取 |
| 工具太多导致模型不加载 | 加 `-Lite` |
| 连不上 TIA | 跑 `TiaMcpServer.exe doctor`；加 `--fix` 可自动补 Openness 用户组 |
| 只装了 V20 但工具起不来 | 启动器在"没开 TIA"时兜底 V18。用 `-Version 20` 钉住，或先打开 TIA |

## 六、各客户端的配置文件位置（脚本自动找，一般不用管）

| 客户端 | 配置文件 |
|---|---|
| WorkBuddy | `%USERPROFILE%\.workbuddy\mcp.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Claude Code | `%USERPROFILE%\.claude.json` |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` |
| VS Code | `%APPDATA%\Code\User\mcp.json` |
