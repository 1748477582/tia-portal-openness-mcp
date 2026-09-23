using System.Collections.Generic;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// S7ResScanner 扫 .s7res（YAML）里缺 en-US 的 MultiLingualText id。
    /// 它错了不会抛：要么"什么都没缺"（放过真缺的），要么"全缺"（乱警告）。
    /// 旧实现把 YAML 当 XML 喂给 XDocument.Load，每个真文件都抛、预检从未报过警 —— 用真形状钉住。
    /// </summary>
    internal static class S7ResScannerTests
    {
        private static List<string> Scan(params string[] lines)
            => S7ResScanner.GetMissingEnUsIdsFromLines(lines);

        public static void Run()
        {
            // 容器缺失 => 不认得这个形状 => 返回空（"不知道"，而不是"全缺"）
            T.Eq("no container -> empty (unknown shape)", 0, Scan("Foo:", "  - id: X").Count);

            // 有 id 且有 en-US -> 不算缺
            T.Eq("item with en-US -> not missing", 0,
                 Scan("MultiLingualTexts:", "  - id: MLC_a", "    zh-CN: 起升", "    en-US: Hoist").Count);

            // 缺 en-US -> 报出该 id
            var miss = Scan("MultiLingualTexts:", "  - id: MLC_b", "    zh-CN: 起升");
            T.Eq("item without en-US -> missing", "MLC_b", miss.Count == 1 ? miss[0] : "<none>");

            // en-US 键在但值为空 -> 仍算缺（空字符串不是"有"）
            var empty = Scan("MultiLingualTexts:", "  - id: MLC_c", "    en-US:   ");
            T.Eq("empty en-US -> missing", "MLC_c", empty.Count == 1 ? empty[0] : "<none>");

            // 多条只报缺的那条
            var two = Scan("MultiLingualTexts:", "  - id: A", "    en-US: a", "  - id: B", "    zh-CN: b");
            T.Eq("only the missing one reported", 1, two.Count);
            T.Eq("...and it is B", "B", two.Count == 1 ? two[0] : "<none>");

            // 注释与空行忽略
            T.Eq("comments/blank lines ignored", 0,
                 Scan("MultiLingualTexts:", "# a comment", "", "  - id: D", "    en-US: d").Count);

            // 首行带 UTF-8 BOM 仍要认出容器
            T.Eq("leading BOM tolerated", 0,
                 Scan("\uFEFFMultiLingualTexts:", "  - id: E", "    en-US: e").Count);

            // 值带引号 -> 去引号后仍是有效 en-US
            T.Eq("quoted en-US value accepted", 0,
                 Scan("MultiLingualTexts:", "  - id: F", "    en-US: \"Hello\"").Count);
        }
    }
}
