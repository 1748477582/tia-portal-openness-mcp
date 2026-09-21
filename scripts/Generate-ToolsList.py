"""Regenerate manifest/tools-list.json from a built MCP engine's real tools/list response.

Ported from the upstream project (scripts/Generate-ToolsList.py) and adapted to this fork:

  * This fork selects the tool roster with the TIA_MCP_PROFILE environment variable
    (full | lite). There is no --profile CLI flag, so we force TIA_MCP_PROFILE=full
    instead of passing --profile full.
  * TIAMCP_NO_REDIRECT=1 is set for the child so the engine's version self-router
    (Siemens/EngineRouter.cs) never re-execs a sibling exe mid-capture.
  * Non-JSON lines on stdout are skipped instead of aborting (startup banners etc.).

SAFETY: tools/list is pure metadata. It never issues Connect, so no TIA Portal instance
is attached to or touched. Never point this at a project-mutating flow.

Usage:
  python scripts/Generate-ToolsList.py [exe-path] [out-path]
"""

import datetime
import json
import os
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else (
    ROOT / "runtime" / "v21" / "TiaMcpServer.exe"
)
OUT = pathlib.Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else ROOT / "manifest" / "tools-list.json"

if not EXE.exists():
    raise SystemExit(f"engine exe not found: {EXE}")

env = dict(os.environ)
env["TIA_MCP_PROFILE"] = "full"        # this fork: env-selected roster (default is full, be explicit)
env["TIAMCP_NO_REDIRECT"] = "1"        # never re-exec a sibling exe mid-capture

process = subprocess.Popen(
    [str(EXE), "--logging", "0"],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    text=True, encoding="utf-8", errors="replace", bufsize=1, env=env,
)
seq = 0


def request(method, params=None):
    global seq
    seq += 1
    message = {"jsonrpc": "2.0", "id": seq, "method": method}
    if params is not None:
        message["params"] = params
    process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
    process.stdin.flush()
    while True:
        line = process.stdout.readline()
        if not line:
            raise RuntimeError("engine exited: " + process.stderr.read())
        line = line.strip()
        if not line:
            continue
        try:
            response = json.loads(line)
        except json.JSONDecodeError:
            continue                                  # skip startup banner / non-protocol noise
        if response.get("id") == seq:
            if "error" in response:
                raise RuntimeError(str(response["error"]))
            return response["result"]


try:
    request("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                           "clientInfo": {"name": "tools-list-generator", "version": "1"}})
    process.stdin.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}) + "\n")
    process.stdin.flush()
    result = request("tools/list")
    rows = []
    for tool in result.get("tools", []):
        description = tool.get("description", "")
        match = re.match(r"^\[(L\d+)\]\[(?:Category:)?([^\]]+)\]", description)
        schema = tool.get("inputSchema", {}) or {}
        rows.append({
            "name": tool.get("name", ""),
            "layer": match.group(1) if match else "L?",
            "domain": match.group(2) if match else "Misc",
            "method": tool.get("name", ""),
            "returnType": "",
            "parameters": list((schema.get("properties", {}) or {}).keys()),
            "description": description,
        })
    rows.sort(key=lambda item: item["name"].lower())
    document = {
        "package": "TIA_MCP_Delivery_v2.3.0",
        "generatedAt": datetime.datetime.now(
            datetime.timezone(datetime.timedelta(hours=8))).isoformat(),
        "source": f"live MCP tools/list of {EXE.name} (dev build)",
        "toolCount": len(rows),
        "note": "Full roster. Regenerate with scripts/Generate-ToolsList.py after any tool "
                "add/remove. Runtime tools/list remains authoritative when the server is running.",
        "tools": rows,
    }
    OUT.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(rows)} tools to {OUT}")

    # Keep package-manifest.json's roster fields in sync (count + layer breakdown).
    # Without this the two manifests drift — which is exactly what happened before.
    pm_path = ROOT / "manifest" / "package-manifest.json"
    if pm_path.exists():
        pm = json.loads(pm_path.read_text(encoding="utf-8-sig"))
        layers = {}
        for r in rows:
            layers[r["layer"]] = layers.get(r["layer"], 0) + 1
        caps = pm.setdefault("capabilities", {})
        caps["mcpToolCount"] = len(rows)
        # Record EVERY observed layer so the breakdown always sums to mcpToolCount.
        # ('L?' shows up when a [Description] is missing the "[Lx][Domain]" prefix — a source
        #  defect the generator must surface, not hide.)
        caps["mcpToolLayers"] = {k: layers[k] for k in sorted(layers)}
        pm["refreshedAt"] = document["generatedAt"]
        pm_path.write_text(json.dumps(pm, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"Synced package-manifest.json: mcpToolCount={len(rows)}, layers={caps['mcpToolLayers']}")
finally:
    process.terminate()
    try:
        process.wait(5)
    except subprocess.TimeoutExpired:
        process.kill()
