# -*- coding: utf-8 -*-
"""tia_router.py — 单连接器 + 按工程版本自动路由到对应引擎的 MCP 前置进程。

为什么需要它（硬约束，不是设计偏好）
    一个 TIA 版本的 Siemens.Engineering 程序集只能加载进一个进程，引擎进程启动即绑定版本，
    运行中无法切换。因此"一条连接器 + 按 .apNN 自动选版本"只能由一个前置进程实现：
    对外是一个 MCP 服务端（stdio），对内为每个版本各起一个 TiaMcpServer 子引擎，按请求转发。

对外行为
    initialize   → 转发给"默认版本"子引擎，并记录；同时拉起另一个版本用于枚举工具清单
    tools/list   → 返回两个子引擎工具清单的并集（同名以当前活动子引擎为准）
    tools/call   → name == OpenProject 时按工程扩展名选择子引擎（必要时切换），其余转发给活动子引擎
    切换时        → 先让旧子引擎 Disconnect（避免两个 TIA 实例并存），再启动/激活新子引擎，
                    并向客户端发 notifications/tools/list_changed

回滚
    本文件不修改任何配置。只有当它被验证通过、并且用户明确同意后，
    才把 mcp.json 里 tia-portal 的 command 改成调用本脚本。
    改回原样 = 把 command 换回 tia_mcp_launcher.bat，无需其他动作。

用法（被 MCP 宿主调用，stdin/stdout 走 JSON-RPC）
    python tia_router.py [--default 20]

诊断（人工执行）
    python tia_router.py --probe        # 打印探测到的版本与将要使用的引擎路径，不进入转发循环
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import threading
import time
import queue
import argparse

HERE = os.path.dirname(os.path.abspath(__file__))
LAUNCHER_DIR = HERE                      # detect_tia_version.ps1 与 bin-vXX 都在这一层

# Windows 上 Python 默认用系统代码页（中文机 = GBK）写 stdout/stderr。
# 工具描述里存在 '↔' 之类的字符，GBK 编不出来 ⇒ 一次 tools/list 就把整个连接器打崩。
# 这里强制 UTF-8；配合下面 json.dumps(ensure_ascii=True) 形成双保险。
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

TIA_COMMON_PATHS = [
    r"C:\Program Files\Common Files\Siemens\Automation\Simatic OAM\bin",
    r"C:\Program Files (x86)\Common Files\Siemens\Bin",
    r"C:\Program Files (x86)\Common Files\Siemens\CommonArchiving",
    r"C:\Program Files (x86)\Common Files\Siemens\ACE\Bin",
]


def log(msg: str) -> None:
    """诊断输出一律走 stderr，绝不污染 stdout 的 JSON-RPC。"""
    try:
        sys.stderr.write("[router] " + msg + "\n")
        sys.stderr.flush()
    except Exception:
        pass


# --------------------------------------------------------------------------- discovery
def discover() -> dict:
    """调用仓库里的探测脚本，拿到"已安装且本工具支持"的版本与安装根。"""
    ps1 = os.path.join(LAUNCHER_DIR, "detect_tia_version.ps1")
    if not os.path.isfile(ps1):
        raise SystemExit("detect_tia_version.ps1 not found: %s" % ps1)
    out = subprocess.run(
        ["powershell", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ps1, "-List"],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
    ).stdout
    if not out.strip():
        raise SystemExit("detect_tia_version.ps1 -List returned nothing")
    info = json.loads(out)
    info["by_version"] = {int(s["version"]): s["root"] for s in (info.get("supported") or [])}
    return info


def engine_path(version: int) -> str:
    """与 launcher 相同的两种布局：bin-vXX（开发树）/ runtime\\vXX（发行包）。"""
    a = os.path.join(LAUNCHER_DIR, "src", "TiaMcpServer", "bin-v%d" % version, "Release", "net48", "TiaMcpServer.exe")
    if os.path.isfile(a):
        return a
    b = os.path.abspath(os.path.join(LAUNCHER_DIR, "..", "..", "runtime", "v%d" % version, "TiaMcpServer.exe"))
    if os.path.isfile(b):
        return b
    raise SystemExit("no engine build for V%d (looked in bin-v%d and runtime\\v%d)" % (version, version, version))


def project_version(path: str) -> int | None:
    m = re.search(r"\.a(?:p|ls)(\d{2})$", os.path.splitext(path or "")[1], re.IGNORECASE)
    return int(m.group(1)) if m else None


def reap_orphan_children() -> None:
    """回收上一次路由器被强杀后遗留的子引擎。

    判据只有一条：**父进程已经不存在**的 TiaMcpServer 进程。
    父进程还活着的（宿主的连接器引擎、别人手动起的）一律不碰。
    """
    try:
        ps = (
            "$out=@();"
            "Get-CimInstance Win32_Process -Filter \"Name='TiaMcpServer.exe'\" | ForEach-Object {"
            "  $pp = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $_.ParentProcessId) -ErrorAction SilentlyContinue;"
            "  if (-not $pp) { $out += $_.ProcessId }"
            "};"
            "$out -join ','"
        )
        r = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                           capture_output=True, text=True, timeout=60)
        ids = [x for x in (r.stdout or "").strip().split(",") if x.strip().isdigit()]
        for pid in ids:
            log("reaping orphaned engine pid=%s (its parent is gone)" % pid)
            subprocess.run(["powershell", "-NoProfile", "-Command",
                            "Stop-Process -Id %s -Force -Confirm:$false -ErrorAction SilentlyContinue" % pid],
                           capture_output=True, timeout=30)
    except Exception as ex:
        log("orphan reap skipped: %s" % ex)


# --------------------------------------------------------------------------- child engine
class SwitchAborted(Exception):
    """切版本被中止（例如旧引擎里的工程保存失败）。不吞掉，交给客户端看见。"""
    pass


class Child:
    """一个版本一个子引擎进程，带独立的 stdout 读取线程。"""

    def __init__(self, version: int, root: str):
        self.version = version
        self.root = root
        self.proc: subprocess.Popen | None = None
        self.q: "queue.Queue[dict]" = queue.Queue()
        self.seq = 0
        self.tools: list = []
        self.init_result: dict | None = None
        self._alive = False

    # -- lifecycle
    def start(self) -> None:
        exe = engine_path(self.version)
        env = dict(os.environ)
        env["PATH"] = ";".join([os.path.join(self.root, "Bin")] + TIA_COMMON_PATHS + [env.get("PATH", "")])
        env["TIAMCP_NO_REDIRECT"] = "1"          # 不允许它再重定向到别的版本 exe
        env["TIA_MCP_VERSION"] = str(self.version)
        env["TIA_MCP_PORTAL_LOCATION"] = self.root
        # 本机 Attach() 会挂死（两次独立复现）。子引擎必须快速放弃挂接，否则每次首次调用
        # 都要白等一整个超时窗口。
        env.setdefault("TIA_MCP_ATTACH_TIMEOUT_MS", "3000")
        env.setdefault("TIA_MCP_PROFILE", "full")
        log("starting V%d: %s" % (self.version, exe))
        self.proc = subprocess.Popen(
            [exe, "--logging", "0", "--tia-major-version", str(self.version),
             "--tia-portal-location", self.root],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8", errors="replace", bufsize=1, env=env,
        )
        self._alive = True
        threading.Thread(target=self._reader, daemon=True, name="child-%d-stdout" % self.version).start()
        threading.Thread(target=self._err_reader, daemon=True, name="child-%d-stderr" % self.version).start()

    def _reader(self) -> None:
        try:
            for line in self.proc.stdout:
                line = (line or "").strip()
                if not line:
                    continue
                try:
                    self.q.put(json.loads(line))
                except json.JSONDecodeError:
                    log("child V%d: non-JSON on stdout: %s" % (self.version, line[:120]))
        finally:
            self._alive = False
            self.q.put({"_closed": True})

    def _err_reader(self) -> None:
        # 子引擎的 stderr 只在显式开启时才转播：MCP 宿主的 stderr 管道通常很小，
        # 一味刷屏会把管道写满并让整个路由器阻塞（实测踩过）。
        verbose = os.environ.get("TIA_ROUTER_VERBOSE", "") not in ("", "0", "false", "False")
        try:
            for line in self.proc.stderr:
                if not verbose:
                    continue
                line = (line or "").rstrip()
                if line:
                    log("child V%d stderr: %s" % (self.version, line[:200]))
        except Exception:
            pass

    @property
    def alive(self) -> bool:
        return self._alive and self.proc is not None and self.proc.poll() is None

    # -- io
    def send(self, msg: dict) -> None:
        if not self.alive:
            raise RuntimeError("child V%d is not running" % self.version)
        # ensure_ascii=True: 纯 ASCII 写给子引擎，避免路径里的中文在 GBK 代码页下编不出来
        self.proc.stdin.write(json.dumps(msg, ensure_ascii=True) + "\n")
        self.proc.stdin.flush()

    def request(self, method: str, params=None, timeout=180.0) -> dict:
        self.seq += 1
        mid = self.seq
        msg = {"jsonrpc": "2.0", "method": method, "id": mid}
        if params is not None:
            msg["params"] = params
        self.send(msg)
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                item = self.q.get(timeout=max(0.05, deadline - time.time()))
            except queue.Empty:
                break
            if item.get("_closed"):
                raise RuntimeError("child V%d closed stdout" % self.version)
            if item.get("id") == mid:
                return item
            emit_to_client(item)          # 子引擎的进度通知照原样转给客户端
        raise TimeoutError("child V%d did not answer %s within %.0fs" % (self.version, method, timeout))

    def notify(self, method: str, params=None) -> None:
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        self.send(msg)

    @staticmethod
    def _payload(reply: dict) -> dict:
        """把工具的 content[0].text 解析成 dict；解析不了就返回空 dict。"""
        try:
            res = reply.get("result") or {}
            for item in res.get("content") or []:
                if item.get("type") == "text":
                    return json.loads(item.get("text") or "{}")
        except Exception:
            pass
        return {}

    def bound_project(self) -> str:
        try:
            return str(self._payload(self.request("tools/call", {"name": "GetState", "arguments": {}}, timeout=60))
                       .get("project") or "")
        except Exception:
            return ""

    def stop(self, graceful: bool = True) -> None:
        """关闭子引擎。返回 False = 工程没能安全保存，调用方必须中止切换。

        安全规则（用户要求"绝不丢改动"）：
          先 CloseProject(saveBeforeClose=True) 存盘关闭；保存失败就中止切换，不硬关。
          只有工程确认已关闭后才 Disconnect / 结束进程。
        """
        if self.proc is None:
            return True
        if graceful and self.alive:
            proj = self.bound_project()
            if proj and proj not in ("-", ""):
                log("child V%d has project '%s' open; saving before switch" % (self.version, proj))
                try:
                    r = self.request("tools/call",
                                     {"name": "CloseProject", "arguments": {"saveBeforeClose": True}},
                                     timeout=180)
                except Exception as ex:
                    log("child V%d CloseProject raised: %s" % (self.version, ex))
                    return False
                if r.get("error") or (r.get("result") or {}).get("isError"):
                    detail = json.dumps(r, ensure_ascii=True)[:300]
                    log("child V%d CloseProject failed: %s" % (self.version, detail))
                    return False
                log("child V%d project closed and saved" % self.version)
            try:
                self.request("tools/call", {"name": "Disconnect", "arguments": {"saveBeforeClose": True}},
                             timeout=90)
            except Exception as ex:
                log("child V%d Disconnect raised: %s" % (self.version, ex))
        try:
            if self.alive:
                self.proc.terminate()
        except Exception:
            pass
        try:
            self.proc.wait(timeout=15)
        except Exception:
            try:
                self.proc.kill()
            except Exception:
                pass
        self._alive = False
        return True

    # -- introspection
    def initialize(self, client_params: dict) -> dict:
        """MCP 只允许一个子引擎被 initialize 一次；结果缓存起来供客户端回放。"""
        if self.init_result is not None:
            return self.init_result
        self.init_result = self.request("initialize", client_params)
        try:
            self.notify("notifications/initialized", {})
        except Exception:
            pass
        return self.init_result

    def load_tools(self) -> list:
        res = self.request("tools/list", {})
        self.tools = (res.get("result") or {}).get("tools") or []
        log("child V%d exposes %d tools" % (self.version, len(self.tools)))
        return self.tools


# --------------------------------------------------------------------------- client io
_client_out_lock = threading.Lock()


def emit_to_client(msg: dict) -> None:
    # ensure_ascii=True：输出纯 ASCII（中文/箭头都变成 \uXXXX），即使流的编码又被环境改回 GBK，
    # 也不会因为某个字符编不出来而让整个连接器崩掉。JSON 语义不变。
    with _client_out_lock:
        sys.stdout.write(json.dumps(msg, ensure_ascii=True) + "\n")
        sys.stdout.flush()


def read_client_line() -> str:
    return sys.stdin.readline()


# --------------------------------------------------------------------------- router
class Router:
    def __init__(self, default_version: int):
        self.info = discover()
        self.available = self.info["by_version"]
        if not self.available:
            raise SystemExit("no usable TIA Portal install found (detect says: %s)" % self.info.get("status"))
        if default_version not in self.available:
            fallback = max(self.available)
            log("requested default V%d not usable; falling back to V%d" % (default_version, fallback))
            default_version = fallback
        self.default_version = default_version
        self.active_version = default_version
        self.children: dict[int, Child] = {}
        self.union_tools: dict[str, dict] = {}
        self.client_init_params: dict | None = None

    # -- helpers
    def child(self, version: int) -> Child:
        if version not in self.children:
            c = Child(version, self.available[version])
            c.start()
            self.children[version] = c
        return self.children[version]

    def active(self) -> Child:
        return self.child(self.active_version)

    def rebuild_union(self) -> None:
        union: dict[str, dict] = {}
        for c in self.children.values():
            for t in c.tools:
                if t["name"] not in union:
                    union[t["name"]] = t
        # 活动子引擎的 schema 优先
        for t in self.active().tools:
            union[t["name"]] = t
        self.union_tools = union
        log("union tools: %d" % len(self.union_tools))

    def switch_to(self, version: int) -> None:
        if version == self.active_version:
            return
        old = self.active_version
        log("switching engine V%d -> V%d" % (old, version))
        if old in self.children:
            log("  stop old V%d ..." % old)
            old_proj = self.children[old].bound_project()
            if self.children[old].stop(graceful=True) is False:
                # 旧引擎里的工程没能存盘关闭 ⇒ 绝不强切，把选择权交回用户
                raise SwitchAborted(
                    "已中止切换：V%d 引擎里打开着工程 '%s'，但它没能被保存关闭。"
                    " 现在强行切到 V%d 会丢掉未保存的修改。请在 TIA 里手动保存（或关闭该工程）后重试；"
                    " 本工具不会替你丢修改。"
                    % (old, old_proj or "(未知)", version))
            self.children.pop(old, None)
            log("  old V%d stopped" % old)
        log("  start new V%d ..." % version)
        c = self.child(version)
        if not c.tools:
            log("  initialize new V%d ..." % version)
            c.initialize(self.client_init_params or {
                "protocolVersion": "2024-11-05", "capabilities": {},
                "clientInfo": {"name": "tia-router", "version": "1"}})
            c.load_tools()
        self.active_version = version
        self.rebuild_union()
        emit_to_client({"jsonrpc": "2.0", "method": "notifications/tools/list_changed", "params": {}})
        log("  switch to V%d done" % version)

    # -- main loop
    def run(self) -> None:
        # 预热两个版本，才能给出并集清单；不调用 Connect，因此不会启动 TIA
        for v in sorted(self.available, reverse=True):
            try:
                c = self.child(v)
                c.initialize({"protocolVersion": "2024-11-05", "capabilities": {},
                              "clientInfo": {"name": "tia-router", "version": "1"}})
                c.load_tools()
            except Exception as ex:
                log("warm-up V%d failed: %s" % (v, ex))
                self.children.pop(v, None)
        if not self.children:
            raise SystemExit("no child engine could be started")
        if self.active_version not in self.children:
            self.active_version = max(self.children)
        self.rebuild_union()

        while True:
            line = read_client_line()
            if not line:
                break
            line = line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                emit_to_client({"jsonrpc": "2.0", "id": None,
                                "error": {"code": -32700, "message": "router: invalid JSON"}})
                continue
            try:
                self.dispatch(msg)
            except Exception as ex:
                log("dispatch error: %s" % ex)
                if msg.get("id") is not None:
                    emit_to_client({"jsonrpc": "2.0", "id": msg.get("id"),
                                    "error": {"code": -32603, "message": "router: %s" % ex}})
        self.shutdown()

    def dispatch(self, msg: dict) -> None:
        method = msg.get("method")
        mid = msg.get("id")
        params = msg.get("params") or {}

        if method == "initialize":
            self.client_init_params = params
            # 子引擎在 warm-up 阶段已经 initialize 过；重复发送会把它打崩。
            # 这里回放缓存的握手结果，客户端看到的就是一台正常的 MCP 服务端。
            res = self.active().initialize(params)
            payload = dict(res)
            payload["id"] = mid
            emit_to_client(payload)
            return

        if method == "notifications/initialized":
            return                              # 各子引擎在 warm-up 时已各自初始化

        if method == "tools/list":
            emit_to_client({"jsonrpc": "2.0", "id": mid,
                            "result": {"tools": list(self.union_tools.values())}})
            return

        if method == "ping":
            emit_to_client({"jsonrpc": "2.0", "id": mid, "result": {}})
            return

        if method in ("notifications/cancelled",):
            return

        if method != "tools/call":
            # prompts/resources 等一律转发给活动子引擎
            reply = self.active().request(method, params)
            reply["id"] = mid          # 子引擎的 id 是路由器自己的序号，必须换回客户端 id
            emit_to_client(reply)
            return

        name = params.get("name")
        args = params.get("arguments") or {}

        if name == "OpenProject":
            want = project_version(args.get("path", ""))
            if want and want in self.available and want != self.active_version:
                log("OpenProject needs V%d (active V%d) -> switching" % (want, self.active_version))
                self.switch_to(want)

        if name not in self.union_tools:
            emit_to_client({"jsonrpc": "2.0", "id": mid,
                            "error": {"code": -32601, "message": "router: unknown tool '%s'" % name}})
            return

        # 工具只存在于非活动版本 → 明确报错，不擅自切（切换会中断用户当前工程）
        if name not in {t["name"] for t in self.active().tools}:
            owners = [c.version for c in self.children.values() if name in {t["name"] for t in c.tools}]
            emit_to_client({"jsonrpc": "2.0", "id": mid,
                            "error": {"code": -32601,
                                      "message": "router: tool '%s' is only available on V%s (active V%d). "
                                                 "Switch by opening a project of that version first."
                                                 % (name, "/V".join(map(str, owners)), self.active_version)}})
            return

        log("forward %s -> V%d ..." % (name, self.active_version))
        reply = self.active().request("tools/call", params)
        reply["id"] = mid              # CRITICAL: 子引擎回复里的 id 是路由器内部序号，
                                       # 不回写客户端 id 的话客户端会一直等（实测踩过）
        log("forward %s <- V%d ok=%s" % (name, self.active_version, not (reply.get("error") or (reply.get("result") or {}).get("isError"))))
        emit_to_client(reply)

    def shutdown(self) -> None:
        for c in list(self.children.values()):
            c.stop(graceful=True)
        self.children.clear()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--default", type=int, default=0, help="default TIA major version (0 = highest available)")
    ap.add_argument("--probe", action="store_true", help="print discovery result and exit")
    a = ap.parse_args()

    info = discover()
    log("detect status=%s supported=%s" % (info.get("status"), info["by_version"]))
    reap_orphan_children()
    if a.probe:
        for v, root in sorted(info["by_version"].items(), reverse=True):
            print("V%-3d %s" % (v, root))
            try:
                print("      engine: %s" % engine_path(v))
            except SystemExit as ex:
                print("      engine: MISSING (%s)" % ex)
        return 0

    default_version = a.default or (max(info["by_version"]) if info["by_version"] else 0)
    Router(default_version).run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
