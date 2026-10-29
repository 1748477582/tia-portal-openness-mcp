# 提交、版本与发布规范

> 本文是本仓库的硬规则。**改代码前先读一遍**；提交与发布不符合本文即视为未完成。
> 依据：同类项目（Czarnak/tia-portal-mcp 等）的实践 + 本仓历史踩坑（见 `docs/lessons-and-shortcuts.md`）。

---

## 1. 提交信息

格式固定为 **Conventional Commits**：

```
<type>(<scope>): <subject>

<body：为什么改、改了什么、证据在哪>

<footer：Refs #12 / BREAKING CHANGE: ...>
```

### type（只允许这些）

| type | 用于 | 是否影响版本 |
|---|---|---|
| `feat` | 新增工具、新增能力、新增规则 | MINOR |
| `fix` | 修正错误行为（含"假成功"、错误判断） | PATCH |
| `docs` | 文档、README、CHANGELOG、注释 | PATCH |
| `refactor` | 不改变外部行为的重构 | PATCH |
| `test` | 测试与离线套件 | PATCH |
| `perf` | 性能 | PATCH |
| `build` | csproj、依赖版本（含 dependabot 式升级） | PATCH |
| `ci` | 工作流与闸门 | PATCH |
| `chore` | 杂务（清单重生成、格式化） | PATCH |
| `revert` | 回滚 | PATCH |

### scope（本仓约定）

`router`（tia_router.py）· `engine`（TiaMcpServer）· `prompts`（MCP Prompt）· `audit`（体检脚本）·
`launcher`（探测/启动器）· `manifest`（tools-list/package-manifest）· `docs` · `release`

### subject 规则

- 祈使句、不加句号、**不超过 50 字**
- 中文可以（本仓语境），但**必须是 "做了什么" 而不是 "尝试/调整一下"**
- 同一提交只做一件事；不把"修 bug + 升版本 + 改文档"混在一起

### body 规则（**默认简短**）

- **只写"哪个功能/能力变了"**，2–3 行以内。实现细节（函数名、锁名、字段名、内部调用链）**不写进提交信息**——
  它们属于代码与 `docs/lessons-and-shortcuts.md`。
- `fix` 类可多一句"原先什么症状"，但仍不展开根因推理。
- 需要证据的只在 CHANGELOG 的验证行里给一句结论，不在这里贴长表格。

---

## 2. 版本号

语义化版本 `MAJOR.MINOR.PATCH`：

- 破坏性变更（工具签名/行为不兼容、配置文件格式变化）→ **MAJOR**
- 新增工具/能力 → **MINOR**
- 修正、文档、重构、构建 → **PATCH**
- 一个版本一次发布；**不允许跳号占位**

版本号三处必须同时改（`scripts/Check-VersionConsistency.py` 会查）：
三个 csproj 的 `<AssemblyVersion>`、`manifest/package-manifest.json` 的 `bundleVersion`。

---

## 3. CHANGELOG

结构固定（Keep a Changelog 的子集）：

```markdown
## [X.Y.Z] — YYYY-MM-DD

> 一句话：本版重点。（工具集是否变化）

### 新增 / 变更 / 修复 / 文档   ← 按需出现，标题固定这四个

### 验证状态（如实）            ← 本仓强制小节
```

**粒度要求（重要）**：

- 每版 **不超过 5 条**，**一行一条**，只写"新增/变更/修复了什么能力"
- **不写实现细节**（函数名、锁名、字段名、参数名、内部流程），也不贴证据表格
- 验证压成**一行**：有实测就写"实测通过"，只有编译就写"仅编译/静态审查"，没做就写"未做"
- 需要长说明的，放 `docs/lessons-and-shortcuts.md`，CHANGELOG 只留指针式一句话

不允许把"编译通过"写成"已验证"。

---

## 4. 发布（Release）

1. 三条闸门 + 离线套件全绿：
   `Check-VersionConsistency.py`、`Check-ManifestConsistency.py`、`Check-DeadToolReferences.py`、`OfflineChecks`（228 passed）
2. 推送代码（见 §6）后打 tag：`vX.Y.Z`
3. 在 GitHub 建 Release，**正文直接取 CHANGELOG 该版本段落原文**（不另写一套）
4. Release 说明的分类由 `.github/release.yml` 按 **label** 自动归类：
   `enhancement` → Features · `bug` → Fixed · `documentation` → Documentation ·
   `dependencies` / `github_actions` → Maintenance · 其余 → Other changes
   ⇒ 提 PR/Issue 时**必须打 label**，否则发布说明全落进 "Other changes"

---

## 5. 证据规则（本仓最容易翻车的地方）

- 结论必须锚定可复核来源：命令输出、实测数字、文件行号
- **"工具返回成功" ≠ 成功**：导出要看磁盘、写入要看回读、编译要看错误数
- 反例测试：规则类改动必须给出"该报的时候报了、不该报的时候沉默"
- 环境类结论会过期：写"核实于某日"，并附复核命令

---

## 6. 分支与推送

- 主干 `master`；改动走 `<type>/<短描述>` 分支 + PR（单人仓可直推，但**提交信息与门禁不能省**）
- **禁止** force push 到 `master`、禁止改写已发布的提交历史
- 推送前必须：`_full_diff.py` 看清单 → 确认**没有私货**（token、本地绝对路径、`_*.log`、`push_*.py`）→ `_full_push.py`
- csproj 类文件远端是超集，**只能外科手术改版本行**（`_full_diff.py` 会跳过它们）

---

## 7. 发布前检查清单

- [ ] 版本号四处一致（3 个 csproj + manifest）
- [ ] CHANGELOG 有该版本段，且含"验证状态（如实）"
- [ ] 三条闸门 PASS，离线套件 228 passed
- [ ] `_full_diff.py` 清单里没有私货文件
- [ ] README 的版本号徽章与当前版本一致
- [ ] 有运行证据的项写清命令；没有的写明"未验证"
