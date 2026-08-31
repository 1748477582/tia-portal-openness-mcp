#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
V18 端到端动态冒烟测试驱动 (stdio MCP 协议)
启动 bin-v18/TiaMcpServer.exe, 完成 MCP handshake, 顺序调用工具并收集结构化结果。
链路:
  1. initialize + initialized            (握手)
  2. Bootstrap                          (只读环境自检)
  3. Connect                            (拉起 TIA Portal V18 无头实例)
  4. CreateProject                      (建空工程)
  5. GetProjectSkeleton / GetProjectTree(验证读取)
  6. AddDevice (S7-1500)                (加 PLC, 才能编译)
  7. CompileAndDiagnosePlc              (验证编译诊断链路)
  8. SaveProject                        (验证持久化)
  9. ScaffoldProject (dryRun=false)     (验证高层"建含PLC工程+编译+保存"封装)
 10. Disconnect
"""
import subprocess, json, sys, os, time, threading

EXE = r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v18\Release\net48\TiaMcpServer.exe"
SMOKE_DIR = r"E:\TIA_SmokeTest"
PROJECT_NAME = "SmokeV18"

results = {}      # name -> dict
proc = None
_msg_id = 0
_lock = threading.Lock()

def log(msg):
    print(f"[smoke] {msg}", flush=True)

def send(obj):
    global _msg_id
    data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
    frame = b"Content-Length: %d\r\n\r\n%s" % (len(data), data)
    proc.stdin.write(frame)
    proc.stdin.flush()

def recv_msg(timeout=120):
    """Read one MCP frame from stdout."""
    proc.stdout.readline  # ensure
    # read headers
    headers = {}
    while True:
        line = proc.stdout.readline()
        if not line:
            return None
        line = line.decode("utf-8", errors="replace").rstrip("\r\n")
        if line == "":
            break
        if ":" in line:
            k, v = line.split(":", 1)
            headers[k.strip().lower()] = v.strip()
    length = int(headers.get("content-length", "0"))
    body = b""
    while len(body) < length:
        chunk = proc.stdout.read(length - len(body))
        if not chunk:
            return None
        body += chunk
    return json.loads(body.decode("utf-8", errors="replace"))

def call_tool(name, args=None, timeout=180):
    global _msg_id
    with _lock:
        _msg_id += 1
        rid = _msg_id
    req = {
        "jsonrpc": "2.0",
        "id": rid,
        "method": "tools/call",
        "params": {"name": name, "arguments": args or {}}
    }
    send(req)
    # wait for matching response (skip notifications)
    deadline = time.time() + timeout
    while time.time() < deadline:
        msg = recv_msg(timeout=int(deadline - time.time()) + 5)
        if msg is None:
            return {"_error": "no response (timeout or process died)"}
        if msg.get("id") == rid:
            return msg
        # else: notification or out-of-order, keep waiting
    return {"_error": "timeout waiting for response"}

def run():
    global proc
    log("启动 MCP server (stdio)...")
    proc = subprocess.Popen(
        [EXE],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        bufsize=0, cwd=os.path.dirname(EXE)
    )
    reader = threading.Thread(target=drain_stderr, daemon=True)
    reader.start()

    # 1. initialize
    global _msg_id
    _msg_id += 1
    send({
        "jsonrpc": "2.0", "id": _msg_id, "method": "initialize",
        "params": {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "smoke-test", "version": "1.0"}
        }
    })
    init = recv_msg(timeout=60)
    results["initialize"] = {"ok": bool(init and init.get("result")), "serverInfo": (init or {}).get("result", {}).get("serverInfo")}
    log(f"initialize: {results['initialize']['ok']}")

    # initialized notification
    send({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}})
    time.sleep(1)

    # 2. Bootstrap
    log("调用 Bootstrap (环境自检)...")
    r = call_tool("Bootstrap", timeout=60)
    bs = extract_content(r)
    results["Bootstrap"] = bs
    log(f"Bootstrap: {short(bs)}")

    # 3. Connect (拉起 TIA V18; 首次可能弹授权框)
    log("调用 Connect (拉起 TIA Portal V18)...")
    t0 = time.time()
    r = call_tool("Connect", {}, timeout=180)
    dt = time.time() - t0
    c = extract_content(r)
    results["Connect"] = {"elapsed_s": round(dt, 1), **c}
    log(f"Connect: {short(c)} (耗时 {dt:.1f}s)")

    # 4. CreateProject
    os.makedirs(SMOKE_DIR, exist_ok=True)
    log("调用 CreateProject...")
    r = call_tool("CreateProject", {"directoryPath": SMOKE_DIR, "projectName": PROJECT_NAME}, timeout=180)
    cp = extract_content(r)
    results["CreateProject"] = cp
    log(f"CreateProject: {short(cp)}")

    # 5. GetProjectSkeleton + GetProjectTree
    log("调用 GetProjectSkeleton...")
    r = call_tool("GetProjectSkeleton", {"softwarePath": "PLC_1"}, timeout=120)
    sk = extract_content(r)
    results["GetProjectSkeleton"] = sk
    log(f"GetProjectSkeleton: {short(sk)}")

    log("调用 GetProjectTree...")
    r = call_tool("GetProjectTree", {}, timeout=120)
    tr = extract_content(r)
    results["GetProjectTree"] = tr
    log(f"GetProjectTree: {short(tr)}")

    # 6. AddDevice (S7-1500 CPU)
    log("调用 AddDevice (S7-1500)...")
    r = call_tool("AddDevice", {"orderNumber": "6ES7517-3AP00-0AB0", "version": "V3.0", "deviceName": "PLC_1"}, timeout=180)
    ad = extract_content(r)
    results["AddDevice"] = ad
    log(f"AddDevice: {short(ad)}")

    # 7. CompileAndDiagnosePlc
    log("调用 CompileAndDiagnosePlc (PLC_1)...")
    r = call_tool("CompileAndDiagnosePlc", {"softwarePath": "PLC_1"}, timeout=240)
    cd = extract_content(r)
    results["CompileAndDiagnosePlc"] = cd
    log(f"CompileAndDiagnosePlc: {short(cd)}")

    # 8. SaveProject
    log("调用 SaveProject...")
    r = call_tool("SaveProject", {}, timeout=180)
    sp = extract_content(r)
    results["SaveProject"] = sp
    log(f"SaveProject: {short(sp)}")

    # 9. ScaffoldProject (dryRun=false, 含 PLC + 编译 + 保存)
    log("调用 ScaffoldProject (dryRun=false)...")
    spec = {
        "projectName": "ScaffoldV18",
        "directoryPath": SMOKE_DIR,
        "plcName": "PLC_1",
        "plcFamily": "S7-1500",
        "compile": True,
        "save": True,
        "dryRun": False
    }
    r = call_tool("ScaffoldProject", spec, timeout=300)
    sc = extract_content(r)
    results["ScaffoldProject"] = sc
    log(f"ScaffoldProject: {short(sc)}")

    # 10. Disconnect
    log("调用 Disconnect...")
    r = call_tool("Disconnect", {}, timeout=60)
    results["Disconnect"] = extract_content(r)

    # dump results
    out = os.path.join(SMOKE_DIR, "smoke_result.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2, default=str)
    log(f"结果已写入: {out}")

    try:
        proc.terminate()
    except Exception:
        pass

def drain_stderr():
    try:
        for line in proc.stderr:
            sys.stderr.write("[tia-stderr] " + line.decode("utf-8", errors="replace"))
    except Exception:
        pass

def extract_content(resp):
    if not isinstance(resp, dict):
        return {"_raw": resp}
    if "_error" in resp:
        return resp
    if "error" in resp:
        return {"error": resp["error"]}
    res = resp.get("result", {})
    content = res.get("content", [])
    texts = []
    for c in content:
        if isinstance(c, dict) and c.get("type") == "text":
            texts.append(c.get("text", ""))
    is_error = res.get("isError", False)
    merged = "\n".join(texts)
    try:
        parsed = json.loads(merged) if merged.strip().startswith("{") else merged
    except Exception:
        parsed = merged
    return {"isError": is_error, "text": parsed}

def short(obj, n=300):
    s = json.dumps(obj, ensure_ascii=False, default=str) if not isinstance(obj, str) else obj
    return s if len(s) <= n else s[:n] + "..."

if __name__ == "__main__":
    try:
        run()
    except Exception as e:
        log(f"FATAL: {e}")
        import traceback; traceback.print_exc()
        if proc:
            try: proc.kill()
            except: pass
        sys.exit(1)
