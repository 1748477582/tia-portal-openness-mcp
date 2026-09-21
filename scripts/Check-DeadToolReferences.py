"""死引用闸：工具描述里点名的工具，必须真的注册过。

为什么要有这道闸：描述文案写错一个工具名，Agent 照着调只会撞 "tool not found"，
然后自己找别的路子绕 —— 若撞上的恰是安全敏感操作，后果更差。这类漂移人记不住，
只能靠对拍：拿**引擎实际注册的名字**，扫所有 Agent 能看到的 [Description] 文字。

本移植版对上游脚本做了两处修正（上游会把注释掉的声明也算作"已注册"）：
  1. 注册名优先取 `manifest/tools-list.json`（由 live tools/list 生成，是运行时真相）；
     manifest 缺失时才回退到源码扫描，且**跳过以 // 开头的行**。
  2. 扫描 [Description] 时跳过被注释掉的声明（否则 `// [DISABLED-A] [...Description("...")]`
     里的文案会被当成活跃文案）。

用法（在仓库根目录运行）：
    python scripts/Check-DeadToolReferences.py             # 0=干净 1=有死引用
    python scripts/Check-DeadToolReferences.py --selftest  # 哨兵：注入假名字，必须被抓到
"""
import collections
import io
import json
import os
import re
import sys

# Windows 上的 Python 默认按 cp1252 输出（GitHub Actions 的 windows runner 就是），
# 打印中文会直接 UnicodeEncodeError 崩掉 —— 闸门"跑不起来"和"发现问题"长得一样，
# 所以显式切到 UTF-8，让它在任何宿主上都能输出。
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = "tools/tiaportal-mcp/src/TiaMcpServer"
MANIFEST = "manifest/tools-list.json"

# 白名单：形状像工具名、但不是本服务器的工具，因此不该被判死引用。
# 每条必须写明它到底是什么 —— 没有理由的白名单等于把闸门关掉。
ALLOWED = {
    "GetService": "Openness IEngineeringObject.GetService<T>()",
    "GetAttribute": "Openness IEngineeringObject.GetAttribute()",
    "GetAttributeInfos": "Openness IEngineeringObject.GetAttributeInfos()",
    "GetSupportedFileFormats": "Openness Workspace.GetSupportedFileFormats()",
    "ConnectObject": "Openness Workspace.ConnectObject()",
    "ImportDocumentOptions": "Openness 枚举类型名（importOption 参数的取值来源）",
    "DownloadProvider": "Openness DownloadProvider 类型名",
    "AddSignalBoard": "TIA 里那个操作的俗称（描述原文即『这就是 InsertDeviceItem / AddSignalBoard 操作』）",
    "BuildPlc": "BuildPlc* 工具族的通配前缀（BuildPlcUdtXml / BuildPlcObXml / …）",
    "CompileError": "错误码取值，不是工具名",
    "RunOut": "EnsureStartStopUnifiedHmi 建的 HMI 变量名",
    "DeleteDb": "描述原文即『没有单独的 DeleteDb/DeleteGlobalDb/DeleteFunctionBlock，用 DeleteBlock』",
    "DeleteGlobalDb": "同上",
    "DeleteFunctionBlock": "同上",
    "ImportInstanceTexts": "描述原文即 'not yet exposed'",
}

VERB = re.compile(
    r"^(Get|Set|Add|Import|Export|Create|Delete|Compile|Download|Sync|Analyze"
    r"|Build|Write|Read|Ensure|Find|List|Describe|Invoke|Generate|Apply|Bind"
    r"|Move|Rename|Save|Open|Close|Connect|Run|Check|Validate|Preflight|Scaffold|Attach)[A-Z]")
STR = r'"[^"]*"'                                   # 描述文案里没有转义引号，简单形态足够
LIT = re.compile(r"Description\(\s*((?:@?" + STR + r"\s*\+?\s*)+)\)", re.S)
PIECE = re.compile(STR)
TOK = re.compile(r"\b([A-Z][A-Za-z0-9]{3,})\b")
DECL = re.compile(r'McpServerTool\(Name\s*=\s*"([A-Za-z0-9_]+)"')


def load(root):
    src = {}
    for dp, _, fs in os.walk(root):
        for f in fs:
            if f.endswith(".cs"):
                p = os.path.join(dp, f)
                src[p] = io.open(p, encoding="utf-8-sig", errors="replace").read()
    return src


def is_commented(s, pos):
    """pos 所在行的行首（去掉空白后）是否以 // 开头。"""
    ls = s.rfind("\n", 0, pos) + 1
    return s[ls:pos].lstrip().startswith("//")


def registered_from_manifest():
    if os.path.isfile(MANIFEST):
        with io.open(MANIFEST, encoding="utf-8-sig") as f:
            d = json.load(f)
        return set(t["name"] for t in d.get("tools", [])), MANIFEST
    return None, None


def registered_from_source(src):
    names = set()
    for s in src.values():
        for line in s.splitlines():
            if line.lstrip().startswith("//"):
                continue
            names |= set(DECL.findall(line))
    return names


def scan(src, names, extra_text=None):
    """返回 {疑似死引用名: [出处]}。extra_text 供哨兵注入用。"""
    items = list(src.items())
    if extra_text:
        items.append(("<sentinel>", extra_text))
    bad = collections.defaultdict(list)
    for p, s in items:
        for m in LIT.finditer(s):
            if p != "<sentinel>" and is_commented(s, m.start()):
                continue                       # 注释掉的声明不算活跃文案
            text = " ".join(x[1:-1] for x in PIECE.findall(m.group(1)))
            line = s[:m.start()].count("\n") + 1
            for t in set(TOK.findall(text)):
                if t in names or t in ALLOWED or not VERB.match(t):
                    continue
                bad[t].append(os.path.basename(p) + ":" + str(line))
    return bad


def main():
    src = load(ROOT)
    if not src:
        print("找不到源码目录 %s —— 请在仓库根目录运行。" % ROOT)
        return 2

    names, where = registered_from_manifest()
    if names is None:
        names = registered_from_source(src)
        where = "source scan (%d .cs files)" % len(src)
    print("已注册工具：%d 个（来源：%s）；扫描文件：%d 个" % (len(names), where, len(src)))

    # 哨兵：注入一个必然不存在的工具名，闸门必须抓到它。
    # 本仓吃过「检查自己坏了却全绿」的亏，所以这条不是形式主义。
    sentinel = '[McpServerTool(Name = "SentinelTool"), Description("Use GetNonexistentSentinelTool first.")]'
    if "GetNonexistentSentinelTool" not in scan(src, names, extra_text=sentinel):
        print("[FAIL] 哨兵没被抓到 —— 这个检查自己坏了，它的 PASS 不可信。")
        return 2

    bad = scan(src, names)
    if not bad:
        print("[PASS] 工具描述里点名的工具全部真实注册（哨兵已验证闸门有效）。")
        return 0
    print("[FAIL] 下列名字在 [Description] 文案里被点名，但没有任何已注册工具叫这个名字：")
    for t, locs in sorted(bad.items()):
        print("  %-40s %2d 处  %s" % (t, len(locs), ", ".join(sorted(set(locs))[:4])))
    print("修法：要么改文案说清事实与替代路径，要么把工具真的注册上。"
          "若它本就不是工具名，加进本脚本的 ALLOWED 并写明理由。")
    return 1


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        src = load(ROOT)
        names, _ = registered_from_manifest()
        if names is None:
            names = registered_from_source(src)
        ok = "GetNonexistentSentinelTool" in scan(
            src, names, extra_text='[Description("Use GetNonexistentSentinelTool first.")]')
        print("哨兵自检：" + ("PASS（假名字被抓到）" if ok else "FAIL（假名字没被抓到）"))
        sys.exit(0 if ok else 1)
    sys.exit(main())
