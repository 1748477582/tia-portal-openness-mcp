using System;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// LadTextRenderer 把导出的 SimaticML 还原成人能读的文本。它错了不会抛异常，
    /// 只会让人看到"不存在的逻辑"或"空白的网络" —— 所以错误路径与正常路径都要盯。
    /// </summary>
    internal static class LadTextRendererTests
    {
        public static void Run()
        {
            // ---- 解析失败必须明说，不能假装是空块 ----
            T.Contains("invalid xml -> explicit parse error",
                       LadTextRenderer.Render("this is not xml"),
                       "Could not parse block XML");

            // ---- 没有程序段：如实说"没有"，而不是返回空串 ----
            T.Contains("no CompileUnit -> says so",
                       LadTextRenderer.Render("<Document><Foo/></Document>"),
                       "No LAD/SCL networks found");

            // ---- 一个 SCL 程序段 ----
            var scl = RenderUnits("SCL");
            T.Contains("unit header numbered", scl, "── 程序段 1");
            T.Contains("language tag shown", scl, "[SCL]");
            T.Contains("empty SCL body reported", scl, "(无代码或纯声明)");
            T.NotContains("a real unit is NOT reported as 'no networks'", scl, "No LAD/SCL networks found");

            // ---- 多个程序段要分别编号（漏了会让人以为只有一段）----
            T.Contains("second unit numbered 2", RenderUnits("SCL", "SCL"), "── 程序段 2");

            // ---- 不认识的语言要明说，不能静默略过 ----
            T.Contains("unsupported language reported",
                       RenderUnits("GRAPH"),
                       "(无 FlgNet / 不支持的语言)");

            // ---- 反向哨兵：解析成功的文档不该带错误前缀 ----
            T.NotContains("valid doc has no parse-error prefix", scl, "Could not parse block XML");
        }

        private static string RenderUnits(params string[] languages)
        {
            var sb = new System.Text.StringBuilder("<Document>");
            int id = 1;
            foreach (var lang in languages)
            {
                sb.Append($"<SW.Blocks.CompileUnit ID=\"{id}\"><AttributeList>");
                sb.Append($"<ProgrammingLanguage>{lang}</ProgrammingLanguage>");
                sb.Append("</AttributeList></SW.Blocks.CompileUnit>");
                id++;
            }
            sb.Append("</Document>");
            return LadTextRenderer.Render(sb.ToString());
        }
    }
}
