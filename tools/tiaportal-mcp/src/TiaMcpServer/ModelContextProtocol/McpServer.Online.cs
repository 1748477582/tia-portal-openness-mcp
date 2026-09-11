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
    // Partial: online / download / watch-table / OPC-UA / alarms / technology-objects. Split out of McpServer.PlcSoftware.cs (god-file split); behavior unchanged.
    public static partial class McpServer
    {
        #region online

        [McpServerTool(Name = "GetPlcWatchTables"), Description("[L2][PLC-Software]List PLC watch/monitor table names (PlcWatchTable). Read-only.")]
        public static ResponseStringList GetPlcWatchTables(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetPlcWatchTables(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"PLC watch tables listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"PLC software not found at '{softwarePath}'.{Portal.AvailablePlcPathsSuffix()}", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing PLC watch tables: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetPlcForceTables"), Description(
            "[L2][PLC-Online][PreCondition:Connect+OpenProject]" +
            " List all force table names in the PLC software." +
            " Force tables configure which variables are continuously forced to specific values while the CPU is online." +
            " Use SetForceTableEntry to configure entries, then go online for the forces to take effect.")]
        public static ResponseStringList GetPlcForceTables(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                var names = Portal.GetPlcForceTables(softwarePath);
                return new ResponseStringList
                {
                    Items = names ?? new List<string>(),
                    Message = names == null ? $"PLC software '{softwarePath}' not found." : $"{names.Count} force table(s) found.",
                    Meta = new JsonObject { ["softwarePath"] = softwarePath, ["timestamp"] = DateTime.Now }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing force tables for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "SetWatchTableModifyValue"), Description(
            "[L2][PLC-Online][ONLINE-WRITE][PreCondition:Connect+OpenProject+GoOnline]" +
            " Configure a watch table entry to write a value to a PLC variable once (or on a trigger)." +
            " This is an OFFLINE CONFIGURATION step — the value is written to the PLC only when TIA Portal is online and the trigger fires." +
            " Trigger options: Permanent (every cycle), PermanentAtStart (every cycle, at scan start), OnceOnlyAtStart (single write at scan start), PermanentAtEnd, OnceOnlyAtEnd, OnceOnlyAtStop." +
            " Use GoOnline before calling this for the write to reach the PLC." +
            " Does NOT use Force — variable reverts to PLC logic after the modify. To hold a value persistently, use SetForceTableEntry instead." +
            " Example: SetWatchTableModifyValue('PLC_1', 'Debug_WT', 'DB1.DBX0.0', 'TRUE', 'OnceOnlyAtStart')")]
        public static ResponseMessage SetWatchTableModifyValue(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("tableName: name of the watch table to configure (created if not existing)")] string tableName,
            [Description("address: variable address, e.g. 'DB1.DBX0.0', '%M0.0', 'MyTag'")] string address,
            [Description("modifyValue: value to write, e.g. 'TRUE', '42', '3.14'")] string modifyValue,
            [Description("trigger: when to apply the write — Permanent | PermanentAtStart | OnceOnlyAtStart | PermanentAtEnd | OnceOnlyAtEnd | OnceOnlyAtStop (default: Permanent)")] string trigger = "Permanent")
        {
            try
            {
                return Portal.EnsureWatchTableEntry(softwarePath, tableName, address, modifyValue, trigger);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error setting watch table entry: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        // Force-write capability retained in the Portal layer but intentionally NOT exposed as an MCP tool:
        // forcing overrides live PLC logic and must not be AI-invocable. Online monitoring stays read-only
        // (see RunOnlineMonitoringSafetySelfTest / Test_OnlineMonitoringNoUnsafeToolNames). Use TIA Portal
        // directly for commissioning forces. Removed from the tool surface in 0.0.38.
        public static ResponseMessage SetForceTableEntry(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("tableName: name of the force table to configure (created if not existing)")] string tableName,
            [Description("address: variable address to force, e.g. 'DB1.DBX0.0', '%M0.0'")] string address,
            [Description("forceValue: value to force, e.g. 'TRUE', '42'")] string forceValue)
        {
            try
            {
                return Portal.EnsureForceTableEntry(softwarePath, tableName, address, forceValue);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error setting force table entry: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportPlcWatchTable"), Description("[L2][PLC-Software]Export one PLC watch/monitor table (PlcWatchTable) to XML file. Read-only against the TIA project.")]
        public static ResponseExportFile ExportPlcWatchTable(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("watchTableName: PLC watch table name")] string watchTableName,
            [Description("exportPath: full file path to write to")] string exportPath)
        {
            try
            {
                var ok = Portal.ExportPlcWatchTable(softwarePath, watchTableName, exportPath);
                if (ok)
                {
                    return new ResponseExportFile
                    {
                        Message = $"PLC watch table '{watchTableName}' exported",
                        ExportPath = exportPath,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"Failed exporting PLC watch table '{watchTableName}' from '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error exporting PLC watch table: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportPlcWatchTablesToDirectory"), Description("[L2][PLC-Software]Export all PLC watch/monitor tables to XML files. Read-only against the TIA project.")]
        public static ResponseImportBatch ExportPlcWatchTablesToDirectory(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("dir: output directory")] string dir,
            [Description("regexName: optional regex filter applied to table name")] string regexName = "")
        {
            try
            {
                var result = Portal.ExportPlcWatchTablesToDirectory(softwarePath, dir, regexName);
                return new ResponseImportBatch
                {
                    Message = $"Exported {result.Imported?.Count() ?? 0} PLC watch tables to '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error exporting PLC watch tables: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ReadPlcWatchTableCurrentValuesReadOnly"), Description("[L2][PLC-Online] Read current/monitor value properties from an existing PLC watch table only. It does not create/modify watch tables, write PLC values, go offline, or use force operations.")]
        public static ResponseJsonReport ReadPlcWatchTableCurrentValuesReadOnly(
            [Description("softwarePath: PLC software path resolved from GetProjectTree/ValidateAutomationContext.")] string softwarePath,
            [Description("watchTableName: existing PLC watch table path/name returned by GetPlcWatchTables.")] string watchTableName,
            [Description("maxEntries: maximum entries to inspect.")] int maxEntries = 50)
        {
            try
            {
                var result = Portal.ReadPlcWatchTableCurrentValuesReadOnly(softwarePath, watchTableName, maxEntries);
                result.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = result.Ok == true };
                return result;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error reading PLC watch table values read-only: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        private static ResponseJsonReport BuildOnlineMonitoringPlanResponse(bool ok, string softwarePath, string mode, JsonArray acceptedTags, JsonArray rejectedTags, JsonArray warnings, JsonArray policy, string message)
        {
            return new ResponseJsonReport
            {
                Ok = ok,
                Message = message,
                Data = new JsonObject
                {
                    ["softwarePath"] = softwarePath,
                    ["mode"] = mode,
                    ["readOnly"] = true,
                    ["connectsToTia"] = false,
                    ["goesOnlineOrOffline"] = false,
                    ["modifiesWatchTables"] = false,
                    ["writesPlcValues"] = false,
                    ["usesForce"] = false,
                    ["acceptedTags"] = acceptedTags,
                    ["rejectedTags"] = rejectedTags,
                    ["warnings"] = warnings,
                    ["policy"] = policy
                },
                Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = ok
                }
            };
        }

        private static string? GetOnlineMonitoringTagRejectReason(string tagPath)
        {
            if (string.IsNullOrWhiteSpace(tagPath))
            {
                return "Tag path is empty.";
            }

            var forbiddenIntent = new[]
            {
                "force", "write", "modify", "update", "create",
                "delete", "remove", "import", "insert", "download", "activate", "start", "stop",
                "goonline", "gooffline", "watchtable", "forcetable"
            };
            var compact = Regex.Replace(tagPath, @"[\s_\-\.]+", string.Empty);
            var segments = Regex.Split(tagPath, @"[\.\s_\-]+").Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var forbidden = forbiddenIntent.FirstOrDefault(x =>
                compact.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                segments.Any(segment => segment.StartsWith(x, StringComparison.OrdinalIgnoreCase)));
            if (forbidden != null)
            {
                return $"Tag path contains unsafe online/write/force/watch-table intent keyword '{forbidden}'.";
            }

            if (Regex.IsMatch(tagPath, @"^%?[MIQ][BWD]?\d+(\.\d+)?$", RegexOptions.IgnoreCase))
            {
                return "Absolute I/Q/M address is not accepted for HMI/online planning. Use a declared PLC symbol or DB member read back from the project.";
            }

            if (!Regex.IsMatch(tagPath, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$"))
            {
                return "Use a symbolic PLC path with at least one member separator, for example DB_HMI.MotorRun.";
            }

            return null;
        }

        [McpServerTool(Name = "GetTechnologyObjects"), Description(
            "[L2][Drive][PreCondition:Connect+OpenProject]" +
            " List all Technology Objects (TOs) in the PLC software: axes, cams, measuring inputs, etc." +
            " Returns each TO's Name, type (OfSystemLibElement), and firmware version (OfSystemLibVersion)." +
            " Use this to discover TO names before ExportTechnologyObject or GetAxisParameters." +
            " TOs are stored as TechnologicalInstanceDB instances in the TechnologicalObjectGroup.")]
        public static ResponseTechnologyObjectList GetTechnologyObjects(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                var items = Portal.GetTechnologyObjects(softwarePath);
                var typed = items.Select(jo => new TechnologyObjectInfo
                {
                    Name = jo["Name"]?.GetValue<string>(),
                    OfSystemLibElement = jo["OfSystemLibElement"]?.GetValue<string>(),
                    OfSystemLibVersion = jo["OfSystemLibVersion"]?.GetValue<string>(),
                    TypeHint = jo["TypeHint"]?.GetValue<string>(),
                }).ToArray();

                return new ResponseTechnologyObjectList
                {
                    Ok = true,
                    SoftwarePath = softwarePath,
                    Count = typed.Length,
                    Items = typed,
                    Message = $"{typed.Length} technology object(s) found in '{softwarePath}'."
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing technology objects: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportTechnologyObject"), Description(
            "[L2][Drive][PreCondition:Connect+OpenProject]" +
            " Export a single Technology Object (axis, cam, measuring input, etc.) to an XML file." +
            " The XML can be inspected, modified offline, and re-imported with ImportTechnologyObject." +
            " Use GetTechnologyObjects first to confirm the exact TO name.")]
        public static ResponseMessage ExportTechnologyObject(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("toName: exact name of the technology object, e.g. 'Axis_1'")] string toName,
            [Description("exportPath: full file path for the XML output, e.g. 'C:\\Temp\\Axis_1.xml'")] string exportPath)
        {
            try { return Portal.ExportTechnologyObject(softwarePath, toName, exportPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error exporting technology object: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ExportTechnologyObjectsToDirectory"), Description(
            "[L2][Drive][PreCondition:Connect+OpenProject]" +
            " Batch-export all (or regex-filtered) Technology Objects to XML files in a directory." +
            " Each TO is saved as '<TOName>.xml'. Returns lists of exported names and any failures." +
            " Use regexName to filter by TO name, e.g. 'Axis_.*' for all axes.")]
        public static ResponseImportBatch ExportTechnologyObjectsToDirectory(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportDir: directory to write XML files to, e.g. 'C:\\Temp\\TOs'")] string exportDir,
            [Description("regexName: optional regex filter on TO name; empty = export all")] string regexName = "")
        {
            try { return Portal.ExportTechnologyObjectsToDirectory(softwarePath, exportDir, regexName); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error batch-exporting technology objects: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ImportTechnologyObject"), Description("[L2][PLC-Software]Import one PLC Technology Object XML file into PLC software (best-effort)")]
        public static ResponseMessage ImportTechnologyObject(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional technology object group path (use empty for root)")] string folderPath,
            [Description("importPath: full file path of Technology Object XML")] string importPath)
        {
            try
            {
                Portal.ImportTechnologyObject(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"Technology object imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed importing technology object from '{importPath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing technology object: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportTechnologyObjectsFromDirectory"), Description("[L2][PLC-Software]Batch import PLC technology object .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportTechnologyObjectsFromDirectory(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional technology object group path (use empty for root)")] string folderPath,
            [Description("dir: directory containing technology object XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportTechnologyObjectsFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} technology objects from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing technology objects from '{dir}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetOpcUaConfig"), Description(
            "[L2][PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Read the full OPC UA server configuration for a PLC: server interfaces, SIMATIC interfaces, and reference namespaces — each with their Name, Enabled state, and key properties." +
            " Use this to audit what OPC UA interfaces exist before enabling or exporting them." +
            " Enabled=true means the interface is active and will be downloaded to the CPU.")]
        public static ResponseJsonReport GetOpcUaConfig(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try { return Portal.GetOpcUaConfig(softwarePath); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error reading OPC UA config: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "SetOpcUaInterfaceEnabled"), Description(
            "[L2][PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Enable or disable an OPC UA server interface, SIMATIC interface, or reference namespace." +
            " Setting Enabled=true activates the interface — download to PLC is required for the change to take effect on the CPU." +
            " interfaceType options: 'ServerInterface' (default), 'SimaticInterface', 'ReferenceNamespace'." +
            " Workflow: GetOpcUaConfig → SetOpcUaInterfaceEnabled → DownloadToPlc.")]
        public static ResponseMessage SetOpcUaInterfaceEnabled(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("interfaceName: exact name of the interface as shown in GetOpcUaConfig")] string interfaceName,
            [Description("enabled: true to enable, false to disable")] bool enabled,
            [Description("interfaceType: 'ServerInterface' (default), 'SimaticInterface', or 'ReferenceNamespace'")] string interfaceType = "ServerInterface")
        {
            try { return Portal.SetOpcUaInterfaceEnabled(softwarePath, interfaceName, enabled, interfaceType); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error setting OPC UA interface enabled state: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ExportOpcUaInterface"), Description(
            "[L2][PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Export an OPC UA server interface or reference namespace to an XML file." +
            " The exported XML can be inspected, modified, and re-imported." +
            " interfaceType: 'ServerInterface' (default), 'SimaticInterface', 'ReferenceNamespace'.")]
        public static ResponseMessage ExportOpcUaInterface(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("interfaceName: exact name of the interface to export")] string interfaceName,
            [Description("exportPath: full file path for the XML output, e.g. 'C:\\Temp\\OpcUa_Interface.xml'")] string exportPath,
            [Description("interfaceType: 'ServerInterface' (default), 'SimaticInterface', or 'ReferenceNamespace'")] string interfaceType = "ServerInterface")
        {
            try { return Portal.ExportOpcUaInterface(softwarePath, interfaceName, exportPath, interfaceType); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error exporting OPC UA interface: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ImportOpcUaInterface"), Description(
            "[L2][PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Import an OPC UA server interface or reference namespace from an XML file." +
            " If an interface with the same name (derived from the file name) already exists, it is updated in place." +
            " Otherwise a new interface is created." +
            " Download to PLC after import to apply changes to the CPU." +
            " interfaceType: 'ServerInterface' (default), 'ReferenceNamespace'.")]
        public static ResponseMessage ImportOpcUaInterface(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("importPath: full file path to the XML file")] string importPath,
            [Description("interfaceType: 'ServerInterface' (default) or 'ReferenceNamespace'")] string interfaceType = "ServerInterface")
        {
            try { return Portal.ImportOpcUaInterface(softwarePath, importPath, interfaceType); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error importing OPC UA interface: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ExportAlarmClasses"), Description(
            "[L2][PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Export PLC alarm classes to a file. Alarm classes define severity, acknowledgment behavior, and display colors for alarms." +
            " The exported file can be edited and re-imported to update alarm class configurations." +
            " Use before bulk alarm class updates to create a backup.")]
        public static ResponseMessage ExportAlarmClasses(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportPath: full file path for the export, e.g. 'C:\\Temp\\AlarmClasses.xml'")] string exportPath)
        {
            try { return Portal.ExportAlarmClasses(softwarePath, exportPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error exporting alarm classes: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ImportAlarmClasses"), Description(
            "[L2][PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Import PLC alarm classes from a previously exported file." +
            " Overwrites existing alarm class definitions. Run CompileSoftware after import.")]
        public static ResponseMessage ImportAlarmClasses(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("importPath: full file path to import from")] string importPath)
        {
            try { return Portal.ImportAlarmClasses(softwarePath, importPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error importing alarm classes: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ExportAlarmTextLists"), Description(
            "[L2][PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Export all PLC alarm text lists to an XLSX (Excel) file." +
            " Text lists contain the text strings shown for each alarm condition." +
            " Supports multi-language projects — all configured languages are exported." +
            " Typical use: export → translate in Excel → ImportAlarmTextLists.")]
        public static ResponseMessage ExportAlarmTextLists(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportPath: full file path for the XLSX output, e.g. 'C:\\Temp\\AlarmTexts.xlsx'")] string exportPath)
        {
            try { return Portal.ExportAlarmTextLists(softwarePath, exportPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error exporting alarm text lists: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ImportAlarmTextLists"), Description(
            "[L2][PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Import PLC alarm text lists from an XLSX file." +
            " The file must match the format exported by ExportAlarmTextLists." +
            " Run CompileSoftware after import to validate alarm configuration.")]
        public static ResponseMessage ImportAlarmTextLists(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("importPath: full file path to the XLSX file")] string importPath)
        {
            try { return Portal.ImportAlarmTextLists(softwarePath, importPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error importing alarm text lists: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "ExportAlarmInstanceTexts"), Description(
            "[L2][PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Export PLC alarm instance texts to an XLSX file." +
            " Instance texts are the alarm messages tied to specific FB/FC instances (e.g. Motor_01.AlarmText)." +
            " Options control what additional columns are included in the export." +
            " Typical use: export → fill in alarm descriptions → ImportInstanceTexts (not yet exposed — edit via TIA Portal UI).")]
        public static ResponseMessage ExportAlarmInstanceTexts(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportPath: full file path for the XLSX output")] string exportPath,
            [Description("includeInfoText: include the Info Text column (default: true)")] bool includeInfoText = true,
            [Description("includeAdditionalTexts: include Additional Texts columns (default: true)")] bool includeAdditionalTexts = true,
            [Description("includeAlarmClass: include the Alarm Class column (default: true)")] bool includeAlarmClass = true)
        {
            try { return Portal.ExportAlarmInstanceTexts(softwarePath, exportPath, includeInfoText, includeAdditionalTexts, includeAlarmClass); }
            catch (Exception ex) when (ex is not McpException)
            { throw McpError.WithRecovery(ex, $"Unexpected error exporting alarm instance texts: {ex.Message}{McpHints.Recovery(ex)}"); }
        }

        [McpServerTool(Name = "GetOnlineState"), Description(
            "[L1][PLC-Online][PreCondition:Connect+OpenProject]" +
            " Read the current online connection state of a PLC (Offline/Connecting/Online/Incompatible/NotReachable/Protected/Disconnecting)." +
            " Does NOT change state — purely a read operation." +
            " Use before GoOnline to check current state, or after DownloadToPlc to verify the CPU is reachable." +
            " State=Online means the PC is communicating with the physical CPU." +
            " State=Incompatible means online but firmware/config mismatch — download required." +
            " State=NotReachable means network or IP configuration issue." +
            " NOTE: This reports Openness connection state, NOT the CPU operating mode (RUN/STOP)." +
            " The TIA Portal public API does not expose CPU operating mode — check the CPU front panel LEDs or HMI for RUN/STOP status.")]
        public static ResponseOnlineState GetOnlineState(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                return Portal.GetOnlineState(softwarePath);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error reading online state for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GoOnline"), Description(
            "[L1][PLC-Online][ONLINE-CONNECT][PreCondition:Connect+OpenProject]" +
            " Establish an online connection from TIA Portal to the physical PLC." +
            " Required before DownloadToPlc to confirm reachability, or for future online monitoring tools." +
            " Returns State=Online on success." +
            " If ipAddress is omitted, uses the IP address configured in the project's hardware configuration." +
            " If ipAddress is provided, overrides the configured IP for this session (useful for commissioning with a different IP)." +
            " Common failures: NotReachable (wrong IP / no cable), Protected (CPU requires authentication — supply password), Incompatible (firmware mismatch).")]
        public static ResponseOnlineState GoOnline(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("ipAddress: optional IP address override, e.g. '192.168.1.10'. Leave empty to use the project's configured IP.")] string ipAddress = "",
            [Description("password: optional CPU access password. Required when the CPU has read/write protection configured. Leave empty for unprotected CPUs.")] string password = "")
        {
            try
            {
                return Portal.GoOnline(
                    softwarePath,
                    string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress,
                    string.IsNullOrWhiteSpace(password) ? null : password);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error going online for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GoOffline"), Description(
            "[L1][PLC-Online][PreCondition:Connect+OpenProject]" +
            " Disconnect the online session between TIA Portal and the physical PLC." +
            " Safe to call even if not currently online. Always go offline when monitoring or download is complete.")]
        public static ResponseMessage GoOffline(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                return Portal.GoOffline(softwarePath);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error going offline for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GoOfflineAll"), Description(
            "[L1][PLC-Online][PreCondition:Connect+OpenProject]" +
            " Take EVERY PLC in the open project offline in one call and report each PLC's before/after online state." +
            " Use this whenever CompileSoftware/Export*/Import* is blocked by 'operation not permitted in online mode':" +
            " a UI-initiated online session or a second online PLC is NOT released by GoOffline on a single softwarePath." +
            " Fully autonomous — never ask the user to toggle online/offline in the TIA UI, and never OCR the toolbar.")]
        public static ResponseJsonReport GoOfflineAll()
        {
            try
            {
                var data = Portal.GoOfflineAll();
                bool all = data["allOffline"]?.GetValue<bool>() ?? false;
                return new ResponseJsonReport
                {
                    Ok = all,
                    Message = data["message"]?.ToString() ?? "GoOfflineAll completed.",
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = all }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"GoOfflineAll failed: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        // Autonomy helper: when a compile/export/import is blocked because TIA is in online mode,
        // take ALL PLCs offline via Openness and retry once — never hand the toggle back to the user.
        internal static bool IsOnlineModeError(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                var m = e.Message ?? string.Empty;
                if (m.IndexOf("online mode", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (m.IndexOf("not permitted", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    m.IndexOf("online", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        [McpServerTool(Name = "CompareSoftwareToOnline"), Description(
            "[L2][PLC-Online][PreCondition:Connect+OpenProject+GoOnline]" +
            " Compare the offline PLC software in the project against the program currently running on the physical CPU." +
            " Use after editing blocks to confirm what differs from the live CPU before downloading," +
            " or after a download to verify offline/online consistency." +
            " Returns a tree-walked list of differences (only entries where ComparisonResult is not 'Equal' are reported)." +
            " Requires GoOnline to be called first; will return IsOnline=false with guidance otherwise.")]
        public static ResponseCompare CompareSoftwareToOnline(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("maxDepth: maximum tree depth to walk (default 4). Lower = faster but less detail.")] int maxDepth = 4,
            [Description("maxEntries: cap on differences returned (default 200). Truncated=true in response if reached.")] int maxEntries = 200)
        {
            try
            {
                return Portal.CompareSoftwareToOnline(softwarePath, maxDepth, maxEntries);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error comparing '{softwarePath}' to online: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "CheckDownloadReadiness"), Description(
            "[L1][PLC-Online][PreCondition:Connect+OpenProject+CompileSoftware]" +
            " Check whether a PLC is ready to receive a program download WITHOUT actually downloading." +
            " Verifies: DownloadProvider service is available, a network/IP configuration exists in the hardware config." +
            " Returns Ready=true only when all checks pass." +
            " Use this before DownloadToPlc to surface problems early (missing IP, no hardware config, etc.)." +
            " Does NOT compile — run CompileSoftware first to ensure blocks are consistent.")]
        public static ResponseCheckDownload CheckDownloadReadiness(
            [Description("softwarePath: path to the PLC software in the project tree, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                return Portal.CheckDownloadReadiness(softwarePath);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error checking download readiness for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DownloadToPlc"), Description(
            "[L1][PLC-Online][ONLINE-WRITE][PreCondition:Connect+OpenProject+CompileSoftware+CheckDownloadReadiness]" +
            " Download the compiled PLC program to the physical CPU over the network." +
            " The CPU will stop briefly during download and restart automatically (controlled by startAfterDownload)." +
            " SAFETY: Verify no personnel are near the machine before downloading. This changes live PLC behavior." +
            " Workflow: Connect → OpenProject → CompileSoftware → CheckDownloadReadiness → DownloadToPlc → GetCpuOnlineState." +
            " On success State=Success or Warning. On Error check Errors[] for details." +
            " Default options (keepActualValues=true, consistentBlocksOnly=true) are safe for most scenarios." +
            " Set keepActualValues=false only when DB initial values must be reset — this is irreversible.")]
        public static ResponseDownload DownloadToPlc(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("consistentBlocksOnly: true=download only consistent blocks (safe default), false=download all blocks even inconsistent ones")] bool consistentBlocksOnly = true,
            [Description("keepActualValues: true=preserve current DB actual values (safe default), false=reset all DB values to initial values (irreversible)")] bool keepActualValues = true,
            [Description("startAfterDownload: true=automatically set CPU to RUN after download (default), false=leave CPU in STOP")] bool startAfterDownload = true,
            [Description("stopBeforeDownload: true=automatically stop CPU before download (required for most downloads), false=attempt online download without stopping")] bool stopBeforeDownload = true,
            [Description("password: optional CPU access password. Required when the CPU has download protection configured. Leave empty for unprotected CPUs.")] string password = "",
            [Description("pgPcInterface: optional PG/PC interface name filter, e.g. 'PLCSIM' to download to PLCSIM (Advanced) Softbus instead of a physical NIC. Empty = first available interface.")] string pgPcInterface = "")
        {
            try
            {
                var result = Portal.DownloadToPlc(
                    softwarePath,
                    consistentBlocksOnly,
                    keepActualValues,
                    startAfterDownload,
                    stopBeforeDownload,
                    string.IsNullOrWhiteSpace(password) ? null : password,
                    string.IsNullOrWhiteSpace(pgPcInterface) ? null : pgPcInterface);

                if (result.Ok == false && result.Errors != null && result.Errors.Length > 0)
                    throw new McpException(
                        $"Download to '{softwarePath}' failed: {result.Message}",
                        McpErrorCode.InternalError);

                return result;
            }
            catch (McpException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error downloading to '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        #endregion
    }
}
