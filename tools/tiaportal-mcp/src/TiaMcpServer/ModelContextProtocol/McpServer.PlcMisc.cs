using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using TiaMcpServer.Siemens;


namespace TiaMcpServer.ModelContextProtocol
{
    // Partial: global-library / release-reporting / device-attributes. Split out of McpServer.PlcSoftware.cs (god-file split); behavior unchanged.
    public static partial class McpServer
    {
        #region plc misc

        [McpServerTool(Name = "BuildPlcSymbolManifestFromXmlPath"), Description("[L2][PLC-Builders]Offline-only helper: extract a PLC symbol manifest from PLC tag table and GlobalDB XML files or directories. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildPlcSymbolManifestFromXmlPath(
            [Description("path: XML file or directory containing PLC tag table / GlobalDB XML exports.")] string path)
        {
            try
            {
                var data = PlcSymbolManifestBuilder.BuildFromXmlPath(path);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "PLC symbol manifest built offline" : "PLC symbol manifest built with findings",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true,
                        ["symbolCount"] = data["symbolCount"]?.GetValue<int>() ?? 0
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building PLC symbol manifest offline: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "RunOfflineReleaseValidationSuite"), Description("[L2][Reports]Offline-only helper: run the release smoke suite covering PLC Builder, Classic HMI, PLC symbol extraction, Unified HMI template layout, HMI action recipes, and online-monitoring safety guardrails. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport RunOfflineReleaseValidationSuite(
            [Description("workspaceRoot: repository/workspace root containing TMP_EXPORT, docs, and tools.")] string workspaceRoot,
            [Description("reportDirectory: directory where suite files and reports will be written.")] string reportDirectory)
        {
            try
            {
                var data = OfflineReleaseValidationSuite.Run(workspaceRoot, reportDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Offline release validation suite passed" : "Offline release validation suite found issues",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error running offline release validation suite: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "RunV2PlanCompletionAudit"), Description("[L2][Reports]Offline-only strict audit for docs/TIA_MCP_常见操作全覆盖方案_V2_二次优化计划.md. It reports verified hard-gate percentage and blocks 100% claims when real TIA/online evidence is missing.")]
        public static ResponseJsonReport RunV2PlanCompletionAudit(
            [Description("workspaceRoot: repository/workspace root containing docs, tools, and reports.")] string workspaceRoot,
            [Description("reportDirectory: directory where V2 audit reports will be written.")] string reportDirectory)
        {
            try
            {
                var data = V2PlanCompletionAuditor.Run(workspaceRoot, reportDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = "V2 plan completion audit finished",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error running V2 plan completion audit: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "BuildReleaseDiagnosticReport"), Description("[L2][Reports]Build an offline diagnostic report from a previously generated OfflineReleaseValidationSuite JSON report. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildReleaseDiagnosticReport(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(offlineReleaseSuiteJsonPath) || !File.Exists(offlineReleaseSuiteJsonPath))
                    throw new FileNotFoundException("Offline release suite JSON report not found.", offlineReleaseSuiteJsonPath);
                var root = JsonNode.Parse(File.ReadAllText(offlineReleaseSuiteJsonPath)) as JsonObject
                    ?? throw new InvalidOperationException("Offline release suite JSON root must be an object.");
                var data = ReleaseDiagnosticReportBuilder.Build(root);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release diagnostic report built.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building release diagnostic report: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "BuildReleaseRunbook"), Description("[L2][Reports]Build an offline first-user runbook from a previously generated OfflineReleaseValidationSuite JSON report. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildReleaseRunbook(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(offlineReleaseSuiteJsonPath) || !File.Exists(offlineReleaseSuiteJsonPath))
                    throw new FileNotFoundException("Offline release suite JSON report not found.", offlineReleaseSuiteJsonPath);
                var root = JsonNode.Parse(File.ReadAllText(offlineReleaseSuiteJsonPath)) as JsonObject
                    ?? throw new InvalidOperationException("Offline release suite JSON root must be an object.");
                var diagnostics = root["diagnostics"] as JsonObject ?? ReleaseDiagnosticReportBuilder.Build(root);
                var data = ReleaseRunbookBuilder.Build(root, diagnostics);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release runbook built.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building release runbook: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "BuildReleaseManifest"), Description("[L2][Reports]Build an offline machine-readable release manifest from a previously generated OfflineReleaseValidationSuite JSON report. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildReleaseManifest(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(offlineReleaseSuiteJsonPath) || !File.Exists(offlineReleaseSuiteJsonPath))
                    throw new FileNotFoundException("Offline release suite JSON report not found.", offlineReleaseSuiteJsonPath);
                var root = JsonNode.Parse(File.ReadAllText(offlineReleaseSuiteJsonPath)) as JsonObject
                    ?? throw new InvalidOperationException("Offline release suite JSON root must be an object.");
                var diagnostics = root["diagnostics"] as JsonObject ?? ReleaseDiagnosticReportBuilder.Build(root);
                var runbook = root["runbook"] as JsonObject ?? ReleaseRunbookBuilder.Build(root, diagnostics);
                var data = ReleaseManifestBuilder.Build(root, diagnostics, runbook);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release manifest built.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building release manifest: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "RebuildReleaseHandoffArtifacts"), Description("[L2][Reports]Rebuild diagnostics, runbook, and manifest files from an existing OfflineReleaseValidationSuite JSON report. Offline-only and does not connect to TIA Portal.")]
        public static ResponseJsonReport RebuildReleaseHandoffArtifacts(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath,
            [Description("outputDirectory: directory where rebuilt handoff artifacts will be written.")] string outputDirectory)
        {
            try
            {
                var data = ReleaseHandoffArtifactBuilder.RebuildFromSuiteJson(offlineReleaseSuiteJsonPath, outputDirectory);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release handoff artifacts rebuilt.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error rebuilding release handoff artifacts: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        private static int GetManifestInt(JsonObject? obj, string name)
        {
            if (obj == null || obj[name] == null) return 0;
            return int.TryParse(obj[name]?.ToString(), out var value) ? value : 0;
        }

        [McpServerTool(Name = "ProbeGlobalLibrary"), Description("[L2][HMI-Library]Open a TIA global library (.al21) read-only/best-effort and list accessible master copies/types/folders through public/reflection APIs. It does not import library content.")]
        public static ResponseGlobalLibraryProbe ProbeGlobalLibrary(
            [Description("libraryPath: full path to .al21 file or its containing folder")] string libraryPath,
            [Description("maxItems: maximum items per list")] int maxItems = 500)
        {
            try
            {
                var result = Portal.ProbeGlobalLibrary(libraryPath, maxItems);
                result.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = result.Ok == true };
                return result;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error probing global library: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportMasterCopyFromGlobalLibrary"), Description("[L2][HMI-Library] Import one MasterCopy from a TIA global library into a real Unified HMI screen and return ScreenItems readback evidence. This modifies the project, must be tried in a temporary project first, and reports failure unless the imported item is visible after readback.")]
        public static ResponseGlobalLibraryImport ImportMasterCopyFromGlobalLibrary(
            [Description("libraryPath: full path to .al21 file or its containing folder")] string libraryPath,
            [Description("masterCopyName: exact or suffix path/name from ProbeGlobalLibrary MasterCopies readback")] string masterCopyName,
            [Description("hmiSoftwarePath: real Unified HMI software path resolved from GetProjectTree, e.g. HMI_RT_1")] string hmiSoftwarePath,
            [Description("screenName: existing target Unified screen name; create it first with EnsureUnifiedHmiScreen if needed")] string screenName,
            [Description("importedItemName: optional expected item name after import; empty means use masterCopyName leaf")] string importedItemName = "",
            [Description("left: optional Left coordinate applied after import when supported")] int left = 0,
            [Description("top: optional Top coordinate applied after import when supported")] int top = 0)
        {
            try
            {
                var result = Portal.ImportMasterCopyFromGlobalLibrary(libraryPath, masterCopyName, hmiSoftwarePath, screenName, importedItemName, left, top);
                result.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = result.Ok == true };
                return result;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing global-library master copy: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "AnalyzeGlobalLibraryPackage"), Description("[L2][HMI-Library]Analyze a TIA global library folder offline by file-system structure. It does not connect to TIA Portal, open the library, import content, or modify files.")]
        public static ResponseJsonReport AnalyzeGlobalLibraryPackage(
            [Description("libraryPath: global library folder path or .al* file path")] string libraryPath)
        {
            try
            {
                var data = GlobalLibraryPackageAnalyzer.Analyze(libraryPath);
                data["timestamp"] = DateTime.Now.ToString("O");
                data["safetyPolicy"] = new JsonObject
                {
                    ["mode"] = "Offline file-system analysis only.",
                    ["tia"] = "TIA Portal is not connected or opened by this analysis.",
                    ["write"] = "No global library content is imported, modified, or written."
                };

                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Global library package offline analysis completed" : "Global library package offline analysis completed with findings",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error analyzing global library package: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "PlanGlobalLibraryTemplateReuse"), Description("[L2][HMI-Library] Plan the commercial fallback when direct MasterCopy import is not publicly verifiable: learn reference/global-library template evidence and rebuild screens with native Unified HMI MCP theme/layout/action tools. Offline planning only; it does not import library content or modify projects.")]
        public static ResponseJsonReport PlanGlobalLibraryTemplateReuse(
            [Description("libraryPath: reference global library folder path or .al* file path.")] string libraryPath,
            [Description("templateIntentJson: optional JSON {\"screenType\":\"overview\",\"targetRuntime\":\"Unified\",\"preferredComponents\":[...]}.")] string templateIntentJson = "{}")
        {
            try
            {
                var analysis = GlobalLibraryPackageAnalyzer.Analyze(libraryPath);
                var intent = ParseJsonObjectOrEmpty(templateIntentJson, "templateIntentJson");
                var exists = analysis["exists"]?.GetValue<bool>() == true;
                var hasCoreFiles = analysis["ok"]?.GetValue<bool>() == true;
                var stringHints = analysis["stringHints"] as JsonObject;
                var patternCounts = stringHints?["patternCounts"] as JsonObject;
                var screenHintCount = patternCounts?["Screen"]?.GetValue<int>() ?? 0;
                var templateHintCount = patternCounts?["Template"]?.GetValue<int>() ?? 0;
                var masterCopyHintCount = patternCounts?["MasterCopy"]?.GetValue<int>() ?? 0;

                var data = new JsonObject
                {
                    ["libraryPath"] = libraryPath,
                    ["intent"] = intent,
                    ["offlineAnalysisOk"] = exists,
                    ["hasCoreGlobalLibraryFiles"] = hasCoreFiles,
                    ["strategy"] = "template-learn-and-native-rebuild",
                    ["directMasterCopyImportRequired"] = false,
                    ["directMasterCopyImportStatus"] = "optional-unverified-path",
                    ["commercialFallbackReady"] = exists,
                    ["safety"] = new JsonObject
                    {
                        ["offlineOnly"] = true,
                        ["importsLibraryContent"] = false,
                        ["modifiesProject"] = false,
                        ["requiresReadbackBeforeClaimingDirectImport"] = true
                    },
                    ["templateEvidence"] = new JsonObject
                    {
                        ["screenHintCount"] = screenHintCount,
                        ["templateHintCount"] = templateHintCount,
                        ["masterCopyHintCount"] = masterCopyHintCount
                    },
                    ["recommendedMcpTools"] = new JsonArray(
                        "AnalyzeGlobalLibraryPackage",
                        "ProbeGlobalLibrary",
                        "BuildUnifiedHmiThemeDesignJson",
                        "BuildUnifiedHmiLayoutDesignJson",
                        "BuildUnifiedHmiTemplateApplyDesignJson",
                        "ApplyUnifiedHmiScreenDesignJson",
                        "EnsureUnifiedHmiButtonAction"),
                    ["validationGates"] = new JsonArray(
                        "Template plan has offline package evidence.",
                        "Generated Unified design JSON passes layout QA.",
                        "Applied HMI screen items are read back by DescribeHmiScreenItem.",
                        "Button actions pass SyntaxCheck with zero errors.",
                        "HMI tags bind only to declared PLC symbols/DB members."),
                    ["reconstructionPlan"] = new JsonArray(
                        "Analyze global library/package structure and string hints without importing content.",
                        "Use ProbeGlobalLibrary only as read-only evidence when TIA is available; do not claim direct MasterCopy import unless readback succeeds.",
                        "Map reusable UI intent to Unified HMI native tools: theme, layout, template apply design, and button action recipes.",
                        "Apply generated design with ApplyUnifiedHmiScreenDesignJson and verify with item readback plus action SyntaxCheck.",
                        "Bind controls only to declared PLC symbols or DB members discovered from project exports/readback."),
                    ["analysis"] = analysis
                };

                return new ResponseJsonReport
                {
                    Ok = exists,
                    Message = exists
                        ? "Global library template reuse plan built. Direct MasterCopy import remains optional until real readback is verified."
                        : "Global library template reuse plan blocked because the library path was not found.",
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = exists }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error planning global library template reuse: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetDeviceIpAddress"), Description(
            "[L1][Hardware][PreCondition:Connect+OpenProject]" +
            " Read a device's configured IP address straight from the TIA project (Openness PROFINET node) —" +
            " NOT by probing the CPU over S7 and NOT by exporting/parsing AML. Returns the primary IE IP plus all network nodes" +
            " (address, subnet, type). This is the correct, fast way to discover a PLC's IP before GoOnline/ReadPlcLiveValuesS7.")]
        public static ResponseJsonReport GetDeviceIpAddress(
            [Description("devicePath: device name from GetProjectTree, e.g. 'PLC_1'.")] string devicePath)
        {
            try
            {
                var data = Portal.GetDeviceIpAddress(devicePath);
                bool found = data["found"]?.GetValue<bool>() ?? false;
                var ip = data["ipAddress"]?.ToString() ?? string.Empty;
                return new ResponseJsonReport
                {
                    Ok = found,
                    Message = found
                        ? $"{devicePath} IP: {(string.IsNullOrEmpty(ip) ? "(no address configured on any node)" : ip)}"
                        : (data["message"]?.ToString() ?? "Device not found."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = found }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"GetDeviceIpAddress failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetProjectTopology"), Description(
            "[L1][Hardware][PreCondition:Connect+OpenProject]" +
            " One-shot, read-only project topology from Openness: every device with its network nodes (IP, subnet, node type)." +
            " Call this early to understand the project's devices and subnets at a glance, instead of probing S7 or parsing AML.")]
        public static ResponseJsonReport GetProjectTopology()
        {
            try
            {
                var data = Portal.GetProjectTopology();
                int count = data["deviceCount"]?.GetValue<int>() ?? 0;
                return new ResponseJsonReport
                {
                    Ok = count > 0,
                    Message = $"Project topology: {count} device(s).",
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = count > 0 }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"GetProjectTopology failed: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DumpDeviceAttributes"), Description(
            "[L2][Hardware][PreCondition:Connect+OpenProject]" +
            " Read-only inventory of EVERY Openness attribute exposed on a device's items (CPU, modules, interfaces, ports):" +
            " name, access mode (read-only vs read/write), current value, value type." +
            " Run this ONCE per CPU/firmware to learn what is actually exposed, then drive hardware reads/writes from that" +
            " ground truth instead of guessing attribute names. Optional nameFilter narrows to attributes whose name contains" +
            " a substring (e.g. 'protection', 'putget', 'ip'). NOTE: GetAttributeInfos() does not enumerate every gettable" +
            " attribute on all CPUs, so absence here means 'not enumerated', not a guaranteed 'no interface'.")]
        public static ResponseJsonReport DumpDeviceAttributes(
            [Description("devicePath: device name, CPU/program name, or full name (e.g. 'S7-1200 station_3', '安全PLC', 'S7-1500/ET200MP station_1').")] string devicePath,
            [Description("nameFilter: optional case-insensitive substring to narrow attribute names (e.g. 'protection'). Empty = all.")] string? nameFilter = null)
        {
            try
            {
                var data = Portal.DumpDeviceAttributes(devicePath, nameFilter);
                bool found = data["found"]?.GetValue<bool>() ?? false;
                int items = data["itemCount"]?.GetValue<int>() ?? 0;
                int attrs = data["totalAttributes"]?.GetValue<int>() ?? 0;
                int writable = data["writableAttributes"]?.GetValue<int>() ?? 0;
                return new ResponseJsonReport
                {
                    Ok = found,
                    Message = found
                        ? $"{devicePath}: {attrs} attribute(s) across {items} item(s) ({writable} writable)."
                        : (data["message"]?.ToString() ?? "Not found."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = found }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"DumpDeviceAttributes failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetPutGetAccess"), Description(
            "[L2][Hardware][PreCondition:Connect+OpenProject]" +
            " Read whether a CPU permits remote PUT/GET access — the precondition for ReadPlcLiveValuesS7 on DB areas." +
            " If enabled=false, S7 absolute reads of DBs will fail; enable with SetPutGetAccess (then hardware DownloadToPlc).")]
        public static ResponseJsonReport GetPutGetAccess(
            [Description("devicePath: device name from GetProjectTree, e.g. 'PLC_1'.")] string devicePath)
        {
            try
            {
                var data = Portal.GetPutGetAccess(devicePath);
                bool found = data["found"]?.GetValue<bool>() ?? false;
                bool enabled = data["enabled"]?.GetValue<bool>() ?? false;
                return new ResponseJsonReport
                {
                    Ok = found,
                    Message = found
                        ? $"PUT/GET access on {devicePath}: {(enabled ? "ENABLED" : "DISABLED")} (attribute '{data["attributeName"]}')."
                        : (data["message"]?.ToString() ?? "Not found."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = found }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"GetPutGetAccess failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "SetPutGetAccess"), Description(
            "[L2][Hardware][CONFIG-WRITE][PreCondition:Connect+OpenProject]" +
            " Enable or disable remote PUT/GET access on a CPU (the precondition for S7 DB reads)." +
            " This is a hardware-configuration change — you must run DownloadToPlc afterwards for it to take effect on the live CPU." +
            " Returns before/after readback evidence.")]
        public static ResponseJsonReport SetPutGetAccess(
            [Description("devicePath: device name from GetProjectTree, e.g. 'PLC_1'.")] string devicePath,
            [Description("enable: true to permit remote PUT/GET access, false to forbid it.")] bool enable = true)
        {
            try
            {
                var data = Portal.SetPutGetAccess(devicePath, enable);
                bool ok = data["ok"]?.GetValue<bool>() ?? false;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok
                        ? $"PUT/GET access on {devicePath} set to {enable}. Download hardware config to apply."
                        : (data["message"]?.ToString() ?? "SetPutGetAccess failed."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = ok }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"SetPutGetAccess failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        #endregion
    }
}
