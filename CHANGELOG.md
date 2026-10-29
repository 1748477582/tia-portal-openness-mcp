---
summary: 版本变更记录：每版的新增 / 变更（含行为变更）/ 修复 / 移除，以及工具总数
kind: doc
when: 升级前、排查回归、确认某个行为从哪一版开始变
env: none
---

# 更新记录 / Changelog

> **维护规则（硬性）**
> 1. **每次发版都必须在这里加一条**，按 `新增 / 变更 / 修复 / 移除` 四类写；删了什么、加了什么要写清。
> 2. 版本号以 `manifest/package-manifest.json` 的 `bundleVersion` 为准，两边必须一致。
> 3. **每加/删/改一个 MCP 工具，必须同步两份 manifest**：
>    `manifest/tools-list.json`（`toolCount` + `tools[]`）**和**
>    `manifest/package-manifest.json`（`capabilities.mcpToolCount` + `capabilities.mcpToolLayers`）。
>    CI 的 `manifest-consistency` 会互相对拍，漏一份就红。
> 4. 工具总数 / 分层（L0/L1/L2）变化必须写进本文件，方便一眼看出"这版多了什么"。

---

## [2.6.2] — 2026-10-28

> 代码改动。工具集不变（232 / V18 204）；Prompt 33。
> 本版重点：**"改程序必须有窗口"变成结构性硬门**（用户要求）。

### 新增

- **写操作硬门**（`McpServer.WindowPolicy.cs`）
  在工具注册的唯一入口 `WrapTools` 上再加一层：命中写工具名单 **且** 当前是引擎自建的**无窗口实例**，
  直接拒绝，并告诉调用方"先 Disconnect，再用 `ConnectIsolated(withUserInterface=true)` 重连"。
  - 名单 **65 个**工具：块/类型/变量表/技术对象/外部源导入删除、编号与移动、硬件组态与网络、
    驱动参数、HMI 画面/变量/连接、报警。**含 `PlcBuildAndImport`**（建块主工具）。
  - 刻意**不含**：导出与落盘（`SaveExport`/`Export*`）、本机文件（`WritePlcSclSourceFile`）、
    工程存档与另存（`ArchiveProject`/`SaveAsProject`）、在线操作（`GoOnline`/`GoOffline`/`DownloadToPlc`）、
    在线改值、Windows 组与版本控制工作区、`CreateProject`（新工程无既有内容可毁）。
  - 挂接用户自己的 TIA（`attached`）时用户本来就看得见 → 放行。
  - 放行/拒绝判据：`Portal.IsHeadlessInstance = (ConnectMode ∈ {isolated, new-instance}) && !IsolatedWithUserInterface`

### 验证（实测，2026-10-29 08:31）

| 用例 | 结果 |
|---|---|
| 无窗口实例上调 `PlcBuildAndImport` | **被拒**，`isError=true`，正文可读：`会改动程序内容…请先 Disconnect，再用 ConnectIsolated(withUserInterface=true) 重连` |
| 有窗口实例上调同一调用 | **成功**建块 `MCP_WINDOW_GUARD`，随后删块 + `SaveProject`，工程无残留 |

- 离线校验：名单 65 条、零死条目、无读类误纳。
- 修一处实现缺陷：最初用 `throw new McpException(...)`，经 SDK 转成结果时**正文被丢掉**（客户端只看到"出错了"）。
  改为返回带 `TextContentBlock` 的错误结果后，正文完整到达客户端。

---

## [2.6.1] — 2026-10-28

> 代码 + 脚本改动。工具集不变（232 / V18 204）；Prompt 30 → 33。
> 本版重点：**导出自己证明磁盘事实** + **三条跨工具工作流** + **命名规范类规则默认不打扰**。

### 新增

- **`ExportBlocks` 回报磁盘事实**（此前只回报"引擎认为导出成功"）
  meta 新增 `filesWritten` / `emptyFiles` / `unparsableFiles` / `totalBytes` / `diskVerified`，
  并给出 `emptyFileNames` / `unparsableFileNames`（各最多 20 条）。
  **`success` 现在由磁盘决定**：新文件为 0，或存在空文件/不可解析 XML → `success=false`，
  message 里直写 `disk shows N new file(s)` 或 `treat this export as FAILED`。
  实现只统计"本次新增"的文件（导出前快照差集），不会把目录里的旧文件算进来。
  - 实测：3 个块 → `filesWritten=5`（含 2 个实例 DB），`totalBytes=32997`，
    与目录实际 `.xml` 数量一致；message 为 `... disk shows 5 new file(s), 32997 bytes.`

- **三条跨工具工作流 Prompt**（MCP Prompt，只给 AI 用，不是新工具）
  - `Workflow_AuditProject`：身份门 → 树 → 软件目录 → 块层级 → 一致性 → 类型 → 导出对账 → 离线规则 → 报告结构
  - `Workflow_ExportForReview`：先锁范围 → 一致性前置 → 导出 → **强制磁盘对账** → 大响应取法 → 交给离线脚本
  - `Workflow_EditBlock`：影响面分析 → 一致性/保护校验 → 导出源 → 改 → 回导 → 编译 → 复核 → 保存
  每条都带 `GATE`（不满足就停）与 `STOP RULES`（不许做什么）。实测 `prompts/list` 33 条、正文 1692 字符。

### 变更

- **命名规范类规则默认不检查**：R4（组命名前缀）/ R9（块名前缀与类型）/ R10（块名特殊字符）
  收进 `--naming-check` 开关，默认不报——命名属用户自身程序框架，不该由工具当体检标准。
  报告里新增一段"默认不检查"说明。实测默认条目 14 → 12；加开关后 R9 重现。

---

## [2.6.0] — 2026-10-28

> 脚本改动（工程体检）。工具集不变（232 / V18 204）。
> 本版重点：**块级体检闭环**——新增孤儿块判定，并把"结论的适用范围"写进报告。

### 新增

- **R13 疑似孤儿块（无调用者）**：`tia_project_audit.py --blocks <导出目录>`
  从 `ExportBlocks` 产出的 SimaticML 里读 `<CallInfo BlockType="FB" Name="...">`，建"谁调用谁"的图；
  **没有任何调用者的 FB/FC** 报 P2（仅用户块，OB 是入口不算）。
  规则自带限制声明：可能被 HMI / OPC / 间接引用，或调用者不在本次导出范围内 ⇒ **启发式，不能单独作为删除依据**。
- **R13b 调用图覆盖率**：报告里写明"本次扫描了多少块"，避免把局部结论当成全量结论。

- 规则质量：`scan_block_dir` 不再静默吞掉读文件异常（读不了的文件标 `error` 计入），
  `--blocks` 给了但解析不出任何块时**直接报错退出**，不再"安静地判成没有孤儿"。

### 实测（导出子集，机制验证）

| 用例 | 期望 | 结果 |
|---|---|---|
| 构造样本：A 调 B、C 无人调用 | 孤儿 = A、C | **PASS**（B 正确地不算孤儿）|
| 真实子集（`OP_00_Main` / `OP_03_Manual` / `UN_00_Main` 三个 FB） | 三者均无本集合内调用者 → 全列 | **PASS**，并给出覆盖率声明 |

### 已知代价（实测，非估算）

- `ExportBlocks` 吞吐：**约 6.4 KB/块、3–5 秒/块**（本机 V20，`项目1.ap20`）。
  363 块全量导出 ≈ **2.3 MB 文件、18–30 分钟**。属一次性批处理，不适合交互式调用。

### 仍未做

- 未使用变量、变量表规范、AF/ISA-88 分层符合度。

---

## [2.5.9] — 2026-10-28

> 脚本改动（工程体检）。工具集不变（232 / V18 204）。
> 本版重点：**块级规则第一次真正跑起来**，并补上"两个数据源对账"。

### 新增

- **`tia_project_audit.py` 新增 R0 / R0b 对账**（喂 `--hierarchy` 时生效）
  - R0：骨架 `blockCount` 与层级实际遍历到的块数必须一致，否则 **P1** ——
    两个数据源描述的不是同一批对象时，后面所有结论都不成立。
  - R0b：按类型对账（`DB` = `InstanceDB` + `GlobalDB`），任一类型数量不符 → **P1**。
  - 反例测试：把骨架 `blockCount` 从 363 改成 300 → R0 如期报 P1；真实数据下 R0 沉默。

### 修复

- **R10 误报**：原先对**系统 OB**（`Time error interrupt` / `Programming error` / `02_Cyclic interrupt`）
  也报"块名含空格"，但这些名称由 TIA 生成、不可修改。
  现只检查用户自建块（`FB` / `FC` / `GlobalDB` / `InstanceDB`）。
- **R9 影响描述不准**：原文写"名字写 FB_ 实际是 FC"，实际命中是 DB（`FB_AcycComDrive_DB` 为 InstanceDB）。

### 证据（真实工程 `项目1.ap20`，363 块）

| 规则 | 结果 |
|---|---|
| R7 未编译块 | 沉默（实测 `isConsistent=false` 为 **0** 个，正确）|
| R8 know-how 保护块 | **P2：27 个**（例：`MC-Servo`、`MC-Interpolator`、`EM01_CM03_AllAxisCtrlMain`…）|
| R9 前缀与类型不符 | **P2：2 个**（`FB_AcycComDrive_DB`=InstanceDB，`DB_Test`=GlobalDB）|
| R10 块名含空格 | 修正后**沉默**（原报的 3 个全是系统 OB）|
| R11 空组 / R12 语言分布 | INFO（SCL 283 / LAD 19 / DB 59 / ProDiag 2）|

> 说明：块级规则（R7–R12）此前已写好但从未执行——历次体检只喂了骨架、没喂层级。
> 本版起 `--hierarchy` 一并提供即可产出块级结论。

---

## [2.5.8] — 2026-10-28

> 代码 + 文档改动。工具集不变（232 / V18 204）。
> 本版重点：**把"描述与事实不符"的四处对齐**，其中一处是真实缺陷（含 `/` 的设备名解析不出来）。

### 修复

- **设备名含 `/` 时 `device/software` 路径永远解析不了**（真实缺陷）
  实测工程 `test1.ap18` 的设备名是 `S7-1500/ET200MP station_1`，而 `softwarePath` 用 `/` 切段，
  于是 `S7-1500/ET200MP station_1/IS-CPU-A` 必然失败，只有裸软件名 `IS-CPU-A` 能用。
  - 现在：整路径解析失败时，**回退到最后一个片段**再做一次设备/组遍历（软件容器挂在同名 DeviceItem 上，
    扁平搜索能找到），并写一条警告日志说明原因。
  - 验收：同一路径修复前 `unresolved`，修复后 `success=true` 且块数正常。

### 变更（描述修正，行为未变）

- `ExportBlock` 的 `exportPath`：原文"path where to export the block"会被当成文件路径，
  实际是**目录**（文件写到 `<exportPath>/<BlockName>.xml`）。描述已改，并写明传文件名会创建同名目录。
- `PlcBuildAndImport` 的 `softwarePath`：描述原称"Required only when dryRun=false"，但 schema 实际一直必填。
  已改为如实说明（dryRun 时也要填）。
- `PlcBuildAndImport` 的 `json`：明确它是 **JSON 字符串**（不是对象），并给出 globaldb 的完整示例——
  传对象会直接失败且报错不可读。

### 证据

| 项 | 结果 |
|---|---|
| 含 `/` 设备名（`S7-1500/ET200MP station_1/IS-CPU-A`） | 修复前 unresolved → 修复后 **OK** |
| 裸软件名（`IS-CPU-A`） | 仍 OK（无回归） |
| 离线套件 | 228 passed / 0 failed |
| 三道闸门 | 全 PASS |

---

## [2.5.7] — 2026-10-28

> 代码 + 脚本改动。工具集不变（232 / V18 204）。
> 本版重点：**多版本连接导向**——按工程文件的版本自动落到对应引擎；版本不符时明确指路。

### 新增

- **`scripts/Register-TiaConnectors.ps1`**：为本机每个"已安装且本工具支持"的版本各生成一条 MCP 连接器条目
  （`tia-portal-v18` / `tia-portal-v20` …），env 里钉住 `TIA_MCP_VERSION` 与 `TIA_MCP_PORTAL_LOCATION`；
  默认只预演，`-Apply` 才写入并生成时间戳备份。原 `tia-portal`（自动探测）保持不变。
  - 之所以要"每版本一条"：引擎在进程启动时绑定一个 TIA 版本的 Openness 程序集，**跑起来后无法切换版本**；
    而单实例锁名为 `TiaMcpServer.SingleAttach.V<major>`，**按版本加锁**，所以 V18 与 V20 可同时存活，
    同版本两个引擎才会互斥。
- **工程版本识别与指路**：`OpenProject` 读工程扩展名（`.ap18`/`.als20`…），与当前引擎版本比较：
  - **工程版本 > 引擎版本 → 直接拒绝**，并指明该用哪条连接器，例如
    `REFUSED: '项目1.ap20' is a V20 project, but this engine is V18 ... Use the connector 'tia-portal-v20'`；
  - 工程版本 < 引擎版本 → 允许（用户已确认升级可接受），日志记录"将被升级"。

### 修复

- **`OpenProject` 会先关掉当前工程，而 `CloseProject` 不检查归属**——这条绕过了 2.5.4 的保护，
  能关掉用户挂接的工程。现在：
  - `CloseProject` 在 `_ownsProject == false`（挂接而来）时**拒绝**并说明"请在 TIA 里自己关"；
  - `OpenProject` 的预关闭只在**自己打开的**工程上执行。

### 证据（本机实测）

| 用例 | 结果 |
|---|---|
| `Register-TiaConnectors.ps1` 预演 | 识别 V20@D:、V18@C:，生成 2 条条目与路由表 |
| `-Apply` 写入 | 备份 `mcp.json.bak-20261028-101938`，原 `tia-portal` 保留，新增 2 条 |
| V20 路由链路 | serverInfo 2.5.7，tools=**232** |
| V18 路由链路 | serverInfo 2.5.7，tools=**204**（V18 隐藏 28 个 Unified HMI/VCI 工具） |
| V18 引擎开 `.ap20` 工程 | **REFUSED**，消息含 `V20 project` 与 `tia-portal-v20` |

### 已知未做

- 导出仍只回报"工具成功"，不回报磁盘事实（`filesWritten`/空文件/不可解析）。
- 跨工具工作流 Prompt（10 步 + 停层门）未做。

---

## [2.5.6] — 2026-10-28

> 代码改动。工具集不变（232 / V18 204）。
> 本版重点：**断开连接时说清那个 TIA 到底关了没有、是谁的**。

### 修复

- **`Disconnect` 之后 TIA 进程残留且无任何交代**
  `Dispose()` 返回早于操作系统真正收尾，实测断开 4 秒后进程仍在；响应里也只写"Disconnected"，
  既不说明实例是否还在，也不给 PID。
  - 现在按用户规则（2026-10-28 原话："断开实例只关掉工具自己打开的程序…如果是需要工具正在修改
    当然得观看，这种情况只有用户主动关闭程序即可"）分流：
    **headless（只读）实例 → 关闭，并最多等 10 秒确认真的退出**；
    **可见（修改）实例 → 保留**，由用户手动关窗口。
  - `ConnectIsolated` 启动实例时记录 PID（前后进程表差集，绝不误认别人的 TIA）。
  - `Disconnect` 的响应新增 `instanceKept` / `instancePid` / `instanceStillRunning`，消息随情形改写。

### 证据（本机实测）

| 用例 | 期望 | 实测 |
|---|---|---|
| `ConnectIsolated(false)` → `Disconnect` | kept=false，进程消失 | kept=false，pid=9288，stillRunning=false，进程已退出 |
| `ConnectIsolated(true)` → `Disconnect` | kept=true，进程保留 | kept=true，pid=19696，stillRunning=true，窗口仍在 |

### 已知未做

- 导出仍只回报"工具成功"，不回报磁盘上到底生成了什么（`filesWritten`/空文件/不可解析）。
- 引擎侧版本不符仍抛 `FileNotFoundException` 堆栈；工程扩展名与引擎版本的方向校验也尚未加。

---

## [2.5.5] — 2026-10-28

> 脚本改动（启动器 + 版本探测）。工具集不变（232 / V18 204）。
> 本版重点：**新机器不再"路径找不到"、不再静默退成 V18、多版本不再猜**。

### 修复

- **新机器安装后"连接博图安装路径出错"且默认 V18**
  三处叠加造成：① `tia_mcp_launcher.bat` 把安装根写死（V20 → `D:\...`，其余 → `C:\...`）；
  ② 探测不到就 `set VER=18` 静默降级；③ `detect_tia_version.ps1` 只看环境变量和**正在运行的** TIA，从不扫盘。
  - 探测重写：扫描**所有固定盘**的 `Program Files` / `Program Files (x86)` / `Siemens\Automation` 下的 `Portal V*`；
    判"已安装"只认磁盘实物——`Portal V<XX>\bin\Siemens.Automation.Portal.exe`
    **且** `PublicAPI\V<XX>\Siemens.Engineering.dll`，且 `<XX>` 与目录名一致。
  - **注册表不再作为证据**。实测：`HKLM\...\Openness` 列 16/17/18/19/20，磁盘只有 V18/V20；
    原因是每次安装会附带多版 Openness 客户端（`Portal V20\PublicAPI` 内含 V17–V20），且不完整卸载会留残留。
  - 选择规则（按用户 2026-10-28 要求）：唯一可用 → 用它；有运行中的 TIA → 用它；
    设了 `TIA_MCP_VERSION` → 尊重，但不可用即报错；**多个可用且无提示 → `status=ambiguous`，不猜**。
  - 启动器删除硬编码路径与 V18 兜底，改为按状态给出可操作提示并以 3/4/5/6 退出；
    新增 `TIA_MCP_LAUNCHER_ECHO_ONLY=1` 仅打印解析结果不启动引擎（供安装/验收脚本使用）。

### 证据（本机实测）

| 场景 | 期望 | 实测 |
|---|---|---|
| 多版本共存、未钉版本 | 不猜，列出候选，exit 6 | `ambiguous`，列出 `20@D:\...`、`18@C:\...`，exit 6 |
| 钉住 V20 | exit 0 并给出 portal/engine | exit 0，portal=D:\...V20，engine=bin-v20 |
| 钉住 V20 + 路径不存在 | 报错不静默替换，exit 5 | `bad-location`，exit 5 |
| 钉住 V19（未装/无构建） | exit 4 并列出可用项 | `pinned-unusable`，exit 4 |
| 经 launcher 全链路 | initialize 成功、232 工具 | serverInfo 2.5.5，tools=232 |

### 已知未做（下一版）

- 引擎侧：构建版本与请求版本不符时仍抛 `FileNotFoundException` 堆栈，未改成人话（L3）。
- `OpenProject` 仍默认 `Projects.OpenWithUpgrade`，用高版本打开低版本工程会**走升级通道**（L3）。
- 多版本并发（两个连接器条目各自钉版本）未做。

---

## [2.5.4] — 2026-10-27

> 代码改动。工具集不变（232 / V18 204）。
> 本版重点：**空结果不再冒充事实**；`OpenProject` 不再关闭"只是挂接"的用户工程。

### 修复

- **`GetProjectSkeleton` 不再把"没解析到"报成"空工程"**
  `GetBlocks` 把三种不同实情——① 未绑定工程、② `softwarePath` 解析不到、③ 真的 0 块——压成同一个空列表，
  工具却照回 `success=true, blockCount=0`。
  **事实**：本次排查中，同一个工程两次调用分别得到 `0 块` 与 `363 块`，且 0 块那次的响应是 `success=true`，
  下游体检报告因此变成"全部正常"。
  **推断**（未完全证实）：触发路径是 `Attach` 后 `OpenProject` 走了"关闭→重开"，使 `_project` 处于中间态。
  现在 0 块时先取绑定诊断：未解析则回 `success=false` + `unresolvedReason` + `diag_*`
  （`projectBound` / `portalBound` / `connectMode` / `softwareResolved` / `requestedSoftwarePath`），
  Message 明写 `this is NOT an empty project`。真的空工程仍回 `success=true`。

- **`OpenProject` 拒绝关闭非本服务打开的工程**
  `_ownsProject=false`（Attach 挂接）时，若目标工程与当前绑定工程不同，直接返回 `false` 并在
  `LastConnectError` 给出可操作说明，不再 `Close()` 用户工程去重开（与 2026-09-17 同类风险）。
  同一工程重复调用 `OpenProject` 现在幂等复用，不再"关闭→重开"。

### 新增

- `Portal.GetBindingDiagnostics(softwarePath)`：STA 安全、只读，回答"块读取为什么是空的"，不改动 `GetBlocks` 既有契约。

---

## [2.5.3] — 2026-10-22

> 代码改动 + 安装入口 + 文档。工具集不变（232 / V18 204）。
> 本版重点：移动失败不再丢对象、下载不再擅自取消用户测试、就绪检查不再假绿。

### 新增
- **一行安装改为「整包」写法（不依赖 `raw.githubusercontent.com`）**：实测在**去掉代理**的网络下
  `raw` 超时（WinError 10053），而 `codeload` / `api.github.com` / `github.com` 均可达。
  原写法第一步就从 raw 拉脚本 ⇒ 在这种网络上**直接失败**。现在主推：
  `codeload 下 ZIP → Expand-Archive → 调包内 install-mcp.ps1`，全程不碰 raw，也不使用 `iex`。
  版本仍由用户给（`-TiaMajorVersion 20`）或由交互选择，不预设。
- **`bootstrap.ps1 -Check`（安装前自检，只读）**：报告 PowerShell 版本、.NET Framework 4.8、
  执行策略、TIA 版本、磁盘余量、**四条下载通道哪条能通（含耗时）**、会被改动的客户端配置，并给结论。
  把"安装前要注意什么"从文档变成可执行检查。
- **README / `同事安装_给Agent的一句话.md` 新增「安装前须知」**：前提条件表（系统 / TIA+Openness / .NET / 磁盘 / 网络 / 管理员权限）、
  **会改动哪些文件**、**回滚办法**、**离线安装**（不能上网时用 U 盘带 ZIP）。
- **`bootstrap.ps1`（一行式安装入口）**：把"复制一条命令就能装"做成真的。典型用法是把这一整行发给对方：
  `powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing '<raw url>/bootstrap.ps1' -OutFile .\tia-bootstrap.ps1; & .\tia-bootstrap.ps1"`
  - **不带版本**：只探测并列出本机 TIA，然后把"填好版本号的下一行"打印出来 —— **不替你选版本**（与本版新规则一致）。
  - **带 `-Version 20`** / **`-FollowOpenTia`**：下载交付包到 `%USERPROFILE%\tia-mcp`，转调 `install-mcp.ps1`（`-Lite/-DryRun/-Direct` 均透传）。
  - 刻意不用 `irm | iex`：那条写法在部分环境被安全策略拦、且无法传参；本条只用 `Invoke-WebRequest` + `&`，可传参、可验证。
- **`AGENTS.md`（接手入口）**：换电脑 / 换 agent / 换人时的唯一入口。四步上手（装 → doctor → 四条自检 → 离线套件）、
  必读顺序、**六条硬规则**（别关用户 TIA、别跨线程传 RCW、推送前审计新旧方向、改工具同步三份、加密块先问密码、别用 git push）、
  目录地图、**新增内容时的义务**、常见任务入口、以及换机交接清单（哪些必须带走、哪些会丢要重配）。
- **`docs/INDEX.md` + `docs/index.json`（内容索引，自动生成）**：把"各种形式的内容"（文档 / 脚本 / 清单 / 构建文件 / 批处理入口）
  编成一份可检索目录，每项带**一句话用途 / 何时用 / 需要什么环境**（`none` / `python` / `dotnet` / `tia`）。
  由 `scripts/Generate-DocIndex.py` 从文件自述（`.md` 用 front-matter，脚本用 `# SUMMARY:` 标记）生成，**不手写**。
- **`scripts/Generate-DocIndex.py`**：生成器 + `--check`。新增 **CI job `content-index`**：索引过期就红。
  理由很实际——**索引一旦能和实际文件脱节，就等于没有索引**；换机器时读到"看起来有、实际不对"的目录比没有更糟。
- **`docs/lessons-and-shortcuts.md`（踩过的坑与捷径，v2 全面版）**：把散在 33 份日志、7 份主题分册、9 个技能包里的失败经验
  收敛成一份可检索文档，按**症状 / 根因 / 正确做法 / 证据**四段式。共 15 章：
  两起**关掉用户 TIA** 的事故与判定规则｜RCW 类型嗅探说谎（`GetType()` 报 `System.__ComObject`，
  根本没有 `Siemens.Engineering.Portal` 这个类型）｜**TIA/Openness 硬限制清单**（在线禁止修改、`OpenProject` 只认 `.apXX`、
  SCL 的 `ExportAsDocuments` 必失败、自动编号工程禁改块号、块号合法区间 `1;123-32767`、Acx/Startdrive 白名单）｜
  关系查询（`callers` 而非 depth、块名带组路径、正则用非捕获组）｜**构建环境坑**（`MSB3027`、`CS0111`/`CS0579`、
  `BaseIntermediateOutputPath` 不生效、`dotnet` 吞空格、遥测 `AccessViolation`）｜编码与 BOM｜
  **PS 5.1 七类陷阱**（三元/`<-`/转义引号/`Properties` 索引赋值/包装器 `.Value`/TLS/chcp）｜Git Bash 与 heredoc｜
  推送（只读连接器、token 非 JSON、代理端口、404 两种成因、`sha` 409、`Contents API` 备选、`base_tree` 是合并语义、
  新旧方向审计、HEAD 中止、拆分推送留红）｜工具设计（不静默覆盖、危险默认值、大响应寄存、风险标注自洽、
  `McpPrompts` 不是工具、宿主不在列表里怎么办）｜实机验证 harness｜**我自己的 9 条行为教训**｜DLP 两种加密与绕法｜
  know-how 与密码｜捷径命令清单｜覆盖与复核方法｜**仍未解决的 6 项**。
- **`install-mcp.ps1`**：单文件安装器。探测本机 TIA（**≥V18 才计入**，V16/V17 明确提示不支持）→ 定位引擎/启动器 →
  写进已安装的客户端配置（**WorkBuddy** / Claude Desktop / Claude Code / Cursor / VS Code）→ 跑 `doctor` 体检。
  幂等、改前备份 `*.bak`、不动同文件里其它 MCP 条目与嵌套结构；写出**无 BOM**（有 BOM 会让 JSON 解析器报错）。
  开关：`-Client / -Lite / -DryRun / -Remove / -Direct / -TiaMajorVersion / -Source / -ConfigPath / -SkipDoctor`。
- **`配置MCP-v18.bat`**：补齐一直缺失的 V18 一键配置入口（原有 V21 / V20 两个）。
- **`同事安装_给Agent的一句话.md`**：可整段复制给他人 agent 的一句话安装指令、完整版、卸载、配置位置与常见卡点。
- **`tools/tiaportal-mcp/push_repo.py`**：仓库内的推送工具（`--plan` / `--push`）。原先把推送能力放在两个**只存在于本机**的
  脚本里，换台电脑就推不了；现在它在仓库里。两条安全规则保留：**只推远端已存在的文件 + 显式 `--add` 的新文件**（本地树可能是远端子集，
  全量推会静默回退），**绝不自动删除远端文件**。
- **`docs/user-directives.md`（用户明确交代过的事项，硬约束）**：把散在日志里的"用户要求"单独成文——交付验证（独立验证自述、
  验证深度如实、摘要与磁盘一致）、安全（绝不关用户 TIA、密钥不外泄、已授权连接只读、加密块先问密码）、流程（先计划后执行、
  3 次不成就停手、别让人反复提醒）、文风（去 AI 腔、正式书面语、不写琐碎、不提 AI 工具）、存储（云端优先、百度网盘限制）、
  本仓库专属（CHANGELOG 必记、改工具同步三份、一份配置覆盖多版本、换机可发现）。附**交付前 10 条自查**与**违反记录**。
  `AGENTS.md` 已把它列为第 2 必读条目。
- **各家 agent 的入口指针（薄壳）**：`CLAUDE.md`、`llms.txt`、`.cursorrules`、`.cursor/rules/tia-mcp.mdc`、
  `.github/copilot-instructions.md`。不同客户端自动读取的约定不同，这几份让"下一个 agent 无需被告知就能读到入口"；
  内容只指向 `AGENTS.md`，不复制正文（多份副本必然漂移）。`llms.txt` 另按 llmstxt 约定给出项目、起始顺序与"不能碰的"。
  内容索引已收录（55 项，`kind=entry`）。

### 修复
- **`SetWatchTableModifyValue` 写不进去也报"已设置"**：`Address`、`ModifyValue` 用的是 `TrySetProperty`
  （返回 bool），`ModifyTrigger` 用的 `SetEnumPropertyByName`（原来返回 void 且 `catch {}` 吞掉一切），
  三处返回值全被忽略。现在 `SetEnumPropertyByName` 改为返回 bool，三个结果逐个检查，任一失败就抛出
  并列出是哪一项、以及合法的 trigger 取值。
- **`ImportBlocksFromDirectory` 空结果记成成功**：`Import` 返回 null/空（被 `overwrite=false` 拒掉、
  或 XML 里没有块）时，原来把文件名塞进 `imported`。现在记进 `Failed`，并区分"TIA 因 overwrite=false 拒了"
  和"XML 里没有块"两种原因。
- 修掉 `ImportBlocksFromDirectory` 里一条过时注释（还写着"下面仍用 Override 语义"，实际已是 `ImportOptions.None`）。

- 🔴 **恢复范围太窄，删除后任何异常都会丢块**（实机故障注入测出）：`block.Delete()` 原来写在恢复用的
  `try` **外面**，导入失败能恢复，但"删除成功之后、导入之前的其它异常"（反射异常、意外错误）会直接
  逃逸——块已删、临时导出又被 `finally` 清掉，块凭空消失。现在 `Delete` 与导入同处一个 `try`，
  **只要删除执行过，之后任何异常都走恢复**。
- 🔴 **STA 包装把内层异常消息丢了**：`StaExecutor` 原来只保留 `ex.GetType().FullName`，现场只能看到
  "某个 Openness 异常"，看不到哪一句失败、内层说了什么。现在带上类型、消息与一层内层异常。
  正是这个修复才看清"导出失败是因为块不一致（`Inconsistent blocks ... cannot be exported`）"。
- **新增故障注入开关（仅测试用）**：环境变量 `TIA_MCP_FAULT_MOVE_IMPORT=1` 时，在删除之后、导入之前
  抛异常，用来验证恢复分支。生产环境不要设置。

### 实测（E://临时文件//项目1.ap20，真实 V20 引擎，外部源生成 FB → 编译 → 注入移动）
- 编译后移动、注入失败 → 返回 `Moving 'FB_MoveMe' to '02_Obj' failed; the block was put back into its original group.`
- 随后清理步骤解析到的路径是 `程序块/FB_MoveMe` ⇒ **块确实回到原组，没有丢**
- 反证：未编译的块在"导出"阶段就失败（`Inconsistent blocks ... cannot be exported`），根本不会进入删除 ⇒ 也安全

- 🔴 **移动块/类型/画面失败会丢对象**（对照上游 2.7.4 查出）：`MoveBlockToGroup` / `MoveBlocksToGroup` /
  类型与画面的移动都是「导出 → 删除 → 导入」往返，因为 Openness 不支持重挂父级；而 `finally` 里
  **把临时目录（唯一一份导出）删掉了**。导入一旦失败，对象已删、副本也没了 ⇒ **永久丢失**。
  现在抽出统一的 `RunBlockMoveRoundTrip`：导入失败**先放回原组**；放不回就**保留导出文件**并把恢复路径
  （含建议的 `ImportBlock(filePath=...)`）写进错误消息。类型/画面四处同样改为"失败保留导出"，失败项在
  返回文本里带上 `(export kept: …)`。
- 🔴 **`CheckDownloadReadiness` 的 `IsConsistent` 是写死的 `true`**：未编译的工程也会被判"可下载"，
  等到真正下载才失败。现在逐块读 `IsConsistent`（递归所有块组），未编译的块列进 `Issues`（最多列 8 个）。
- **`DownloadToPlc` 默认不再取消用户的活动测试/调试**：原来对 `ActiveTestCanBeAborted` /
  `ActiveTestCanPreventDownload` 一律答 `AcceptAll`，会在装载时**取消正在跑的测试与调试功能**。
  新增参数 `abortActiveTests`（**默认 false**），要取消必须显式打开；不打开时答 `RejectAll`，
  下载被活动测试挡住就明确失败，而不是悄悄把测试停掉。
- 🔴 **从仓库/ZIP 安装会找不到引擎**（一行安装实测发现）：`bin-v18`/`bin-v20` **不进仓库**（推送规则排除），
  仓库里只有 `runtime/v18|v20|v21/TiaMcpServer.exe`。而 `install-mcp.ps1` 的 `Find-Engine` 只认
  `tools/.../bin-vXX/...` ⇒ 装完配置写好了、引擎却不存在。现在两种形态都认（先 bin-vXX，再 runtime/vXX）。
- 🔴 **启动器同样只认 `bin-vXX`**：仓库形态下会指向不存在的路径。现在加 `runtime/vXX` 回退；
  都找不到时打印**两个查找路径**并退出 3（原来会静默失败）。
- 🔴 **启动器不读 `TIA_MCP_PORTAL_LOCATION`**：安装器把该变量写进了配置，启动器却只用写死的
  `C:\...\Portal V18` / `D:\...\Portal V20` ⇒ 装在其它盘符的机器会注入错误的 PATH。现在显式变量优先。
- **`bootstrap.ps1` 下载通道回退**：`github.com/archive/...` 在本机（企业网/国内网络常见）连不上。
  现在依次尝试 `codeload.github.com` → `api.github.com/zipball` → `github.com/archive`，并支持 `HTTPS_PROXY`；
  三个都不通时给出人工下载指引，不再抛一句"无法连接到远程服务器"。
- 🔴 **安装器不再替用户预设版本**（按用户要求）：原先是"探测后自动取最高版本"（后来还加过"单版本自动钉住"），
  等于替用户做了决定。现在改为 **探测 → 用户确认探测是否正确 → 用户选用哪个版本**；
  **非交互运行且没给版本时直接报错并列出可选项，绝不猜**（`-TiaMajorVersion 18|20|21` 或 `-FollowOpenTia`）。
  `-Remove`（卸载）不需要选版本。
- **新增 `switch-version.ps1`**：装完以后随时换版本。列出本机已装版本与各客户端当前模式，
  让用户选；**只改 `env.TIA_MCP_VERSION` 这一项**，命令/参数/其它 MCP 条目与顶层键都保留，改前备份 `*.bak`。
  支持 `-Show`（只看）、`-TiaMajorVersion N`、`-FollowOpenTia`、`-ConfigPath`（自定义/测试）。
- 🔴 **单版本机器装完会起不来**（靠"新电脑"角色演练发现）：启动器的探测顺序是「env → 当前运行的 TIA → **兜底 V18**」，
  兜底写死 V18。若本机只装 V20 且启动时没开 TIA，会去加载 `bin-v18` 构建，而 V18 的 PublicAPI 目录不存在
  ⇒ 表现为"装了却用不了"，报错不直观。**当时的做法是"单版本自动钉住"，该做法已被上一条"用户选择"取代**：
  现在由用户决定；但用户若选"跟随当前打开的 TIA"且本机只有一个版本，脚本会提示兜底风险。
- **`AGENTS.md` 补前置条件**：第 1–2 步只需 TIA + PowerShell，**第 3–4 步（闸门/离线套件）才需要 .NET SDK 与 Python**；
  新电脑缺这两个不算仓库坏了。另补"未找到 TIA Portal"的处理（`setx TiaPortalLocation`）。
- **`content-index` 闸门在 CI 上误报**：Windows runner 的 checkout 可能带 CRLF，而生成器写 LF，逐字符比较把"换行风格"
  报成"内容过期"（本地 PASS、CI FAIL，文件集合完全一致 49=49）。改为**换行归一化后比较**——闸门要抓内容漂移，不是换行风格。
- **`push_repo.py` 把自己的文件排除了**：规则 `^push_.*\.py$` 本意是排除旧的一次性推送脚手架，却命中了工具自身，
  把它判成"远端独有"（差一步被当成删除）。加 `ALWAYS_INCLUDE` 例外，优先于规则。
- **DLP 结论更正**：此前记"按内容加密的脚本不可用、删不掉"，实测是**落盘加密、授权进程透明解密**——
  `python _full_diff.py` 能正常跑出 0/0/0，只有编译工具链会拒绝（`CS2015`）。**判死活要用目标进程跑一遍，不要看字节头。**
- **`Validate-Bundle.ps1` 的错误记录**：此前在 `package-manifest` 的 notes 里写它"2026-09-28 已删除"，实际**脚本仍在**
  （本地与远端都有 5198 B），09-28 只是把它**从 CI 移出**（需要已构建产物）。已恢复 `offlineValidationScript` 指向它并更正说明。


### 变更
- 🔴 **安装默认走启动器，而不是写死某个版本的 exe**。`tia_mcp_launcher.bat` 读 `env.TIA_MCP_VERSION` 选构建，
  不设则自动探测 —— **一份配置切 TIA 版本**。此前若把配置直连某个 `bin-vXX` 的 exe，换版本就得改配置。
  想要锁定单版本的仍可用 `-Direct`（省掉启动器约 1.3 秒的版本探测）。
- **不钉版本时连 `TIA_MCP_PORTAL_LOCATION` 也不写** —— 否则等于把探测结果又固化回配置，与自动探测的用意相反。

### 说明（为什么二进制必须按版本分开）
源码与工具集是兼容的（**204 个工具在 V18 / V20 上都能用**），但二进制不能通用：
① .NET 强命名程序集绑定 —— 每个构建引用对应版本目录下的 `Siemens.Engineering.dll`，程序集版本不同，
跨版本会因找不到匹配程序集而启动失败；② V18 程序集不含 `Siemens.Engineering.HmiUnified`，
那 23 个 Unified HMI + 5 个 VCI 工具在 V18 **编译期**即被 `#if !TIA_V18` 剔除。
所以"区分版本"指编译产物，不是功能取舍。

---

## [2.5.0] — 2026-10-09（**已定版**）

### 新增
- **`FindTools`**（L0，Meta）：按能力词检索**全部**工具，返回精确名、参数签名与**风险标注**
  （`read-only` / `reads the project, writes a file` / `writes or overwrites engineering data` /
  `RISK changes CPU, deletes data or closes project - call by name`）。**对外通告的工具列表不变**，纯增量。
- **工具风险分级表 `ToolSafety`**（移植上游 53731356，工具名换成本仓实际注册的）：
  15 个高危工具标为 `DirectOnly`（动运行中CPU / 删工程数据 / 关或替换工程 ⇒ 必须直呼其名，让用户看见）。
- **`TempArtifactSweeper`**：启动时清理本服务器自建、带唯一后缀且超 24 小时的 `%TEMP%` 产物
  （导出块 / 接口回读 / 覆盖前备份 —— 含受 know-how 保护块的副本）。固定名目录与他人进程文件一律不动。
- **离线套件 219 → 228**：新增 `ToolSafetyTests`（38 条，正反两向）与 `TempArtifactSweeperTests`（9 条边界）。
- **闸门增强**：dead-tool-reference 现在还会校验 `ToolSafety.DirectOnly` 里的每个名字都真实注册
  （改名后静默失效的方向恰好最坏）；已做负向验证——注入假名字 FAIL、还原 PASS。**从不会触发的闸门等于没有闸门。**

### 变更
- 🔴 **反射桥不再通向 Portal / 工程**：`InvokeObject` / `InvokeService` 调用 `TiaPortal` 或 `Project` 的方法一律拒绝，
  报错指向 `CloseProject` / `Disconnect` / `SaveProject` / `OpenProject`。
  此前 `InvokeObject(portal, Dispose, allowWrite=true)` **实测返回 OK 并关掉了已打开的工程**
  —— 与 2026-09-24 事故同一类路径。真机复验：`portal.Dispose` / `portal.Quit` / `project.Close` / `project.Save` 全部被拒，
  portal 与工程安然无恙。判定依据是**寻址路径**（`resultKind`）而非实例类型：这些都是 COM CoClass RCW，
  `GetType()` 给的是 `System.__ComObject`，且 `instance is TiaPortal` / `as Project` 实测均不成立。
- 🔴 **8 个危险默认值改为省略即安全**：`overwrite`（`ImportBlocksFromDirectory`、`ImportHmiScreensFromDirectory`、
  `ImportHmiTagTablesFromDirectory`、`ImportTechnologyObjectsFromDirectory`、`ImportPlcTagTablesFromDirectory`）
  与 `autoCreateGroup`（`MoveBlockToGroup` / `MoveBlocksToGroup`）、`autoCreate`（`AutoClassifyBlocks`）默认改为 `false`。
  **`CloseProject` / `Disconnect` 的 `saveBeforeClose=true` 刻意保持不变** —— 它本来就是安全方向，改 false 才会丢未保存改动。
  内部调用（如 `PlcBuildAndImport`）本来就显式传 `overwrite: true`，故无连带影响。
  ⚠️ **老调用方注意**：原先依赖省略即覆盖的流程需显式补 `overwrite=true`。
- 工具总数 **231 → 232**（L0 6 → 7 / L1 57 / L2 168）；V18 构建 **203 → 204**。

### 修复
- 🔴 **`WritePlcSclSourceFile` 静默覆盖手改源文件**：目标已存在且内容不同 ⇒ 报错并要求显式 `overwrite=true`；
  内容相同仍幂等成功。此前重新生成的 SCL 会无声覆盖你手改过的同名文件。
- **下载预检的假成功**：`AlarmTextLibrariesDownload` / `UserManagementDownload`（本应是选择项）与
  `DownloadCertificate`（只是提示）原先被一并置 `Checked`——这些类型没有 `Checked` 属性，
  调用静默失败而预检仍报成功。现在只 `CheckBeforeDownload` 置 Checked，其余三者**显式不答并写日志**。
- **`DownloadConfigSetChecked` / `DownloadConfigSetSelection` 不再吞异常**：改为返回 bool 并记录
  哪个提示没答上、为什么（缺属性 / 非枚举 / 应用时抛异常）。
- **`HttpMcpServer` 的 `WWW-Authenticate` 改用 `AddHeader`**：该响应头在 .NET Framework 上受限，
  走索引器赋值会抛异常并中断处理器，导致错密钥的客户端看到**连接被重置**而不是 401。
- **HTTP 桥补 `Origin` 白名单**：浏览器发 `text/plain` 跨站 POST 不需要 CORS 预检但一定带 Origin，
  而该端点能**下载到 PLC、删块**。新增 `HttpSecurity.IsAllowedOrigin`（移植上游 53731356）。
- **`ImportBlocksFromDirectory(overwrite=false)` 改用 `ImportOptions.None`**：原先的块名检查只看当前组与文件名，
  而 XML 里的块名可能不同、块也可能在别的组里；现在由 TIA 自己拒绝替换。
- `InvokeService` 支持 `SecureString` 参数（`Protected` 一族需要密码），并可用 `allowWrite` 控制写方法。

### 移除
- （无）

### 工具总数
- **232**（L0 7 / L1 57 / L2 168）；V18 构建 **204**（隐藏 28 个：23 Unified HMI + 5 VCI）。

### 验证
V18/V20 构建 0 错误；离线套件 **228 passed / 0 failed**；三道闸门 + 清单/矩阵同步全PASS；
A/B/C/D 四批均经**真机隔离实例**验证（反射桥守卫、覆盖保护、默认值翻转各一组，共 30+ 断言全过）。

---

## [2.4.0] — 2026-09-28（**已定版**）

### 新增
- **VCI 版本控制 5 工具**（**V20+**，V18 构建下由 `#if !TIA_V18` 隐藏）：
  `GetVersionControlWorkspaces` / `CreateVersionControlWorkspace` / `GetVersionControlStatus` /
  `SyncVersionControlWorkspace` / `ConnectProjectToWorkspace`
  —— 把 TIA 工程变成 Git 能 diff / 提交的文本树；整工程自动映射与同步**默认 dryRun**。
- **`CompileAndDiagnoseHmi`**（V20+）：HMI 软件编译与诊断。
  同时 `CompileSoftware` 的编译服务解析统一为 `ResolveCompileService`（PLC / HMI 同源，HMI 走自己设备上溯）。

### 变更
- 🔴 **驱动写操作加护栏**：`AddDriveComponent` / `SetDriveParameter` 的 **`dryRun` 默认改为 `true`**
  —— **不显式传 `dryRun=false` 就只预览、不写入**（老调用方注意）。
  `AddDriveComponent` 实插后**按路径读回验证**，验证不通过**不谎报成功**。
- 🔴 **`Connect` / `GetState` 明确回报连接方式**：区分
  `attached (project X)` 与 **`started a NEW EMPTY instance — 你打开的工程没有被绑定`**，并给出排查清单；
  meta 里新增 `connectMode` / `attached` / `project` / `attachTimeoutMs`。
  （此前两种结果都只回一句 `Connected to TIA-Portal`，导致"工具看不到你开着的工程"无法自证。）
- **attach 超时可配**：原硬编码 30s → **默认 120s**，可用环境变量 `TIA_MCP_ATTACH_TIMEOUT_MS` 覆盖
  （小于 5s 视为笔误忽略）。
- **工具去行业化**（通用化）：
  - 语义词表外置为 `SemanticLexicon`：默认只放**中性跨行业**术语，行业词由环境变量
    `TIA_MCP_SEMANTIC_MOTION_EXTRA` / `TIA_MCP_SEMANTIC_PID_EXTRA` 注入（不改代码）。
  - HMI 模板确定性别名表外置：`TIA_MCP_HMI_TEMPLATE_ALIASES` 指向 JSON，**缺省为空表**（规则不生效、回落到打分）。
  - 清掉代码/文档里遗留的行业示例（起重机等）。

### 修复
- 🔴 **`S7ResScanner` 在 V20 上静默失效**：实测 V20 导出的 `.s7res` 是 **XML**
  （`<Comment Id="MLC_xxx">` + `<MultiLanguageText Lang="en-US">`），**V21 才是 YAML**；
  原实现只认 YAML ⇒ 对每个真文件都返回空 ⇒ **en-US 预检从未报警**。现按内容**双格式**解析
  （首个非空白字符 `<` → XML 分支），空 `<root />` 仍视为"没什么可警告"。
- 🔴 **`TiaMcpServer.V20.csproj` 缺 `obj-stage-*` / `bin-stage-*` 排除项** ⇒ 任何 V20 stage 构建都会把
  自己生成的 `AssemblyInfo.cs` 也 glob 进来源、报 **CS0579**；已补齐（与 V18 一致）。
- **A2b 接线（4 处）**：
  - `LadTextRenderer` 回读改用 `SimaticMlText` —— 修 `ABS(#a - #b)` 被读成 `#a.b`（凭空造出不存在的符号）；
  - HMI 画面遍历改用 `HmiScreenWalk` —— 嵌套画面组不再"查不到"；
  - `S7ResScanner` 接入 `.s7res` en-US 预检（见上）；
  - `PortalFailureClassifier` 接入 `McpHints.Recovery` —— 区分"这次调用失败"与"TIA 进程已死、未保存改动全丢"。
- **CI `dead tool references` 红**：新增工具后漏同步 `manifest/tools-list.json` 与
  `manifest/package-manifest.json`（222→225 那次）；已补并加进上面的维护规则。
- 🔴 **`Connect` 的隐式回退会凭空弹出博图窗口**（用户报"执行任务时老是莫名打开博图窗口，实际没创建新项目"）：
  attach 失败时回退自起实例，而这个回退分支**沿用了 `--with-ui`** ⇒ **每次重连会话都可能弹出一个空的 TIA 窗口**。
  现改为：**隐式回退一律强制无界面**；要窗口必须显式 `TIA_MCP_AUTOSTART_UI=1`；
  `Connect` 的 meta 新增 `fallbackHeadless`，消息里写明 `headless (no window)` 或 `WITH a user interface`；
  显式的 `ConnectIsolated` 仍尊重 `--with-ui`（那才是"我要看窗口"的表达）。
- 🔴 **新建实例会自动加载"上次用过的工程"**（用户报"自动连接 Openness 直接指向测试程序"）：
  TIA 会重新加载该 Windows 用户上次使用的工程 ⇒ 回退新建的实例一上来就开着别人/上次的工程，
  既让工具静默操作一个没人要求的工程，**又会占住那个工程文件、把用户自己的 TIA 挡在门外**。
  现改为：回退新建实例后**关闭它自动加载的任何工程（绝不保存）**，名字记入
  `Portal.AutoClosedProjects`，并通过 `Connect` 的 `meta.autoClosedProjects` 回报。

### 移除
- 无（本次未删任何工具）。

### 已知限制（2026-09-25 本机实测）
- 🔴 **本机 `Attach()` 通道不可用**：`Connect` 的"附着到已打开实例"路径与 `AttachToOpenProject` 在本机**一律挂死**
  —— 用对照实验确认：对**用户打开的 TIA** 和**我自己刚起的干净实例**，`Attach()` 都**不返回**（超时），
  与工程状态无关（`TiaPortal.GetProcesses()` 能看到进程，说明是 `Attach()` 本身挂死）。
  ⇒ **本机请走 `ConnectIsolated` + `OpenProject(path)` / `CreateProject`**（让工具自建实例去打开工程）；
  前提：该工程**未被 TIA UI 打开**（否则文件被锁）。
- 为此 `Connect` 现在会在 meta 里回报 **`attachAttempts`**（逐进程：版本跳过 / attach 抛错 / 超时 / 成功+有无工程），
  这类"看不到你的工程"的问题**从此不需要靠猜**。

### 工具数
| | 2.3.0 基线 | 2.4.0 |
|---|---|---|
| 工具总数 | ≈222 | **231** |
| L0 / L1 / L2 | — | **6 / 57 / 168** |

---

## [2.3.0] — 2026-07-04（本文件之前的交付基线）

- 交付基线（工具数 ≈222）。此前的逐版历史不在本文件内，见项目日志与仓库提交历史。
- **本文件自 2.4.0 起逐版维护。**
