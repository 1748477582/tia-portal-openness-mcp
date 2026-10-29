#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TIA 工程体检（只读）v0.2 —— 采集 + 分析 + 出报告。

端到端（自己起引擎，要求没有别的 TiaMcpServer 在跑）：
    python tia_project_audit.py --collect --project "<apXX>" --software <PLC名> --out report.md
离线（只吃已采集 JSON，可进 CI）：
    python tia_project_audit.py --skeleton s.json --hierarchy h.json --out report.md
分级：P1 误用/返工 · P2 可维护性 · INFO 事实。每条给 证据/影响/建议；报告末列"未覆盖"。
"""
import argparse, json, os, re, subprocess, sys, time
from collections import Counter
from datetime import datetime

sys.stdout.reconfigure(encoding="utf-8")
SEV = {"P1": 0, "P2": 1, "INFO": 2}
ENGINE_DEFAULT = (r"C:\Users\106905\Downloads\TIA_MCP_V18_Work\TIA_MCP_Delivery_v2.3.0_20260704"
                  r"\tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48\TiaMcpServer.exe")


class McpClient:
    def __init__(self, engine):
        self.p = subprocess.Popen([engine], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, text=True, encoding="utf-8",
                                  bufsize=1, cwd=os.path.dirname(engine))
        self.id = 0
        self._read(self._send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                                             "clientInfo": {"name": "tia-audit", "version": "1"}}), 60)
        self._send("notifications/initialized", notify=True)

    def _send(self, method, params=None, notify=False):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not notify:
            self.id += 1
            msg["id"] = self.id
        self.p.stdin.write(json.dumps(msg, ensure_ascii=False) + "\n"); self.p.stdin.flush()
        return None if notify else self.id

    def _read(self, want, timeout=300):
        t0 = time.time()
        while time.time() - t0 < timeout:
            line = self.p.stdout.readline()
            if not line:
                return {"__eof__": True}
            line = line.strip()
            if not line:
                continue
            try:
                o = json.loads(line)
            except Exception:
                continue
            if o.get("id") == want:
                return o
        return {"__timeout__": True}

    def call(self, tool, args, timeout=300):
        return self._read(self._send("tools/call", {"name": tool, "arguments": args}), timeout)

    @staticmethod
    def payload(resp):
        if "result" not in resp:
            raise RuntimeError("工具调用失败：" + json.dumps(resp.get("error", resp), ensure_ascii=False)[:400])
        c = resp["result"].get("content") or []
        if not c:
            return {}
        try:
            outer = json.loads(c[0].get("text", ""))
        except Exception:
            return {}
        if isinstance(outer, dict) and isinstance(outer.get("message"), str):
            try:
                return json.loads(outer["message"])
            except Exception:
                return outer
        return outer

    def close(self):
        try:
            self.p.stdin.close(); self.p.terminate(); self.p.wait(timeout=10)
        except Exception:
            pass


def collect(engine, project, software):
    """采集。任何一步失败都必须抛错——绝不能在空数据上出一份'全绿'报告。"""
    name = os.path.splitext(os.path.basename(project))[0]
    c = McpClient(engine)
    try:
        att = c.call("AttachToOpenProject", {"projectName": name}, 120)
        if "result" not in att:
            op = c.call("OpenProject", {"path": project}, 300)
            if "result" not in op:
                raise RuntimeError("打开工程失败："
                                   + json.dumps(op.get("error", op), ensure_ascii=False)[:500]
                                   + " ｜ Attach 也失败："
                                   + json.dumps(att.get("error", att), ensure_ascii=False)[:300])

        sk = c.payload(c.call("GetProjectSkeleton", {"softwarePath": software}, 180))
        meta = sk.get("meta", sk) or {}
        if not meta.get("blockCount"):
            reason = meta.get("unresolvedReason") or meta.get("diag_unresolvedReason")
            if meta.get("success") is False and reason:
                raise RuntimeError(f"工程未解析，拒绝出报告（software='{software}'）：{reason}"
                                   f"原始返回：{json.dumps(sk, ensure_ascii=False)[:500]}")
            raise RuntimeError(f"采集到 0 个块，拒绝出报告（software='{software}'）。"
                               f"原始返回：{json.dumps(sk, ensure_ascii=False)[:500]}")

        hi = c.payload(c.call("GetBlocksWithHierarchy", {"softwarePath": software}, 300))
        if not (hi or {}).get("root"):
            raise RuntimeError("块层级为空，拒绝出报告（skeleton 说有块，hierarchy 却是空，说明 softwarePath 或工程状态不对）")
        return sk, hi
    finally:
        c.close()


def bucket(n):
    for lim, tag in ((100, "<100"), (1000, "100-999"), (10000, "1k-9.9k"), (100000, "10k-99k")):
        if n < lim:
            return tag
    return ">=100k"


def flatten(node, path="", out=None):
    out = out if out is not None else []
    for g in (node.get("groups") or []):
        flatten(g, path + "/" + (g.get("name") or ""), out)
    for b in (node.get("blocks") or []):
        out.append((path or "/", b.get("typeName"), b.get("name"), b))
    return out


def all_groups(node, path="", acc=None):
    acc = acc if acc is not None else []
    for g in (node.get("groups") or []):
        p = path + "/" + (g.get("name") or "")
        acc.append((p, len(g.get("blocks") or []), len(g.get("groups") or [])))
        all_groups(g, p, acc)
    return acc


PREFIX = re.compile(r"^(FB|FC|DB|OB)_", re.I)
BAD = re.compile(r"[\s\u3000\uFF00-\uFFEF]")


CALL_RE = re.compile(r'<CallInfo\b[^>]*\bName="([^"]+)"')
KIND_RE = re.compile(r"<SW\.Blocks\.(FB|FC|OB|GlobalDB|InstanceDB)\b")


def scan_block_dir(root_dir):
    """扫描 ExportBlocks 产出的 SimaticML 目录，得到每块的类型与被调用关系。

    只读文件，不连 TIA、不改 XML。返回 {块名: {"kind": ..., "callees": set()}}。
    调用引用形如 <CallInfo UId="29" BlockType="FB" Name="UN_01_PackML_ModuleSum">。
    """
    out = {}
    for name in sorted(os.listdir(root_dir)):
        if not name.lower().endswith(".xml"):
            continue
        path = os.path.join(root_dir, name)
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                raw = fh.read()
        except Exception as ex:
            # 不静默：读不了的文件要能被上层看见，否则"扫到 0 块"会被误当成"没有孤儿"
            out[os.path.splitext(name)[0]] = {"kind": "?", "callees": set(), "error": str(ex)}
            continue
        kind_m = KIND_RE.search(raw)
        kind = kind_m.group(1) if kind_m else "?"
        callees = set(CALL_RE.findall(raw))
        out[os.path.splitext(name)[0]] = {"kind": kind, "callees": callees}
    return out


def audit(sk, hi, block_index=None, naming=False):
    meta = (sk or {}).get("meta", sk) or {}
    counts, total = meta.get("counts", {}) or {}, meta.get("blockCount", 0) or 0
    groups = meta.get("topLevelGroups", []) or []
    loose = meta.get("topLevelBlocks", []) or []
    nums = {"FB": meta.get("usedFbNumbers") or [], "FC": meta.get("usedFcNumbers") or [],
            "DB": meta.get("usedDbNumbers") or [], "OB": meta.get("usedObNumbers") or []}
    I = []

    def add(rule, sev, ev, imp, adv):
        I.append({"rule": rule, "severity": sev, "evidence": ev, "impact": imp, "advice": adv})

    if loose:
        add("R1 根目录散块", "P2", f"根目录下直接放着 {len(loose)} 个块：{', '.join(loose[:10])}",
            "散块不进入任何设备/任务分组，按设备找人时会被漏看。",
            "用 MoveBlocksToGroup 归入对应组（如 VersionRecord → 50_Data）。")

    for a, b in (("FB", "FC"), ("FB", "DB"), ("FC", "DB")):
        hit = sorted(set(nums[a]) & set(nums[b]))
        if hit:
            add(f"R2 跨类型同号 {a}/{b}", "INFO",
                f"{a} 与 {b} 有 {len(hit)} 个相同编号：{', '.join(map(str, hit[:12]))}" + (" …" if len(hit) > 12 else ""),
                "TIA 允许，不影响编译运行；人工查引用时容易看错对象。",
                "不强制改造；若要做，只在新块上按类型分区间。")

    sc = {}
    for t, arr in nums.items():
        if arr:
            bk = Counter(bucket(x) for x in arr)
            if len(bk) >= 3:
                sc[t] = (len(arr), bk)
    if sc:
        add("R3 编号体系未分区", "P2",
            "；".join(f"{t} {n} 个跨 {len(bk)} 个数量级（" + "、".join(f"{k}={v}" for k, v in sorted(bk.items())) + "）"
                     for t, (n, bk) in sc.items()),
            "同类型编号跨多个数量级，说明没有统一编号规约，新增块随手取号。",
            "先定编号段规约；存量不建议批量重排（会动交叉引用），只约束新块。")

    bad = [g for g in groups if not re.match(r"^\d{2,3}_", g)]
    if naming and bad:
        add("R4 组命名缺编号前缀", "P2", f"{len(bad)}/{len(groups)} 个顶层组不符合 'NN_名称'：{', '.join(bad[:10])}",
            "同级里有的带序号有的不带，排序会乱，交接看不出层级意图。", "统一为两位前缀。")

    for t, arr in nums.items():
        if len(arr) >= 10 and max(arr) >= 1000:
            d = len(arr) / max(arr)
            if d < 0.05:
                add(f"R5 编号空间稀疏（{t}）", "INFO", f"最大号 {max(arr)}，实际 {len(arr)} 个，占用率 {d:.1%}",
                    "不影响正确性；补号时易与大号段混排。", "补号优先填空洞。")

    if hi:
        root = hi.get("root", {})
        blocks, grp = flatten(root), all_groups(root)

        # R0 对账：骨架与层级必须描述同一批块，否则后面所有结论都不可信
        h_total = len(blocks)
        if total and h_total != total:
            add("R0 数据源对账失败", "P1",
                f"骨架 blockCount={total}，层级实际遍历到 {h_total} 个块",
                "两个数据源描述的不是同一批对象，任何基于它们的结论都不成立。",
                "重新采集骨架与层级，确认用的是同一工程、同一 softwarePath。")
        hb = Counter(t for (_p, t, _n, _b) in blocks)
        pairs = {"FB": hb.get("FB", 0), "FC": hb.get("FC", 0), "OB": hb.get("OB", 0),
                 "DB": hb.get("GlobalDB", 0) + hb.get("InstanceDB", 0)}
        mism = {k: (counts.get(k, 0), v) for k, v in pairs.items() if counts and counts.get(k, 0) != v}
        if mism:
            add("R0b 类型计数对账失败", "P1",
                "；".join(f"{k}: 骨架 {a} vs 层级 {b}" for k, (a, b) in sorted(mism.items())),
                "同一类型在两个数据源里数量不同，说明采集范围或分类口径不一致。",
                "核对是否漏采子组，或 DB 是否被 InstanceDB/GlobalDB 口径拆开。")

        inc = [n for (_p, _t, n, b) in blocks if b.get("isConsistent") is False]
        if inc:
            add("R7 存在未编译块", "P1",
                f"{len(inc)}/{len(blocks)} 个块 isConsistent=false，例：{', '.join(inc[:10])}",
                "Openness 不能导出未编译块，下载也会因不一致被拒；移动/导出这类操作会直接失败。",
                "先跑 CompileAndDiagnosePlc 清零错误，再动这些块。")

        kp = [n for (_p, _t, n, b) in blocks if b.get("isKnowHowProtected") is True]
        if kp:
            add("R8 know-how 保护块", "P2", f"{len(kp)} 个块受保护，例：{', '.join(kp[:10])}",
                "Openness 读不到内部逻辑、也导不出源码；改这些块必须人工在 TIA 内解锁。",
                "确认是否仍需保护；文档/交接里标注密码保管人。")

        mm = [f"{n}({t})" for (_p, t, n, _b) in blocks
              if PREFIX.match(n or "") and t and PREFIX.match(n).group(1).upper() != t.upper()]
        if naming and mm:
            add("R9 块名前缀与类型不符", "P2", f"{len(mm)} 个，例：{', '.join(mm[:10])}",
                "名字前缀与实际类型不符（如 FB_ 开头却是 DB、DB_ 开头却是全局 DB 之外的类型），按名字判断类型会出错。", "重命名对齐类型，不动编号。")

        # 只查用户自建块：系统 OB（Time error interrupt 等）的名称由 TIA 生成、不可改，报出来是误报
        USER_TYPES = ("FB", "FC", "GlobalDB", "InstanceDB")
        bn = [n for (_p, t, n, _b) in blocks if t in USER_TYPES and BAD.search(n or "")]
        if naming and bn:
            add("R10 块名含空格或全角字符", "P2", f"{len(bn)} 个，例：{', '.join(bn[:10])}",
                "含空格的块名在 Openness 路径、脚本参数里都要转义，最易出错。"
                "（系统 OB 不在检查范围：其名称由 TIA 生成，不可修改。）",
                "改为下划线命名（'Valve Ctrl' → 'ValveCtrl'）。")

        emp = [p for (p, nb, ng) in grp if nb == 0 and ng == 0]
        if emp:
            add("R11 空组", "INFO", f"{len(emp)} 个空组，例：{', '.join(emp[:8])}",
                "不影响功能；组结构留空洞会让人以为内容丢了。", "确认无用后删除。")

        langs = Counter(b.get("programmingLanguage") or "?" for (_p, _t, _n, b) in blocks)
        if langs:
            add("R12 编程语言分布", "INFO", "、".join(f"{k}={v}" for k, v in langs.most_common()),
                "语言混杂影响可维护性与自动分析（LAD 比 SCL 难分析）。", "知悉；新块优先 SCL。")

    if block_index:
        # 注意：不要把这个参数叫 blocks —— 上面的层级分支里已有局部变量 blocks（list），会把它覆盖掉。
        callers = {}
        for caller, info in block_index.items():
            for callee in info["callees"]:
                callers.setdefault(callee, set()).add(caller)
        # 只有同名块存在时才算（避免把库/系统块当成孤儿）
        known = {n for n, i in block_index.items() if i["kind"] in ("FB", "FC")}
        orphans = sorted(n for n in known if not (callers.get(n, set()) & known))
        if orphans:
            add("R13 疑似孤儿块（无调用者）", "P2",
                f"{len(orphans)}/{len(known)} 个 FB/FC 在导出的块集合里没有任何调用者：{', '.join(orphans[:12])}"
                + (" …" if len(orphans) > 12 else ""),
                "可能是不再使用的死代码，占了维护注意力；也可能是被 HMI / OPC / 间接引用，"
                "或调用者不在本次导出范围内——本规则是启发式，不能单独作为删除依据。",
                "逐个用 GetCrossReferences 或在 TIA 里查交叉引用确认后再决定；确认无用的先归档再删。")
        # 覆盖率声明：结论只在"导出了多少块"范围内成立
        add("R13b 调用图覆盖率", "INFO",
            f"本次扫描导出块 {len(block_index)} 个（其中 FB/FC {len(known)} 个）",
            "孤儿块结论只在这批块范围内成立：没被导出的调用者看不到，会抬高孤儿数量。",
            "要得到全量结论，需一次性导出全部块（实测约 3-5 秒/块，363 块约 18-30 分钟）。")

    if total >= 300:
        add("R6 工程规模", "INFO",
            f"共 {total} 个块（FB {counts.get('FB',0)} / FC {counts.get('FC',0)} / DB {counts.get('DB',0)} / OB {counts.get('OB',0)}），{len(groups)} 个顶层组",
            "规模已到需要自动体检与文档化的量级。", "纳入定期体检 + 块级文档生成。")

    I.sort(key=lambda x: SEV.get(x["severity"], 9))
    return {"generated": datetime.now().strftime("%Y-%m-%d %H:%M"), "total": total, "counts": counts,
            "groups": len(groups), "has_hi": bool(hi), "issues": I}


def render(r, source):
    L = ["# TIA 工程体检报告\n",
         f"- 生成时间：{r['generated']}",
         f"- 数据来源：{source}（只读，未修改工程）",
         f"- 规模：**{r['total']} 个块** · FB {r['counts'].get('FB',0)} / FC {r['counts'].get('FC',0)} / "
         f"DB {r['counts'].get('DB',0)} / OB {r['counts'].get('OB',0)} · {r['groups']} 个顶层组",
         f"- 块级检查：{'已启用' if r['has_hi'] else '未启用（只给了 skeleton）'}\n"]
    c = Counter(i["severity"] for i in r["issues"])
    L += ["## 一、结论摘要\n", "| 级别 | 含义 | 条数 |", "|---|---|---|",
          f"| P1 | 会引发误用/返工 | {c.get('P1',0)} |", f"| P2 | 影响可维护性 | {c.get('P2',0)} |",
          f"| INFO | 事实统计 | {c.get('INFO',0)} |", ""]
    need = c.get("P1", 0) + c.get("P2", 0)
    L.append(f"**需要处理：{need} 项**（不含 INFO）\n" if need else "**未发现结构性问题。**\n")
    L.append("## 二、问题清单\n")
    for n, i in enumerate(r["issues"], 1):
        L += [f"### {n}. [{i['severity']}] {i['rule']}\n", f"- **证据**：{i['evidence']}",
              f"- **影响**：{i['impact']}", f"- **建议**：{i['advice']}\n"]
    L += ["## 三、本版覆盖与未覆盖\n",
          "已覆盖：数据源对账（R0/R0b）、根目录散块、跨类型同号、编号分区、编号稀疏度、"
          "未编译块、know-how 保护、空组、语言分布、规模、孤儿块（R13，启发式，需 --blocks）。\n",
          "**默认不检查**（属用户自身程序框架，加 --naming-check 才报）：组命名前缀、块名前缀与类型、块名特殊字符。\n",
          "**未覆盖**（下一版）：未使用变量、变量表规范、AF/ISA-88 分层符合度；"
          "未提供 --blocks 时不判孤儿块，且结论只覆盖已导出的块。\n"]
    return "\n".join(L)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--skeleton"); ap.add_argument("--hierarchy"); ap.add_argument("--blocks")
    ap.add_argument("--collect", action="store_true")
    ap.add_argument("--engine", default=ENGINE_DEFAULT)
    ap.add_argument("--project"); ap.add_argument("--software", default="PLC_1")
    ap.add_argument("--out")
    ap.add_argument("--naming-check", action="store_true",
                    help="启用命名规范类检查（组命名前缀/块名前缀/块名特殊字符）。默认关闭：命名属用户自身框架决定。")
    a = ap.parse_args()

    if a.collect:
        if not a.project:
            sys.exit("--collect 需要 --project")
        try:
            sk, hi = collect(a.engine, a.project, a.software)
        except Exception as e:
            print(f"[采集失败] {e}")
            sys.exit(2)
        src = f"引擎采集（{a.project}）"
        if a.out:
            base = os.path.splitext(a.out)[0]
            json.dump(sk, open(base + ".skeleton.json", "w", encoding="utf-8"), ensure_ascii=False)
            json.dump(hi, open(base + ".hierarchy.json", "w", encoding="utf-8"), ensure_ascii=False)
    else:
        if not a.skeleton:
            sys.exit("离线分析需要 --skeleton")
        sk = json.load(open(a.skeleton, encoding="utf-8-sig"))
        hi = json.load(open(a.hierarchy, encoding="utf-8-sig")) if a.hierarchy else None
        src = a.skeleton + (f" + {a.hierarchy}" if a.hierarchy else "")
        if a.blocks:
            src += f" + {a.blocks}（导出块目录）"

    blocks = scan_block_dir(a.blocks) if a.blocks else None
    if a.blocks and not blocks:
        sys.exit(f"--blocks 目录里没有可解析的 .xml：{a.blocks}")
    r = audit(sk, hi, blocks, naming=a.naming_check)
    md = render(r, src)
    if a.out:
        open(a.out, "w", encoding="utf-8").write(md)
        print(f"已写入 {a.out}")
        for i in r["issues"]:
            print(f"  [{i['severity']}] {i['rule']}")
    else:
        print(md)


if __name__ == "__main__":
    main()
