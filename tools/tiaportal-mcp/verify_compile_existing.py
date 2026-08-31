#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Verify the STA-compile fixes against an EXISTING open project (PLC / 1500T_KGSB),
avoiding CreateProject (which fails when the attached TIA already has an unsaved
open project). Exercises all three compile tools that read CompilerResult.Messages
off-STA (the 0xE0434352 crash class) and asserts the server stays alive:
  - CompileAndDiagnosePlc   (McpServer.PlcSoftware.cs:892 routed)
  - CompileSoftware         (McpServer.PlcSoftware.cs:3505 routed - NEW)
  - GetCompileDiagnostics   (McpServer.Optimizations.cs:343 routed - NEW)
"""
import json, os, subprocess, sys, time, urllib.request, urllib.error
from pathlib import Path

BASE_DIR = Path(r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v18\Release\net48")
EXE = BASE_DIR / "TiaMcpServer.exe"
BASE_URL = "http://127.0.0.1:8774/"
RPC_URL = BASE_URL + "mcp"
SOFTWARE = "1500T_KGSB"
REQUEST_ID = 0

def log(m): print(f"[{time.strftime('%H:%M:%S')}] {m}", flush=True)
def next_id():
    global REQUEST_ID; REQUEST_ID += 1; return str(REQUEST_ID)
def rpc(method, params=None, timeout=600):
    payload = {"jsonrpc": "2.0", "id": next_id(), "method": method}
    if params is not None: payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(RPC_URL, data=data, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code}: {e.read().decode('utf-8')}") from e
def call_tool(name, args=None, timeout=600):
    raw = rpc("tools/call", {"name": name, "arguments": args or {}}, timeout=timeout)
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

        # All three compile tools that previously crashed off-STA when reading .Messages.
        c1, raw, text = call_tool("CompileAndDiagnosePlc", {"softwarePath": SOFTWARE}, timeout=600)
        log(f"CompileAndDiagnosePlc({SOFTWARE}): {'OK' if c1 else 'FAIL'} {text[:160]}")
        c2, raw, text = call_tool("CompileSoftware", {"softwarePath": SOFTWARE}, timeout=600)
        log(f"CompileSoftware({SOFTWARE}): {'OK' if c2 else 'FAIL'} {text[:160]}")
        c3, raw, text = call_tool("GetCompileDiagnostics", {"softwarePath": SOFTWARE}, timeout=600)
        log(f"GetCompileDiagnostics({SOFTWARE}): {'OK' if c3 else 'FAIL'} {text[:160]}")
        compile_ok = c1 and c2 and c3

        # Liveness probe: a crash would make this call fail/timeout.
        ok, raw, text = call_tool("GetProjectTree", timeout=120)
        log(f"GetProjectTree(liveness probe): {'OK' if ok else 'FAIL'}")
        still_alive = ok

        # Close WITHOUT saving (project is IsModified; saving could prompt a dialog).
        ok, raw, text = call_tool("Disconnect", {"saveBeforeClose": False}, timeout=120)
        log(f"Disconnect(no-save): {'OK' if ok else 'FAIL'} {text[:80]}")
        still_alive = still_alive and ok

        log("=" * 64)
        log("RESULT: " + ("VERIFIED OK" if (compile_ok and still_alive) else "NEEDS REVIEW"))
        log(f"  CompileAndDiagnosePlc  no-crash : {c1}")
        log(f"  CompileSoftware        no-crash : {c2}")
        log(f"  GetCompileDiagnostics  no-crash : {c3}")
        log(f"  Server still alive after all    : {still_alive}")
        log("=" * 64)
    finally:
        log("Terminating server...")
        try: proc.terminate()
        except Exception: pass
        try: proc.wait(timeout=15)
        except Exception: proc.kill()

if __name__ == "__main__":
    main()
