"""Manifest 一致性闸：两份 manifest 必须彼此自洽。

修的是本仓正在发生的真实问题（2026-09-21）：`tools-list.json` 写 200、`package-manifest.json`
写 201、源码实有 211 —— 三个数互相打架，谁也没挡住。这类"看起来对其实是错的"数据，
只有对拍能挡住。

检查项：
  T1 tools-list.toolCount      == len(tools)
  T2 tools-list 工具名唯一（无重名）
  T3 每条都有 name / layer / domain / 非空 description
  P1 package-manifest.capabilities.mcpToolCount == len(tools)
  P2 sum(mcpToolLayers) == len(tools)
  P3 mcpToolLayers == 从 tools-list 实际统计出的分层计数（不是各说各话）
  P4 package-manifest.entrypoints 里的路径存在（只查仓库内相对路径）

用法（仓库根目录）：
    python scripts/Check-ManifestConsistency.py             # 0=干净 1=不一致
    python scripts/Check-ManifestConsistency.py --selftest  # 故障注入：必须能报红
"""
import collections
import io
import json
import os
import sys

# Windows 上的 Python 默认按 cp1252 输出（GitHub Actions 的 windows runner 就是），
# 打印中文会直接 UnicodeEncodeError 崩掉 —— 闸门"跑不起来"和"发现问题"长得一样，
# 所以显式切到 UTF-8，让它在任何宿主上都能输出。
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TOOLS_LIST = "manifest/tools-list.json"
PKG_MANIFEST = "manifest/package-manifest.json"


def load_json(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def check(tl, pm, root="."):
    fails = []
    tools = tl.get("tools", [])

    # T1
    if tl.get("toolCount") != len(tools):
        fails.append("T1 tools-list.toolCount=%r 但实际有 %d 条" % (tl.get("toolCount"), len(tools)))

    # T2
    names = [t.get("name", "") for t in tools]
    dups = [n for n, c in collections.Counter(names).items() if c > 1]
    if dups:
        fails.append("T2 tools-list 有重名工具：%s" % dups)

    # T3
    for t in tools:
        for key in ("name", "layer", "domain", "description"):
            if not str(t.get(key, "")).strip():
                fails.append("T3 工具 %r 的 %s 为空" % (t.get("name"), key))

    caps = (pm.get("capabilities") or {})

    # P1
    if caps.get("mcpToolCount") != len(tools):
        fails.append("P1 package-manifest.mcpToolCount=%r 但 tools-list 有 %d 条"
                     % (caps.get("mcpToolCount"), len(tools)))

    # P2
    layers = caps.get("mcpToolLayers") or {}
    if sum(layers.values()) != len(tools):
        fails.append("P2 mcpToolLayers 合计 %d 但 tools-list 有 %d 条"
                     % (sum(layers.values()), len(tools)))

    # P3
    actual = dict(collections.Counter(t.get("layer", "?") for t in tools))
    if layers and layers != actual:
        fails.append("P3 mcpToolLayers=%s 与 tools-list 实际分层 %s 不一致" % (layers, actual))

    # P4 —— 只查"本该在检出里"的路径。构建产物（路径里含 bin/ 或 obj/ 段的）在干净检出里
    # 本来就不存在，要求它存在会把所有 CI 都判红 —— 这正是本闸门第一次上 CI 时暴露的问题。
    for key, rel in (pm.get("entrypoints") or {}).items():
        if not rel:
            continue
        parts = rel.replace("\\", "/").split("/")
        if "bin" in parts or "obj" in parts:
            continue
        if not os.path.exists(os.path.join(root, rel.replace("/", os.sep))):
            fails.append("P4 entrypoints.%s 指向不存在的路径：%s" % (key, rel))
    return fails


def main():
    if not os.path.isfile(TOOLS_LIST) or not os.path.isfile(PKG_MANIFEST):
        print("找不到 manifest 文件 —— 请在仓库根目录运行。")
        return 2
    tl = load_json(TOOLS_LIST)
    pm = load_json(PKG_MANIFEST)
    print("tools-list: %d 条 ｜ package-manifest.mcpToolCount: %r"
          % (len(tl.get("tools", [])), (pm.get("capabilities") or {}).get("mcpToolCount")))
    fails = check(tl, pm)
    if not fails:
        print("[PASS] 两份 manifest 完全自洽（含 entrypoints 路径存在性）。")
        return 0
    print("[FAIL] manifest 不自洽：")
    for f in fails:
        print("  -", f)
    return 1


def selftest():
    """故障注入：把每个检查项各弄坏一次，必须都能报红。"""
    tl = load_json(TOOLS_LIST)
    pm = load_json(PKG_MANIFEST)
    assert not check(tl, pm), "自检前提：当前 manifest 必须是干净的"

    import copy
    cases = []

    a = copy.deepcopy(tl); a["toolCount"] = 999
    cases.append(("T1", check(a, copy.deepcopy(pm))))

    b = copy.deepcopy(tl); b["tools"][1]["name"] = b["tools"][0]["name"]
    cases.append(("T2", check(b, copy.deepcopy(pm))))

    c = copy.deepcopy(tl); c["tools"][0]["domain"] = ""
    cases.append(("T3", check(c, copy.deepcopy(pm))))

    d = copy.deepcopy(pm); d["capabilities"]["mcpToolCount"] = 1
    cases.append(("P1", check(copy.deepcopy(tl), d)))

    e = copy.deepcopy(pm); e["capabilities"]["mcpToolLayers"] = {"L0": 1}
    cases.append(("P2/P3", check(copy.deepcopy(tl), e)))

    f = copy.deepcopy(pm); f["entrypoints"]["toolRoster"] = "manifest/does-not-exist.json"
    cases.append(("P4", check(copy.deepcopy(tl), f)))

    ok = True
    for label, fails in cases:
        caught = bool(fails)
        print("  注入 %-6s -> %s" % (label, "报红 OK" if caught else "!! 没报红"))
        ok = ok and caught
    print("故障注入自检：" + ("PASS（每项都能报红）" if ok else "FAIL（有检查项形同虚设）"))
    return 0 if ok else 1


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        sys.exit(selftest())
    sys.exit(main())
