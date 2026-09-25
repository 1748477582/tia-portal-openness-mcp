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

## [2.4.0] — 2026-09-24（**未打版**：代码已就位并过 CI，版本号待统一 bump）

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
