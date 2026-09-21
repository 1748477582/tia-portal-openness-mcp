using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.OfflineChecks
{
    /// <summary>
    /// 仿真能力判定。这块最危险的失败方式不是崩，而是**给出一个看起来很确定的错误结论**：
    /// 把"没查到"当成"没有"、把 Advanced 当成经典 PLCSIM、把"装了"当成"能用"。
    /// 所以用例重点是：证据不全时必须说 unknown、两种产品必须分开、逐 TIA 版本必须对齐。
    /// </summary>
    internal static class SimulationProbeTests
    {
        private static SimulationEvidence RealMachine() => new SimulationEvidence
        {
            // = 本机 2026-09-21 实测（只读探测所得）
            InstalledProductKeys = new List<string>
            {
                "PLCSIM_V18", "S7_PLCSIM_V16", "PLCSIMADV",
                "TIAP12", "TIAP16", "TIAP18", "TIAP20"
            },
            ClassicInstallDirs = new List<string> { "PLCSIM_V18", "PLCSIMADV" },
            AdvancedApiVersions = new List<string> { "3.0", "4.0", "4.1", "5.0", "6.0", "7.0" },
            AdvancedRuntimePresent = true,
            AdvancedAppPresent = true,
        };

        public static void Run()
        {
            // ── 本机实况（用户报"TIA V20 启动仿真提示未安装"的那台）──────────
            var r = SimulationProbe.Analyse(RealMachine());
            T.Eq("real machine: classic versions", "16,18", string.Join(",", r.ClassicPlcsimVersions));
            T.Eq("real machine: TIA versions", "12,16,18,20", string.Join(",", r.TiaMajorVersions));
            T.Eq("real machine: verdict", "advanced-ready", r.Verdict);
            T.Eq("real machine: V16 start-simulation usable", true, r.TiaStartSimulationUsable[16]);
            T.Eq("real machine: V18 start-simulation usable", true, r.TiaStartSimulationUsable[18]);
            T.Eq("real machine: V20 start-simulation NOT usable", false, r.TiaStartSimulationUsable[20]);
            T.Eq("real machine: V12 start-simulation NOT usable", false, r.TiaStartSimulationUsable[12]);
            T.Check("real machine: advanced drivable (api+runtime)",
                    r.AdvancedUsable, "expected usable");
            T.Check("real machine: next actions call out V20",
                    r.NextActions.Any(a => a.Contains("V20")), "V20 not mentioned");
            // "PLCSIMADV" 目录名不能被当成经典 PLCSIM 的版本号
            T.Check("advanced dir name is not parsed as a classic version",
                    !r.ClassicPlcsimVersions.Contains(0) && r.ClassicPlcsimVersions.Count == 2,
                    string.Join(",", r.ClassicPlcsimVersions));

            // ── 只有经典 ──────────────────────────────────────────────────
            var onlyClassic = SimulationProbe.Analyse(new SimulationEvidence
            {
                InstalledProductKeys = new List<string> { "S7_PLCSIM_V16", "TIAP16" },
            });
            T.Eq("classic-only verdict", "classic-only", onlyClassic.Verdict);
            T.Eq("classic-only: V16 usable", true, onlyClassic.TiaStartSimulationUsable[16]);
            T.Check("classic-only: advanced not installed", !onlyClassic.AdvancedInstalled);

            // ── 有 TIA 但没有任何仿真 ─────────────────────────────────────
            var none = SimulationProbe.Analyse(new SimulationEvidence
            {
                InstalledProductKeys = new List<string> { "TIAP20" },
            });
            T.Eq("tia-but-no-simulation verdict", "none", none.Verdict);
            T.Eq("tia-but-no-simulation: V20 not usable", false, none.TiaStartSimulationUsable[20]);
            T.Check("none: tells the user installing is their call",
                    none.NextActions.Any(a => a.Contains("装与不装由你决定")));

            // ── Advanced 残件：有键有 UI，但驱动不了 ───────────────────────
            var partialApi = SimulationProbe.Analyse(new SimulationEvidence
            {
                InstalledProductKeys = new List<string> { "PLCSIM_V18", "PLCSIMADV", "TIAP18" },
                AdvancedAppPresent = true,
                AdvancedApiVersions = new List<string>(),      // 没有 API 程序集
                AdvancedRuntimePresent = false,
            });
            T.Eq("advanced without API -> present-but-unusable", "present-but-unusable", partialApi.Verdict);
            T.Check("advanced without API is not 'usable'", !partialApi.AdvancedUsable);
            T.Check("advises repair/reinstall instead of installing for them",
                    partialApi.NextActions.Any(a => a.Contains("Repair")) &&
                    partialApi.NextActions.All(a => !a.Contains("我来装")));

            var partialRuntime = SimulationProbe.Analyse(new SimulationEvidence
            {
                InstalledProductKeys = new List<string> { "PLCSIMADV", "TIAP18" },
                AdvancedApiVersions = new List<string> { "7.0" },
                AdvancedRuntimePresent = false,                // 有 API，没有运行时
            });
            T.Eq("advanced without runtime -> present-but-unusable", "present-but-unusable", partialRuntime.Verdict);

            // ── 证据全空：必须说 unknown，不许说 none ──────────────────────
            var blind = SimulationProbe.Analyse(new SimulationEvidence());
            T.Eq("no evidence at all -> unknown (never 'none')", "unknown", blind.Verdict);
            T.Check("unknown carries an explicit reason", blind.Unknowns.Count > 0);

            // ── 取证阶段报的错要冒到 Unknowns，不能被吞 ─────────────────────
            var withErr = SimulationProbe.Analyse(new SimulationEvidence
            {
                InstalledProductKeys = new List<string> { "TIAP20" },
                CollectionErrors = new List<string> { "读注册表 Registry64 视图失败：SecurityException" },
            });
            T.Check("collection errors surface as unknowns",
                    withErr.Unknowns.Any(u => u.Contains("Registry64")));

            // ── 只用目录名（注册表读不到时的兜底）也要能认出来 ─────────────
            var dirsOnly = SimulationProbe.Analyse(new SimulationEvidence
            {
                ClassicInstallDirs = new List<string> { "PLCSIM_V18" },
                AdvancedAppPresent = true,
                AdvancedApiVersions = new List<string> { "7.0" },
                AdvancedRuntimePresent = true,
            });
            T.Eq("dir-only evidence detects classic 18", "18", string.Join(",", dirsOnly.ClassicPlcsimVersions));
            T.Check("dir-only evidence is not treated as 'no evidence'", dirsOnly.Verdict != "unknown");

            // ── 纪律断言：永远不猜授权、永远声明只读 ───────────────────────
            T.Check("always states licensing was not probed",
                    r.NextActions.Any(a => a.Contains("授权")));
            T.Check("always states the probe is read-only",
                    r.NextActions.Any(a => a.Contains("只读")));

            // ── 输出形状 ──────────────────────────────────────────────────
            var lines = SimulationProbe.Format(r);
            T.Check("format starts with the verdict", lines[0].StartsWith("判定："), lines[0]);
            T.Check("format includes next actions section", lines.Any(l => l.Trim() == "下一步："));
            var blindLines = SimulationProbe.Format(blind);
            T.Check("format surfaces unknowns for the blind case",
                    blindLines.Any(l => l.Contains("不确定")));
        }
    }
}
