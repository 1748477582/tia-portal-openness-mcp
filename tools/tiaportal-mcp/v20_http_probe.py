#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""V20 TIA Portal MCP HTTP smoke probe."""

import json
import os
import subprocess
import sys
import time
import urllib.request
import urllib.error
from pathlib import Path

BASE_DIR = Path(r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48")
EXE = BASE_DIR / "TiaMcpServer.exe"
BASE_URL = "http://127.0.0.1:8765/"
RPC_URL = BASE_URL + "mcp"
LOG_DIR = Path("E:/TIA_SmokeTest")
LOG_FILE = LOG_DIR / "v20_http.log"
PROJECT_DIR = LOG_DIR / "V20SmokeProject"
PROJECT_NAME = "V20Smoke"
REQUEST_ID = 0


def log(msg):
    t = time.strftime("%Y-%m-%d %H:%M:%S")
    line = f"[{t}] {msg}"
    print(line)
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def next_id():
    global REQUEST_ID
    REQUEST_ID += 1
    return str(REQUEST_ID)


def rpc(method, params=None):
    payload = {
        "jsonrpc": "2.0",
        "id": next_id(),
        "method": method,
    }
    if params is not None:
        payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        RPC_URL,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            body = resp.read().decode("utf-8")
            return json.loads(body)
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8")
        raise RuntimeError(f"HTTP {e.code}: {body}") from e


def wait_for_server(timeout=60):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            urllib.request.urlopen(BASE_URL, timeout=2)
            return True
        except Exception:
            time.sleep(0.5)
    return False


def main():
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    try:
        if LOG_FILE.exists():
            LOG_FILE.unlink()
    except OSError:
        pass

    if not EXE.exists():
        log(f"FATAL: executable not found: {EXE}")
        sys.exit(1)

    # Clean previous project directory to avoid collisions
    if PROJECT_DIR.exists():
        import shutil
        shutil.rmtree(PROJECT_DIR, ignore_errors=True)
    PROJECT_DIR.mkdir(parents=True, exist_ok=True)

    env = os.environ.copy()
    # NOTE: do NOT prepend V20 Bin to PATH — doing so loads Siemens.Engineering
    # native DLLs from Bin that mismatch the PublicAPI assembly version and
    # crash the host with 0xC0000005 (access violation). Openness resolves
    # correct runtimes via TiaPortalLocation / GAC, not PATH.

    cmd = [
        str(EXE),
        "--tia-major-version", "20",
        "--transport", "http",
        "--http-prefix", BASE_URL,
    ]
    log(f"Starting server: {' '.join(cmd)}")
    proc = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        cwd=str(BASE_DIR),
        env=env,
    )

    try:
        log("Waiting for HTTP server...")
        if not wait_for_server(timeout=120):
            log("FATAL: server did not become ready in time")
            out, _ = proc.communicate(timeout=10)
            log(f"Server stdout:\n{out}")
            sys.exit(1)
        log("HTTP server is reachable")

        log("=== MCP initialize ===")
        result = rpc("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "v20-smoke-probe", "version": "1.0.0"},
        })
        log(f"initialize result: {json.dumps(result, ensure_ascii=False, indent=2)[:500]}")

        log("=== tools/list ===")
        result = rpc("tools/list")
        tools = result.get("result", {}).get("tools", [])
        log(f"Tool count: {len(tools)}")
        v20_tools = [t["name"] for t in tools if "V20" in t.get("name", "") or "Unified" in t.get("name", "")]
        log(f"V20/Unified specific tools: {len(v20_tools)}")

        log("=== Connect ===")
        result = rpc("tools/call", {"name": "Connect", "arguments": {}})
        log(f"Connect result: {json.dumps(result, ensure_ascii=False, indent=2)[:800]}")
        if result.get("result", {}).get("isError"):
            raise RuntimeError("Connect returned error")

        log("=== CreateProject ===")
        result = rpc("tools/call", {
            "name": "CreateProject",
            "arguments": {
                "directoryPath": str(PROJECT_DIR),
                "projectName": PROJECT_NAME,
            },
        })
        log(f"CreateProject result: {json.dumps(result, ensure_ascii=False, indent=2)[:800]}")
        if result.get("result", {}).get("isError"):
            raise RuntimeError("CreateProject returned error")

        log("=== AddDevice (CPU 1517-3 PN/DP) ===")
        result = rpc("tools/call", {
            "name": "AddDevice",
            "arguments": {
                "orderNumber": "6ES7517-3AP00-0AB0",
                "version": "V3.0",
                "deviceName": "PLC_1",
            },
        })
        log(f"AddDevice result: {json.dumps(result, ensure_ascii=False, indent=2)[:1000]}")
        if result.get("result", {}).get("isError"):
            raise RuntimeError("AddDevice returned error")

        log("=== GetProjectTree (with PLC) ===")
        result = rpc("tools/call", {"name": "GetProjectTree", "arguments": {}})
        log(f"GetProjectTree result: {json.dumps(result, ensure_ascii=False, indent=2)[:1500]}")

        log("=== GetProjectSkeleton ===")
        result = rpc("tools/call", {"name": "GetProjectSkeleton", "arguments": {"softwarePath": "PLC_1"}})
        log(f"GetProjectSkeleton result: {json.dumps(result, ensure_ascii=False, indent=2)[:1500]}")
        if result.get("result", {}).get("isError"):
            raise RuntimeError("GetProjectSkeleton returned error")

        log("=== CompileAndDiagnosePlc ===")
        result = rpc("tools/call", {"name": "CompileAndDiagnosePlc", "arguments": {"softwarePath": "PLC_1"}})
        log(f"CompileAndDiagnosePlc result: {json.dumps(result, ensure_ascii=False, indent=2)[:1500]}")

        log("=== SaveProject ===")
        result = rpc("tools/call", {"name": "SaveProject", "arguments": {}})
        log(f"SaveProject result: {json.dumps(result, ensure_ascii=False, indent=2)[:800]}")
        if result.get("result", {}).get("isError"):
            raise RuntimeError("SaveProject returned error")

        log("=== Disconnect ===")
        result = rpc("tools/call", {"name": "Disconnect", "arguments": {}})
        log(f"Disconnect result: {json.dumps(result, ensure_ascii=False, indent=2)[:800]}")

        log("SMOKE TEST PASSED")
    except Exception as e:
        log(f"SMOKE TEST FAILED: {e}")
        try:
            out, _ = proc.communicate(timeout=15)
            log(f"Server stdout tail:\n{out[-4000:]}")
        except Exception:
            pass
        sys.exit(1)
    finally:
        log("Terminating server...")
        proc.terminate()
        try:
            proc.wait(timeout=30)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=10)
        log(f"Server exit code: {proc.returncode}")


if __name__ == "__main__":
    main()
