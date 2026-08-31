#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""V20 TIA Portal MCP — full feature smoke test (RCW-safe, post-Phase B refactor)."""

import json
import os
import subprocess
import sys
import time
import urllib.request
import urllib.error
import shutil
from pathlib import Path

BASE_DIR = Path(r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48")
EXE = BASE_DIR / "TiaMcpServer.exe"
BASE_URL = "http://127.0.0.1:8765/"
RPC_URL = BASE_URL + "mcp"
LOG_DIR = Path("E:/TIA_SmokeTest")
LOG_FILE = LOG_DIR / "v20_full_test.log"
PROJECT_DIR = LOG_DIR / "V20FullTestProject"
PROJECT_NAME = "V20FullTest"
REQUEST_ID = 0

RESULTS = {"pass": 0, "fail": 0, "skip": 0, "tests": []}


def log(msg):
    t = time.strftime("%Y-%m-%d %H:%M:%S")
    line = f"[{t}] {msg}"
    print(line)
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except OSError:
        pass


def next_id():
    global REQUEST_ID
    REQUEST_ID += 1
    return str(REQUEST_ID)


def rpc(method, params=None):
    payload = {"jsonrpc": "2.0", "id": next_id(), "method": method}
    if params is not None:
        payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        RPC_URL, data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=180) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8")
        raise RuntimeError(f"HTTP {e.code}: {body}") from e


def call_tool(name, args=None):
    """Call an MCP tool and return (ok, result_obj, raw_text)."""
    raw = rpc("tools/call", {"name": name, "arguments": args or {}})
    content = raw.get("result", {}).get("content", [])
    text = ""
    for c in content:
        if c.get("type") == "text":
            text += c.get("text", "")
    is_error = raw.get("result", {}).get("isError", False)
    return not is_error, raw, text


def check(label, ok, detail=""):
    status = "PASS" if ok else "FAIL"
    RESULTS["tests"].append((label, status, detail[:200]))
    if ok:
        RESULTS["pass"] += 1
    else:
        RESULTS["fail"] += 1
    log(f"  [{status}] {label} {detail[:120]}")


def wait_for_server(timeout=120):
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
    # Skip log deletion — sandbox prevents file ops on E:

    if not EXE.exists():
        log(f"FATAL: {EXE} not found")
        sys.exit(1)

    if PROJECT_DIR.exists():
        try: shutil.rmtree(PROJECT_DIR, ignore_errors=True)
        except OSError: pass
    PROJECT_DIR.mkdir(parents=True, exist_ok=True)

    env = os.environ.copy()
    # Full Siemens PATH (same pattern as V18 mcp.json config, adapted for V20 on D:)
    env["PATH"] = (
        r"D:\Program Files\Siemens\Automation\Portal V20\Bin;"
        + r"C:\Program Files\Common Files\Siemens\Automation\Simatic OAM\bin;"
        + r"C:\Program Files (x86)\Common Files\Siemens\Bin;"
        + r"C:\Program Files (x86)\Common Files\Siemens\CommonArchiving;"
        + r"C:\Program Files (x86)\Common Files\Siemens\ACE\Bin;"
        + env.get("PATH", "")
    )
    env["TiaPortalLocation"] = r"D:\Program Files\Siemens\Automation\Portal V20"
    cmd = [str(EXE), "--tia-major-version", "20", "--transport", "http", "--http-prefix", BASE_URL]
    log(f"Starting: {' '.join(cmd)}")
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace",
                            cwd=str(BASE_DIR), env=env)

    software_path = None
    try:
        # ── Phase 0: startup ──
        log("--- Phase 0: Server Startup ---")
        if not wait_for_server(timeout=180):
            log("FATAL: server timeout")
            sys.exit(1)
        check("Server reachable", True)

        # ── Phase 1: initialize + tool list ──
        log("--- Phase 1: Initialize & Tool List ---")
        result = rpc("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "v20-full-test", "version": "1.0.0"},
        })
        check("Initialize", "result" in result)
        svr = result.get("result", {}).get("serverInfo", {})
        check("ServerName", "TiaMcp" in svr.get("name", ""))

        result = rpc("tools/list")
        tools = result.get("result", {}).get("tools", [])
        tool_names = [t["name"] for t in tools]
        log(f"Total tools: {len(tools)}")
        check("Tool count > 160", len(tools) > 160, f"got {len(tools)}")

        # V20-specific tools
        unified_tools = [n for n in tool_names if "Unified" in n]
        check("Unified HMI tools present", len(unified_tools) > 0, f"found {len(unified_tools)}: {unified_tools[:5]}...")
        log(f"UnifiedHmi tools: {unified_tools}")

        # ── Phase 2: Connect + Project Setup ──
        log("--- Phase 2: Connect & Project Setup ---")
        ok, raw, text = call_tool("Connect")
        check("Connect", ok, text[:100])

        # Try to get the open project name first (from Attach)
        ok, raw, text = call_tool("GetProjects")
        log(f"GetProjects raw: {text[:500]}")
        projects = []
        try:
            result_content = raw.get("result", {}).get("content", [])
            for c in result_content:
                if c.get("type") == "text":
                    ct = c.get("text", "")
                    try:
                        parsed = json.loads(ct)
                        if isinstance(parsed, list):
                            projects = parsed
                        elif isinstance(parsed, dict) and "items" in parsed:
                            projects = parsed["items"]
                    except:
                        pass
        except:
            pass

        if projects:
            proj = projects[0]
            proj_name = proj.get("name", "") if isinstance(proj, dict) else str(proj)
            log(f"Found open project: {proj_name}")
            ok2, raw2, text2 = call_tool("AttachToOpenProject", {"projectName": proj_name})
            check("AttachToOpenProject", ok2, text2[:100])
            software_path = proj_name  # Use existing project's PLC
        else:
            # Fallback: create new project
            ok, raw, text = call_tool("CreateProject", {
                "directoryPath": str(PROJECT_DIR), "projectName": PROJECT_NAME,
            })
            check("CreateProject", ok, text[:100])

            # Try AddDevice with a simpler CPU (V20 may not have 1517T)
            ok, raw, text = call_tool("AddDevice", {
                "orderNumber": "6ES7517-3AP00-0AB0",
                "version": "V3.0",
                "deviceName": "PLC_1",
            })
            check("AddDevice S7-1517", ok, text[:100])
            if ok:
                software_path = "PLC_1"

        if not software_path:
            # Last resort: try to find software path from project tree
            log("WARNING: No PLC available, using project name as software path")
            software_path = PROJECT_NAME

        log(f"Using software_path: {software_path}")

        # ── Phase 3: Device operations (RCW-safe) ──
        log("--- Phase 3: Device Operations ---")
        ok, raw, text = call_tool("GetDevices")
        check("GetDevices", ok, text[:100])

        ok, raw, text = call_tool("GetProjectTree")
        check("GetProjectTree", ok, text[:120])

        ok, raw, text = call_tool("GetSoftwareTree", {"softwarePath": software_path})
        check("GetSoftwareTree", ok, text[:120])

        # ── Phase 4: Block operations (RCW-safe) ──
        log("--- Phase 4: Block Operations ---")
        ok, raw, text = call_tool("GetBlocks", {"softwarePath": software_path, "regexName": ""})
        check("GetBlocks (empty)", ok, text[:100])

        ok, raw, text = call_tool("GetBlocksWithHierarchy", {"softwarePath": software_path})
        check("GetBlocksWithHierarchy", ok, text[:100])

        ok, raw, text = call_tool("GetProjectSkeleton", {"softwarePath": software_path})
        check("GetProjectSkeleton", ok, text[:100])

        ok, raw, text = call_tool("SuggestBlockNumber", {
            "softwarePath": software_path, "blockType": "FB", "preferredNumber": 100,
        })
        check("SuggestBlockNumber FB100", ok, text[:100])

        # ── Phase 5: Type operations (RCW-safe) ──
        log("--- Phase 5: Type Operations ---")
        ok, raw, text = call_tool("GetTypes", {"softwarePath": software_path, "regexName": ""})
        check("GetTypes (empty)", ok, text[:100])

        # ── Phase 6: Compile ──
        log("--- Phase 6: Compile ---")
        ok, raw, text = call_tool("CompileAndDiagnosePlc", {"softwarePath": software_path})
        check("CompileAndDiagnosePlc", ok, text[:100])

        # ── Phase 7: AnalyzeBlockImpact (RCW-safe internal) ──
        log("--- Phase 7: AnalyzeBlockImpact ---")
        ok, raw, text = call_tool("AnalyzeBlockImpact", {
            "softwarePath": software_path, "blockName": "Main", "blockScope": "",
        })
        check("AnalyzeBlockImpact Main", ok, text[:100])

        # ── Phase 8: Export (RCW-safe, all wrapped in STA) ──
        log("--- Phase 8: Export Operations ---")
        export_dir = str(LOG_DIR / "V20Export")
        shutil.rmtree(export_dir, ignore_errors=True)
        os.makedirs(export_dir, exist_ok=True)

        ok, raw, text = call_tool("ExportBlocks", {
            "softwarePath": software_path, "exportPath": export_dir, "regexName": "",
        })
        check("ExportBlocks", ok, text[:100])

        ok, raw, text = call_tool("ExportBlock", {
            "softwarePath": software_path, "blockPath": "Main",
            "exportPath": export_dir, "preservePath": False,
        })
        check("ExportBlock Main", ok, text[:100])

        # Check export file exists
        main_xml = Path(export_dir) / "Main.xml"
        check("Main.xml exists", main_xml.exists(), str(main_xml))

        # ── Phase 9: V20-specific: ExportAsDocuments ──
        log("--- Phase 9: V20 ExportAsDocuments ---")
        doc_dir = str(LOG_DIR / "V20Docs")
        shutil.rmtree(doc_dir, ignore_errors=True)
        os.makedirs(doc_dir, exist_ok=True)

        ok, raw, text = call_tool("ExportBlocksAsDocuments", {
            "softwarePath": software_path, "exportPath": doc_dir, "regexName": "",
        })
        check("ExportBlocksAsDocuments", ok, text[:100])

        # ── Phase 10: V20-specific: ImportFromDocuments ──
        log("--- Phase 10: V20 ImportFromDocuments ---")
        ok, raw, text = call_tool("ImportBlocksFromDocuments", {
            "softwarePath": software_path, "groupPath": "",
            "importPath": doc_dir, "regexName": "",
        })
        check("ImportBlocksFromDocuments (existing)", ok, text[:100])

        # ── Phase 11: V20-specific: Unified HMI tools ──
        log("--- Phase 11: V20 Unified HMI ---")
        if unified_tools:
            # Probe a few Unified HMI tools (read-only first)
            hmi_tool = unified_tools[0]
            ok, raw, text = call_tool(hmi_tool)
            check(f"Unified HMI probe: {hmi_tool}", ok, text[:100])
        else:
            check("Unified HMI tools", False, "no Unified tools found")
            RESULTS["skip"] += 1

        # ── Phase 12: Save & Disconnect ──
        log("--- Phase 12: Save & Disconnect ---")
        ok, raw, text = call_tool("SaveProject")
        check("SaveProject", ok, text[:100])

        ok, raw, text = call_tool("Disconnect")
        check("Disconnect", ok, text[:100])

    except Exception as e:
        log(f"FATAL: {e}")
        RESULTS["fail"] += 1
        try:
            out, _ = proc.communicate(timeout=15)
            log(f"Server stdout tail:\n{out[-4000:]}")
        except Exception:
            pass
    finally:
        log("Terminating server...")
        proc.terminate()
        try:
            proc.wait(timeout=30)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=10)
        log(f"Server exit code: {proc.returncode}")

    # ── Summary ──
    log("=" * 60)
    log(f"RESULTS: {RESULTS['pass']} pass, {RESULTS['fail']} fail, {RESULTS['skip']} skip")
    for label, status, detail in RESULTS["tests"]:
        log(f"  [{status}] {label}  {detail}")
    log("=" * 60)
    if RESULTS["fail"] > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()