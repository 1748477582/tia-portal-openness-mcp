using System;
using System.Collections.Generic;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// 参数诊断。这块坏了不会崩 —— 只会让"参数写错"退回成一句零信息失败
    /// （"An error occurred invoking X."），或者反过来把本来能跑的调用拦下来。
    /// 两种都是"看起来正常"的坏，所以两条方向都要有用例钉住。
    /// </summary>
    internal static class ArgDiagnosticsTests
    {
        public static void Run()
        {
            var known = new List<string> { "softwarePath", "tableName", "address", "modifyValue", "trigger" };
            var required = new List<string> { "softwarePath", "tableName", "address", "modifyValue" };
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["softwarePath"] = "string", ["tableName"] = "string", ["address"] = "string",
                ["modifyValue"] = "string", ["trigger"] = "string",
            };

            // ── 正确的调用必须**完全沉默**（不许在正常路径上多说一句）─────────
            T.Eq("correct call is silent", "",
                ArgDiagnostics.Check("SetWatchTableModifyValue", known, required,
                    new List<string> { "softwarePath", "tableName", "address", "modifyValue" }, types));
            T.Eq("correct call with optional is silent", "",
                ArgDiagnostics.Check("SetWatchTableModifyValue", known, required,
                    new List<string> { "softwarePath", "tableName", "address", "modifyValue", "trigger" }, types));

            // ── 少传必填：点名 + 给签名 + 给"什么都没执行"的保证 ─────────────
            var miss = ArgDiagnostics.Check("SetWatchTableModifyValue", known, required,
                new List<string> { "softwarePath", "address", "modifyValue" }, types);
            T.Check("missing names the missing arg", miss.Contains("tableName"), miss);
            T.Check("missing is labelled as required", miss.Contains("missing required"), miss);
            T.Check("missing shows the signature", miss.Contains("Expected signature"), miss);
            T.Check("missing promises nothing ran", miss.Contains("nothing was executed"), miss);

            // ── 参数名写错：必须点破"会被静默忽略"，并给纠名 ────────────────
            var typo = ArgDiagnostics.Check("SetWatchTableModifyValue", known, required,
                new List<string> { "softwarePath", "tabelName", "address", "modifyValue" }, types);
            T.Check("typo is reported as silently ignored", typo.Contains("SILENTLY IGNORED"), typo);
            T.Check("typo gets a did-you-mean", typo.Contains("Did you mean") && typo.Contains("tabelName -> tableName"), typo);

            // ── 判不了就不拦：known 为空 = schema 读不出来 ─────────────────
            T.Eq("unreadable schema never blocks", "",
                ArgDiagnostics.Check("Whatever", new List<string>(), new List<string>(),
                    new List<string> { "anyOldThing" }, null));
            T.Eq("null known never blocks", "",
                ArgDiagnostics.Check("Whatever", null, null, new List<string> { "x" }, null));

            // ── 大小写不敏感（client 常把 camelCase 写错大小写）─────────────
            T.Eq("case-insensitive match is silent", "",
                ArgDiagnostics.Check("T", known, required,
                    new List<string> { "SOFTWAREPATH", "tablename", "Address", "modifyvalue" }, types));

            // ── 签名渲染：必填无 ?、可选带 ?、类型跟着写 ────────────────────
            var sig = ArgDiagnostics.RenderSignature("SetWatchTableModifyValue", known, required, types);
            T.Check("signature marks required without '?'", sig.Contains("softwarePath: string"), sig);
            T.Check("signature marks optional with '?'", sig.Contains("trigger?: string"), sig);

            // ── 纠名要保守：够不着就别硬猜 ────────────────────────────────
            T.Eq("close typo gets a suggestion", "tableName",
                ArgDiagnostics.NearestName("tabelName", known) ?? "<null>");
            T.Eq("far-off name gets no suggestion", "<null>",
                ArgDiagnostics.NearestName("completelyDifferentThing", known) ?? "<null>");
            T.Eq("empty candidate gets no suggestion", "<null>",
                ArgDiagnostics.NearestName("", known) ?? "<null>");

            // ── Levenshtein 基本正确性 ────────────────────────────────────
            T.Eq("distance identical", 0, ArgDiagnostics.Distance("abc", "abc"));
            T.Eq("distance insert", 1, ArgDiagnostics.Distance("abc", "abcd"));
            T.Eq("distance delete", 1, ArgDiagnostics.Distance("abcd", "abc"));
            T.Eq("distance substitute", 1, ArgDiagnostics.Distance("abc", "abd"));
            T.Eq("distance empty vs nonempty", 3, ArgDiagnostics.Distance("", "abc"));
            T.Eq("distance classic kitten/sitting", 3, ArgDiagnostics.Distance("kitten", "sitting"));

            // ── 同时少传又写错：两件事都要说 ──────────────────────────────
            var both = ArgDiagnostics.Check("T", known, required,
                new List<string> { "softwarePath", "adress", "modifyValue" }, types);
            T.Check("both problems reported (missing)", both.Contains("tableName"), both);
            T.Check("both problems reported (unknown)", both.Contains("adress"), both);
        }
    }
}
