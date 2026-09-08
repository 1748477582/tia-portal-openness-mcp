#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""最小化复现 CreateProject 失败, 抓取服务端 stdout 的真实异常。"""
import json, os, subprocess, sys, time, urllib.request, urllib.error, shutil, threading
from pathlib import Path

BASE_DIR = Path(r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20-test\Release\net48")
EXE = BASE_DIR / "TiaMcpServer.exe"
BASE_URL = "http://127.0.0.1:8767/"
RPC_URL = BASE_URL + "mcp"
LOG_DIR = Path(r"E:\TIA_V20_Test")
SRV_LOG = LOG_DIR / "server_stdout.log"
PROJECT_DIR = LOG_DIR / "V20Dbg"
RID = 0


def rpc(method, params=None):
    global RID
    RID += 1
    payload = {"jsonrpc": "2.0", "id": str(RID), "method": method}
    if params is not None:
        payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(RPC_URL, data=data,
                                headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=240) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"_http_error": e.code, "_body": e.read().decode("utf-8", "replace")[:500]}
    except Exception as e:
        return {"_error": str(e)[:300]}


def call_tool(name, args=None):
    raw = rpc("tools/call", {"name": name, "arguments": args or {}})
    content = raw.get("result", {}).get("content", [])
    text = "".join(c.get("text", "") for c in content if c.get("type") == "text")
    return (not raw.get("result", {}).get("isError", False)), text


def main():
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    if PROJECT_DIR.exists():
        shutil.rmtree(PROJECT_DIR, ignore_errors=True)
    # 关键: 不预建目录, 让 Openness 自己建 (排查"目录已存在"假设)
    env = os.environ.copy()
    env["PATH"] = (r"D:\Program Files\Siemens\Automation\Portal V20\Bin;"
                   r"C:\Program Files\Common Files\Siemens\Automation\Simatic OAM\bin;"
                   r"C:\Program Files (x86)\Common Files\Siemens\Bin;"
                   r"C:\Program Files (x86)\Common Files\Siemens\CommonArchiving;"
                   r"C:\Program Files (x86)\Common Files\Siemens\ACE\Bin;"
                   + env.get("PATH", ""))
    env["TiaPortalLocation"] = r"D:\Program Files\Siemens\Automation\Portal V20"
    cmd = [str(EXE), "--tia-major-version", "20", "--transport", "http", "--http-prefix", BASE_URL]
    srvlog = open(SRV_LOG, "w", encoding="utf-8", errors="replace")
    proc = subprocess.Popen(cmd, stdout=srvlog, stderr=subprocess.STDOUT,
                            cwd=str(BASE_DIR), env=env)
    try:
        # 等服务
        deadline = time.time() + 120
        while time.time() < deadline:
            try:
                urllib.request.urlopen(BASE_URL, timeout=2)
                break
            except Exception:
                time.sleep(0.5)
        else:
            print("FATAL: 服务未起来")
            return
        print("[ok] 服务可达")
        print("[init]", rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                                           "clientInfo": {"name": "dbg", "version": "1"}}).get("result", {}).get("serverInfo"))
        ok, t = call_tool("Connect")
        print(f"[Connect] ok={ok} {t[:200]}")
        if not ok:
            return
        ok, t = call_tool("CreateProject", {"directoryPath": str(PROJECT_DIR), "projectName": "V20DbgPrj"})
        print(f"[CreateProject] ok={ok} {t[:400]}")
        if ok:
            ok2, t2 = call_tool("GetProjectTree", {})
            print(f"[GetProjectTree] ok={ok2} {t2[:300]}")
            call_tool("Disconnect", {})
    finally:
        try:
            proc.terminate(); proc.wait(timeout=15)
        except Exception:
            try: proc.kill()
            except Exception: pass
        srvlog.flush(); srvlog.close()

    print("\n===== 服务端 stdout (含异常) =====")
    try:
        txt = SRV_LOG.read_text(encoding="utf-8", errors="replace")
        # 只打关键行
        for line in txt.splitlines():
            if any(k in line for k in ("Create", "create", "Exception", "Error", "error", "Fail", "fail")):
                print(line[:400])
    except Exception as e:
        print(f"读日志失败: {e}")


if __name__ == "__main__":
    main()
