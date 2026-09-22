# -*- coding: utf-8 -*-
"""端到端验「参数诊断」接线真的生效（不需要 TIA）。

为什么必须端到端：ArgDiagnostics 的**判定逻辑**有 24 条离线用例，但"这层有没有真的被调用"
只有真起引擎、真发一次错参数才知道。B7 的教训就是：编译过 + 逻辑单测绿 ≠ 接线生效。

断言（两个方向都要，缺一不可）：
  ① 正向：写错参数名 → 报错里必须点破"会被静默忽略" + 给出正确签名 + 纠名。
  ② 正向：少传必填（从真 schema 里挑一个有 required 的工具）→ 必须点名缺哪个。
  ③ 正向：**无参工具**喂垃圾参数 → 必须拦（否则 SaveProject 这类会被喂垃圾还照常执行）。
  ④ 反向哨兵：合法调用**不许被拦**（诊断层坏掉的另一种表现是把能跑的拦下来）。
  ⑤ 反向哨兵：无参工具正常调用不许报错。

用法： python scripts/Smoke-ArgDiagnostics.py [exe 路径]
退出码 0 = 接线生效；非 0 = 有断言失败。
"""
import json
import os
import pathlib
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else (
    ROOT / "runtime" / "v21" / "TiaMcpServer.exe")

NO_ARG_TOOL = "GetState"          # 无参：正反两个方向都要试
SAFE_TOOL = "ListExports"         # 有可选参数、纯内存，用来验"合法调用不被拦"


def session():
    env = dict(os.environ)
    env["TIAMCP_NO_REDIRECT"] = "1"
    env["TIA_MCP_PROFILE"] = "full"
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
        p.stdin.write(json.dumps(msg, ensure_ascii=False) + "\n")
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

    return p, send


def text_of(res):
    r = (res or {}).get("result", {})
    return ("\n".join(b.get("text") or "" for b in r.get("content", []) if b.get("type") == "text"),
            bool(r.get("isError")))


def main():
    if not EXE.exists():
        print("[FAIL] engine not found:", EXE)
        return 1
    print("engine:", EXE)

    failures = []
    p, send = session()
    try:
        send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                            "clientInfo": {"name": "argdiag-smoke", "version": "1"}})
        send("notifications/initialized", {}, notify=True)

        # 从真 schema 里挑一个"有必填参数"的工具（这样断言用的就是真签名，不是我猜的）
        tools = send("tools/list", {}).get("result", {}).get("tools", [])
        required_tool, required_names = None, []
        for t in tools:
            req = (t.get("inputSchema") or {}).get("required") or []
            props = (t.get("inputSchema") or {}).get("properties") or {}
            if req and props:
                required_tool, required_names = t["name"], list(req)
                break
        print("  schema 探得必填工具: %s required=%s" % (required_tool, required_names))

        # ① 参数名写错
        txt, err = text_of(send("tools/call", {"name": SAFE_TOOL,
                                               "arguments": {"limitt": 5}}))
        print("① %s(limitt=5) isError=%s" % (SAFE_TOOL, err))
        print("   msg: %s" % txt[:220].replace("\n", " "))
        if not err:
            failures.append("写错参数名没有被拦下来")
        if "SILENTLY IGNORED" not in txt:
            failures.append("没有点破'参数会被静默忽略'")
        if "Expected signature" not in txt:
            failures.append("没有给出正确签名")
        if "Did you mean" not in txt or "limitt -> limit" not in txt:
            failures.append("没有给出纠名（limitt -> limit）")
        if "nothing was executed" not in txt:
            failures.append("没有给出'什么都没执行'的保证")

        # ② 少传必填（用真 schema 挑出来的工具）
        if required_tool:
            txt2, err2 = text_of(send("tools/call", {"name": required_tool, "arguments": {}}))
            print("② %s() isError=%s" % (required_tool, err2))
            print("   msg: %s" % txt2[:220].replace("\n", " "))
            if not err2:
                failures.append("%s 少传必填却没被拦" % required_tool)
            if "missing required" not in txt2:
                failures.append("少传必填时没有说明'缺必填参数'")
            if required_names and not any(n in txt2 for n in required_names):
                failures.append("少传必填时没有点名缺哪个（%s）" % required_names)
        else:
            failures.append("tools/list 里找不到带 required 的工具 —— 无法验必填路径")

        # ③ 无参工具喂垃圾参数：必须拦（这是上游修过的真坑）
        txt3, err3 = text_of(send("tools/call", {"name": NO_ARG_TOOL,
                                                 "arguments": {"whatever": 1}}))
        print("③ %s(whatever=1) isError=%s" % (NO_ARG_TOOL, err3))
        print("   msg: %s" % txt3[:220].replace("\n", " "))
        if not err3:
            failures.append("%s 是无参工具，喂垃圾参数竟然没被拦（后果：SaveProject 这类会照常执行）" % NO_ARG_TOOL)
        if "takes no arguments" not in txt3:
            failures.append("无参工具的参数错误没有说清'takes no arguments'")

        # ④ 反向哨兵：合法调用不许被拦
        txt4, err4 = text_of(send("tools/call", {"name": SAFE_TOOL, "arguments": {"limit": 5}}))
        print("④ %s(limit=5) isError=%s（应为 False）" % (SAFE_TOOL, err4))
        if err4:
            failures.append("合法调用被拦下来了：%s" % txt4[:200])

        # ⑤ 反向哨兵：无参工具正常调用不许报错
        txt5, err5 = text_of(send("tools/call", {"name": NO_ARG_TOOL, "arguments": {}}))
        print("⑤ %s() isError=%s（应为 False）" % (NO_ARG_TOOL, err5))
        if err5:
            failures.append("无参工具正常调用被拦：%s" % txt5[:200])
    finally:
        try:
            p.stdin.close()
        except Exception:
            pass
        p.terminate()

    for f in failures:
        print("[FAIL]", f)
    if failures:
        return 1
    print("[ ok ] 参数诊断接线生效：写错/少传/无参喂垃圾都被拦且说清了原因；合法调用不受影响。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
