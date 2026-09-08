#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""V20 全面功能测试 v2。

修正:
  - 导入改用 V20 的 ImportFromDocuments(.s7dcl), 而非 ImportBlock(.xml)
  - 不预建工程目录 (Openness 要求由它自己创建)
  - 新增健壮性用例: 坏块路径不得杀进程
  - 验证 Bug A 修复: GetPlcTagTables 不再报 cross-thread
链路: 启动 -> initialize -> tools/list -> Connect -> CreateProject -> AddDevice
      -> 读工具 -> 写工具(导入/回读/改号/分组/导出/删除) -> 健壮性 -> 编译 -> 保存 -> Disconnect
"""
import json, os, subprocess, sys, time, urllib.request, urllib.error, shutil
from pathlib import Path

BASE_DIR = Path(r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20-test\Release\net48")
EXE = BASE_DIR / "TiaMcpServer.exe"
BASE_URL = "http://127.0.0.1:8766/"
RPC_URL = BASE_URL + "mcp"
LOG_DIR = Path(r"E:\TIA_V20_Test")
LOG_FILE = LOG_DIR / "v20_test.log"
PROJECT_DIR = LOG_DIR / "V20TestProject"
PROJECT_NAME = "V20Test"
SCL_DIR = LOG_DIR / "s7dcl"
REQUEST_ID = 0

RESULTS = {"pass": 0, "fail": 0, "skip": 0, "tests": []}
KNOWN_TOOLS = set()

S7DCL_BODY = b"""\xef\xbb\xbfFUNCTION_BLOCK "FB_Test001"
{ S7_Optimized_Access := 'TRUE' }
VERSION : 0.1
   VAR_INPUT
      Enable : Bool;
      Setpoint : Int := 100;
   END_VAR
   VAR_OUTPUT
      Done : Bool;
      Actual : Int;
   END_VAR
BEGIN
   #Done := #Enable;
   #Actual := #Setpoint;
END_FUNCTION_BLOCK
"""

SCL_BODY = b"""\xef\xbb\xbfFUNCTION_BLOCK "FB_Test002"
{ S7_Optimized_Access := 'TRUE' }
VERSION : 0.1
   VAR_INPUT
      Start : Bool;
      Target : Int := 50;
   END_VAR
   VAR_OUTPUT
      Busy : Bool;
      Pos : Int;
   END_VAR
BEGIN
   #Busy := #Start;
   #Pos := #Target;
END_FUNCTION_BLOCK
"""

# FC 块: 用于对比验证 ExportAsDocuments 是否为 FB 特有限制
FC_BODY = b"""\xef\xbb\xbfFUNCTION "FC_Test003" : Void
VERSION : 0.1
   VAR_INPUT
      In1 : Bool;
   END_VAR
   VAR_OUTPUT
      Out1 : Bool;
   END_VAR
BEGIN
   #Out1 := #In1;
END_FUNCTION
"""

# 用于后续写入/导出/删除测试的实际块名(由导入阶段决定)
TEST_BLOCK = "FB_Test001"


def log(msg):
    line = f"[{time.strftime('%H:%M:%S')}] {msg}"
    print(line, flush=True)
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except OSError:
        pass


def next_id():
    global REQUEST_ID
    REQUEST_ID += 1
    return str(REQUEST_ID)


def rpc(method, params=None, timeout=180):
    payload = {"jsonrpc": "2.0", "id": next_id(), "method": method}
    if params is not None:
        payload["params"] = params
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(RPC_URL, data=data,
                                headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"_http_error": e.code, "_body": e.read().decode("utf-8", "replace")[:300]}
    except Exception as e:
        return {"_error": str(e)[:200]}


def alive():
    """探测服务进程是否还活着(用于崩溃检测)"""
    try:
        urllib.request.urlopen(BASE_URL, timeout=3)
        return True
    except Exception:
        return False


def call_tool(name, args=None, timeout=180):
    if KNOWN_TOOLS and name not in KNOWN_TOOLS:
        RESULTS["skip"] += 1
        RESULTS["tests"].append((name, "SKIP", "工具未注册"))
        log(f"  [SKIP] {name} — 工具未注册")
        return None, ""
    raw = rpc("tools/call", {"name": name, "arguments": args or {}}, timeout)
    if "_error" in raw or "_http_error" in raw:
        return False, f"[{raw.get('_error') or raw.get('_http_error')}] {raw.get('_body','')[:200]}"
    content = raw.get("result", {}).get("content", [])
    text = "".join(c.get("text", "") for c in content if c.get("type") == "text")
    return (not raw.get("result", {}).get("isError", False)), text


def check(label, ok, detail=""):
    if ok is None:
        return
    d = (detail or "").replace("\n", " ")[:260]
    if ok:
        RESULTS["pass"] += 1
    else:
        RESULTS["fail"] += 1
    RESULTS["tests"].append((label, "PASS" if ok else "FAIL", d))
    log(f"  [{'PASS' if ok else 'FAIL'}] {label} {d[:180]}")


def get_tia_pids():
    """枚举当前所有 Siemens.Automation.Portal.exe 的 PID (wmic)。"""
    try:
        out = subprocess.run(
            ["wmic", "process", "where", "name='Siemens.Automation.Portal.exe'",
             "get", "ProcessId", "/value"],
            capture_output=True, text=True, timeout=15).stdout
        return {int(x) for x in out.split("ProcessId=") if x.strip().isdigit() for x in [x.strip()]}
    except Exception:
        return set()


def kill_only_mine(before_pids):
    """只杀测试开始后才新出现的 TIA Portal 进程 (即我拉起的 headless 实例),
    绝不触碰测试前就在跑的实例(如用户自己打开的 V20 GUI 16388)。"""
    mine = get_tia_pids() - before_pids
    for pid in sorted(mine):
        log(f"[清理] 终止测试拉起的 TIA 实例 PID={pid}")
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"],
                       capture_output=True, timeout=30)
    return mine


def main():
    # 快照: 测试开始前已在跑的 TIA Portal 进程(用户的 16388 等) — 全程保护
    BEFORE_PIDS = get_tia_pids()
    log(f"受保护的用户 TIA 实例 PID: {sorted(BEFORE_PIDS) or '无'}")
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    if LOG_FILE.exists():
        try:
            LOG_FILE.unlink()
        except OSError:
            pass
    if PROJECT_DIR.exists():
        shutil.rmtree(PROJECT_DIR, ignore_errors=True)
    if SCL_DIR.exists():
        shutil.rmtree(SCL_DIR, ignore_errors=True)
    SCL_DIR.mkdir(parents=True, exist_ok=True)
    (SCL_DIR / "FB_Test001.s7dcl").write_bytes(S7DCL_BODY)
    (SCL_DIR / "FB_Test002.scl").write_bytes(SCL_BODY)
    (SCL_DIR / "FC_Test003.scl").write_bytes(FC_BODY)

    if not EXE.exists():
        log(f"FATAL: {EXE} 不存在")
        sys.exit(1)

    env = os.environ.copy()
    env["PATH"] = (r"D:\Program Files\Siemens\Automation\Portal V20\Bin;"
                   r"C:\Program Files\Common Files\Siemens\Automation\Simatic OAM\bin;"
                   r"C:\Program Files (x86)\Common Files\Siemens\Bin;"
                   r"C:\Program Files (x86)\Common Files\Siemens\CommonArchiving;"
                   r"C:\Program Files (x86)\Common Files\Siemens\ACE\Bin;"
                   + env.get("PATH", ""))
    env["TiaPortalLocation"] = r"D:\Program Files\Siemens\Automation\Portal V20"
    env["TIA_MCP_HTTP_TIMEOUT_SECONDS"] = "420"
    # 隔离开关: 绝不 attach 到任何在跑的 TIA 实例(尤其用户自己打开的 V20 GUI),
    # 强制 Connect 走 new TiaPortal(WithoutUserInterface) 起独立 headless 实例。
    env["TIA_MCP_NO_ATTACH"] = "1"
    cmd = [str(EXE), "--tia-major-version", "20", "--transport", "http", "--http-prefix", BASE_URL]
    log(f"启动 V20 测试构建")
    srvlog = open(LOG_DIR / "v20_server_stdout.log", "w", encoding="utf-8", errors="replace")
    proc = subprocess.Popen(cmd, stdout=srvlog, stderr=subprocess.STDOUT,
                            cwd=str(BASE_DIR), env=env)
    sp = "PLC_1"
    try:
        log("--- Phase 0: 启动 ---")
        deadline = time.time() + 150
        while time.time() < deadline:
            if alive():
                break
            time.sleep(0.5)
        else:
            log("FATAL: 服务未启动")
            return
        check("Server 可达", True)

        log("--- Phase 1: initialize + 工具清单 ---")
        r = rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                               "clientInfo": {"name": "v20-test", "version": "2.0"}})
        check("initialize", "result" in r, str(r.get("result", {}).get("serverInfo"))[:100])
        r = rpc("tools/list")
        tools = r.get("result", {}).get("tools", [])
        names = sorted(t["name"] for t in tools)
        KNOWN_TOOLS.update(names)
        log(f"** V20 注册工具总数: {len(tools)} **")
        check("工具数 > 160", len(tools) > 160, f"got {len(tools)}")
        unified = [n for n in names if "Unified" in n]
        check("Unified HMI 工具(V20 专属)", len(unified) > 0, f"{len(unified)} 个")
        (LOG_DIR / "v20_tools.json").write_text(
            json.dumps(names, ensure_ascii=False, indent=1), encoding="utf-8")

        log("--- Phase 2: Connect + 建工程 + 加 PLC ---")
        ok, t = call_tool("Connect", timeout=420)
        check("Connect", ok, t[:200])
        if not ok:
            log("Connect 失败 -> 中止")
            return
        ok, t = call_tool("CreateProject", {"directoryPath": str(PROJECT_DIR),
                                            "projectName": PROJECT_NAME})
        check("CreateProject", ok, t[:200])
        added = False
        for order, ver in [("6ES7517-3AP00-0AB0", "V3.0"), ("6ES7515-2AM01-0AB0", "V2.9")]:
            ok, t = call_tool("AddDevice", {"orderNumber": order, "version": ver, "deviceName": "PLC_1"})
            check(f"AddDevice {order}", ok, t[:150])
            if ok:
                added = True
                break
        if not added:
            log("!! 无 PLC, 后续块类用例会失败")

        log("--- Phase 3: 工程 & 设备读取 ---")
        for tn, args in [("GetProjectTree", {}), ("GetProjectTopology", {}),
                         ("GetDevices", {}), ("Bootstrap", {}),
                         ("GetState", {}), ("Doctor", {}),
                         ("RunCapabilitySelfTest", {})]:
            ok, t = call_tool(tn, args)
            check(tn, ok, t[:160])

        log("--- Phase 4: 软件 & 块读取 (含 BugA 验证) ---")
        for tn, args in [("GetSoftwareTree", {"softwarePath": sp}),
                         ("GetProjectSkeleton", {"softwarePath": sp}),
                         ("GetSoftwareInfo", {"softwarePath": sp}),
                         ("GetBlocks", {"softwarePath": sp, "regexName": ""}),
                         ("GetBlocksWithHierarchy", {"softwarePath": sp}),
                         ("GetTypes", {"softwarePath": sp, "regexName": ""}),
                         ("GetPlcTagTables", {"softwarePath": sp}),
                         ("SuggestBlockNumber", {"softwarePath": sp, "blockType": "FB",
                                                 "preferredNumber": 100})]:
            ok, t = call_tool(tn, args)
            check(tn, ok, t[:160])

        log("--- Phase 5a: .scl 外部源导入 (V20 主导入链路) ---")
        global TEST_BLOCK
        ok, t = call_tool("ImportPlcExternalSource", {
            "softwarePath": sp, "groupPath": "",
            "filePath": str(SCL_DIR / "FB_Test002.scl")}, timeout=180)
        check("ImportPlcExternalSource(FB_Test002.scl)", ok, t[:260])
        if ok:
            ok, t = call_tool("GenerateBlocksFromExternalSource", {
                "softwarePath": sp, "externalSourceName": "FB_Test002"}, timeout=180)
            check("GenerateBlocksFromExternalSource(FB_Test002)", ok, t[:260])
            if ok:
                TEST_BLOCK = "FB_Test002"

        # FC 块 (对比测试 ExportAsDocuments 块类型限制)
        ok, t = call_tool("ImportPlcExternalSource", {
            "softwarePath": sp, "groupPath": "",
            "filePath": str(SCL_DIR / "FC_Test003.scl")}, timeout=180)
        check("ImportPlcExternalSource(FC_Test003.scl)", ok, t[:260])
        if ok:
            ok, t = call_tool("GenerateBlocksFromExternalSource", {
                "softwarePath": sp, "externalSourceName": "FC_Test003"}, timeout=180)
            check("GenerateBlocksFromExternalSource(FC_Test003)", ok, t[:260])

        # 关键夹具: 删除外部源, 解除块与外部源的 linked 状态,
        # 否则块处于锁定关联 (改号/导出/删除受限, Openness 报 generic error)
        ok, t = call_tool("DeletePlcExternalSource", {
            "softwarePath": sp, "externalSourceName": "FB_Test002.scl"}, timeout=120)
        check("DeletePlcExternalSource(FB_Test002) 解锁块", ok, t[:200])
        ok, t = call_tool("DeletePlcExternalSource", {
            "softwarePath": sp, "externalSourceName": "FC_Test003.scl"}, timeout=120)
        check("DeletePlcExternalSource(FC_Test003) 解锁块", ok, t[:200])
        ok, t = call_tool("GetPlcExternalSources", {"softwarePath": sp})
        check("GetPlcExternalSources(应为空)", ok, t[:160])

        log("--- Phase 5b: 导入后编译 (新块需编译才 consistent) ---")
        ok, t = call_tool("CompileAndDiagnosePlc", {"softwarePath": sp}, timeout=300)
        check("CompileAndDiagnosePlc(导入后)", ok, t[:260])

        log("--- Phase 5c: 文档导出 (SCL 块预期限制 + LAD 块链路验证) ---")
        # 已实证: 本机 V20 (2000.0.9501.1) 的 ExportAsDocuments 对 SCL 块一律报
        # mixed programming languages (外部源生成块与 XML 导入块两种来源都验证过).
        # 此处验证工具优雅处理(不杀进程); LAD 块(Main OB)验证 SD 链路本身可用性.
        DOC_DIR = LOG_DIR / "doc_export"
        DOC_DIR.mkdir(parents=True, exist_ok=True)
        for blk_type, blk_name in [("FB", TEST_BLOCK), ("FC", "FC_Test003")]:
            call_tool("ExportBlocksAsDocuments", {
                "softwarePath": sp, "exportPath": str(DOC_DIR / blk_type),
                "regexName": blk_name, "preservePath": False}, timeout=240)
            survived_sd = alive()
            log(f"  {blk_type}(SCL) 导出: 服务存活={survived_sd}")
            check(f"ExportBlocksAsDocuments({blk_type}, SCL 限制)[健壮性]", survived_sd,
                  f"SCL 块预期拒绝, 服务存活={survived_sd}")
        # LAD 块 (Main OB) — 验证 SD 链路对 LAD 块可用, 走完整导出->导入往返
        ok, t = call_tool("ExportBlocksAsDocuments", {
            "softwarePath": sp, "exportPath": str(DOC_DIR / "LAD"),
            "regexName": "^Main$", "preservePath": False}, timeout=240)
        lad_files = sorted((DOC_DIR / "LAD").rglob("*.s7dcl"))
        log(f"  Main(LAD) 导出得到 .s7dcl: {[f.name for f in lad_files]}")
        check("ExportBlocksAsDocuments(Main OB, LAD)", ok and len(lad_files) > 0,
              f"{t[:180]} | files={len(lad_files)}")
        if lad_files:
            ok, t = call_tool("ImportFromDocuments", {
                "softwarePath": sp, "groupPath": "",
                "importPath": str(lad_files[0].parent),
                "fileNameWithoutExtension": lad_files[0].stem,
                "importOption": "Override"}, timeout=240)
            check("ImportFromDocuments(LAD .s7dcl 往返)", ok, t[:240])
        # 健壮性: 手写/无效 .s7dcl 必须优雅报错, 不得杀进程
        ok, t = call_tool("ImportFromDocuments", {
            "softwarePath": sp, "groupPath": "",
            "importPath": str(SCL_DIR), "fileNameWithoutExtension": "FB_Test001",
            "importOption": "Override"}, timeout=240)
        survived_after_import = alive()
        check("  [健壮性] 无效文档导入: 报错但不杀进程", (not ok) and survived_after_import,
              f"ok={ok} 服务存活={survived_after_import}")
        if not survived_after_import:
            log("!!! 服务进程已死, 中止")
            return

        log(f"--- Phase 5d: 读回测试块 {TEST_BLOCK} ---")
        ok, t = call_tool("GetBlocks", {"softwarePath": sp, "regexName": TEST_BLOCK})
        check(f"回读 {TEST_BLOCK}", ok, t[:250])

        ok, t = call_tool("GetBlockInfo", {"softwarePath": sp, "blockPath": TEST_BLOCK})
        check("GetBlockInfo", ok, t[:250])

        ok, t = call_tool("DescribeBlockLogic", {"softwarePath": sp, "blockPath": TEST_BLOCK})
        check("DescribeBlockLogic", ok, t[:250])

        ok, t = call_tool("AnalyzeBlockImpact", {"softwarePath": sp, "blockName": TEST_BLOCK})
        check("AnalyzeBlockImpact", ok, t[:250])

        log("--- Phase 6: 健壮性 (坏路径不得杀进程) ---")
        ok, t = call_tool("DescribeBlockLogic", {"softwarePath": sp, "blockPath": "__不存在__"})
        survived = alive()
        check("坏块路径: 返回错误但不崩溃", (not ok) and survived,
              f"ok={ok} 服务存活={survived} {t[:120]}")
        if not survived:
            log("!!! 服务进程已死, 后续用例无意义, 中止")
            return

        log("--- Phase 7: 改号 / 建组 / 分组 / 导出 / 删除 ---")
        ok, t = call_tool("SetBlockNumber", {"softwarePath": sp, "blockName": TEST_BLOCK,
                                             "number": 101})
        check("SetBlockNumber -> 101", ok, t[:200])

        ok, t = call_tool("CreatePlcBlockGroup", {"softwarePath": sp, "groupPath": "_MCPTest"})
        check("CreatePlcBlockGroup(_MCPTest)", ok, t[:200])

        # 块在改号后处于 inconsistent, 移动走 export->delete->import 往返会失败,
        # 必须先编译恢复 consistent 再移动
        ok, t = call_tool("CompileAndDiagnosePlc", {"softwarePath": sp}, timeout=300)
        check("改号后编译恢复 consistent", ok, t[:200])

        ok, t = call_tool("MoveBlocksToGroup", {"softwarePath": sp, "nameRegex": TEST_BLOCK,
                                                "targetGroupPath": "_MCPTest"})
        check("MoveBlocksToGroup", ok and "failed" not in t, t[:240])

        # 移动是 export->delete->import 往返, 移动后块再次 inconsistent, 须编译后才能导出
        ok, t = call_tool("CompileAndDiagnosePlc", {"softwarePath": sp}, timeout=300)
        check("移动后编译恢复 consistent", ok, t[:200])

        # [定性记录] XML 导入块(移动往返后) SD 导出已在前轮实证仍报 mixed languages,
        # 与外部源生成块同款 -> 本机 V20 ExportAsDocuments 对 SCL 块为真实限制,
        # LAD 块链路验证已在 Phase 5c 完成, 此处不再重复.

        ok, t = call_tool("ExportBlock", {"softwarePath": sp, "blockPath": f"_MCPTest/{TEST_BLOCK}",
                                          "exportPath": str(LOG_DIR / "export")})
        check("ExportBlock", ok, t[:200])

        ok, t = call_tool("DeleteBlock", {"softwarePath": sp, "blockName": TEST_BLOCK})
        check("DeleteBlock", ok, t[:200])

        log("--- Phase 8: 编译 + 保存 ---")
        ok, t = call_tool("CompileAndDiagnosePlc", {"softwarePath": sp}, timeout=300)
        check("CompileAndDiagnosePlc", ok, t[:300])

        ok, t = call_tool("SaveProject", {}, timeout=180)
        check("SaveProject", ok, t[:150])

        ok, t = call_tool("Disconnect", {}, timeout=120)
        check("Disconnect", ok, t[:150])

    finally:
        # 先优雅关闭我起的 headless 实例 (Disconnect 内部已 _portal.Dispose),
        # 再兜底: 只杀 AFTER - BEFORE 之差里的 TIA 进程(绝不动用户实例)。
        try:
            kill_only_mine(BEFORE_PIDS)
        except Exception as e:
            log(f"[清理] 兜底清理异常(忽略): {e}")
        try:
            proc.terminate()
            proc.wait(timeout=20)
        except Exception:
            try:
                proc.kill()
            except Exception:
                pass
        try:
            srvlog.flush(); srvlog.close()
        except Exception:
            pass

    log("=" * 62)
    log(f"总计 PASS={RESULTS['pass']}  FAIL={RESULTS['fail']}  SKIP={RESULTS['skip']}")
    if RESULTS["fail"]:
        log("失败明细:")
        for name, st, d in RESULTS["tests"]:
            if st == "FAIL":
                log(f"  - {name}: {d[:260]}")


if __name__ == "__main__":
    main()
