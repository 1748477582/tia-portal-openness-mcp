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

## [未发布] — 2026-10-19

> 只加安装入口与文档，**不动代码、不动工具集**（仍 232 / V18 204），因此不占版本号。
> 下次有代码改动时并入对应版本条目。

### 新增
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
