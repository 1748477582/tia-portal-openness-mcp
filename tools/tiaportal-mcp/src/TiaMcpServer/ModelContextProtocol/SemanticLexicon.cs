using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// 语义打分用的**可配置词表**。
    ///
    /// 过去这些词表是写死在报告打分里的，而且混进了**特定行业**的术语（Gantry / Crane / 大车 / 小车 / 行走）——
    /// 那等于把某一个行业的语境固化进了一个通用工具。这里改成：
    ///   · 默认只放**中性、跨行业**的术语（轴/运动/速度/位置/伺服…、PID/给定/反馈…）；
    ///   · 行业术语**不写进代码**，由使用者 / 用户分析阶段通过环境变量补充：
    ///       `TIA_MCP_SEMANTIC_MOTION_EXTRA`、`TIA_MCP_SEMANTIC_PID_EXTRA`（逗号或分号分隔）。
    ///     例如起重行业自加 "大车,小车,行走"、制药行业自加 "灌装,加塞" —— 都能在**不改工具**的前提下生效。
    ///
    /// 零依赖（只 System.*），所以能被离线套件喂真输入盯住。
    /// </summary>
    internal static class SemanticLexicon
    {
        internal static readonly string[] MotionDefaults =
        {
            "Axis", "Motion", "Servo", "Speed", "Position", "Velocity", "Encoder", "Drive", "Gear", "Cam", "Homing", "Jog",
            "轴", "运动", "速度", "位置", "伺服", "驱动", "编码器", "齿轮", "凸轮", "回零", "点动"
        };

        internal static readonly string[] PidDefaults =
        {
            "PID", "PV", "SP", "Kp", "Ki", "Kd", "Ti", "Td", "OUT", "Setpoint", "Feedback", "Error",
            "输出", "给定", "反馈", "设定值", "测量值", "偏差"
        };

        internal const string MotionExtraEnv = "TIA_MCP_SEMANTIC_MOTION_EXTRA";
        internal const string PidExtraEnv = "TIA_MCP_SEMANTIC_PID_EXTRA";

        private static Regex? _motion;
        private static Regex? _pid;

        internal static bool MatchesMotion(string? text) => !string.IsNullOrEmpty(text) && Motion.IsMatch(text!);
        internal static bool MatchesPid(string? text) => !string.IsNullOrEmpty(text) && Pid.IsMatch(text!);

        private static Regex Motion => _motion ??= Build(MotionDefaults, MotionExtraEnv);
        private static Regex Pid => _pid ??= Build(PidDefaults, PidExtraEnv);

        /// <summary>清除缓存（供测试在改过环境变量后重建）。</summary>
        internal static void ResetCache()
        {
            _motion = null;
            _pid = null;
        }

        internal static Regex Build(IEnumerable<string> defaults, string extraEnvVar)
        {
            var terms = new List<string>(defaults);
            var extra = Environment.GetEnvironmentVariable(extraEnvVar);
            if (!string.IsNullOrWhiteSpace(extra))
            {
                terms.AddRange(extra.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .Where(t => t.Length > 0));
            }

            var parts = terms.Distinct(StringComparer.OrdinalIgnoreCase).Select(TermPattern);
            return new Regex(string.Join("|", parts), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// 短 ASCII 术语（≤3 字母数字）加**词边界**，长术语按子串。
        /// 不加边界时 "Ti" 会在 "Position" 里命中（P-o-s-i-**t-i**-on）——那种假阳性会把无关符号推高分。
        /// </summary>
        private static string TermPattern(string term)
        {
            bool ascii = term.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));
            var escaped = Regex.Escape(term);
            if (ascii && term.Length <= 3)
                return "(?<![A-Za-z0-9])" + escaped + "(?![A-Za-z0-9])";
            return escaped;
        }
    }
}
