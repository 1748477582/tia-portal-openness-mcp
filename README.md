# TIA Portal Openness MCP（V18 / V20 / V21）

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![TIA Portal](https://img.shields.io/badge/TIA%20Portal-V18%20%2F%20V20%20%2F%20V21-blue)
![Tools](https://img.shields.io/badge/tools-232%20(V20)%20%2F%20204%20(V18)-green)

让 AI 通过 **MCP（stdio）** 直接操作博途工程：读结构、导出块、生成 PLC 代码、改硬件组态、做工程体检。
仓库自带**已编译运行时**，下载解压即用，不需要自己编译。

---

## 1. 它能做什么

| 类别 | 典型能力 |
|---|---|
| **读** | 工程树、软件目录、块层级、块接口、标签表、UDT、交叉引用、调用链、在线值 |
| **导出** | 单块 / 批量块 / 类型 → SimaticML XML；**回报磁盘事实**（文件数、空文件、不可解析、字节数） |
| **写** | 建/删/移动块、改编号、导入 UDT / 变量表 / 技术对象、外部源导入、SCL 重生成 |
| **硬件** | 加设备、插模块、改 IO 地址、配 PROFINET 子网、驱动参数与报文 |
| **HMI** | Classic / Comfort / Unified 画面、变量、连接、按钮动作 |
| **诊断** | 编译并收集消息、断链与地址一致性检查、不一致块清单 |
| **体检** | 离线规则引擎（见 §5），可进 CI |

工具分层：`L0` 引导 7 个 · `L1` 常用 57 个 · `L2` 专用 168 个。
V20 构建暴露 **232** 个；V18 构建隐藏 28 个 V18 不具备的能力（23 个 Unified HMI + 5 个 VersionControl），暴露 **204** 个。

---

## 2. 60 秒接入

**前提**：Windows x64 + **TIA Portal V18/V20/V21（安装时勾选 Openness）** + .NET Framework 4.8，
当前 Windows 用户属于本地组 `Siemens TIA Openness`（改完需注销重登）。

### 方式 A：一行安装（推荐）

```powershell
powershell -ExecutionPolicy Bypass -File .\install-mcp.ps1
```

脚本会**先扫描本机的 TIA 安装**（所有固定盘，不猜默认版本），列出来让你确认，再写 MCP 配置。

### 方式 B：手动挂载

在 MCP 客户端配置里指向解压目录中的启动器：

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

**只挂一条**。版本由启动器/路由器按你要打开的工程自动决定（见 §3）。

---

## 3. 三个关键设计

### 3.1 一条连接器，按工程版本自动选引擎

一个 TIA 版本的 Openness 程序集只能加载进一个进程，所以：

- `tia_mcp_launcher.bat`：**扫描所有固定盘**找已安装的 TIA，判定只认磁盘实物
  （`Portal V<XX>\bin\Siemens.Automation.Portal.exe` **且** `PublicAPI\V<XX>\Siemens.Engineering.dll`）。
  **不信任注册表**——安装不完整会留残留键；`Portal V20\PublicAPI` 里本来就带 V17–V20 的多版客户端 DLL。
- `tia_router.py`：对外是**一个** MCP 服务端，内部按需拉起 V18/V20 子引擎。
  `OpenProject` 读工程扩展名（`.ap18` / `.ap20`）→ 自动切到对应引擎；切换前**先保存关闭**当前工程，
  存不了就中止切换，不会硬关。
- 多版本共存时**不猜**：没有明确目标就报 `ambiguous` 并列出候选，让你定一次后写进配置。

### 3.2 只读无窗口，改程序必须有窗口

- `ConnectIsolated(withUserInterface=false)`：无窗口，用于读结构、读块、导出、体检。
- `ConnectIsolated(withUserInterface=true)`：打开可见 TIA 窗口，用于任何改动。
- **写操作硬门**：65 个写工具在无窗口实例上会被直接拒绝，并告诉你「先 Disconnect，再用
  `withUserInterface=true` 重连」。工具不会在你看不见的窗口里改程序。
- 只关**工具自己打开**的实例与工程；挂接来的用户工程永远不动。

### 3.3 工具说成功不算数，磁盘说才算

导出、编译、写入这类操作都会**回读实际结果**：导出的 `meta` 里带
`filesWritten / emptyFiles / unparsableFiles / totalBytes / diskVerified`，
新文件为 0 或有坏文件时 `success=false`，消息里直写 `disk shows N new file(s)` 或 `treat this export as FAILED`。

---

## 4. 三条工作流（给 AI 用，不是新工具）

挂载后可直接选用（MCP Prompt），它们把顺序、停层门和「不许做什么」写死：

| Prompt | 用途 |
|---|---|
| `Workflow_AuditProject` | 只读体检：身份核对 → 树 → 目录 → 块层级 → 一致性 → 类型 → 导出对账 → 离线规则 → 报告 |
| `Workflow_ExportForReview` | 导出并**证明**磁盘产物，再交给离线解析 |
| `Workflow_EditBlock` | 影响面分析 → 备份 → 改 → 回导 → 编译 → 复核 → 保存 |

---

## 5. 工程体检（离线，可进 CI）

```powershell
# 采集（连 TIA，只读）
python scripts\tia_project_audit.py --collect --project "<工程.apXX>" --software <PLC名> --out report.md

# 只吃已采集数据（不连 TIA）
python scripts\tia_project_audit.py --skeleton s.json --hierarchy h.json --out report.md
```

规则（默认开启，均为**事实判定**，不含命名风格）：

| 规则 | 说明 |
|---|---|
| R0 / R0b | 骨架与层级**数据源对账**（块数、按类型计数），不一致即 P1 |
| R1 / R3 / R5 | 根目录散块、编号体系未分区、编号空间稀疏 |
| R7 | 未编译块（`isConsistent=false`）——导出与下载都会被拒 |
| R8 | know-how 保护块（读不到内部逻辑，需人工解锁） |
| R11 / R12 | 空组、编程语言分布 |
| R13 / R13b | 疑似孤儿块（无调用者，启发式）+ **调用图覆盖率声明** |

命名规范类（组名前缀、块名前缀、块名特殊字符）**默认不检查**——命名属于你自身程序框架，
加 `--naming-check` 才会报。

---

## 6. 版本与工具数

| 构建 | 输出目录 | TIA | 工具数 | 说明 |
|---|---|---|---|---|
| V18 | `bin-v18` | V18 | 204 | 隐藏 28 个 V18 不具备的能力 |
| V20 | `bin-v20` | V20 | 232 | 完整工具集 |
| V21 | `bin` | V21 | 232 | 上游原版构建 |

不支持 V16 / V17 / V19：这些版本没有可用的 Openness 通道（或本仓库未提供构建）。
探测到不支持的版本时，工具会**明说**，不会静默降级。

---

## 7. 与同类工具的定位差异

同类开源项目里最活跃的是 [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp)
（**v4.2.0**，2026-10-25 发布；.NET 10；TIA V21；据其 v3.0.0 讨论为 14 个读写工具 / 4 个只读工具）。

| 维度 | 本项目（2.6.2） | Czarnak/tia-portal-mcp（v4.2.0） |
|---|---|---|
| 工具粒度 | **多而细**（232 个，可组合） | **少而复合**（十余个，preview→apply） |
| TIA 版本 | V18 / V20 / V21 三构建，**按工程自动路由** | V21 单一版本 |
| 运行时 | .NET Framework 4.8（Win 自带） | .NET 10（需装运行时或用自包含包） |
| 写保护 | **窗口硬门**（无窗口实例直接拒写）+ 只关自建实例 | 访问模式（read-only 隐藏写工具）+ 预览令牌 + 审计 JSONL |
| 体检 | 自带离线规则引擎（对账/未编译/保护/孤儿…） | 无独立体检脚本 |
| 分发 | 仓库自带运行时，解压即用 | 自包含 zip / NuGet 全局工具 |

两者取向不同：本项目面向「一个工程师本机、多版本工程、要能解释每一步为什么」的场景。

---

## 8. 目录结构

```
tools/tiaportal-mcp/
  tia_mcp_launcher.bat      # 版本探测 + 启动对应构建
  detect_tia_version.ps1    # 扫全盘找已安装 TIA（磁盘为准）
  tia_router.py             # 单连接器 + 按工程版本自动路由
  src/TiaMcpServer/         # 引擎源码（三 csproj：V18 / V20 / 上游）
scripts/                    # 体检、清单生成、闸门检查、切版本
manifest/                   # tools-list.json / package-manifest.json
docs/                       # 索引、用户指令、经验与坑
runtime/                    # 发行包用的已编译运行时
```

---

## 9. 常见问题

**新机器装完报「安装路径找不到」或总用 V18？**
2.5.5 起已修：探测扫所有固定盘、以磁盘为准、找不到就报错而不是退 V18。若仍异常，跑
`powershell -File tools\tiaportal-mcp\detect_tia_version.ps1 -List` 看它发现了什么。

**装了 V18 和 V20，怎么切？**
不用切。一条连接器即可，打开 `.ap18` 走 V18、`.ap20` 走 V20。也可用
`scripts\Set-TiaVersion.ps1 -Version 18 -Apply` 把默认版本钉死。

**`AttachToOpenProject` 挂不上？**
部分环境的 `Attach()` 会挂死（本机两次独立复现）。请改用 `ConnectIsolated` + `OpenProject`，
不要依赖挂接。

**导出成功但目录是空的？**
2.6.1 起不会了：导出会回报 `filesWritten` 等磁盘事实，`success=false` 即表示引擎与磁盘不一致。

**V20 工程编译很慢？**
100+ 工艺对象时 60 秒以上属正常，`timeoutSeconds` 给 600。

---

## 10. 变更与许可

逐版本变更见 [CHANGELOG.md](CHANGELOG.md)。当前 **2.6.2**。

MIT License，见 [LICENSE](LICENSE)。
