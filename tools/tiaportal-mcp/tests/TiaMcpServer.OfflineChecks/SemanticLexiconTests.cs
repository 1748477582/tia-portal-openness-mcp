using System;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// SemanticLexicon 是通用化的关键：默认词表**只能**放跨行业的中性术语，行业词一律走环境变量注入。
    /// 这条边界如果被悄悄写回代码（比如又有人把某个行业的词硬编码进去），工具就不再通用 —— 所以正反两向都要钉。
    /// </summary>
    internal static class SemanticLexiconTests
    {
        public static void Run()
        {
            SemanticLexicon.ResetCache();

            // ---- 中性默认：应当命中 ----
            T.Check("motion: Axis", SemanticLexicon.MatchesMotion("Axis1"));
            T.Check("motion: Speed", SemanticLexicon.MatchesMotion("DB1.Motor_Speed"));
            T.Check("motion: 位置(中文)", SemanticLexicon.MatchesMotion("轴1位置"));
            T.Check("pid: PID", SemanticLexicon.MatchesPid("PID_Controller"));
            T.Check("pid: 反馈(中文)", SemanticLexicon.MatchesPid("速度反馈"));

            // ---- 🔴 默认词表**不得**含任何行业专有词（这些符号里不含任何中性术语）----
            T.Check("NO industry term: 大车", !SemanticLexicon.MatchesMotion("大车"));
            T.Check("NO industry term: 小车", !SemanticLexicon.MatchesMotion("小车"));
            T.Check("NO industry term: 行走", !SemanticLexicon.MatchesMotion("行走机构"));
            T.Check("NO industry term: Crane", !SemanticLexicon.MatchesMotion("CraneX"));
            T.Check("NO industry term: Gantry", !SemanticLexicon.MatchesMotion("Gantry"));

            // ---- 短术语要带词边界："Ti" 不能命中 "Position" 里的 t-i ----
            T.Check("short term boundary: Position is NOT pid", !SemanticLexicon.MatchesPid("Position"));
            T.Check("short term boundary: Ti IS pid", SemanticLexicon.MatchesPid("Ti"));
            T.Check("short term boundary: Kp_x IS pid", SemanticLexicon.MatchesPid("Kp_x"));

            // ---- 扩展口：行业词由环境变量注入，代码不用改 ----
            try
            {
                Environment.SetEnvironmentVariable(SemanticLexicon.MotionExtraEnv, "大车,小车,行走,Crane");
                SemanticLexicon.ResetCache();
                T.Check("extra motion term injected: 大车", SemanticLexicon.MatchesMotion("大车"));
                T.Check("extra motion term injected: Crane", SemanticLexicon.MatchesMotion("Crane"));
                T.Check("extra does not leak into PID", !SemanticLexicon.MatchesPid("Crane"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(SemanticLexicon.MotionExtraEnv, null);
                SemanticLexicon.ResetCache();
            }

            // ---- 边界：null / 空 ----
            T.Check("null -> no match", !SemanticLexicon.MatchesMotion(null) && !SemanticLexicon.MatchesPid(null));
            T.Check("empty -> no match", !SemanticLexicon.MatchesMotion("") && !SemanticLexicon.MatchesPid(""));
        }
    }
}
