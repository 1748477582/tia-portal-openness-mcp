#!/usr/bin/env python
# -*- coding: utf-8 -*-
# SUMMARY: 推送工具：比对本地树与远端，生成计划（新增/修改/删除）并可选推送；默认只推已存在文件+显式新文件，绝不自动删远端
# WHEN: 每次提交前（--plan 看差异）和确认后（--push --message-file）；推完再跑一次 --plan 应显示零差异
# ENV: python
"""
push_repo.py — 本仓库的推送工具（替代原先只在本机存在的 _full_diff.py / _full_push.py）。

为什么需要它
    本机到 github.com:443 不通，`git push` 用不了；GitHub MCP 连接器只读（写操作 403）。
    唯一可用的通道是 Git Database REST API（api.github.com 可达，需代理）。
    而这套工具原先只存在于某一台机器上 —— 换机器就推不了。所以它必须在仓库里。

两条安全设计（都是踩过坑换来的）
    1. **只推"远端已存在"的文件 + 显式新增的文件**。不做"本地有就推"：
       本地树可能是远端的**子集/旧版**（例如 V20/V21 的 csproj），全量推会静默回退远端。
    2. **绝不自动删除远端文件**。远端独有的一律只报告、不动手。

用法
    python tools/tiaportal-mcp/push_repo.py --plan
    python tools/tiaportal-mcp/push_repo.py --plan --add docs/NEW.md --add scripts/x.py
    python tools/tiaportal-mcp/push_repo.py --push --message-file msg.txt
    python tools/tiaportal-mcp/push_repo.py --push --message-file msg.txt --add docs/NEW.md

凭据
    环境变量 GITHUB_TOKEN / GH_TOKEN，或 TIA_MCP_TOKEN_FILE 指向的明文文件（默认本机 D:/Users/Desktop/APIkey.json）。
    代理：HTTPS_PROXY（api.github.com 在本机必须走代理）。
"""
import base64
import json
import os
import re
import ssl
import sys
import time
import urllib.request

OWNER = "1748477582"
REPO = "tia-portal-openness-mcp"
BRANCH = "master"

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# 不参与推送的目录/文件（构建产物、日志、本机配置、一次性脚本）
EXCLUDE_DIRS = {".git", "__pycache__", "bin", "obj", "bin-v18", "bin-v20",
                "obj-v18", "obj-v20", "bin-ci-v20", "obj-ci-v20", ".vs", "admin"}
EXCLUDE_FILE_RE = re.compile(
    r"(^_|\.oldrun|\.bak$|\.log$|^push_.*\.py$|^push_.*\.json$|^manifest\.py$|^\.mcp\.json$|\.user$)",
    re.IGNORECASE)

# 远端比本地新/全、绝不能全量推的文件（本地是简化子集或过期副本）
NEVER_PUSH = {
    "tools/tiaportal-mcp/src/TiaMcpServer/TiaMcpServer.csproj",
    "tools/tiaportal-mcp/src/TiaMcpServer/TiaMcpServer.V20.csproj",
    ".gitignore",
    # 2026-10-19 实测确认：这四份本地是**过期副本**，推上去等于回退远端。
    #   docs/server-maturity-roadmap.md        远端多 146 B（本地缺新增章节）
    #   docs/在线实时读值_使用指南.md            远端多 354 B（本地另有去行业化的改动，属分叉，需人工合并）
    #   scripts/Check-LiteProfile.py           远端含 2.4.0 新增的 CompileAndDiagnoseHmi / VCI 工具
    #   scripts/Check-VersionConsistency.py    本地注释还写着"本仓没有 CHANGELOG.md"（旧事实）
    # 处理办法：需要时从远端取回覆盖本地，别反过来推。见 docs/lessons-and-shortcuts.md §7.3。
    "docs/server-maturity-roadmap.md",
    "docs/在线实时读值_使用指南.md",
    "scripts/Check-LiteProfile.py",
    "scripts/Check-VersionConsistency.py",
}


def read_token():
    kf = os.environ.get("TIA_MCP_TOKEN_FILE") or r"D:/Users/Desktop/APIkey.json"
    if os.path.exists(kf):
        for line in open(kf, encoding="utf-8", errors="ignore"):
            if "github" in line.lower():
                m = re.search(r"(gh[pousr]_[A-Za-z0-9]+)", line)
                if m:
                    return m.group(1)
    return os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")


def api(path, token, method="GET", payload=None, tries=5):
    url = "https://api.github.com" + path
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    last = None
    for i in range(tries):
        try:
            req = urllib.request.Request(url, data=data, method=method)
            req.add_header("Authorization", "Bearer " + token)
            req.add_header("Accept", "application/vnd.github+json")
            req.add_header("User-Agent", "tia-mcp-push")
            if data is not None:
                req.add_header("Content-Type", "application/json")
            with urllib.request.urlopen(req, timeout=180, context=ctx) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            body = ""
            try:
                body = e.read().decode("utf-8", "replace")[:300]
            except Exception:
                pass
            if 400 <= e.code < 500:
                raise RuntimeError("HTTP %s %s %s -> %s" % (e.code, method, path, body))
            last = e
            time.sleep(2 ** i)
        except Exception as e:                      # 网络层错误必须也重试
            last = e
            time.sleep(2 ** i)
    raise last


def local_files(extra_added):
    out = {}
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
        for name in filenames:
            if EXCLUDE_FILE_RE.search(name):
                continue
            p = os.path.join(dirpath, name)
            rel = os.path.relpath(p, ROOT).replace(os.sep, "/")
            out[rel] = p
    # 显式新增的即使命中排除规则也收进来（由调用方负责有意义）
    for rel in extra_added:
        p = os.path.join(ROOT, rel.replace("/", os.sep))
        if os.path.isfile(p):
            out[rel] = p
    return out


def remote_tree(token, head_sha):
    """返回 {path: blob_sha}（只含 blob）。"""
    t = api("/repos/%s/%s/git/trees/%s?recursive=1" % (OWNER, REPO, head_sha), token)
    return {e["path"]: e["sha"] for e in t.get("tree", []) if e.get("type") == "blob"}


def blob_sha(token, path):
    """本地文件按 git blob 规则算 sha：sha1('blob %d\\0' + bytes)。"""
    import hashlib
    data = open(path, "rb").read()
    h = hashlib.sha1()
    h.update(b"blob %d\0" % len(data))
    h.update(data)
    return h.hexdigest(), data


def build_plan(token, extra_added):
    head = api("/repos/%s/%s/git/ref/heads/%s" % (OWNER, REPO, BRANCH), token)["object"]["sha"]
    remote = remote_tree(token, head)
    added, modified = [], []
    for rel, p in sorted(local_files(extra_added).items()):
        if rel in NEVER_PUSH:
            continue
        sha, _data = blob_sha(token, p)
        if rel not in remote:
            if rel in extra_added:
                added.append(rel)
            continue                                   # 未知新文件：不擅自加，除非显式 --add
        if remote[rel] != sha:
            modified.append(rel)
    deleted = sorted(set(remote) - set(local_files(extra_added)) - NEVER_PUSH)
    return head, {"added": added, "modified": modified, "deleted": deleted}


def print_plan(head, plan):
    print("HEAD = %s%s" % (head[:8], "" if head else ""))
    for k in ("added", "modified", "deleted"):
        items = plan[k]
        label = {"added": "ADDED（显式新增）", "modified": "MODIFIED（内容不同）",
                 "deleted": "DELETED（远端独有，不会自动删）"}[k]
        print("\n%s - %d" % (label, len(items)))
        for it in items:
            print("   " + it)
    print("\nSUMMARY: added=%d modified=%d deleted=%d" % (
        len(plan["added"]), len(plan["modified"]), len(plan["deleted"])))


def push(token, head, plan, message):
    targets = plan["added"] + plan["modified"]
    if not targets:
        print("没有需要推送的内容。")
        return 0

    # 远端 HEAD 变动就中止：否则会在过期基线上建树，覆盖别人的提交
    now = api("/repos/%s/%s/git/ref/heads/%s" % (OWNER, REPO, BRANCH), token)["object"]["sha"]
    if now != head:
        print("ABORT: remote HEAD moved %s -> %s; 先重跑 --plan" % (head[:10], now[:10]))
        return 2

    base_tree = api("/repos/%s/%s/git/commits/%s" % (OWNER, REPO, head), token)["tree"]["sha"]
    entries = []
    total = 0
    for i, rel in enumerate(targets, 1):
        p = os.path.join(ROOT, rel.replace("/", os.sep))
        data = open(p, "rb").read()
        total += len(data)
        b = api("/repos/%s/%s/git/blobs" % (OWNER, REPO), token, method="POST",
                payload={"content": base64.b64encode(data).decode("ascii"), "encoding": "base64"})
        entries.append({"path": rel, "mode": "100644", "type": "blob", "sha": b["sha"]})
        if i % 10 == 0 or i == len(targets):
            print("  blobs %d/%d (%.1f MB)" % (i, len(targets), total / 1048576.0))

    tree_sha = base_tree
    for i in range(0, len(entries), 100):              # GitHub 对单次 entries 有上限
        chunk = entries[i:i + 100]
        t = api("/repos/%s/%s/git/trees" % (OWNER, REPO), token, method="POST",
                payload={"base_tree": tree_sha, "tree": chunk})
        tree_sha = t["sha"]

    c = api("/repos/%s/%s/git/commits" % (OWNER, REPO), token, method="POST",
            payload={"message": message, "tree": tree_sha, "parents": [head]})
    api("/repos/%s/%s/git/refs/heads/%s" % (OWNER, REPO, BRANCH), token, method="PATCH",
        payload={"sha": c["sha"], "force": False})
    print("PUSHED: %s -> %s  (%d 文件)" % (BRANCH, c["sha"], len(targets)))
    return 0


def main(argv):
    if "--plan" not in argv and "--push" not in argv:
        print(__doc__)
        return 0
    token = read_token()
    if not token:
        print("ERROR: 找不到 GitHub token。设 GITHUB_TOKEN，或用 TIA_MCP_TOKEN_FILE 指向含 'ghp_...' 的文件。")
        return 3
    added = []
    for i, a in enumerate(argv):
        if a == "--add" and i + 1 < len(argv):
            added.append(argv[i + 1].replace(os.sep, "/"))

    head, plan = build_plan(token, added)
    print_plan(head, plan)
    if "--push" not in argv:
        return 0

    msg = None
    for i, a in enumerate(argv):
        if a == "--message-file" and i + 1 < len(argv):
            msg = open(argv[i + 1], encoding="utf-8-sig").read().strip()
        elif a == "--message" and i + 1 < len(argv):
            msg = argv[i + 1]
    if not msg:
        print("ERROR: --push 需要 --message-file 或 --message（提交信息不能靠默认值）")
        return 4
    return push(token, head, plan, msg)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
