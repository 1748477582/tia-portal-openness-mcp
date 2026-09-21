using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    // ───────────────────────────────────────────────────────────────────────────
    //  仿真能力判定 —— 纯逻辑、零依赖、可单测。取证在 McpServer.Simulation.cs。
    //
    //  为什么要有它：用户机器上"装了 V20 的仿真"与"TIA V20 启动仿真提示未安装"可以**同时为真**，
    //  因为它们是两个不同的产品：
    //    * 经典 S7-PLCSIM —— **逐版本绑定**：TIA V20 的「在线→仿真→启动仿真」要的是 S7-PLCSIM V20。
    //    * PLCSIM Advanced —— 独立产品（独立实例 + 虚拟网卡下载），**装多少版都不满足那个按钮**。
    //  只看"有没有装仿真"必然给出错误答案，所以这里把两者分开、并按 TIA 主版本逐一对齐。
    //
    //  两条纪律（吃过亏）：
    //    1. **证据不全就说 unknown**，不许把"没查到"当成"没有"。本仓已经两次因为搜索范围不全
    //       得出反向结论（漏注册表键、漏共享目录）。
    //    2. **不猜授权**。Automation License Manager 不在探测范围；能不能真跑起来只有真做一次才知道。
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>取证结果（全部为只读探测所得；探测不到的项留空，由 Analyse 判为 unknown）。</summary>
    public sealed class SimulationEvidence
    {
        /// <summary>注册表 Siemens\Automation\_InstalledSW 下的**顶层产品键名**（32/64 两个视图合并）。</summary>
        public IList<string> InstalledProductKeys { get; set; } = new List<string>();
        /// <summary>经典 PLCSIM 的安装目录名（如 "PLCSIM_V18"）。</summary>
        public IList<string> ClassicInstallDirs { get; set; } = new List<string>();
        /// <summary>PLCSIM Advanced 的 API 版本目录（如 "3.0".."7.0"）。</summary>
        public IList<string> AdvancedApiVersions { get; set; } = new List<string>();
        /// <summary>Advanced 的运行时管理器是否存在（Simulation.Runtime.Manager.exe）。</summary>
        public bool AdvancedRuntimePresent { get; set; }
        /// <summary>Advanced 的应用目录是否存在。</summary>
        public bool AdvancedAppPresent { get; set; }
        /// <summary>当前正在跑的仿真相关进程名。</summary>
        public IList<string> RunningSimulationProcesses { get; set; } = new List<string>();
        /// <summary>取证阶段本身失败的说明（读不到注册表之类）。</summary>
        public IList<string> CollectionErrors { get; set; } = new List<string>();
    }

    /// <summary>判定结果。Verdict 取值：none / classic-only / advanced-ready / present-but-unusable / unknown。</summary>
    public sealed class SimulationReport
    {
        public string Verdict { get; set; } = "unknown";
        public IList<int> TiaMajorVersions { get; set; } = new List<int>();
        public IList<int> ClassicPlcsimVersions { get; set; } = new List<int>();
        /// <summary>TIA 主版本 → 该版本的「启动仿真」按钮是否具备同版本经典 PLCSIM。</summary>
        public IDictionary<int, bool> TiaStartSimulationUsable { get; set; } = new Dictionary<int, bool>();
        public bool AdvancedInstalled { get; set; }
        public bool AdvancedUsable { get; set; }
        public IList<string> AdvancedApiVersions { get; set; } = new List<string>();
        public IList<string> Evidence { get; set; } = new List<string>();
        public IList<string> NextActions { get; set; } = new List<string>();
        /// <summary>探测不到 / 判定不了的项 —— 明确列出来，别让读者以为已知。</summary>
        public IList<string> Unknowns { get; set; } = new List<string>();
    }

    public static class SimulationProbe
    {
        private static readonly Regex ClassicKey = new Regex(@"^(?:S7_)?PLCSIM_V(\d{2})$", RegexOptions.IgnoreCase);
        private static readonly Regex ClassicDir = new Regex(@"^PLCSIM_V(\d{2})$", RegexOptions.IgnoreCase);
        private static readonly Regex TiaKey = new Regex(@"^TIAP(\d{2})$", RegexOptions.IgnoreCase);

        public static SimulationReport Analyse(SimulationEvidence? e)
        {
            var r = new SimulationReport();
            if (e == null)
            {
                r.Unknowns.Add("没有取证数据。");
                return r;
            }

            foreach (var err in e.CollectionErrors) r.Unknowns.Add(err);

            // ── 经典 PLCSIM：注册表产品键 ∪ 安装目录名 ─────────────────────
            var classic = new SortedSet<int>();
            foreach (var k in e.InstalledProductKeys)
            {
                var m = ClassicKey.Match((k ?? "").Trim());
                if (m.Success) classic.Add(int.Parse(m.Groups[1].Value));
            }
            foreach (var d in e.ClassicInstallDirs)
            {
                var m = ClassicDir.Match((d ?? "").Trim());
                if (m.Success) classic.Add(int.Parse(m.Groups[1].Value));
            }
            r.ClassicPlcsimVersions = classic.ToList();

            // ── TIA 主版本 ──────────────────────────────────────────────────
            var tia = new SortedSet<int>();
            foreach (var k in e.InstalledProductKeys)
            {
                var m = TiaKey.Match((k ?? "").Trim());
                if (m.Success) tia.Add(int.Parse(m.Groups[1].Value));
            }
            r.TiaMajorVersions = tia.ToList();

            // ── PLCSIM Advanced ────────────────────────────────────────────
            bool advKey = e.InstalledProductKeys.Any(k =>
                string.Equals((k ?? "").Trim(), "PLCSIMADV", StringComparison.OrdinalIgnoreCase));
            r.AdvancedInstalled = advKey || e.AdvancedAppPresent || e.AdvancedApiVersions.Count > 0;
            r.AdvancedApiVersions = e.AdvancedApiVersions
                .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
            // 「可用」= 有 API 程序集 **且** 有运行时管理器。少任何一个都驱动不了 ——
            // 只有 UI 或只有 API 都是残件，那正是用户会看到「装了却用不了」的情形。
            r.AdvancedUsable = r.AdvancedInstalled && r.AdvancedApiVersions.Count > 0 && e.AdvancedRuntimePresent;

            // ── 逐 TIA 版本对齐「启动仿真」按钮 ────────────────────────────
            foreach (var v in r.TiaMajorVersions)
                r.TiaStartSimulationUsable[v] = classic.Contains(v);

            // ── 判定 ───────────────────────────────────────────────────────
            bool noEvidence = e.InstalledProductKeys.Count == 0
                              && e.ClassicInstallDirs.Count == 0
                              && e.AdvancedApiVersions.Count == 0
                              && !e.AdvancedAppPresent;
            if (noEvidence)
            {
                r.Verdict = "unknown";
                r.Unknowns.Add("三项证据（产品键 / 经典安装目录 / Advanced API）全为空 —— 可能是注册表读不到或路径变了，"
                             + "本次判定不可信，请先解决取证问题。");
            }
            else if (!classic.Any() && !r.AdvancedInstalled)
            {
                r.Verdict = "none";
            }
            else if (r.AdvancedUsable)
            {
                r.Verdict = "advanced-ready";
            }
            else if (classic.Any() && !r.AdvancedInstalled)
            {
                r.Verdict = "classic-only";
            }
            else
            {
                r.Verdict = "present-but-unusable";
            }

            // ── 证据（人话）────────────────────────────────────────────────
            r.Evidence.Add(r.ClassicPlcsimVersions.Count > 0
                ? "经典 S7-PLCSIM：V" + string.Join(" / V", r.ClassicPlcsimVersions)
                : "经典 S7-PLCSIM：一个都没探测到");
            r.Evidence.Add(r.TiaMajorVersions.Count > 0
                ? "TIA Portal：V" + string.Join(" / V", r.TiaMajorVersions)
                : "TIA Portal：主版本一个都没探测到");
            r.Evidence.Add(r.AdvancedInstalled
                ? $"PLCSIM Advanced：已安装；API 版本 [{(r.AdvancedApiVersions.Count > 0 ? string.Join(", ", r.AdvancedApiVersions) : "无")}]；"
                  + $"运行时管理器 {(e.AdvancedRuntimePresent ? "在" : "缺失")}"
                : "PLCSIM Advanced：未探测到");
            if (e.RunningSimulationProcesses.Count > 0)
                r.Evidence.Add("正在运行的仿真进程：" + string.Join(", ", e.RunningSimulationProcesses));
            foreach (var kv in r.TiaStartSimulationUsable)
                r.Evidence.Add($"TIA V{kv.Key} 的「启动仿真」：{(kv.Value ? "具备同版本经典 PLCSIM" : "缺同版本经典 PLCSIM")}");

            // ── 下一步（按事实给，不含任何"替你装"的动作）──────────────────
            var missingClassic = r.TiaStartSimulationUsable.Where(kv => !kv.Value).Select(kv => kv.Key).OrderBy(v => v).ToList();
            if (missingClassic.Count > 0)
            {
                r.NextActions.Add("TIA V" + string.Join(" / V", missingClassic)
                    + " 的「在线→仿真→启动仿真」按钮用的是**与 TIA 同版本的经典 S7-PLCSIM**；这些版本缺失。"
                    + "要让该按钮可用，需补装对应版本的 S7-PLCSIM（在 TIA 安装介质里作为可选组件）。");
            }
            if (r.AdvancedUsable)
            {
                r.NextActions.Add("PLCSIM Advanced 可用（另一条路线，不要求与 TIA 版本匹配）：先在 Advanced 里起一个实例，"
                    + "再在 TIA 里把下载目标的接口选成 PLCSIM Virtual Ethernet Adapter，用「下载到设备」——"
                    + "那条路**不经过**「启动仿真」按钮。");
            }
            else if (r.AdvancedInstalled)
            {
                r.NextActions.Add("PLCSIM Advanced 探测到了，但 API 程序集或运行时管理器不全 → 驱动不了。"
                    + "可用它自己的安装程序做 **Repair**，或重装并确认勾选了 API 组件。");
            }
            if (r.Verdict == "none")
                r.NextActions.Add("没有探测到任何仿真软件。装与不装由你决定；本工具不会下载或安装任何东西。");

            r.NextActions.Add("授权未在探测范围内：能不能真跑起来，只有实际起一次实例/下一次载才知道 —— 本工具不猜授权。");
            r.NextActions.Add("本探测全程只读：不下载、不安装、不改注册表、不动任何工程。");
            return r;
        }

        /// <summary>把报告排成人能读的多行文本（工具返回用）。</summary>
        public static IList<string> Format(SimulationReport r)
        {
            var lines = new List<string>
            {
                $"判定：{r.Verdict}",
                ""
            };
            lines.AddRange(r.Evidence.Select(x => "  " + x));
            if (r.Unknowns.Count > 0)
            {
                lines.Add("");
                lines.Add("不确定 / 探不到（别当成已知）：");
                lines.AddRange(r.Unknowns.Select(x => "  ? " + x));
            }
            lines.Add("");
            lines.Add("下一步：");
            lines.AddRange(r.NextActions.Select(x => "  - " + x));
            return lines;
        }
    }
}
