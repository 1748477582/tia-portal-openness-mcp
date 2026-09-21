# -*- coding: utf-8 -*-
"""lite 档必须是一个**能用**的档：只拿到 lite 的模型仍要能走完黄金路径。

为什么需要这道闸：lite 只暴露 [L0]/[L1]。曾经（同样的坑）lite 会话能连上工程、看到树，
却既列不出块、也用不了首选的文档导入 —— 因为那些工具是 [L2]。没有任何东西在盯这件事，
所以谁也没发现。这道闸就是盯它的。

⚠️ 本仓与上游的档位机制不同，闸门按本仓实际语义断言：
   * 本仓用**环境变量** `TIA_MCP_PROFILE=lite|full` 切换（上游是 `--profile` 命令行）；
   * 本仓**默认就是 full**（lite 是 opt-in），所以这里断言 default == full，
     而不是上游的 default == lite。环境变了，断言也得跟着变，否则闸门会假报红/假报绿。

用法： python scripts/Check-LiteProfile.py [exe 路径]
退出码 0 = lite 自足、够得着全部黄金路径、且在 host 上限内；1 = 有问题。
"""
import json
import os
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT / "runtime" / "v21" / "TiaMcpServer.exe"

# 文档化的黄金路径：定位 → 连接/打开 → 读 → 写 → 编译 → 保存。
# 这里的每个名字都被 README/GetAuthoringGuide/runbook 引用过；lite 少一个，
# 就等于对外宣传了一条它走不通的流程。
# 注意：FindTools/CallTool 是上游的"从 lite 逃出去"的桥，本仓**没有**这两个工具，
# 所以不列进来（否则闸门会因"工具根本不存在"报红 —— 那是另一件事，属补齐清单 B 类）。
REQUIRED = [
    # 定位 / 诊断
    "Bootstrap", "Doctor", "GetAuthoringGuide", "GetState",
    # 会话 + 工程
    "Connect", "ConnectIsolated", "Disconnect", "OpenProject", "CreateProject", "AttachToOpenProject",
    "CloseProject", "SaveProject", "GetProject", "GetProjectTree", "GetSoftwareTree",
    # 读 / 理解
    "GetBlocks", "GetBlockInfo", "DescribeBlockLogic", "GetPlcTagTables", "GetCrossReferences",
    # 写（首选文档路径）
    "ScaffoldProject", "PlcBuildAndImport", "WritePlcSclSourceFile",
    "ImportFromDocuments", "ExportBlocksAsDocuments", "ImportBlocksFromDocuments",
    "GenerateBlocksFromExternalSource",
    # 校验
    "CompileSoftware", "CompileAndDiagnosePlc",
]

# VS Code 拒绝启用超过这个数的工具；lite 存在的意义之一就是压在这条线以内。
HOST_TOOL_CAP = 128


def tools_for_profile(profile):
    """profile=None 表示"用户不给任何旗标、也不设环境变量时拿到的那套"。"""
    env = dict(os.environ)
    env.pop("TIA_MCP_PROFILE", None)
    env["TIAMCP_NO_REDIRECT"] = "1"
    if profile:
        env["TIA_MCP_PROFILE"] = profile
    p = subprocess.Popen([str(EXE), "--logging", "0"],
                         stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                         text=True, encoding="utf-8", errors="replace", bufsize=1, env=env)
    seq = [0]

    def send(method, params=None, notify=False):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not notify:
            seq[0] += 1
            msg["id"] = seq[0]
        p.stdin.write(json.dumps(msg) + "\n")
        p.stdin.flush()
        if notify:
            return None
        while True:
            line = p.stdout.readline()
            if not line:
                raise SystemExit("engine closed stdout:\n" + p.stderr.read())
            line = line.strip()
            if not line:
                continue
            try:
                d = json.loads(line)
            except json.JSONDecodeError:
                continue
            if d.get("id") == seq[0]:
                return d

    try:
        send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                            "clientInfo": {"name": "lite-check", "version": "1"}})
        send("notifications/initialized", {}, notify=True)
        return [t["name"] for t in send("tools/list", {})["result"]["tools"]]
    finally:
        try:
            p.stdin.close()
        except Exception:
            pass
        p.terminate()


def main():
    if not EXE.exists():
        print("[FAIL] engine not found:", EXE)
        return 1
    print("engine:", EXE)

    full = tools_for_profile("full")
    lite = tools_for_profile("lite")
    default = tools_for_profile(None)
    print("full    profile : %d tools" % len(full))
    print("lite    profile : %d tools" % len(lite))
    print("default profile : %d tools" % len(default))

    failures = []

    missing = [n for n in REQUIRED if n not in lite]
    if missing:
        failures.append("lite 缺少黄金路径工具：%s" % ", ".join(missing))

    unknown = [n for n in REQUIRED if n not in full]
    if unknown:
        failures.append("REQUIRED 列了本引擎根本没有的工具（清单要跟着工具集更新）：%s" % ", ".join(unknown))

    if len(lite) > HOST_TOOL_CAP:
        failures.append("lite 暴露 %d 个工具，超过它本应遵守的 %d 上限" % (len(lite), HOST_TOOL_CAP))

    if not lite:
        failures.append("lite 一个工具都没暴露")

    # 哨兵：两个探针若返回同一套，下面所有断言都失去意义。
    if len(full) <= len(lite):
        failures.append("full(%d) 不大于 lite(%d) —— 两个探针没有区分开档位，本检查证明不了任何事"
                        % (len(full), len(lite)))

    # 本仓默认是 full（lite 为 opt-in）；默认档必须等于 full。
    if sorted(default) != sorted(full):
        failures.append("默认档（无旗标、无环境变量）不是 full：%d 个 vs full 的 %d 个"
                        % (len(default), len(full)))

    for f in failures:
        print("[FAIL]", f)
    if failures:
        return 1
    print("[ ok ] lite 覆盖全部 %d 个黄金路径工具、在 %d 上限内；full 比 lite 多 %d 个；默认档 = full"
          % (len(REQUIRED), HOST_TOOL_CAP, len(full) - len(lite)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
