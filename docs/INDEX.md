# 内容索引（自动生成，别手改）

> 由 `scripts/Generate-DocIndex.py` 生成；`--check` 校验是否过期，CI 的 `content-index` 会查。
> 换机器 / 换 agent 时**先读这里**：每份文件是干什么的、什么时候该用、需要什么环境。
> 新增文件后要重跑生成器，否则索引与实际脱节。

| 共 57 项 | 环境：`none` 纯本机 · `python` 需 Python · `dotnet` 需 .NET SDK · `tia` 需 TIA Portal |

## entry（9）

| 文件 | 说明 | 何时用 | 环境 |
|---|---|---|---|
| [`.cursor/rules/tia-mcp.mdc`](../.cursor/rules/tia-mcp.mdc) | 给对应客户端的入口指针（薄壳）：只指向 AGENTS.md，不重复内容 | — | `none` |
| [`.cursorrules`](../.cursorrules) | 给对应客户端的入口指针（薄壳）：只指向 AGENTS.md，不重复内容 | — | `none` |
| [`.github/copilot-instructions.md`](../.github/copilot-instructions.md) | 给对应客户端的入口指针（薄壳）：只指向 AGENTS.md，不重复内容 | — | `none` |
| [`AGENTS.md`](../AGENTS.md) | 任何 agent / 任何人接手本仓库时的唯一入口：这是什么、先读什么、怎么自检、硬规则、加东西时的义务 | 第一次接触本仓库（换电脑、换 agent、换人）时最先读这一份 | `none` |
| [`CLAUDE.md`](../CLAUDE.md) | 给 Claude 系 agent 的入口指针（薄壳）：只指向 AGENTS.md，不重复内容 | Claude Code / Claude 系客户端自动读取本文件时 | `none` |
| [`README.en.md`](../README.en.md) | English overview - what this MCP is, how to install it (incl. a one-liner for a colleague), version notes and safe defaults | English readers, or a non-Chinese colleague needs the overview | `none` |
| [`README.md`](../README.md) | 项目总览：这套 MCP 是什么、怎么装（含给同事的一句话）、版本说明与安全默认值 | 想知道这个项目能干什么、怎么安装时 | `none` |
| [`llms.txt`](../llms.txt) | 给 LLM/agent 的机器可读入口（llmstxt 约定）：项目是什么、从哪开始、不能碰什么 | — | `none` |
| [`同事安装_给Agent的一句话.md`](../同事安装_给Agent的一句话.md) | 给同事的安装指令：前提条件、一条可复制的一行命令、装前自检、换版本与卸载、离线安装 | 别人的机器要装这套 MCP 时（把「一行安装」整段发过去即可） | `none` |

## doc（21）

| 文件 | 说明 | 何时用 | 环境 |
|---|---|---|---|
| [`CHANGELOG.md`](../CHANGELOG.md) | 版本变更记录：每版的新增 / 变更（含行为变更）/ 修复 / 移除，以及工具总数 | 升级前、排查回归、确认某个行为从哪一版开始变 | `none` |
| [`docs/AI_spec_prompt.md`](AI_spec_prompt.md) | 把下面这段（连同 tia schema 的输出）贴给任意 AI（Claude / GPT / Gemini / 国产模型均可）， | — | `none` |
| [`docs/CLI_quickstart.md`](CLI_quickstart.md) | v2.0 起，交付包里的 同一个 exe 既是 MCP 服务，也是命令行 tia。 | — | `none` |
| [`docs/HMI_Unified_画面生成规范与模板.md`](HMI_Unified_画面生成规范与模板.md) | 本规范用于生成 WinCC Unified 画面、标签、按钮动作和动态化绑定。模板文件位于 templates/hmi/，均为可读、可修改、可直接应用的 designJson。 | — | `none` |
| [`docs/basic-plc-template-library.md`](basic-plc-template-library.md) | 本说明用于配合 templates/plc/ 生成通用 PLC 程序结构。模板覆盖 SCL、LAD 调用网络、变量表、UDT、全局 DB、FC、FB 和 HMI 接口 DB。 | — | `none` |
| [`docs/full-project-generation-runbook.md`](full-project-generation-runbook.md) | 本手册描述从空项目生成 PLC + WinCC Unified 工程的完整流程。所有文件均在交付包内。 | — | `none` |
| [`docs/hmi-connection-driver-matrix.md`](hmi-connection-driver-matrix.md) | 本文件给出 EnsureUnifiedHmiConnection 与 TIA 内创建 HMI 连接时，通讯驱动（CommunicationDriver） 的取值规则。驱动错配会导致 HMI 变量整列红字、运行时无值或连接处显示「未连接」。 | — | `none` |
| [`docs/hmi-plc-tag-binding-and-addressing.md`](hmi-plc-tag-binding-and-addressing.md) | 本包统一以 绝对地址 方式建立 WinCC Unified 与 PLC 的变量互连。原因： | — | `none` |
| [`docs/lad-instruction-library.md`](lad-instruction-library.md) | 本文件汇总在 LAD（梯形图）网络中常见的指令、引脚、操作数与连接方式，配合 tools/tiaportal-mcp/skill/SKILL.md 中已验证的 Part Name 注册表使用。所有示例均为通用语法，不绑定特定工艺。 | — | `none` |
| [`docs/lessons-and-shortcuts.md`](lessons-and-shortcuts.md) | 踩过的坑与捷径：症状 / 根因 / 正确做法 / 证据；含两起关掉用户 TIA 的事故、全部已知平台限制、PowerShell 与推送陷阱 | 动手改代码、碰 TIA、推送、写脚本之前（先搜报错关键词） | `none` |
| [`docs/mcp-ide-and-tool-visibility.md`](mcp-ide-and-tool-visibility.md) | 交付包 不绑定 某一种 IDE（Cursor / VS Code / Claude Desktop / 自建 HTTP 客户端均可）。 | — | `none` |
| [`docs/optional-reference-materials.md`](optional-reference-materials.md) | 交付包 体积受控，不内置大型西门子样板工程。推荐在 本仓库同级或上级 维护目录： | — | `none` |
| [`docs/plc-network-patterns-expanded.md`](plc-network-patterns-expanded.md) | 本页在 docs/basic-plc-template-library.md 与 tools/tiaportal-mcp/skill/SKILL.md（LAD §9–11、SCL §10）基础上，补充 常用网络结构 与 扩展指令方向，便于在… | — | `none` |
| [`docs/scl-instruction-library.md`](scl-instruction-library.md) | 本文件汇总在 S7-1200 / S7-1500 SCL 中常用的语法和指令模板，便于 PlcBuildAndImport(kind=fc\|fb) 的 DSL 转换，或直接写入外部源 .scl 文件后通过 ImportPlcExternal… | — | `none` |
| [`docs/server-maturity-roadmap.md`](server-maturity-roadmap.md) | 服务成熟度路线图：与商业产品逐项对比后的差距清单与推进顺序 | 规划下一步能力时 | `none` |
| [`docs/tool-capability-matrix.md`](tool-capability-matrix.md) | 本文件由源码中的 McpServerTool 静态抽取生成，运行时仍以 tools/list 为准。 | — | `none` |
| [`docs/user-directives.md`](user-directives.md) | 用户明确交代过的要求（硬约束）：交付验证、安全、流程、文风、存储、本仓库专属规矩，附交付前自查清单 | 每次开工前扫一遍；交付前逐条自查。这些不是建议，是用户明确要求过的 | `none` |
| [`docs/verify-low-barrier-features.md`](verify-low-barrier-features.md) | Status date: 2026-06-16. | — | `none` |
| [`docs/在线实时读值_使用指南.md`](在线实时读值_使用指南.md) | 在线实时读值使用指南：如何在不改程序的前提下读取 PLC 当前变量值 | 要在真机上读实时值时 | `none` |
| [`docs/工具使用说明与介绍.md`](工具使用说明与介绍.md) | 一个驱动西门子 TIA Portal（博途） 做自动化工程的工具：连接博途、建项目、加硬件（PLC / WinCC Unified HMI）、生成 PLC 程序（UDT / 全局 DB / 标签表 / SCL / 梯形图）、生成 HMI 画… | — | `none` |
| [`tools/tiaportal-mcp/skill/SKILL.md`](../tools/tiaportal-mcp/skill/SKILL.md) | This is the operating skill for TIA Portal MCP automation. The | — | `none` |

## script（24）

| 文件 | 说明 | 何时用 | 环境 |
|---|---|---|---|
| `bootstrap.ps1` | 一行式安装入口：可直接从 GitHub 拉取并调用 install-mcp.ps1；不带版本时只探测并给出下一条命令（不猜） | 别人要把本 MCP 装到自己机器上时，复制 README 里那条一行命令即可 | `python` |
| `install-mcp.ps1` | 单文件安装器：探测本机 TIA（≥V18）→ 选构建/启动器 → 写进已装客户端配置 → 体检；幂等、改前备份 | 新机器或同事的机器要装这套 MCP 时（先 -DryRun 看计划） | `python` |
| `scripts/Check-DeadToolReferences.py` | 死引用闸：工具描述里点名的工具，必须真的注册过。 | — | `python` |
| `scripts/Check-LiteProfile.py` | lite 档闸门：只拿到精简工具集的模型仍必须能走完黄金路径 | 改 lite 名册或核心工具时 | `python` |
| `scripts/Check-ManifestConsistency.py` | Manifest 一致性闸：两份 manifest 必须彼此自洽。 | — | `python` |
| `scripts/Check-VersionConsistency.py` | 版本一致性闸：csproj 与 package-manifest 的版本号必须彼此一致 | 发版或改版本号时 | `python` |
| `scripts/Generate-DocIndex.py` | 生成并校验 docs/INDEX.md + docs/index.json：把全部文档/脚本/清单编成可检索索引 | 新增或改名任何文档、脚本、清单之后；CI 的 content-index 也会跑 --check | `python` |
| `scripts/Generate-ToolCapabilityMatrix.ps1` | Regenerate docs/tool-capability-matrix.md from the [McpServerTool] attributes in McpServer.cs. | — | `none` |
| `scripts/Generate-ToolsList.py` | Regenerate manifest/tools-list.json from a built MCP engine's real tools/list response. | — | `python` |
| `scripts/Smoke-ArgDiagnostics.py` | 端到端验「参数诊断」接线真的生效（不需要 TIA）。 | — | `python` |
| `scripts/Smoke-ConnectIsolated.py` | ConnectIsolated 的真机冒烟 —— **仅本地手动跑，绝不要放进 CI**。 | — | `python` |
| `scripts/Smoke-ResponseParking.py` | 端到端验「大响应寄存」的接线真的生效（不是只编译通过）。 | — | `python` |
| `scripts/Test-MatchPlcName.ps1` | Offline deterministic test of Guard.MatchPlcName (the tolerant softwarePath matcher). | — | `none` |
| `scripts/Validate-Bundle.ps1` | Offline validation: required files, JSON parse, blueprint bundle list, tool roster count. | — | `none` |
| `scripts/prewarm_tia.py` | Keep a headless TIA Portal warm so every MCP session connects in ~1s instead of cold-starting. | — | `python` |
| `switch-version.ps1` | 切换 MCP 使用的 TIA 版本：列出本机已装版本 → 让用户选 → 改写配置里的 TIA_MCP_VERSION（只改这一项） | 想换一个 TIA 版本工作、或安装时选了"跟随当前 TIA"现在想固定下来 | `python` |
| `tia-v20.cmd` | 命令行入口：同一个 exe 的 tia 子命令（如 doctor） | — | `python` |
| `tia.cmd` | 命令行入口：同一个 exe 的 tia 子命令（如 doctor） | — | `python` |
| `tools/tiaportal-mcp/build.ps1` | TIA MCP build script - outputs UTF-8 log to avoid Chinese garbled text. | — | `tia` |
| `tools/tiaportal-mcp/detect_tia_version.ps1` | Detect which TIA Portal version to serve. | — | `tia` |
| `tools/tiaportal-mcp/tia_mcp_launcher.bat` | 命令行入口：同一个 exe 的 tia 子命令（如 doctor） | — | `python` |
| `配置MCP-v18.bat` | 按 TIA 版本一键把本 MCP 注册进已安装客户端（转调 install-mcp.ps1，改前备份） | — | `python` |
| `配置MCP-v20.bat` | 按 TIA 版本一键把本 MCP 注册进已安装客户端（转调 install-mcp.ps1，改前备份） | — | `python` |
| `配置MCP.bat` | 按 TIA 版本一键把本 MCP 注册进已安装客户端（转调 install-mcp.ps1，改前备份） | — | `python` |

## manifest（3）

| 文件 | 说明 | 何时用 | 环境 |
|---|---|---|---|
| `cursor-mcp.example.json` | MCP 客户端配置示例（Cursor/mcp.json 格式，复制粘贴用） | — | `none` |
| `manifest/package-manifest.json` | 交付包元数据：版本号、能力快照、入口与自检记录 | — | `none` |
| `manifest/tools-list.json` | 工具权威清单（由真引擎 tools/list 生成；改工具后必须重生成） | — | `none` |

---

## 怎么加新内容

1. 文件开头写 front-matter：`summary` / `kind` / `when` / `env`。
2. 跑 `python scripts/Generate-DocIndex.py`。
3. 跑 `python scripts/Generate-DocIndex.py --check`，必须返回 0。

第 1 步漏了不会失败（用正文首段兜底），但**第 2 步漏了 CI 会红**——故意的：
索引一旦能和实际文件脱节，就等于没有索引。
