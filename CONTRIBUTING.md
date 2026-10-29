# 参与与提交

本文件只讲**怎么提**。要理解代码结构，先读 [AGENTS.md](AGENTS.md)。

## 提 Issue

- Bug：贴**症状 + 复现命令 + 期望 vs 实际**。涉及 TIA 的请附 TIA 版本、工具名、返回原文（不要只写"报错了"）。
- 功能：说明使用场景（你在什么工程、什么频次），不要只给方案。

## 提改动

1. 提交信息按 [docs/git-release-conventions.md](docs/git-release-conventions.md)：
   `<type>(<scope>): <subject>`，body 写清"症状 → 根因 → 证据"。
2. **工具集有变化**时三处同步：`manifest/tools-list.json`、`manifest/package-manifest.json`、
   `docs/tool-capability-matrix.md`。
3. 提交前跑完门禁（缺一项就别提）：

```powershell
python scripts\Check-VersionConsistency.py
python scripts\Check-ManifestConsistency.py
python scripts\Check-DeadToolReferences.py
dotnet run --project tools\tiaportal-mcp\tests\TiaMcpServer.OfflineChecks -c Release
# 期望：三条闸门 PASS，离线套件 228 passed / 0 failed
```

## 不允许

- 提交里出现 token、密钥、本机绝对路径、`_*.log`、一次性推送脚本
- force push `master`、改写已发布历史
- 把"编译通过"写成"已验证"；没证据的行为变更必须在 CHANGELOG 的「验证状态（如实）」里写明
- 把多个不相关改动压进一个提交

## 发布（维护者）

见 [docs/git-release-conventions.md](docs/git-release-conventions.md) §4：
闸门全绿 → 推代码 → 打 `vX.Y.Z` tag → Release 正文取 CHANGELOG 该版本段落。
