#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""V18 verification of the two fixes:
  Fix #1 GetSoftwareTree: tool now requires softwarePath (test was missing it) -> we pass it.
  Fix #2 CompileAndDiagnosePlc: CompilerResult.Messages must be read on the STA thread;
        previously off-STA COM access crashed the whole server (0xE0434352).
This script recreates the exact crashing scenario (create project -> add CPU -> GetSoftwareTree
-> CompileAndDiagnosePlc) on V18 and asserts the server process stays alive.
"""
import json, os, subprocess, sys, time, urllib.request, urllib.error, shutil
from pathlib import Path

BASE_DIR = Path(r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v18\Release\net48")
EXE = BASE_DIR / "TiaMcpServer.exe"
BASE_URL = "http://127.0.0.1:8771/"
RPC_URL = BASE_URL + "mcp"
LOG_DIR = Path("E:/TIA_SmokeTest")
PROJECT_DIR = LOG_DIR / "V18CompileTest"
PROJECT_NAME = "V18CompileTest"
REQUEST_ID = 0

def log(m):
    print(f"[{time.strftime('%H:%M:%S')}] {m}")

def next_id():
    global REQUEST_ID; REQUEST_ID += 1; return str(REQUEST_ID)

def rpc(method, params=None):
    payload = {"jsonrpc": "2.0", "id": next_id(), "method": method}
    if params is not None: payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(RPC_URL, data=data, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=240) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code}: {e.read().decode('utf-8')}") from e

def call_tool(name, args=None):
    raw = rpc("tools/call", {"name": name, "arguments": args or {}})
    text = "".join(c.get("text", "") for c in raw.get("result", {}).get("content", []) if c.get("type") == "text")
    return not raw.get("result", {}).get("isError", False), raw, text

def wait_for_server(timeout=180):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            urllib.request.urlopen(BASE_URL, timeout=2); return True
        except Exception: time.sleep(0.5)
    return False

def main():
    if not EXE.exists():
        log("FATAL: exe not found"); sys.exit(1)
    if PROJECT_DIR.exists():
        try: shutil.rmtree(PROJECT_DIR, ignore_errors=True)
        except OSError: pass
    PROJECT_DIR.mkdir(parents=True, exist_ok=True)

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
        if not wait_for_server():
            log("FATAL: server did not start"); sys.exit(1)
        log("Server reachable")

        ok, raw, text = call_tool("Connect"); log(f"Connect: {'OK' if ok else 'FAIL'} {text[:80]}")
        if not ok:
            log("FATAL: Connect failed"); sys.exit(1)

        # Create a fresh project + CPU so we have a real (empty) PLC to compile.
        ok, raw, text = call_tool("CreateProject", {"directoryPath": str(PROJECT_DIR), "projectName": PROJECT_NAME})
        log(f"CreateProject: {'OK' if ok else 'FAIL'} {text[:80]}")

        ok, raw, text = call_tool("AddDevice", {"orderNumber": "6ES7517-3AP00-0AB0", "version": "V3.0", "deviceName": "PLC_1"})
        log(f"AddDevice S7-1517: {'OK' if ok else 'FAIL'} {text[:80]}")

        # Fix #1: GetSoftwareTree now called WITH softwarePath.
        ok, raw, text = call_tool("GetSoftwareTree", {"softwarePath": "PLC_1"})
        log(f"GetSoftwareTree(PLC_1): {'OK' if ok else 'FAIL'} len={len(text)}")
        if not ok:
            log("  -> GetSoftwareTree returned error (not a crash): " + text[:160])

        # Fix #2: CompileAndDiagnosePlc must NOT crash the server (was 0xE0434352).
        c1, raw, text = call_tool("CompileAndDiagnosePlc", {"softwarePath": "PLC_1"})
        log(f"CompileAndDiagnosePlc(PLC_1): {'OK' if c1 else 'FAIL'} {text[:160]}")

        # Newly routed off-STA COM fixes (were the same 0xE0434352 crash class):
        #  - CompileSoftware tool (McpServer.PlcSoftware.cs:3505)
        #  - GetCompileDiagnostics tool (McpServer.Optimizations.cs:343)
        c2, raw, text = call_tool("CompileSoftware", {"softwarePath": "PLC_1"})
        log(f"CompileSoftware(PLC_1): {'OK' if c2 else 'FAIL'} {text[:160]}")
        c3, raw, text = call_tool("GetCompileDiagnostics", {"softwarePath": "PLC_1"})
        log(f"GetCompileDiagnostics(PLC_1): {'OK' if c3 else 'FAIL'} {text[:160]}")
        compile_ok = c1 and c2 and c3

        # Liveness probe: a crash would make this call fail/timeout.
        ok, raw, text = call_tool("GetSoftwareTree", {"softwarePath": "PLC_1"})
        log(f"GetSoftwareTree(liveness probe): {'OK' if ok else 'FAIL'}")
        still_alive = ok

        ok, raw, text = call_tool("Disconnect", {"saveBeforeClose": True})
        log(f"Disconnect(after compile): {'OK' if ok else 'FAIL'} {text[:80]}")
        still_alive = still_alive and ok

        log("=" * 60)
        log("RESULT: " + ("VERIFIED OK" if (compile_ok and still_alive) else "NEEDS REVIEW"))
        log(f"  CompileAndDiagnosePlc  no-crash : {c1}")
        log(f"  CompileSoftware        no-crash : {c2}")
        log(f"  GetCompileDiagnostics  no-crash : {c3}")
        log(f"  Server still alive after all    : {still_alive}")
        log("=" * 60)
    finally:
        log("Terminating server...")
        try: proc.terminate()
        except Exception: pass
        try: proc.wait(timeout=15)
        except Exception: proc.kill()

if __name__ == "__main__":
    main()
