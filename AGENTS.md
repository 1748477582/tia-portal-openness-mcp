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

```powershell
# 1) 装 MCP 到本机客户端（自动探测 TIA 版本、一份配置切版本）
powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -DryRun     # 先看它要改什么
powershell -ExecutionPolicy Bypass -File install-mcp.ps1             # 真装

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

全绿 = 环境就绪。任何一步红，先去 `docs/lessons-and-shortcuts.md` 搜报错关键词（那里面全是踩过的坑）。

---

## 2. 必读顺序（按需，不必一次读完）

| 顺序 | 文件 | 作用 | 什么时候读 |
|---|---|---|---|
| 1 | **`AGENTS.md`**（本文） | 入口与硬规则 | 第一次接触 |
| 2 | **`docs/INDEX.md`** | **全部内容的目录**（文档/脚本/清单各是什么、何时用、需要什么环境） | 想找"有没有现成的" |
| 3 | **`docs/lessons-and-shortcuts.md`** | 踩过的坑与捷径（症状/根因/做法/证据） | **动手前**，尤其碰 TIA、推送、PowerShell |
| 4 | `tools/tiaportal-mcp/skill/SKILL.md` | 工具分层、参数陷阱、HMI schema、调用顺序 | 要实际调工具之前 |
| 5 | `docs/tool-capability-matrix.md` | 232 个工具的能力矩阵（静态索引） | 找具体工具 |
| 6 | `docs/CLI_quickstart.md` | 同一个 exe 的命令行用法（`tia` 子命令） | 想用命令行而非 MCP |
| 7 | `CHANGELOG.md` | 每个版本改了什么、有哪些行为变更 | 升级/排查回归 |

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

细节、证据、绕法全在 `docs/lessons-and-shortcuts.md`（§0 / §1 / §7 / §12）。

---

## 4. 目录地图

| 路径 | 是什么 |
|---|---|
| `install-mcp.ps1` | 单文件安装器（探测 TIA → 装进客户端配置 → 体检） |
| `配置MCP*.bat` | 按版本一键配置（V18 / V20 / V21） |
| `README.md` / `README.en.md` | 对外介绍、安装、版本说明 |
| `CHANGELOG.md` | 版本变更（含行为变更） |
| `AGENTS.md` | **本文**：agent 入口 |
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
| 改一个工具 | 硬规则 #4 + `docs/lessons-and-shortcuts.md` §4.5 的完整链路 |
| 改完推送 | `python tools/tiaportal-mcp/push_repo.py --plan` → `--push --message-file <msg>` → 再 `--plan` 确认零差异 |
| 真机验证（不碰用户 TIA） | `docs/lessons-and-shortcuts.md` §9 + `_upstream_diff/verify_*.py` 骨架 |
| 遇到报错 | 先搜 `docs/lessons-and-shortcuts.md`，再搜 `CHANGELOG.md` |

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
GitHub token、本机代理端口。这三样都写进了 `docs/lessons-and-shortcuts.md`，照着配即可。
