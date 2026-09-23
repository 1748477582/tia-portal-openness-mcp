using System.Xml.Linq;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// SimaticMlText 把 SimaticML 的 Access / SCL 正文回读成文本。它错了不报错、不崩，
    /// 只会让人看到**根本不存在的逻辑**（旧实现把 ABS(#A - #B) 读成 #A.B）——
    /// 这种"静默回读造假"只有会失败的用例盯得住。
    /// </summary>
    internal static class SimaticMlTextTests
    {
        private static (string text, bool literal) Read(string xml)
            => SimaticMlText.ReadAccess(XElement.Parse(xml));

        public static void Run()
        {
            // 常量：读字面量、标 literal
            var c = Read("<Access Scope=\"LiteralConstant\"><Constant><ConstantValue>42</ConstantValue></Constant></Access>");
            T.Eq("constant value read", "42", c.text);
            T.Check("constant flagged literal", c.literal);

            // 具名常量（只有名字没有值）—— 旧实现一律读成 "?"，两个 MOVE 会长得一模一样
            var nc = Read("<Access Scope=\"TypedConstant\"><Constant Name=\"RUN_FWD\"/></Access>");
            T.Eq("named constant read by name", "#RUN_FWD", nc.text);
            T.Check("named constant flagged literal", nc.literal);

            // 局部符号
            var s = Read("<Access><Symbol><Component Name=\"A\"/></Symbol></Access>");
            T.Eq("local symbol -> #A", "#A", s.text);
            T.Check("symbol not literal", !s.literal);

            // 全局符号：根分量加引号，后续分量裸接
            var g = Read("<Access Scope=\"GlobalVariable\"><Symbol><Component Name=\"DB1\"/><Component Name=\"Speed\"/></Symbol></Access>");
            T.Eq("global symbol quoted root", "\"DB1\".Speed", g.text);

            // 含 '/' 的分量必须加引号，否则 M/A 会被读成除法
            var slash = Read("<Access Scope=\"GlobalVariable\"><Symbol><Component Name=\"M/A\"/></Symbol></Access>");
            T.Eq("non-identifier member quoted", "\"M/A\"", slash.text);

            // 数组下标：[#i]
            var arr = Read("<Access><Symbol><Component Name=\"arr\"><Access><Symbol><Component Name=\"i\"/></Symbol></Access></Component></Symbol></Access>");
            T.Eq("array index replayed", "#arr[#i]", arr.text);

            // 🔴 函数调用：旧实现用 Descendants("Component") 会把参数拼成 #A.B；新实现按 token 流重放
            var call = Read("<Access><Instruction Name=\"ABS\"/><Token Text=\"(\"/><Access><Symbol><Component Name=\"A\"/></Symbol></Access><Token Text=\")\"/></Access>");
            T.Eq("function call replayed, not collapsed", "ABS(#A)", call.text);
            T.NotContains("function call not collapsed to #A.B", call.text, "#A.B");

            // SCL 正文重放
            var unit = XElement.Parse(
                "<CompileUnit><StructuredText><Text>x := </Text>" +
                "<Access><Symbol><Component Name=\"A\"/></Symbol></Access><NewLine/></StructuredText></CompileUnit>");
            T.Contains("structured text replayed", SimaticMlText.RenderStructuredText(unit), "x := #A");

            // 没有 StructuredText -> 空串（不是 "?"）
            T.Eq("no StructuredText -> empty", "", SimaticMlText.RenderStructuredText(XElement.Parse("<CompileUnit/>")));
        }
    }
}
