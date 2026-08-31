#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Discovery: connect to running TIA, list open projects + project tree so we can
find an existing PLC softwarePath to compile against (avoids CreateProject which is
flaky when the attached TIA has a modal/unsaved state)."""
import json, os, subprocess, sys, time, urllib.request, urllib.error
from pathlib import Path

BASE_DIR = Path(r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v18\Release\net48")
EXE = BASE_DIR / "TiaMcpServer.exe"
BASE_URL = "http://127.0.0.1:8772/"
RPC_URL = BASE_URL + "mcp"
REQUEST_ID = 0

def log(m): print(f"[{time.strftime('%H:%M:%S')}] {m}"); sys.stdout.flush()
def next_id():
    global REQUEST_ID; REQUEST_ID += 1; return str(REQUEST_ID)
def rpc(method, params=None):
    payload = {"jsonrpc": "2.0", "id": next_id(), "method": method}
    if params is not None: payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(RPC_URL, data=data, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code}: {e.read().decode('utf-8')}") from e
def call_tool(name, args=None):
    raw = rpc("tools/call", {"name": name, "arguments": args or {}})
    text = "".join(c.get("text", "") for c in raw.get("result", {}).get("content", []) if c.get("type") == "text")
    return not raw.get("result", {}).get("isError", False), raw, text
def wait_for_server(timeout=120):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            urllib.request.urlopen(BASE_URL, timeout=2); return True
        except Exception: time.sleep(0.5)
    return False

def main():
    if not EXE.exists(): log("FATAL: exe not found"); sys.exit(1)
    env = os.environ.copy()
    env["PATH"] = (r"C:\Program Files\Siemens\Automation\Portal V18\Bin;"
                   + r"C:\Program Files\Common Files\Siemens\Automation\Simatic OAM\bin;"
                   + r"C:\Program Files (x86)\Common Files\Siemens\Bin;"
                   + r"C:\Program Files (x86)\Common Files\Siemens\CommonArchiving;"
                   + r"C:\Program Files (x86)\Common Files\Siemens\ACE\Bin;" + env.get("PATH", ""))
    env["TiaPortalLocation"] = r"C:\Program Files\Siemens\Automation\Portal V18"
    cmd = [str(EXE), "--tia-major-version", "18", "--transport", "http", "--http-prefix", BASE_URL]
    log("Starting: " + " ".join(cmd))
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace", cwd=str(BASE_DIR), env=env)
    try:
        if not wait_for_server(): log("FATAL: server did not start"); sys.exit(1)
        log("Server reachable")
        ok, raw, text = call_tool("Connect"); log(f"Connect: {'OK' if ok else 'FAIL'} {text[:120]}")
        if not ok: log("FATAL: Connect failed"); sys.exit(1)
        ok, raw, text = call_tool("GetProject"); log(f"GetProject: {'OK' if ok else 'FAIL'}")
        print("----- GetProject -----"); print(text[:2000]); print("----------------------")
        ok, raw, text = call_tool("GetProjectTree"); log(f"GetProjectTree: {'OK' if ok else 'FAIL'}")
        print("----- GetProjectTree -----"); print(text[:3000]); print("--------------------------")
    finally:
        log("Terminating server...")
        try: proc.terminate()
        except Exception: pass
        try: proc.wait(timeout=15)
        except Exception: proc.kill()

if __name__ == "__main__":
    main()
