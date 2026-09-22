# -*- coding: utf-8 -*-
"""端到端验「大响应寄存」的接线真的生效（不是只编译通过）。

为什么需要它：ExportStore 的**逻辑**有离线单测（82 条里的那 30 条），但"接线是否真的被调用"
只有真起引擎、真让一个响应超过阈值才知道。曾经最像"验过了"的假象就是：编译过 + 工具注册上了
+ 逻辑单测全绿 —— 而包装层根本没被 SDK 调用。这类"本地绿 ≠ 真能用"的缺口，只能靠端到端盯。

做法：把阈值压到 200（顺带验 TIA_MCP_MAX_RESPONSE_CHARS 真生效），拿纯离线工具 Bootstrap 当
超长响应，验证：被换成句柄 → 翻页能拼回等长内容 → 头部与首页一致 → 假句柄报错而不是返回空。

不需要 TIA。用法： python scripts/Smoke-ResponseParking.py [exe 路径]
退出码 0 = 接线生效；非 0 = 有断言失败（逐条打印）。
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
LIMIT = 200
BIG_TOOL = "Bootstrap"          # 纯离线、输出稳定超过 200 字符


def rpc_session(env_extra):
    env = dict(os.environ)
    env["TIAMCP_NO_REDIRECT"] = "1"
    env["TIA_MCP_PROFILE"] = "full"
    env.update(env_extra)
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


def envelope(res):
    r = (res or {}).get("result", {})
    txt = "\n".join(b.get("text") or "" for b in r.get("content", []) if b.get("type") == "text")
    try:
        return json.loads(txt), r
    except Exception:
        return {"message": txt, "meta": {}}, r


def main():
    if not EXE.exists():
        print("[FAIL] engine not found:", EXE)
        return 1
    print("engine:", EXE, "| threshold:", LIMIT)

    failures = []
    p, send = rpc_session({"TIA_MCP_MAX_RESPONSE_CHARS": str(LIMIT)})
    try:
        send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                            "clientInfo": {"name": "parking-smoke", "version": "1"}})
        send("notifications/initialized", {}, notify=True)

        e, r = envelope(send("tools/call", {"name": BIG_TOOL, "arguments": {}}))
        m = e.get("meta") or {}
        if r.get("isError"):
            print("[FAIL] %s 调用失败" % BIG_TOOL)
            return 1
        if not m.get("truncated") or not m.get("exportId"):
            failures.append("阈值已压到 %d，但 %s 的响应没有被寄存（truncated/exportId 缺失）—— 接线没生效"
                            % (LIMIT, BIG_TOOL))
        else:
            ex, total, head = m["exportId"], m["totalLength"], e.get("message", "")
            print("  parked: %s totalLength=%s returned=%s" % (ex, total, m.get("returned")))
            if len(head) != LIMIT:
                failures.append("头部切片 %d 字符 != 阈值 %d" % (len(head), LIMIT))

            parts, pages, off, guard = [head], 1, m.get("nextOffset"), 0
            while off is not None and guard < 500:
                guard += 1
                e2, r2 = envelope(send("tools/call", {"name": "GetExport",
                                                      "arguments": {"exportId": ex, "offset": off, "length": LIMIT}}))
                if r2.get("isError"):
                    failures.append("翻页失败：%s" % (e2.get("message") or "")[:200]); break
                m2 = e2.get("meta") or {}
                parts.append(e2.get("message", ""))
                pages += 1
                if m2.get("eof"):
                    break
                nxt = m2.get("nextOffset")
                if nxt is None or nxt <= off:
                    failures.append("翻页原地打转：offset=%s nextOffset=%s" % (off, nxt)); break
                off = nxt

            whole = "".join(parts)
            print("  paged: %d 页 → %d 字符（声明 %s）" % (pages, len(whole), total))
            if len(whole) != total:
                failures.append("翻页拼回的 %d 字符 != 声明的 totalLength %s（内容对不上）" % (len(whole), total))
            if not whole.startswith(head):
                failures.append("翻页结果与返回的头部不一致")
            if total > LIMIT and whole[-60:] in head:
                failures.append("结尾内容与头部相同 —— 可能根本没取到后文")

        _, r3 = envelope(send("tools/call", {"name": "GetExport",
                                            "arguments": {"exportId": "totally-bogus", "offset": 0}}))
        if not r3.get("isError"):
            failures.append("假句柄没有报错（应为 isError=true）—— 会让模型误以为'这份导出是空的'")
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
    print("[ ok ] 大响应寄存接线生效：超过阈值 → 句柄 + 头部；翻页可拼回等长内容；假句柄报错。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
