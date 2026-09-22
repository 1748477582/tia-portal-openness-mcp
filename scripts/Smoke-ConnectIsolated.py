# -*- coding: utf-8 -*-
"""ConnectIsolated 的真机冒烟 —— **仅本地手动跑，绝不要放进 CI**。

⚠️ 它会**真的拉起一个 TIA Portal 进程**，所以只适合在装了 TIA 的机器上、由人显式执行。
   CI runner 上没有 TIA，也不需要这个检查（它验的是"隔离"这条**安全属性**，不是构建正确性）。

验证四件事：
  1. ConnectIsolated 能起一个**全新**实例并返回成功；
  2. **用户原有的 TIA 进程 PID 一个都没变**（最关键的安全性断言）；
  3. 新实例确实是我们的（出现在 PID 差集里）；
  4. Disconnect 之后**只回收我们自己的实例**，用户的仍在。

安全纪律（本脚本自己遵守，也请照做）：测试前快照 → 测试后差集 → **只杀自己拉起的 PID**。
用法： python scripts/Smoke-ConnectIsolated.py [exe 路径]
      （exe 默认取 bin-v20；V18 用户传 bin-v18 的那个）
"""
import json
import os
import pathlib
import subprocess
import sys
import threading
import time

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else (
    ROOT / "tools" / "tiaportal-mcp" / "src" / "TiaMcpServer" / "bin-v20" / "Release" / "net48" / "TiaMcpServer.exe")
WATCHDOG_SEC = 420


def pids_of(name):
    ps = ("Get-Process -Name %s -ErrorAction SilentlyContinue | "
          "Select-Object -ExpandProperty Id" % name)
    out = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                         capture_output=True, text=True).stdout
    return set(int(x) for x in out.split() if x.strip().isdigit())


def snapshot():
    return pids_of("Siemens.Automation.Portal"), pids_of("TiaMcpServer")


def main():
    if not EXE.exists():
        print("[FAIL] engine not found:", EXE)
        print("       先构建对应版本（bin-v20 / bin-v18），或用参数指定 exe。")
        return 1

    before_portal, before_mcp = snapshot()
    print("engine:", EXE)
    print("测试前快照：TIA=%s  TiaMcpServer=%s" % (sorted(before_portal), sorted(before_mcp)))

    env = dict(os.environ)
    env["TIAMCP_NO_REDIRECT"] = "1"
    env["TIA_MCP_PROFILE"] = "full"
    p = subprocess.Popen([str(EXE), "--logging", "0"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                         stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace",
                         bufsize=1, env=env)

    def watchdog():
        if p.poll() is None:
            print("!! 看门狗超时 %ds，强制结束引擎" % WATCHDOG_SEC)
            try:
                p.kill()
            except Exception:
                pass

    timer = threading.Timer(WATCHDOG_SEC, watchdog)
    timer.start()

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
                raise SystemExit("引擎结束了 stdout：\n" + p.stderr.read())
            line = line.strip()
            if not line:
                continue
            try:
                d = json.loads(line)
            except json.JSONDecodeError:
                continue
            if d.get("id") == seq[0]:
                return d

    def text_of(res):
        r = (res or {}).get("result", {})
        return ("\n".join(b.get("text") or "" for b in r.get("content", []) if b.get("type") == "text"),
                bool(r.get("isError")))

    ok = True
    ours = set()
    try:
        send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                            "clientInfo": {"name": "isolated-smoke", "version": "1"}})
        send("notifications/initialized", {}, notify=True)

        print("\n① 调 ConnectIsolated（可能要等 TIA 冷启动，最多 %ds）…" % WATCHDOG_SEC)
        t0 = time.time()
        txt, err = text_of(send("tools/call", {"name": "ConnectIsolated", "arguments": {}}))
        print("   耗时 %.1fs  isError=%s" % (time.time() - t0, err))
        print("   返回：%s" % txt[:300].replace("\n", " "))

        after_portal, _ = snapshot()
        ours = after_portal - before_portal
        print("\n② 差集：新出现的 TIA 进程 = %s" % sorted(ours))
        print("   用户原有 TIA PID 是否全部健在：%s" % before_portal.issubset(after_portal))
        if not before_portal.issubset(after_portal):
            print("   !! 用户进程有变化 —— 立刻停止")
            ok = False
        if err:
            print("   !! ConnectIsolated 报错")
            ok = False
        if not ours:
            print("   !! 没有观察到新实例 —— 无法证明「隔离」生效")
            ok = False

        if ok:
            txt2, err2 = text_of(send("tools/call", {"name": "GetState", "arguments": {}}))
            print("\n③ GetState：isError=%s  %s" % (err2, txt2[:200].replace("\n", " ")))

            print("\n④ 调 Disconnect（应只回收我们自己的实例）…")
            txt3, err3 = text_of(send("tools/call", {"name": "Disconnect", "arguments": {}}))
            print("   isError=%s  返回：%s" % (err3, txt3[:160].replace("\n", " ")))

            gone = False
            for _ in range(30):
                time.sleep(2)
                if not (ours & snapshot()[0]):
                    gone = True
                    break
            now_portal, _ = snapshot()
            print("\n⑤ 自建实例已消失=%s；用户原有 TIA PID 仍全部健在：%s"
                  % (gone, before_portal.issubset(now_portal)))
            if not before_portal.issubset(now_portal):
                print("   !! 用户的 TIA 被我们弄没了 —— 严重")
                ok = False
    finally:
        timer.cancel()
        try:
            p.stdin.close()
        except Exception:
            pass
        try:
            p.terminate()
        except Exception:
            pass

        # 只按差集清理：绝不碰用户原有进程
        time.sleep(2)
        leak = ours & snapshot()[0]
        if leak:
            print("\n清理：只杀我们拉起的 TIA PID %s" % sorted(leak))
            for pid in sorted(leak):
                subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True, text=True)
        final_portal = snapshot()[0]
        print("\n收尾：TIA=%s（用户原有 %s 是否保留：%s）"
              % (sorted(final_portal), sorted(before_portal), before_portal.issubset(final_portal)))
        if not before_portal.issubset(final_portal):
            ok = False
            print("!! 收尾后用户的 TIA 进程丢失")

    print("\n结论：" + ("ConnectIsolated 真机冒烟**通过**（隔离生效、用户实例未受影响、自建实例被正确回收）"
                       if ok else "真机冒烟**未通过** —— 见上面 !! 行"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
