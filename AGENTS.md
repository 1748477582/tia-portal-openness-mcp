---
summary: 任何 agent / 任何人接手本仓库时的唯一入口：这是什么、先读什么、怎么自检、硬规则、加东西时的义务
kind: entry
when: 第一次接触本仓库（换电脑、换 agent、换人）时最先读这一份
env: none
---

# AGENTS.md — 接手本仓库的入口

> **这份文件是给"下一个我"写的**：可能是换了一台电脑、重装了一个 AI agent、或者换了个人。
> 目标：**不需要问任何人，也不依赖任何聊天记录**，就能知道这里有什么、怎么跑、哪些不能碰。
>
> 如果你只读一份文件，就是这一份；读完按 §1 走四步即可。

---

## 0. 这是什么

**TIA Portal Openness MCP 服务**：把西门子 TIA Portal（V18 / V20 / V21）的工程操作暴露成 MCP 工具，
让任意 MCP 客户端（WorkBuddy / Claude / Cursor / VS Code）用自然语言驱动 PLC 工程：建工程、导块、编译、下载、在线读值。

- **源码在同一份树里**，三个 csproj 对应三个 TIA 版本：`TiaMcpServer.V18.csproj` / `.V20.csproj` / `TiaMcpServer.csproj`(V21)
- **二进制不能跨版本通用**（强命名程序集绑定），所以有三个输出目录 `bin-v18/` `bin-v20/` `bin/`
- **当前版本 2.5.0**，工具 **232 个**（V18 构建 204 个：隐藏 28 个 V18 不支持的能力）

---

## 1. 换电脑 / 第一次上手：照这四步

> **如果只是要给别人装**（不是接手开发），用 README 顶部那条**一行命令**即可，不必读本节。
> 一行命令 = `bootstrap.ps1`：不带版本只探测并给出下一条命令；带 `-Version 20` 直接装。

```powershell
# 1) 装 MCP：先探测本机装了哪些 TIA → 让你确认 → 让你选版本（不会替你预设）
powershell -ExecutionPolicy Bypass -File install-mcp.ps1

#   · 非交互/脚本化时必须显式给版本，否则会拒绝（这是故意的：不替你猜）
powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -TiaMajorVersion 20
powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -FollowOpenTia
#   · 只看计划不写文件：
powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -DryRun -FollowOpenTia

# 2) 自检：引擎与 TIA 是否对得上
<交付根>\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48\TiaMcpServer.exe doctor

# 3) 验仓库自身没坏（四条）
python scripts\Check-DeadToolReferences.py
python scripts\Check-ManifestConsistency.py
python scripts\Check-VersionConsistency.py
python scripts\Generate-DocIndex.py --check

# 4) 跑离线测试（不需要 TIA）
dotnet run --project tools\tiaportal-mcp\tests\TiaMcpServer.OfflineChecks\TiaMcpServer.OfflineChecks.csproj -c Release
```

**换 TIA 版本**（装完以后随时可换，改一个变量即可）：

```powershell
powershell -ExecutionPolicy Bypass -File switch-version.ps1              # 交互选
powershell -ExecutionPolicy Bypass -File switch-version.ps1 -Show       # 只看当前
powershell -ExecutionPolicy Bypass -File switch-version.ps1 -TiaMajorVersion 18
powershell -ExecutionPolicy Bypass -File switch-version.ps1 -FollowOpenTia
```

全绿 = 环境就绪。任何一步红，先去 内部经验库（不随仓库发布） 搜报错关键词（那里面全是踩过的坑）。

**前置条件（别误判成"仓库坏了"）**：
- 第 1–2 步只需要 **TIA Portal V18+（含 Openness）+ PowerShell**；**只安装并使用 MCP 的话，到第 2 步就够了**。
- 第 3–4 步需要 **.NET SDK 8** 与 **Python 3.8+**。新电脑上没有这两个，跳过即可，不影响使用。
- 第 1 步若报 **"未找到 TIA Portal"**：多半装在非标准目录。用 `setx TiaPortalLocation "<安装目录>"`（如 `D:\Siemens\Automation\Portal V20`）指过去，重开终端再跑。
- 本机**只装一个版本**时，安装器会**自动钉住**该版本——因为启动器的探测兜底写死 V18，不钉会在"没开 TIA"时错误加载 V18 构建（V18 目录不存在 → 起不来）。多版本机器则不钉，由启动器跟随当前打开的 TIA。

---

## 2. 必读顺序（按需，不必一次读完）

| 顺序 | 文件 | 作用 | 什么时候读 |
|---|---|---|---|
| 1 | **`AGENTS.md`**（本文） | 入口与硬规则 | 第一次接触 |
| 2 | **内部约束清单（不随仓库发布）** | **用户明确交代过的硬约束**（验证方式、安全、流程、文风、存储） | **每次开工前扫一遍；交付前逐条自查** |
| 3 | **`docs/INDEX.md`** | **全部内容的目录**（文档/脚本/清单各是什么、何时用、需要什么环境） | 想找"有没有现成的" |
| 4 | **内部经验库（不随仓库发布）** | 踩过的坑与捷径（症状/根因/做法/证据） | **动手前**，尤其碰 TIA、推送、脚本 |
| 5 | `tools/tiaportal-mcp/skill/SKILL.md` | 工具分层、参数陷阱、HMI schema、调用顺序 | 要实际调工具之前 |
| 6 | `docs/tool-capability-matrix.md` | 232 个工具的能力矩阵（静态索引） | 找具体工具 |
| 7 | `docs/CLI_quickstart.md` | 同一个 exe 的命令行用法（`tia` 子命令） | 想用命令行而非 MCP |
| 8 | `CHANGELOG.md` | 每个版本改了什么、有哪些行为变更 | 升级/排查回归 |

**想找"有没有现成脚本/文档"**：先看 `docs/INDEX.md`（自动生成，50 项，带"何时用/环境"），别翻目录。

---

## 3. 硬规则（违反会造成真实损失）

| # | 规则 | 违反的后果 |
|---|---|---|
| 1 | **绝不关掉用户正在用的 TIA**：杀进程只按"我启动的进程树"（`_iso_cleanup.safe_cleanup(engine_pid)`，引擎 PID 未知则什么都不杀）；**对 `Attach()` 得到的门户永不 `Dispose()`** | 用户未保存改动丢失（发生过两次） |
| 2 | **绝不跨线程传出 Openness RCW**：属性在 `_sta.Run` 内提成字符串/字典；编译消息走 `CollectCompilerMessagesOnSta` | 进程无异常直接消失（`0xE0434352`） |
| 3 | **推送前必须做「新旧方向」审计**：本地部分文件是远端**子集**（V20/V21 的 csproj 只差版本行），只能外科手术改一行 | 静默回退远端较新内容 |
| 4 | **改工具 = 同步三份**：引擎 `tools/list` = `manifest/tools-list.json` = `docs/tool-capability-matrix.md`（改描述也要） | CI 红；或文档与实际不符 |
| 5 | **遇到 know-how 加密块先问用户要密码**，不做字典/枚举；不为解保护新增工具 | 越权，且用户明确拒绝过 |
| 6 | **别用 `git push`**：本机到 github.com:443 不通，推送走 Git Database API（`tools/tiaportal-mcp/push_repo.py`），且必须设 `HTTPS_PROXY` | 推送失败，或误以为"推上去了" |
| 7 | **用户明确交代过的事是硬约束**：见 内部约束清单（不随仓库发布）。核心几条——独立验证用户自述、验证深度如实说、摘要必须与磁盘一致、仓库与文档去 AI 腔、写操作前先确认 | 这几条都违反过，且都付了代价 |

细节、证据、绕法全在 内部经验库（不随仓库发布）（§0 / §1 / §7 / §12）；用户要求逐条见 内部约束清单（不随仓库发布）。

---

## 4. 目录地图

| 路径 | 是什么 |
|---|---|
| `install-mcp.ps1` | 单文件安装器（探测 TIA → 装进客户端配置 → 体检） |
| `配置MCP*.bat` | 按版本一键配置（V18 / V20 / V21） |
| `README.md` / `README.en.md` | 对外介绍、安装、版本说明 |
| `CHANGELOG.md` | 版本变更（含行为变更） |
| `AGENTS.md` | **本文**：agent 入口 |
| `CLAUDE.md` / `llms.txt` / `.cursorrules` / `.cursor/rules/tia-mcp.mdc` / `.github/copilot-instructions.md` | **各家 agent 的自动读取约定**：都是薄壳，只指向 `AGENTS.md`（内容只保留一处，避免副本漂移） |
| `docs/` | 使用手册、指令库、能力矩阵、**INDEX.md**、**lessons-and-shortcuts.md** |
| `scripts/` | 闸门（`Check-*.py`）、生成器（`Generate-*`）、冒烟脚本 |
| `manifest/` | `tools-list.json`（工具权威清单）、`package-manifest.json`（版本/能力快照） |
| `tools/tiaportal-mcp/src/TiaMcpServer/` | 源码（三个 csproj + `Siemens/` + `ModelContextProtocol/`） |
| `tools/tiaportal-mcp/skill/SKILL.md` | 给模型看的技能文档（工具分层与调用顺序） |
| `tools/tiaportal-mcp/tia_mcp_launcher.bat` | 多版本启动器（读 `TIA_MCP_VERSION` 选构建） |
| `runtime/` | 预置运行时（v18 / v20 / v21 各一套） |
| `.github/workflows/` | CI（离线闸门，不需要 TIA） |

---

## 5. 我要新增东西时的义务（**这条保证索引不会烂掉**）

1. **文件开头写 front-matter**（4 行，见任意 `docs/*.md` 的开头）：
   ```yaml
   ---
   summary: 一句话说清这是什么（≤120 字）
   kind: doc | script | entry | manifest | skill
   when: 什么时候该读/该跑
   env: none | python | dotnet | tia
   ---
   ```
2. **跑 `python scripts/Generate-DocIndex.py`** 重新生成 `docs/INDEX.md` 与 `docs/index.json`。
3. **跑 `python scripts/Generate-DocIndex.py --check`**，必须返回 0（CI 的 `content-index` 也会查）。
4. 如果新增了**工具**：按硬规则 #4 同步三份，并跑闸门。
5. 如果改了**行为**（默认值、报错语义、拒绝规则）：在 `CHANGELOG.md` 的"变更"里写清楚，别只写在提交信息里。

> 第 1 步漏了不会失败（会用正文首段兜底），但**第 2 步漏了 CI 会红**——故意的：
> 索引一旦能和实际文件脱节，就等于没有索引。

---

## 6. 常见任务从哪进

| 我要… | 去哪 |
|---|---|
| 给同事/新机器装 | `install-mcp.ps1`（`-DryRun` 先看），说明见 `同事安装_给Agent的一句话.md` |
| 查某个工具的参数/是否存在 | `FindTools(query)`；或 `docs/tool-capability-matrix.md` |
| **换 TIA 版本** | `switch-version.ps1`（列出本机版本让你选；只改 `TIA_MCP_VERSION`，不碰其它配置） |
| 改一个工具 | 硬规则 #4 + 内部经验库（不随仓库发布） §4.5 的完整链路 |
| 改完推送 | `python tools/tiaportal-mcp/push_repo.py --plan` → `--push --message-file <msg>` → 再 `--plan` 确认零差异 |
| 真机验证（不碰用户 TIA） | 内部经验库（不随仓库发布） §9 + `_upstream_diff/verify_*.py` 骨架 |
| 遇到报错 | 先搜 内部经验库（不随仓库发布），再搜 `CHANGELOG.md` |

---

## 7. 交接清单（换电脑时该带走什么）

| 必须 | 说明 |
|---|---|
| **整个仓库/ZIP** | 含源码、`runtime/` 预编译引擎、文档、脚本。**只要这个就能跑** |
| TIA Portal 安装 | V18 或 V20（含 Openness 选项）。**V16/V17 不支持** |
| .NET Framework 4.8 | Windows 10/11 自带 |
| （可选）Python 3.8+ | 只有跑闸门/生成器/验证脚本时需要 |
| （可选）GitHub token | 只有要**推送**时才需要：设 `GITHUB_TOKEN`，或用 `TIA_MCP_TOKEN_FILE` 指向含 `ghp_...` 的文件。**token 不进仓库** |

**不在仓库里的东西**（换机器会丢，需要重新配置）：`~/.workbuddy/mcp.json` 里的连接器条目（用 `install-mcp.ps1` 重装）、
GitHub token、本机代理端口。这三样都写进了 内部经验库（不随仓库发布），照着配即可。
