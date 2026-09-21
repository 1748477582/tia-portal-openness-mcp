# -*- coding: utf-8 -*-
"""版本一致性闸：csproj / package-manifest 里的版本号必须彼此一致。

为什么：发版时最容易出的事，是"版本号在几个地方各说各话"——csproj 已经 2.4.0，
package-manifest 还写着 2.3.0，用户拿到手根本分不清是哪一版。这类不一致不会让任何
东西崩，只会让排查时多绕半天，只有对拍能挡住。

⚠️ 本仓**没有 CHANGELOG.md，且是有意不维护**（见项目记忆：已删除、不再维护）。
   所以这里**不**校验 CHANGELOG —— 别"顺手"把它加回来。

用法（仓库根目录）：
    python scripts/Check-VersionConsistency.py            # 0=一致 1=不一致
    python scripts/Check-VersionConsistency.py --selftest  # 故障注入：必须能报红
"""
import glob
import io
import json
import os
import re
import sys

SRC = "tools/tiaportal-mcp/src/TiaMcpServer"
PKG_MANIFEST = "manifest/package-manifest.json"
VER_RE = re.compile(r"<AssemblyVersion>\s*([^<\s]+)\s*</AssemblyVersion>")


def collect():
    versions = {}
    for p in sorted(glob.glob(os.path.join(SRC, "*.csproj"))):
        text = io.open(p, encoding="utf-8-sig", errors="replace").read()
        m = VER_RE.search(text)
        versions[os.path.basename(p)] = m.group(1) if m else None
    pm = json.load(io.open(PKG_MANIFEST, encoding="utf-8-sig"))
    return versions, pm


def check(versions, pm):
    fails = []

    missing = [k for k, v in versions.items() if not v]
    if missing:
        fails.append("这些 csproj 里找不到 <AssemblyVersion>：%s" % ", ".join(missing))

    known = sorted(set(v for v in versions.values() if v))
    if len(known) > 1:
        fails.append("各 csproj 的 AssemblyVersion 不一致：%s"
                     % ", ".join("%s=%s" % (k, v) for k, v in sorted(versions.items())))

    bv = pm.get("bundleVersion")
    pn = pm.get("packageName", "") or ""
    if known:
        if bv != known[0]:
            fails.append("package-manifest.bundleVersion=%r 与 csproj 的 %r 不一致" % (bv, known[0]))
        if bv and str(bv) not in pn:
            fails.append("package-manifest.packageName=%r 里不含版本号 %s" % (pn, bv))
    else:
        fails.append("拿不到任何 csproj 版本号，无法比对")
    return fails


def main():
    versions, pm = collect()
    print("csproj AssemblyVersion: %s" % ", ".join("%s=%s" % (k, v) for k, v in sorted(versions.items())))
    print("package-manifest: bundleVersion=%r packageName=%r"
          % (pm.get("bundleVersion"), pm.get("packageName")))
    fails = check(versions, pm)
    if not fails:
        print("[PASS] 版本号在 csproj 与 package-manifest 之间一致。")
        return 0
    print("[FAIL] 版本号不一致：")
    for f in fails:
        print("  -", f)
    return 1


def selftest():
    versions, pm = collect()
    assert not check(versions, pm), "自检前提：当前版本号必须是干净的"
    import copy
    cases = []

    a = copy.deepcopy(versions)
    a[sorted(a)[0]] = "9.9.9"
    cases.append(("csproj 互不一致", check(a, copy.deepcopy(pm))))

    b = copy.deepcopy(pm)
    b["bundleVersion"] = "0.0.1"
    cases.append(("manifest 与 csproj 不一致", check(copy.deepcopy(versions), b)))

    c = copy.deepcopy(pm)
    c["packageName"] = "TIA_MCP_Delivery_无版本号"
    cases.append(("packageName 缺版本号", check(copy.deepcopy(versions), c)))

    ok = True
    for label, fails in cases:
        caught = bool(fails)
        print("  注入 %-22s -> %s" % (label, "报红 OK" if caught else "!! 没报红"))
        ok = ok and caught
    print("故障注入自检：" + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(selftest() if "--selftest" in sys.argv else main())
