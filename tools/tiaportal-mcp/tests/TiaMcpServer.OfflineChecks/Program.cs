using System;
using System.Collections.Generic;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// 极简断言器。没有 xunit 依赖 —— 就是为了能在没装任何东西的 runner 上跑。
    /// 每个检查都必须能失败：HarnessSelfTest 先证明这个断言器抓得住失败，
    /// 否则"全绿"没有任何意义（本仓吃过「检查自己坏了却全绿」的亏）。
    /// </summary>
    internal static class T
    {
        public static int Passed;
        public static int Failed;
        private static bool _probing;
        private static int _probeFails;

        public static void Check(string name, bool ok, string? detail = null)
        {
            if (ok) { Passed++; return; }
            if (_probing) { _probeFails++; return; }
            Failed++;
            Console.WriteLine("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : "  — " + detail));
        }

        public static void Eq<T>(string name, T expected, T actual)
            => Check(name, EqualityComparer<T>.Default.Equals(expected, actual),
                     $"expected <{expected}> got <{actual}>");

        public static void Contains(string name, string haystack, string needle)
            => Check(name, haystack != null && haystack.Contains(needle, StringComparison.Ordinal),
                     $"missing <{needle}> in: {Trim(haystack)}");

        public static void NotContains(string name, string haystack, string needle)
            => Check(name, haystack != null && !haystack.Contains(needle, StringComparison.Ordinal),
                     $"unexpectedly found <{needle}> in: {Trim(haystack)}");

        public static void Throws<TEx>(string name, Action act) where TEx : Exception
        {
            try { act(); Check(name, false, $"expected {typeof(TEx).Name}, nothing thrown"); }
            catch (TEx) { Check(name, true); }
            catch (Exception ex) { Check(name, false, $"expected {typeof(TEx).Name}, got {ex.GetType().Name}"); }
        }

        /// <summary>断言器自检：一个必然为假的检查必须被记为失败。</summary>
        public static bool HarnessSelfTest()
        {
            _probing = true;
            _probeFails = 0;
            Check("__harness_self_test__", false);
            _probing = false;
            return _probeFails == 1;
        }

        private static string Trim(string? s)
        {
            if (s == null) return "<null>";
            s = s.Replace("\r", "").Replace("\n", "\\n");
            return s.Length <= 120 ? s : s.Substring(0, 120) + "…";
        }
    }

    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("TiaMcpServer.OfflineChecks — 零依赖离线自检（不需要 TIA Portal）\n");

            if (!T.HarnessSelfTest())
            {
                Console.WriteLine("[FATAL] 断言器抓不住失败 —— 本次结果不可信。");
                return 2;
            }

            Console.WriteLine("EngineRouter:");
            EngineRouterTests.Run();

            Console.WriteLine("\nLadTextRenderer:");
            LadTextRendererTests.Run();

            Console.WriteLine("\nExportStore:");
            ExportStoreTests.Run();

            Console.WriteLine("\nSimulationProbe:");
            SimulationProbeTests.Run();

            Console.WriteLine($"\n{T.Passed} passed, {T.Failed} failed");
            return T.Failed == 0 ? 0 : 1;
        }
    }
}
