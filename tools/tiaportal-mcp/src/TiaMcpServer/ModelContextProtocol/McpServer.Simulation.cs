using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    // ───────────────────────────────────────────────────────────────────────────
    //  仿真能力探针的**取证 + 接线**层。判定逻辑在零依赖的 SimulationProbe.cs（那份能单测）。
    //
    //  取证只做三件事：读注册表、看文件、看进程。**不写任何东西、不启动任何东西。**
    //
    //  ⚠ 注册表必须 32/64 **两个视图都读**：西门子这些产品键落在 WOW6432Node（32 位视图）下，
    //  而本引擎是 x64 进程 —— 只读默认视图会一个产品都看不到，然后得出"什么都没装"的
    //  **反向错误结论**。本仓已经因为"只搜一层/只搜一处"踩过两次，这里写死两个视图。
    // ───────────────────────────────────────────────────────────────────────────
    public static partial class McpServer
    {
        [McpServerTool(Name = "ProbeSimulationCapability"), Description(
            "[L1][Diagnostics] READ-ONLY probe of the simulation stack on this machine. It separates the two "
            + "products everyone confuses: classic S7-PLCSIM (version-locked to TIA — this is what the "
            + "'Online > Simulation > Start simulation' button needs) and S7-PLCSIM Advanced (independent "
            + "instances plus virtual-adapter download — that button never uses it, no matter which version "
            + "is installed). Reports the installed classic PLCSIM versions, per TIA version whether 'Start "
            + "simulation' can work, whether Advanced is installed AND drivable (API assemblies + runtime "
            + "manager present), what to do next, and an explicit 'unknown' list when the evidence is "
            + "incomplete. It NEVER downloads, installs, repairs or guesses licensing — installing anything "
            + "is the user's decision.")]
        public static ResponseStringList ProbeSimulationCapability()
        {
            var evidence = CollectSimulationEvidence();
            var report = SimulationProbe.Analyse(evidence);
            Logger?.LogInformation("ProbeSimulationCapability: verdict={Verdict} classic=[{Classic}] advanced={Advanced}",
                report.Verdict, string.Join(",", report.ClassicPlcsimVersions), report.AdvancedInstalled);
            return new ResponseStringList
            {
                Message = $"判定：{report.Verdict}（只读探测：未安装、未修改任何东西）",
                Items = SimulationProbe.Format(report),
                Meta = new JsonObject
                {
                    ["ok"] = true,
                    ["verdict"] = report.Verdict,
                    ["readOnly"] = true,
                    ["classicPlcsimVersions"] = new JsonArray(report.ClassicPlcsimVersions
                        .Select(v => (JsonNode)JsonValue.Create(v)).ToArray()),
                    ["tiaMajorVersions"] = new JsonArray(report.TiaMajorVersions
                        .Select(v => (JsonNode)JsonValue.Create(v)).ToArray()),
                    ["advancedInstalled"] = report.AdvancedInstalled,
                    ["advancedUsable"] = report.AdvancedUsable
                }
            };
        }

        /// <summary>只读取证。任何一步失败都记进 CollectionErrors，绝不抛 —— 探针坏了也要给出"为什么坏"。</summary>
        internal static SimulationEvidence CollectSimulationEvidence()
        {
            var e = new SimulationEvidence();

            // ── 1) 注册表产品键（两个视图都读）─────────────────────────────
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = baseKey.OpenSubKey(@"SOFTWARE\Siemens\Automation\_InstalledSW"))
                    {
                        if (k == null)
                        {
                            e.CollectionErrors.Add($"注册表 {view} 视图下没有 _InstalledSW（该视图可能确实没有）。");
                            continue;
                        }
                        foreach (var name in k.GetSubKeyNames())
                            if (!e.InstalledProductKeys.Contains(name)) e.InstalledProductKeys.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    e.CollectionErrors.Add($"读注册表 {view} 视图失败：{ex.GetType().Name} {ex.Message}");
                }
            }

            var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var searchRoots = new[] { pf64, pf86 }.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();

            // ── 2) 经典 PLCSIM 的安装目录 ──────────────────────────────────
            foreach (var pf in searchRoots)
            {
                try
                {
                    var dir = Path.Combine(pf, "Siemens", "Automation");
                    if (!Directory.Exists(dir)) continue;
                    foreach (var d in Directory.GetDirectories(dir))
                    {
                        var n = Path.GetFileName(d);
                        if (n.StartsWith("PLCSIM", StringComparison.OrdinalIgnoreCase)
                            && !e.ClassicInstallDirs.Contains(n)) e.ClassicInstallDirs.Add(n);
                    }
                }
                catch (Exception ex)
                {
                    e.CollectionErrors.Add($"枚举 {pf}\\Siemens\\Automation 失败：{ex.GetType().Name} {ex.Message}");
                }
            }

            // ── 3) PLCSIM Advanced：应用目录 / API 程序集 / 运行时管理器 ────
            foreach (var pf in searchRoots)
            {
                try
                {
                    if (Directory.Exists(Path.Combine(pf, "Siemens", "Automation", "PLCSIMADV")))
                        e.AdvancedAppPresent = true;

                    var advRoot = Path.Combine(pf, "Common Files", "Siemens", "PLCSIMADV");
                    if (!Directory.Exists(advRoot)) continue;

                    if (File.Exists(Path.Combine(advRoot, "Siemens.Simatic.Simulation.Runtime.Manager.exe")))
                        e.AdvancedRuntimePresent = true;

                    var apiRoot = Path.Combine(advRoot, "API");
                    if (Directory.Exists(apiRoot))
                    {
                        foreach (var v in Directory.GetDirectories(apiRoot))
                        {
                            var n = Path.GetFileName(v);
                            if (!e.AdvancedApiVersions.Contains(n)) e.AdvancedApiVersions.Add(n);
                        }
                    }
                }
                catch (Exception ex)
                {
                    e.CollectionErrors.Add($"枚举 {pf}\\Common Files\\Siemens 失败：{ex.GetType().Name} {ex.Message}");
                }
            }

            // ── 4) 正在跑的仿真进程（"装了"≠"在跑"）────────────────────────
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var n = p.ProcessName ?? "";
                        if (n.IndexOf("plcsim", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("simulation", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (!e.RunningSimulationProcesses.Contains(n)) e.RunningSimulationProcesses.Add(n);
                        }
                    }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                e.CollectionErrors.Add($"枚举进程失败：{ex.GetType().Name} {ex.Message}");
            }

            return e;
        }
    }
}
