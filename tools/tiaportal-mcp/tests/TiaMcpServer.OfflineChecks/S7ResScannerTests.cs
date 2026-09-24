using System.Collections.Generic;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// S7ResScanner 扫 .s7res 里缺 en-US 的 MultiLingualText id。
    /// 它错了不会抛：要么"什么都没缺"（放过真缺的），要么"全缺"（乱警告）。
    ///
    /// 🔴 2026-09-24 实测纠正：**V20 的 .s7res 是 XML，不是 YAML**（真实导出样本见下面 RealV20S7Res）。
    /// 而 V21 那份是 YAML。上一版只认 YAML ⇒ 在 V20 上对每个真文件都返回空 ⇒ **预检静默失效**。
    /// 所以两种形状都要钉住。
    /// </summary>
    internal static class S7ResScannerTests
    {
        /// <summary>真实样本：V20 `ExportBlocksAsDocuments` 导出的 OP_00_Main.s7res（原样，未改一字）。</summary>
        private const string RealV20S7Res =
@"<root>
  <Comment Id=""MLC_34j"">
    <MultiLanguageText Lang=""de-DE""></MultiLanguageText>
    <MultiLanguageText Lang=""en-US"">Call the Manual  Logic</MultiLanguageText>
    <MultiLanguageText Lang=""zh-CN""></MultiLanguageText>
  </Comment>
</root>";

        private static List<string> ScanYaml(params string[] lines)
            => S7ResScanner.GetMissingEnUsIdsFromLines(lines);

        private static List<string> ScanText(string text)
            => S7ResScanner.GetMissingEnUsIdsFromText(text);

        public static void Run()
        {
            RunYamlShape();
            RunXmlShape();
            RunDispatch();
        }

        // ── V21: YAML ────────────────────────────────────────────────────
        private static void RunYamlShape()
        {
            // 容器缺失 => 不认得这个形状 => 返回空（"不知道"，而不是"全缺"）
            T.Eq("yaml: no container -> empty (unknown shape)", 0, ScanYaml("Foo:", "  - id: X").Count);

            // 有 id 且有 en-US -> 不算缺
            T.Eq("yaml: item with en-US -> not missing", 0,
                 ScanYaml("MultiLingualTexts:", "  - id: MLC_a", "    zh-CN: 起升", "    en-US: Hoist").Count);

            // 缺 en-US -> 报出该 id
            var miss = ScanYaml("MultiLingualTexts:", "  - id: MLC_b", "    zh-CN: 起升");
            T.Eq("yaml: item without en-US -> missing", "MLC_b", miss.Count == 1 ? miss[0] : "<none>");

            // en-US 键在但值为空 -> 仍算缺（空字符串不是"有"）
            var empty = ScanYaml("MultiLingualTexts:", "  - id: MLC_c", "    en-US:   ");
            T.Eq("yaml: empty en-US -> missing", "MLC_c", empty.Count == 1 ? empty[0] : "<none>");

            // 多条只报缺的那条
            var two = ScanYaml("MultiLingualTexts:", "  - id: A", "    en-US: a", "  - id: B", "    zh-CN: b");
            T.Eq("yaml: only the missing one reported", 1, two.Count);
            T.Eq("yaml: ...and it is B", "B", two.Count == 1 ? two[0] : "<none>");

            // 注释与空行忽略
            T.Eq("yaml: comments/blank lines ignored", 0,
                 ScanYaml("MultiLingualTexts:", "# a comment", "", "  - id: D", "    en-US: d").Count);

            // 首行带 UTF-8 BOM 仍要认出容器
            T.Eq("yaml: leading BOM tolerated", 0,
                 ScanYaml("\uFEFFMultiLingualTexts:", "  - id: E", "    en-US: e").Count);

            // 值带引号 -> 去引号后仍是有效 en-US
            T.Eq("yaml: quoted en-US value accepted", 0,
                 ScanYaml("MultiLingualTexts:", "  - id: F", "    en-US: \"Hello\"").Count);
        }

        // ── V20: XML ─────────────────────────────────────────────────────
        private static void RunXmlShape()
        {
            // ★ 真实样本：en-US 有内容 -> 不缺（这是"不该报警"的基准）
            T.Eq("xml(real sample): en-US present -> not missing", 0, ScanText(RealV20S7Res).Count);

            // en-US 被清空 -> 报出该 id（真实场景：翻译丢了）
            var emptied = RealV20S7Res.Replace(">Call the Manual  Logic<", "><");
            var m1 = ScanText(emptied);
            T.Eq("xml: emptied en-US -> missing", "MLC_34j", m1.Count == 1 ? m1[0] : "<none>");

            // en-US 元素整行被删 -> 同样算缺
            // ⚠️ 先把 CRLF 归一化：CI 在 Windows runner 上检出时会把 LF 转成 CRLF，
            //    直接按 "\n" 做替换会匹配不上（曾因此在 CI 红过一次），本用例必须对换行符免疫。
            var lf = RealV20S7Res.Replace("\r\n", "\n");
            var removed = lf.Replace(
                "    <MultiLanguageText Lang=\"en-US\">Call the Manual  Logic</MultiLanguageText>\n", "");
            T.Eq("xml: crlf normalised for the removal case", true, removed.Length < lf.Length);
            var m2 = ScanText(removed);
            T.Eq("xml: en-US element removed -> missing", "MLC_34j", m2.Count == 1 ? m2[0] : "<none>");

            // 单引号属性也要认
            T.Eq("xml: single-quoted attributes accepted", "MLC_q",
                 firstOrNone(ScanText("<root><Comment Id='MLC_q'><MultiLanguageText Lang='en-US'></MultiLanguageText></Comment></root>")));

            // 自闭合的 en-US（无内容）算缺
            T.Eq("xml: self-closing en-US -> missing", "MLC_s",
                 firstOrNone(ScanText("<root><Comment Id=\"MLC_s\"><MultiLanguageText Lang=\"en-US\" /></Comment></root>")));

            // 两个 id，只报缺的那个
            var two = ScanText("<root>"
                             + "<Comment Id=\"MLC_ok\"><MultiLanguageText Lang=\"en-US\">ok</MultiLanguageText></Comment>"
                             + "<Comment Id=\"MLC_bad\"><MultiLanguageText Lang=\"zh-CN\">中文</MultiLanguageText></Comment>"
                             + "</root>");
            T.Eq("xml: only the missing id reported", 1, two.Count);
            T.Eq("xml: ...and it is MLC_bad", "MLC_bad", two.Count == 1 ? two[0] : "<none>");

            // 空块的空资源文件（真实形态 `<root />`）-> 没有 id 元素 -> 空（不误报"全缺"）
            T.Eq("xml: empty <root /> -> nothing to warn", 0, ScanText("\uFEFF<root />").Count);

            // 换行符形态不该影响判定（CI 上真出现过 LF/CRLF 差异）
            T.Eq("xml: CRLF sample behaves the same", 0, ScanText(RealV20S7Res.Replace("\n", "\r\n")).Count);
            T.Eq("xml: CRLF sample missing en-US still reported", "MLC_34j",
                 firstOrNone(ScanText(RealV20S7Res.Replace("\n", "\r\n").Replace(">Call the Manual  Logic<", "><"))));

            // 非 Comment 的 id 容器（如 Title）同样要认
            T.Eq("xml: non-Comment id element handled", "MLC_t",
                 firstOrNone(ScanText("<root><Title Id=\"MLC_t\"><MultiLanguageText Lang=\"zh-CN\">标题</MultiLanguageText></Title></root>")));
        }

        // ── 形状分派 ─────────────────────────────────────────────────────
        private static void RunDispatch()
        {
            // 以 '<' 开头 -> 走 XML；否则走 YAML。两种形状走同一个入口时都不能退化。
            T.Eq("dispatch: xml text recognised", "MLC_x",
                 firstOrNone(ScanText("<root><Comment Id=\"MLC_x\"><MultiLanguageText Lang=\"zh-CN\">中</MultiLanguageText></Comment></root>")));
            T.Eq("dispatch: yaml text still recognised", "MLC_y",
                 firstOrNone(ScanText("MultiLingualTexts:\n  - id: MLC_y\n    zh-CN: 中\n")));
            T.Eq("dispatch: leading BOM + whitespace before xml", 0,
                 ScanText("\uFEFF  \n" + RealV20S7Res).Count);
            T.Eq("dispatch: null/empty -> empty", 0, ScanText("").Count + ScanText(null!).Count);
        }

        private static string firstOrNone(List<string> ids) => ids.Count == 1 ? ids[0] : "<none>";
    }
}
