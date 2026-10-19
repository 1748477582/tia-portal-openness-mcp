# -*- coding: utf-8 -*-
# SUMMARY: 生成并校验 docs/INDEX.md + docs/index.json：把全部文档/脚本/清单编成可检索索引
# WHEN: 新增或改名任何文档、脚本、清单之后；CI 的 content-index 也会跑 --check
# ENV: python
"""Generate-DocIndex.py — 把仓库里的「各种形式的内容」编成机器可读 + 人可读的索引。

为什么需要它
    换一台电脑、换一个 agent，人只能看到一堆目录和文件名。谁在哪儿、干什么、什么时候该读、
    跑起来需要什么环境——这些如果只存在于某个人的记忆里，就等于不存在。
    所以：**索引由文件自己声明（front-matter）生成，不靠手写**，并进 CI 校验新鲜度。

用法
    python scripts/Generate-DocIndex.py            # 重新生成 docs/INDEX.md + docs/index.json
    python scripts/Generate-DocIndex.py --check    # 过期则返回 1（CI 用）
    python scripts/Generate-DocIndex.py --quiet

文件怎么声明自己（推荐；缺了也能兜底）
    文件开头写：
        ---
        summary: 一句话说清这份东西是干什么的
        kind: doc | script | entry | manifest | skill
        when: 什么时候该读 / 该跑
        env: none | python | dotnet | tia
        ---
    没有 front-matter 时，自动取正文第一段像样的文字 + 按扩展名推断 kind。

扫描范围（要加新位置就改 SCAN）
    docs/*.md · scripts/* · 根目录入口（*.md/*.ps1/*.bat/*.cmd/*.json）·
    tools/tiaportal-mcp/{skill/SKILL.md, *.bat, *.ps1, *.csproj} · manifest/*
"""
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_MD = os.path.join(ROOT, "docs", "INDEX.md")
OUT_JSON = os.path.join(ROOT, "docs", "index.json")

# 不索引"只在某台机器上有"的东西：否则本地生成的索引和 CI 干净检出生成的索引会不一致，
# 而 content-index 闸门比的就是这两者。这类文件包括：本机连接器配置、推送脚手架、临时计划。
SKIP_NAMES = {"INDEX.md", "index.json", ".mcp.json"}
SKIP_NAME_RE = re.compile(r"^(?:_|push_)|_plan\.json$", re.IGNORECASE)
SKIP_DIR_PARTS = {"bin", "obj", "bin-v18", "bin-v20", "obj-v18", "obj-v20", "runtime", "node_modules"}

SCAN = [
    ("", ("*.md", "*.ps1", "*.bat", "*.cmd", "*.json"), "entry"),
    ("docs", ("*.md",), "doc"),
    ("scripts", ("*.py", "*.ps1"), "script"),
    ("manifest", ("*.json",), "manifest"),
    ("tools/tiaportal-mcp", ("skill/SKILL.md", "*.bat", "*.ps1", "*.csproj"), "skill"),
]

EXT_KIND = {".md": "doc", ".py": "script", ".ps1": "script", ".bat": "script",
            ".cmd": "script", ".json": "manifest", ".csproj": "build"}

FM_FENCE = re.compile(r"^---\s*$")


def read_text(path):
    try:
        raw = open(path, "rb").read()
    except OSError:
        return None
    if raw[:2] in (b"MZ", b"PK"):
        return None
    try:
        return raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        return None


def parse_front_matter(text):
    meta = {}
    lines = text.splitlines()
    if not lines or not FM_FENCE.match(lines[0]):
        return meta, text
    for i, line in enumerate(lines[1:], 1):
        if FM_FENCE.match(line):
            return meta, "\n".join(lines[i + 1:])
        if ":" in line:
            k, v = line.split(":", 1)
            k = k.strip().lower()
            v = v.strip().strip('"').strip("'")
            if k in ("summary", "kind", "when", "env", "title", "audience"):
                meta[k] = v
    return meta, text


def first_sentence(body):
    """取第一段像样的说明文字：跳过标题/表格/代码/引用/列表/徽章，并去掉 markdown 装饰。"""
    for raw in body.splitlines():
        s = raw.strip()
        if not s:
            continue
        if s.startswith(("#", "|", "```", ">", "-", "*", "<!--")):
            continue
        if s.startswith("![") or s.startswith("[!["):
            continue
        if raw[:1] in (" ", "\t"):          # 列表项/缩进的续行，不是独立段落
            continue
        if re.match(r"^\[.*\]\(.*\)$", s):   # 纯链接行
            continue
        s2 = re.sub(r"!?\[[^\]]*\]\([^)]*\)", "", s)
        s2 = re.sub(r"[`*_\[\]]", "", s2)
        s2 = re.sub(r"\s+", " ", s2).strip()
        if len(s2) >= 20:
            return s2[:120] + ("…" if len(s2) > 120 else "")
    return ""


def script_meta(text):
    """脚本没有 front-matter（写 `---` 会让 .ps1/.bat 语法失效），改用标记注释：
        # SUMMARY: 一句话
        # WHEN: 什么时候用
        # ENV: python|dotnet|tia|none
    （.py 与 .ps1 都用 `#`；.bat 用 `rem`。这里三种都认。）
    """
    meta = {}
    for raw in text.splitlines()[:80]:
        line = raw.strip()
        m = re.match(r"^(?:#|rem\s+)\s*(SUMMARY|WHEN|ENV)\s*[:：]\s*(.+)$", line, re.I)
        if m:
            meta[m.group(1).lower()] = m.group(2).strip()
    return meta


def script_summary(text):
    """从 docstring / 注释里取一句人话，去掉引号与"文件名 — "这种前缀。"""
    # Python module docstring
    m = re.search(r'^\s*(?:#![^\n]*\n)?\s*(?:#[^\n]*\n)*\s*([ru]?)"""(.*?)"""', text, re.S)
    if m:
        body = m.group(2)
    else:
        # PS 注释帮助块 <# ... #>
        m2 = re.search(r"<#(.*?)#>", text, re.S)
        body = m2.group(1) if m2 else text
    for raw in body.splitlines():
        s = raw.strip().lstrip("#").strip()
        if not s or s.startswith((".PARAMETER", ".SYNOPSIS", ".DESCRIPTION", ".EXAMPLE", ".NOTES", "---")):
            continue
        s = re.sub(r"^[\w.\-]+\.(py|ps1|bat|cmd)\s*[—:-]\s*", "", s)   # 去掉 "X.py — "
        s = s.replace('"""', "").replace("'''", "").strip()
        s = re.sub(r"\s+", " ", s)
        if len(s) >= 12:
            return s[:120] + ("…" if len(s) > 120 else "")
    return ""


def title_of(body, fallback):
    for raw in body.splitlines():
        m = re.match(r"^#\s+(.+?)\s*$", raw)
        if m:
            return m.group(1).strip()
    return fallback


def kind_for(rel, meta):
    if meta.get("kind"):
        return meta["kind"]
    low = rel.lower()
    for ext, k in EXT_KIND.items():
        if low.endswith(ext):
            return k
    return "other"


def env_for(rel, meta):
    if meta.get("env"):
        return meta["env"]
    low = rel.lower()
    if low.endswith(".py"):
        return "python"
    if low.endswith((".bat", ".cmd")) or "install-mcp" in low:
        return "python"
    if low.endswith(".csproj"):
        return "dotnet"
    if "tia" in low and low.endswith((".ps1", ".py")):
        return "tia"
    return "none"


def iter_candidates():
    for sub, patterns, _default in SCAN:
        base = os.path.join(ROOT, sub) if sub else ROOT
        if not os.path.isdir(base):
            continue
        for pat in patterns:
            if "/" in pat:
                p = os.path.join(base, *pat.split("/"))
                if os.path.isfile(p):
                    yield p
                continue
            suffix = pat.lstrip("*")
            for name in sorted(os.listdir(base)):
                p = os.path.join(base, name)
                if not os.path.isfile(p) or name in SKIP_NAMES:
                    continue
                if SKIP_NAME_RE.search(name):
                    continue
                if not name.lower().endswith(suffix):
                    continue
                yield p


# 本地与远端存在内容漂移的 4 份：描述钉死，避免索引随"本地是哪一版"而变（否则 CI 与本地不一致）。
PINNED = {
    "docs/server-maturity-roadmap.md": (
        "服务成熟度路线图：与商业产品逐项对比后的差距清单与推进顺序", "规划下一步能力时"),
    "docs/在线实时读值_使用指南.md": (
        "在线实时读值使用指南：如何在不改程序的前提下读取 PLC 当前变量值", "要在真机上读实时值时"),
    "scripts/Check-LiteProfile.py": (
        "lite 档闸门：只拿到精简工具集的模型仍必须能走完黄金路径", "改 lite 名册或核心工具时"),
    "scripts/Check-VersionConsistency.py": (
        "版本一致性闸：csproj 与 package-manifest 的版本号必须彼此一致", "发版或改版本号时"),
}


def special_summary(rel, text):
    """按文件名给"没法自述"的产物一句人话（JSON 配置、批处理入口）。

    这些文件里塞 front-matter 会破坏格式（JSON 会被闸门校验；.bat 里 `---` 不是注释），
    所以描述放在这里——但规则必须写明是哪个文件的用途，不能泛泛。
    """
    low = rel.lower()
    name = os.path.basename(rel)
    if rel in PINNED:
        return PINNED[rel][0]
    if low.endswith(".json"):
        table = {
            "manifest/tools-list.json": "工具权威清单（由真引擎 tools/list 生成；改工具后必须重生成）",
            "manifest/package-manifest.json": "交付包元数据：版本号、能力快照、入口与自检记录",
            ".mcp.json": "MCP 客户端连接配置（本机用）",
            "cursor-mcp.example.json": "MCP 客户端配置示例（Cursor/mcp.json 格式，复制粘贴用）",
        }
        for k, v in table.items():
            if low.endswith(k):
                return v
        return "JSON 数据/配置（%d 字节）" % len(text.encode("utf-8"))
    if low.endswith((".bat", ".cmd")):
        if name.startswith("配置MCP"):
            return "按 TIA 版本一键把本 MCP 注册进已安装客户端（转调 install-mcp.ps1，改前备份）"
        if name.startswith("tia"):
            return "命令行入口：同一个 exe 的 tia 子命令（如 doctor）"
        if "launcher" in low:
            return "多版本启动器：按 TIA_MCP_VERSION 选 bin-vXX，未设置则探测本机 TIA"
        return "批处理入口（%d 字节）" % len(text.encode("utf-8"))
    return ""


def build():
    entries = {}
    for path in iter_candidates():
        rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
        if any(part in SKIP_DIR_PARTS for part in rel.split("/")):
            continue
        text = read_text(path)
        if text is None:
            continue
        meta, body = parse_front_matter(text)
        if not meta:
            meta = script_meta(text)             # 脚本用 # SUMMARY: 标记
        summary = meta.get("summary")
        if not summary:
            summary = special_summary(rel, text)
        if not summary:
            summary = first_sentence(body) if rel.lower().endswith(".md") else script_summary(text)
        summary = summary or "(未写说明)"
        entries[rel] = {
            "path": rel,
            "title": (PINNED[rel][0].split("：")[0] if rel in PINNED
                      else (meta.get("title") or title_of(body, os.path.basename(rel)))),
            "kind": kind_for(rel, meta),
            "summary": summary,
            "when": (PINNED[rel][1] if rel in PINNED else meta.get("when", "")),
            "env": env_for(rel, meta),
        }
    return [entries[k] for k in sorted(entries)]


def render_md(items):
    by_kind = {}
    for it in items:
        by_kind.setdefault(it["kind"], []).append(it)
    order = ["entry", "doc", "skill", "script", "manifest", "build", "other"]
    out = [
        "# 内容索引（自动生成，别手改）",
        "",
        "> 由 `scripts/Generate-DocIndex.py` 生成；`--check` 校验是否过期，CI 的 `content-index` 会查。",
        "> 换机器 / 换 agent 时**先读这里**：每份文件是干什么的、什么时候该用、需要什么环境。",
        "> 新增文件后要重跑生成器，否则索引与实际脱节。",
        "",
        "| 共 %d 项 | 环境：`none` 纯本机 · `python` 需 Python · `dotnet` 需 .NET SDK · `tia` 需 TIA Portal |" % len(items),
        "",
    ]
    for kind in order:
        group = by_kind.get(kind, [])
        if not group:
            continue
        out += ["## %s（%d）" % (kind, len(group)), "",
                "| 文件 | 说明 | 何时用 | 环境 |", "|---|---|---|---|"]
        for it in group:
            p = it["path"]
            if it["kind"] in ("doc", "entry", "skill"):
                href = p[len("docs/"):] if p.startswith("docs/") else "../" + p
                cell = "[`%s`](%s)" % (p, href)
            else:
                cell = "`%s`" % p
            out.append("| %s | %s | %s | `%s` |" % (
                cell, it["summary"].replace("|", "\\|"),
                (it["when"] or "—").replace("|", "\\|"), it["env"]))
        out.append("")
    out += [
        "---",
        "",
        "## 怎么加新内容",
        "",
        "1. 文件开头写 front-matter：`summary` / `kind` / `when` / `env`。",
        "2. 跑 `python scripts/Generate-DocIndex.py`。",
        "3. 跑 `python scripts/Generate-DocIndex.py --check`，必须返回 0。",
        "",
        "第 1 步漏了不会失败（用正文首段兜底），但**第 2 步漏了 CI 会红**——故意的：",
        "索引一旦能和实际文件脱节，就等于没有索引。",
        "",
    ]
    return "\n".join(out)


def snapshot(items):
    data = {"generatedBy": "scripts/Generate-DocIndex.py", "count": len(items), "items": items}
    return render_md(items), json.dumps(data, ensure_ascii=False, indent=1) + "\n"


def main(argv):
    items = build()
    md, js = snapshot(items)
    if "--check" in argv:
        stale = []
        if (read_text(OUT_MD) or "") != md:
            stale.append("docs/INDEX.md")
        if (read_text(OUT_JSON) or "") != js:
            stale.append("docs/index.json")
        if stale:
            print("[FAIL] 内容索引已过期：%s" % ", ".join(stale))
            print("       跑 `python scripts/Generate-DocIndex.py` 重新生成后再提交。")
            return 1
        print("[PASS] 内容索引是最新的（%d 项）。" % len(items))
        return 0

    os.makedirs(os.path.dirname(OUT_MD), exist_ok=True)
    changed = []
    for path, content in ((OUT_MD, md), (OUT_JSON, js)):
        if (read_text(path) or "") != content:
            io.open(path, "w", encoding="utf-8", newline="\n").write(content)
            changed.append(os.path.relpath(path, ROOT).replace(os.sep, "/"))
    if "--quiet" not in argv:
        print("索引 %d 项；%s" % (len(items), ("已更新: " + ", ".join(changed)) if changed else "无变化"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
