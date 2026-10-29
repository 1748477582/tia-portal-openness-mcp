# TIA Portal Openness MCP

[中文](README.md) · [English](README.en.md) · [文档索引](docs/INDEX.md) · [变更记录](CHANGELOG.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)
[![TIA Portal](https://img.shields.io/badge/TIA_Portal-V18%20%7C%20V20%20%7C%20V21-009999.svg?style=flat-square)](https://www.siemens.com)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4.svg?style=flat-square)](https://dotnet.microsoft.com)
[![MCP](https://img.shields.io/badge/MCP-stdio-000000.svg?style=flat-square)](https://modelcontextprotocol.io)
[![Tools](https://img.shields.io/badge/tools-232%20%2F%20204-2f8f46.svg?style=flat-square)](#工具)
[![Version](https://img.shields.io/badge/version-2.6.3-blue.svg?style=flat-square)](CHANGELOG.md)

基于 Siemens Openness 的 MCP 服务端，让 AI 客户端（WorkBuddy / Claude Desktop / Cursor / VS Code 等）
直接读写 TIA Portal 工程：读结构、导出块、生成 PLC 程序、改硬件组态、编译诊断、跑工程体检。

同时维护 **V18 / V20 / V21** 三个构建，**一条连接器**按打开的工程版本自动选引擎，不需要手工切换。
仓库自带已编译运行时，下载解压即用。

---

## 工具

共 232 个（V20 构建），分三层：`L0` 引导 7 · `L1` 常用 57 · `L2` 专用 168。
V18 构建隐藏 28 个 V18 不具备的能力，暴露 204 个。

| 分类 | 代表工具 | 说明 |
|---|---|---|
| 连接与项目 | `ConnectIsolated` `OpenProject` `AttachToOpenProject` `SaveProject` `CloseProject` `GetState` | 无窗口/有窗口两种实例；项目生命周期 |
| 结构读取 | `GetProjectTree` `GetSoftwareTree` `GetBlocksWithHierarchy` `GetTypes` | 工程树、软件树、块层级（含一致性/保护标志） |
| 块操作 | `PlcBuildAndImport` `ImportBlock` `DeletePlcBlock` `SetBlockNumber` `MoveBlocksToGroup` `RegenerateBlockFromSource` | 建块/改块/删块/编号/分组 |
| 逻辑阅读 | `DescribeBlockLogic` `GetBlockInfo` `AnalyzeBlockImpact` `GetCallChain` | 把 LAD/SCL 读成文本；改之前看爆炸半径 |
| 类型与变量表 | `ImportType` `ImportPlcTagTable` `GetPlcTagTables` `ImportPlcExternalSource` | UDT、标签表、外部源 |
| 硬件与网络 | `AddDevice` `PlugDeviceItem` `SetDeviceItemIoAddress` `EnsureSubnet` `ConnectDeviceNodesToProfinetSubnet` `SetDriveParameter` | 设备/模块/IO/子网/驱动 |
| HMI | `EnsureUnifiedHmiScreen` `ApplyUnifiedHmiLayout` `EnsureClassicHmiConnection` `ImportHmiScreen` `ImportAlarmClasses` | Classic / Comfort / Unified |
| 在线与诊断 | `GoOnline` `GoOffline` `DownloadToPlc` `GetPlcLiveValuesS7` `CompileAndDiagnosePlc` `GetCrossReferences` | 上下载、在线值、编译消息、交叉引用 |
| 导出 | `ExportBlocks` `ExportTypes` `ExportBlockSourceUtf8` `SaveExport` `GetExport` | 批量/单块导出；大响应寄存后落盘 |

**工作流 Prompt**（不是工具，只给 AI 用，把顺序和停层门写死）：
`Workflow_AuditProject` · `Workflow_ExportForReview` · `Workflow_EditBlock`

---

## 写安全

TIA 工程是生产资产，本项目在写入链路上做了四道约束：

- **改程序必须有窗口**。`ConnectIsolated(withUserInterface=false)` 只用于读；命中写工具名单的 **65 个工具**
  在无窗口实例上直接拒绝，并给出「先 Disconnect，再 `withUserInterface=true` 重连」的指引。
- **只关自己开的东西**。挂接（attach）来的用户工程永不自动关闭；切版本前先 `CloseProject(saveBeforeClose=true)`，
  **保存失败就中止切换**，不硬关。
- **工具说成功不算数，磁盘说才算**。导出回报 `filesWritten / emptyFiles / unparsableFiles / totalBytes / diskVerified`，
  新文件为 0 或有坏文件即 `success=false`。`SaveExport` 拒绝写盘根、系统目录、程序目录、TIA 安装目录。
- **know-how 保护块不猜密码**。遇到加密块停下来要密码，不做字典或枚举。

---

## 架构

```
MCP 客户端
   │  stdio
   ▼
tia_mcp_launcher.bat        扫描所有固定盘定位 TIA（磁盘为准，不信任注册表）
   │
   ▼
tia_router.py               一个 MCP 服务端
   ├── OpenProject(.ap18) ──► 子引擎 TiaMcpServer.exe (bin-v18)  204 tools
   └── OpenProject(.ap20) ──► 子引擎 TiaMcpServer.exe (bin-v20)  232 tools
```

一个 TIA 版本的 Openness 程序集只能加载进一个进程，所以版本切换 = 换子引擎。
切之前先存盘关工程；同一版本的多个 TIA 进程可各接一个引擎，抢同一个 TIA 进程会被拒
（锁名 `TiaMcpServer.SingleAttach.V{major}.P{pid}`）。

---

## 快速开始

### 1. 先决条件

| 项 | 要求 | 缺了会怎样 |
|---|---|---|
| Windows | 10 1903+ / 11，x64 | — |
| TIA Portal | V18 / V20 / V21，安装时勾选 **Openness** | 探测不到；V16/V17/V19 不支持 |
| .NET Framework | 4.8 | 引擎起不来 |
| 用户组 | 当前用户属于 `Siemens TIA Openness` | 连接被拒（改完需注销重登） |
| 磁盘 | 约 40 MB | — |

### 2. 安装

```powershell
# 先看它发现了什么（只读，不写配置）
powershell -File tools\tiaportal-mcp\detect_tia_version.ps1 -List

# 交互安装：列出本机 TIA → 你确认 → 写 MCP 配置
powershell -ExecutionPolicy Bypass -File .\install-mcp.ps1
```

手动挂载（只挂**一条**）：

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "cmd",
      "args": ["/c", "<解压目录>\\tools\\tiaportal-mcp\\tia_mcp_launcher.bat"],
      "env": { "TIA_MCP_ATTACH_TIMEOUT_MS": "8000" }
    }
  }
}
```

### 3. 第一次调用

```
ConnectIsolated(withUserInterface=false)      # 只读，不弹窗
OpenProject("D:\\Work\\Line1\\Line1.ap20")    # 自动落到 V20 引擎
GetProjectTree()                              # 拿 softwarePath
GetBlocksWithHierarchy(softwarePath)          # 块清单 + 一致性 + 保护标志
```

要改程序时换成 `ConnectIsolated(withUserInterface=true)`，否则写工具会拒绝并提示。

---

## 兼容性

| 构建 | 输出目录 | TIA | 工具数 | 说明 |
|---|---|---|---|---|
| V18 | `bin-v18` | V18 | 204 | 隐藏 23 个 Unified HMI + 5 个 VersionControl |
| V20 | `bin-v20` | V20 | 232 | 完整工具集 |
| V21 | `bin` | V21 | 232 | 上游构建 |

---

## 工程体检

离线规则引擎，可进 CI；不连 TIA 也能跑。

```powershell
python scripts\tia_project_audit.py --collect --project "<工程.apXX>" --software <PLC名> --out report.md
python scripts\tia_project_audit.py --skeleton s.json --hierarchy h.json --out report.md
```

| 规则 | 说明 |
|---|---|
| R0 / R0b | 骨架与层级**数据源对账**（块数 / 按类型计数），不一致即 P1 |
| R1 / R3 / R5 | 根目录散块、编号未分区、编号空间稀疏 |
| R7 | 未编译块（`isConsistent=false`） |
| R8 | know-how 保护块 |
| R11 / R12 | 空组、语言分布 |
| R13 / R13b | 疑似孤儿块（启发式）+ 调用图覆盖率声明 |

命名规范类默认不检查（属你自己的程序框架），加 `--naming-check` 才报。

---

## 文档

| 文件 | 内容 |
|---|---|
| [docs/INDEX.md](docs/INDEX.md) | 全部文档/脚本/清单的索引（自动生成） |
| [docs/user-directives.md](docs/user-directives.md) | 硬约束（验证/安全/流程/文风） |
| [docs/lessons-and-shortcuts.md](docs/lessons-and-shortcuts.md) | 踩过的坑与捷径（症状→根因→做法→证据） |
| [CHANGELOG.md](CHANGELOG.md) | 逐版本变更 |
| [AGENTS.md](AGENTS.md) | 接手这个仓库的第一步 |

---

## 其他工具

同类开源项目（2026-10-29 查 GitHub API）：

| 项目 | 最新版 | 特点 |
|---|---|---|
| [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) | v4.2.0 | .NET 10 · TIA V21 · 少量复合工具 + preview→apply |
| [asckye/TIA_Portal_Openness_MCP](https://github.com/asckye/TIA_Portal_Openness_MCP) | v3.3.0 | V14 SP1–V21 · Workbench + MCP |
| [renanlido/tia-openness](https://github.com/renanlido/tia-openness) | app-v1.7.0 | V21 · HTTP/JSON 引擎 + 桥 |
| [a4webdev/tiacommander-mcp](https://github.com/a4webdev/tiacommander-mcp) | v3.42.0 | 16 工具 / 166 操作 · V15.1–V21 |
| **本项目** | **2.6.3** | V18/V20/V21 三构建 + 自动路由 · 写窗口硬门 · 离线体检 |

选型参考：要**单版本、少工具、带预览令牌**走 Czarnak；要**多版本共存、细粒度控制、自带体检**用本项目。

---

## 贡献

见 [CONTRIBUTING.md](CONTRIBUTING.md) 与 [docs/git-release-conventions.md](docs/git-release-conventions.md)。
改动请保持：
工具数变更同步 `manifest/tools-list.json` 与 `docs/tool-capability-matrix.md`；
提交前跑 `python scripts/Check-VersionConsistency.py`、`Check-ManifestConsistency.py`、`Check-DeadToolReferences.py`
与离线套件（当前 228 passed）。

---

## 许可与免责

MIT License，见 [LICENSE](LICENSE)。

本工具会**真实写入 TIA 工程**。AI 生成的 PLC 代码必须经人工复核后才能用于生产；
写入前请自行备份（`ArchiveProject` 或版本控制），并在非在用设备上先验证。
