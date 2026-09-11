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
    // Partial: HMI (Classic + WinCC Unified) tools. Split out of McpServer.PlcSoftware.cs (god-file split); behavior unchanged.
    public static partial class McpServer
    {
        #region hmi

        [McpServerTool(Name = "GetHmiProgramInfo"), Description("[L2][HMI] Get HMI software type (Classic/Basic/Unified), version, and list of all screen names. Requires: Connect + OpenProject. softwarePath from GetProjectTree (e.g. 'HMI_RT_1'). Use to confirm HMI type before choosing Classic vs Unified tool variants.")]
        public static ResponseHmiProgramInfo GetHmiProgramInfo(
            [Description("softwarePath: path in the project structure to the HMI software (see GetProjectTree)")] string softwarePath)
        {
            try
            {
                var info = Portal.GetHmiProgramInfo(softwarePath);
                if (info != null)
                {
                    return new ResponseHmiProgramInfo
                    {
                        Message = $"HMI program info retrieved from '{softwarePath}'",
                        Name = info.Value.Name,
                        ProgramType = info.Value.ProgramType,
                        Screens = info.Value.Screens,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }

                throw new McpException($"HMI program not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error retrieving HMI program info from '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DescribeHmiSoftware"), Description("[L2][HMI]Describe the HMI software object (members/methods) via reflection. Useful to discover Export/Import/Create APIs.")]
        public static ResponseObjectDescribe DescribeHmiSoftware(
            [Description("softwarePath: path in the project structure to the HMI software (e.g. 'HMI_RT_1')")] string softwarePath,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiSoftware(softwarePath, maxMembers);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (res.Members != null && res.Members.Any()) };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error describing HMI software '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DescribeHmiScreen"), Description("[L2][HMI]Describe one HMI screen object (members/methods) by name under an HMI software.")]
        public static ResponseObjectDescribe DescribeHmiScreen(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("screenName: screen name, e.g. 'Main'")] string screenName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiScreen(softwarePath, screenName, maxMembers);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (res.Members != null && res.Members.Any()) };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error describing HMI screen '{screenName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DescribeHmiTagTable"), Description("[L2][HMI]Describe one HMI tag table object (members/methods) by name under an HMI software.")]
        public static ResponseObjectDescribe DescribeHmiTagTable(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("tagTableName: tag table name")] string tagTableName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiTagTable(softwarePath, tagTableName, maxMembers);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (res.Members != null && res.Members.Any()) };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error describing HMI tag table '{tagTableName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DescribeHmiTag"), Description("[L2][HMI]Describe one HMI tag object (members/methods) by name under an HMI tag table.")]
        public static ResponseObjectDescribe DescribeHmiTag(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("tagTableName: tag table name")] string tagTableName,
            [Description("tagName: tag name")] string tagName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiTag(softwarePath, tagTableName, tagName, maxMembers);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (res.Members != null && res.Members.Any()) };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error describing HMI tag '{tagName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DescribeHmiScreenItem"), Description("[L2][HMI]Describe one HMI screen item (widget) by name under an HMI screen.")]
        public static ResponseObjectDescribe DescribeHmiScreenItem(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("screenName: screen name, e.g. 'Main'")] string screenName,
            [Description("itemName: widget name, e.g. 'BTN_Start'")] string itemName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiScreenItem(softwarePath, screenName, itemName, maxMembers);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (res.Members != null && res.Members.Any()) };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error describing HMI screen item '{itemName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureStartStopUnifiedHmi"), Description("[L2][HMI-Unified] SHORTCUT for motor start/stop HMI. Ensures HMI_Connection_1 uses the correct PLC driver (1200/1500 vs 300/400 from CPU TypeIdentifier), 4 HMI tags (StartPB/StopPB/EStop/RunOut) with symbolic PLC binding, and a simple styled Main screen. Requires: Connect + OpenProject + PLC + Unified HMI. Call after EnsureUnifiedHmiScreen if you need a fixed screen size. Idempotent.")]
#endif
        public static ResponseMessage EnsureStartStopUnifiedHmi(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name (default 'Main')")] string screenName = "Main",
            [Description("tagTableName: target HMI tag table name (default '默认变量表')")] string tagTableName = "默认变量表",
            [Description("plcName: PLC software path / device name for connection + tag mapping (default 'PLC_1')")] string plcName = "PLC_1",
            [Description("connectionName: Unified HMI connection object name (default 'HMI_Connection_1')")] string connectionName = "HMI_Connection_1")
        {
            try
            {
                var res = Portal.EnsureStartStopUnifiedHmi(hmiSoftwarePath, screenName, tagTableName, plcName, connectionName);
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring unified HMI start/stop: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiScreen"), Description("[L2][HMI-Unified] Create or verify a WinCC Unified HMI screen exists. Requires: Connect + OpenProject + Unified HMI. Idempotent. After creating a screen, add tags with EnsureUnifiedHmiTag, add controls with EnsureUnifiedHmiScreenItem, or apply a complete layout with ApplyUnifiedHmiScreenDesignJson.")]
#endif
        public static ResponseMessage EnsureUnifiedHmiScreen(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("width: optional screen width, 0 means keep current")] uint width = 0,
            [Description("height: optional screen height, 0 means keep current")] uint height = 0)
        {
            try
            {
                return Portal.EnsureUnifiedHmiScreen(hmiSoftwarePath, screenName, width, height);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring HMI screen '{screenName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiTagTable"), Description("[L2][HMI-Unified] Create or verify a Unified HMI tag table exists. Requires: Connect + OpenProject + Unified HMI. Idempotent. Create tag tables before adding tags with EnsureUnifiedHmiTag. Default tag table name is '默认变量表'.")]
#endif
        public static ResponseMessage EnsureUnifiedHmiTagTable(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("tagTableName: target HMI tag table name")] string tagTableName)
        {
            try
            {
                return Portal.EnsureUnifiedHmiTagTable(hmiSoftwarePath, tagTableName);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring HMI tag table '{tagTableName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiTag"), Description("[L2][HMI-Unified] Create or verify a Unified HMI external tag. For PLC-backed tags pass plcTag and address in the same call; the address must read back in Address/LogicalAddress, e.g. %DB200.DBX0.0. Requires: Connect + OpenProject + EnsureUnifiedHmiConnection + EnsureUnifiedHmiTagTable.")]
#endif
        public static ResponseMessage EnsureUnifiedHmiTag(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("tagTableName: target HMI tag table name")] string tagTableName,
            [Description("tagName: HMI tag name")] string tagName,
            [Description("hmiDataType: HMI data type, e.g. Bool, Int, Real, String")] string hmiDataType = "Bool",
            [Description("plcName: PLC name for symbolic binding")] string plcName = "PLC_1",
            [Description("plcTag: PLC tag name/path; empty means same as tagName")] string plcTag = "",
            [Description("connectionName: HMI connection name; empty keeps current/auto")] string connectionName = "",
            [Description("address: optional absolute PLC address, e.g. %DB200.DBX0.0. When supplied it is written as the verified HMI runtime address while plcTag remains available as the symbolic reference.")] string address = "",
            [Description("requireVerifiedBinding: stable public generation should keep this true. Set false only for intentional internal HMI-only tags used by local validation/probes.")] bool requireVerifiedBinding = true)
        {
            try
            {
                return Portal.EnsureUnifiedHmiTag(hmiSoftwarePath, tagTableName, tagName, hmiDataType, plcName, plcTag, connectionName, address, requireVerifiedBinding);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring HMI tag '{tagName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiConnection"), Description("[L2][HMI-Unified] Create or verify the PLC↔HMI communication connection (HMI_Connection_1 by default). Requires: Connect + OpenProject + both PLC and Unified HMI devices. Must exist before PLC-backed HMI tags can exchange data. Call before EnsureUnifiedHmiTag with plcTag binding.")]
#endif
        public static ResponseObjectDescribe EnsureUnifiedHmiConnection(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("connectionName: HMI connection name")] string connectionName = "HMI_Connection_1",
            [Description("plcName: PLC software/device symbolic name")] string plcName = "PLC_1")
        {
            try
            {
                var res = Portal.EnsureUnifiedHmiConnection(hmiSoftwarePath, connectionName, plcName);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring HMI connection '{connectionName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiScreenItem"), Description("[L2][HMI-Unified] Create or verify a single Unified HMI control (button, lamp, IO field, etc.) on a screen. Requires: Connect + OpenProject + EnsureUnifiedHmiScreen. itemType: Button, Rectangle (lamp/indicator), IOField (value display/entry), or full CLR type name. For a complete screen layout use ApplyUnifiedHmiScreenDesignJson instead.")]
#endif
        public static ResponseMessage EnsureUnifiedHmiScreenItem(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("itemName: screen item name")] string itemName,
            [Description("itemType: Button, Rectangle/Lamp, IOField, or full CLR type name")] string itemType = "Button",
            [Description("left: X position")] int left = 0,
            [Description("top: Y position")] int top = 0,
            [Description("width: item width")] uint width = 120,
            [Description("height: item height")] uint height = 40,
            [Description("text: optional button/display text")] string text = "")
        {
            try
            {
                return Portal.EnsureUnifiedHmiScreenItem(hmiSoftwarePath, screenName, itemName, itemType, left, top, width, height, text);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring HMI screen item '{itemName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "ApplyUnifiedHmiScreenDesignJson"), Description("[L2][HMI-Unified] PREFERRED for natural-language HMI design. Apply a complete JSON layout spec to a screen in one call: screen size + multiple controls (Button/Rectangle/IOField) with positions, text, and properties. Requires: Connect + OpenProject + EnsureUnifiedHmiScreen. Better than calling EnsureUnifiedHmiScreenItem multiple times. Use BuildUnifiedHmiLayoutDesignJson to generate the JSON from a grid description.")]
#endif
        public static ResponseMessage ApplyUnifiedHmiScreenDesignJson(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("designJson: JSON object with optional screen properties and items array")] string designJson,
            [Description("strict: true fails the tool when any property/text write fails; false keeps legacy best-effort behavior.")] bool strict = true)
        {
            try
            {
                return Portal.ApplyUnifiedHmiScreenDesignJson(hmiSoftwarePath, screenName, designJson, strict);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error applying Unified HMI design to '{screenName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "BuildUnifiedHmiThemeDesignJson"), Description("[L2][HMI-Unified][Offline] Build ApplyUnifiedHmiScreenDesignJson-compatible JSON from a theme/palette. It does not connect to TIA Portal or modify projects.")]
#endif
        public static ResponseJsonReport BuildUnifiedHmiThemeDesignJson(
            [Description("themeJson: JSON {name?, palette:{Page?,Surface?,Text?,Border?,...}} with TIA ARGB colors like 0xFFF4F6F8.")] string themeJson)
        {
            try
            {
                var root = JsonNode.Parse(themeJson) as JsonObject
                    ?? throw new ArgumentException("themeJson root must be an object.");
                var design = HmiUnifiedThemeLayoutBuilder.BuildThemeDesign(root);
                return new ResponseJsonReport
                {
                    Ok = true,
                    Message = "Unified HMI theme design JSON built offline",
                    Data = design,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["offlineOnly"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid Unified HMI theme JSON: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "BuildUnifiedHmiLayoutDesignJson"), Description("[L2][HMI-Unified][Offline] Build ApplyUnifiedHmiScreenDesignJson-compatible JSON from a grid layout. It does not connect to TIA Portal or modify projects.")]
#endif
        public static ResponseJsonReport BuildUnifiedHmiLayoutDesignJson(
            [Description("layoutJson: JSON {grid?,left?,top?,gap?,columns?,cellWidth?,cellHeight?,items:[{name,type?,row?,col?,rowSpan?,colSpan?,text?,properties?}]}.")] string layoutJson)
        {
            try
            {
                var root = JsonNode.Parse(layoutJson) as JsonObject
                    ?? throw new ArgumentException("layoutJson root must be an object.");
                var design = HmiUnifiedThemeLayoutBuilder.BuildLayoutDesign(root);
                return new ResponseJsonReport
                {
                    Ok = true,
                    Message = "Unified HMI layout design JSON built offline",
                    Data = design,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["offlineOnly"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid Unified HMI layout JSON: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "ApplyUnifiedHmiTheme"), Description("[L2][HMI-Unified] Apply a theme/palette to a real Unified HMI screen through ApplyUnifiedHmiScreenDesignJson. Requires a connected TIA project; verify with DescribeHmiScreenItem/readback before saving.")]
#endif
        public static ResponseMessage ApplyUnifiedHmiTheme(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("themeJson: JSON accepted by BuildUnifiedHmiThemeDesignJson.")] string themeJson)
        {
            try
            {
                var design = BuildUnifiedHmiThemeDesignJson(themeJson).Data?.ToJsonString() ?? "{}";
                return Portal.ApplyUnifiedHmiScreenDesignJson(hmiSoftwarePath, screenName, design);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error applying Unified HMI theme: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "ApplyUnifiedHmiLayout"), Description("[L2][HMI-Unified] Apply a grid layout to a real Unified HMI screen through ApplyUnifiedHmiScreenDesignJson. Requires a connected TIA project; verify changed items with DescribeHmiScreenItem/readback before saving.")]
#endif
        public static ResponseMessage ApplyUnifiedHmiLayout(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("layoutJson: JSON accepted by BuildUnifiedHmiLayoutDesignJson.")] string layoutJson)
        {
            try
            {
                var design = BuildUnifiedHmiLayoutDesignJson(layoutJson).Data?.ToJsonString() ?? "{}";
                return Portal.ApplyUnifiedHmiScreenDesignJson(hmiSoftwarePath, screenName, design);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error applying Unified HMI layout: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "BuildClassicHmiScreenXml"), Description("[L2][HMI]Offline-only helper: build a Classic/Basic WinCC HMI screen XML document from structured JSON. It does not connect to TIA Portal, import screens, or modify projects. Validate in a temporary Classic HMI project before using on a real project.")]
        public static ResponseXmlBuild BuildClassicHmiScreenXml(
            [Description("designJson: JSON object with Screen/Items. Items support Type=Text/Button/IOField/Lamp/Rectangle plus Name/Left/Top/Width/Height/Text/Properties.")] string designJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(ClassicHmiScreenXmlBuilder.BuildFromJson(designJson), "Classic HMI screen XML built offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building Classic HMI screen XML offline: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "BuildClassicHmiTagTableXml"), Description("[L2][HMI]Offline-only helper: build a Classic/Basic WinCC HMI tag table XML document from structured JSON. Supports plain HMI tags and symbolic PLC bindings through Connection + ControllerTag/PlcTag. It does not connect to TIA Portal, import tags, or modify projects.")]
        public static ResponseXmlBuild BuildClassicHmiTagTableXml(
            [Description("tableJson: JSON object with Name/TableName and Tags[]. Tag fields: Name, DataType, Length, optional Connection and ControllerTag/PlcTag.")] string tableJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(ClassicHmiTagTableXmlBuilder.BuildFromJson(tableJson), "Classic HMI tag table XML built offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building Classic HMI tag table XML offline: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "BuildClassicHmiMinimalPackage"), Description("[L2][HMI]Offline-only helper: build a minimal Classic/Basic HMI package from structured JSON. It returns tag-table XML, screen XML, import order, and readiness checks that screen item tag references are declared in the tag table. It does not connect to TIA Portal, import files, or modify projects.")]
        public static ResponseJsonReport BuildClassicHmiMinimalPackage(
            [Description("packageJson: JSON object with Name, ScreenDesign, and TagTable. Screen items may reference HMI tags through Tag/HmiTag/ProcessValueTag or Properties.*Tag.")] string packageJson)
        {
            try
            {
                var data = ClassicHmiMinimalPackageBuilder.BuildFromJson(packageJson);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Classic HMI minimal package built offline" : "Classic HMI minimal package built with validation findings",
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
                throw McpError.WithRecovery(ex, $"Unexpected error building Classic HMI minimal package offline: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "WriteClassicHmiMinimalPackageFiles"), Description("[L2][HMI]Offline-only helper: build a minimal Classic/Basic HMI package and write tag-table XML, screen XML, and manifest JSON to an output directory. It does not connect to TIA Portal, import files, or modify projects.")]
        public static ResponseJsonReport WriteClassicHmiMinimalPackageFiles(
            [Description("packageJson: JSON object with Name, ScreenDesign, and TagTable.")] string packageJson,
            [Description("outputDirectory: directory where tag-table XML, screen XML, and manifest JSON will be written.")] string outputDirectory)
        {
            try
            {
                var data = ClassicHmiMinimalPackageBuilder.WriteFiles(packageJson, outputDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;

                string[]? files = null;
                if (data["files"] is JsonArray arr)
                {
                    files = arr.Where(f => f != null).Select(f => f!.GetValue<string>()).ToArray();
                    if (files.Length == 0) files = null;
                }
                var outDir = data["outputDirectory"]?.GetValue<string>();

                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Classic HMI minimal package files written offline" : "Classic HMI minimal package files written with validation findings",
                    Data = data,
                    OutputPath = outDir,
                    OutputFiles = files,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true,
                        ["fileCount"] = data["fileCount"]?.GetValue<int>() ?? 0
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error writing Classic HMI minimal package files offline: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "ValidateClassicHmiMinimalPackageFiles"), Description("[L2][HMI]Offline-only helper: validate an already written Classic/Basic HMI minimal package folder or manifest. It reads manifest/XML, checks parseability and HMI tag references, and does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport ValidateClassicHmiMinimalPackageFiles(
            [Description("path: package output directory or *_manifest.json path to validate.")] string path)
        {
            try
            {
                var data = ClassicHmiMinimalPackageBuilder.ValidateFiles(path);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Classic HMI minimal package files validated offline" : "Classic HMI minimal package file validation found issues",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true,
                        ["missingTagCount"] = data["missingTagCount"]?.GetValue<int>() ?? 0
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error validating Classic HMI minimal package files offline: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "ValidateClassicHmiMinimalPackagePlcSync"), Description("[L2][HMI]Offline-only helper: validate that Classic/Basic HMI tag-table ControllerTag/PlcTag bindings exist in a caller-provided exact PLC symbol list. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport ValidateClassicHmiMinimalPackagePlcSync(
            [Description("path: package output directory or *_manifest.json path to validate.")] string path,
            [Description("plcSymbolsJson: JSON array of exact PLC symbols, or object with Symbols[]. Example: [\"DB1_MotorData.Motor.Start\"].")] string plcSymbolsJson)
        {
            try
            {
                var data = ClassicHmiMinimalPackageBuilder.ValidateFilesWithPlcSymbols(path, plcSymbolsJson);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Classic HMI minimal package PLC symbol sync validated offline" : "Classic HMI minimal package PLC symbol sync validation found issues",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true,
                        ["missingPlcSymbolCount"] = data["missingPlcSymbolCount"]?.GetValue<int>() ?? 0
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error validating Classic HMI PLC symbol sync offline: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "RunClassicHmiOfflineValidationSuite"), Description("[L2][HMI]Offline-only helper: run the Classic/Basic HMI validation suite covering PLC symbol extraction, HMI package generation, HMI tag references, and PLC-HMI sync positive/negative gates. It writes reports only to the requested report directory.")]
        public static ResponseJsonReport RunClassicHmiOfflineValidationSuite(
            [Description("reportDirectory: directory where suite files and reports will be written.")] string reportDirectory)
        {
            try
            {
                var data = ClassicHmiOfflineValidationSuite.Run(reportDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Classic HMI offline validation suite passed" : "Classic HMI offline validation suite found issues",
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
                throw McpError.WithRecovery(ex, $"Unexpected error running Classic HMI offline validation suite: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "RunClassicHmiTemporaryImportPreflight"), Description("[L2][HMI]Offline-only helper: run the Classic/Basic HMI temporary-import preflight. It checks TIA V21 environment, Openness group, package files, PLC-HMI sync, and emits an import/readback plan without connecting to TIA Portal or creating projects.")]
        public static ResponseJsonReport RunClassicHmiTemporaryImportPreflight(
            [Description("workspaceRoot: repository/workspace root.")] string workspaceRoot,
            [Description("reportDirectory: directory where preflight files and reports will be written.")] string reportDirectory)
        {
            try
            {
                var data = ClassicHmiTemporaryImportPreflightSuite.Run(workspaceRoot, reportDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Classic HMI temporary import preflight passed" : "Classic HMI temporary import preflight blocked",
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
                throw McpError.WithRecovery(ex, $"Unexpected error running Classic HMI temporary import preflight: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "RunHmiTemplatePlcSyncPrecheckSuite"), Description("[L2][Reports]Offline-only helper: verify Unified HMI template RequiredTags against real PLC tag/DB-member XML symbols before any HMI binding. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport RunHmiTemplatePlcSyncPrecheckSuite(
            [Description("templateDirectory: directory containing Unified HMI template JSON files.")] string templateDirectory,
            [Description("plcXmlPath: PLC XML file or directory exported from TIA, containing tag tables and/or GlobalDB XML.")] string plcXmlPath,
            [Description("reportDirectory: directory where reports will be written.")] string reportDirectory,
            [Description("mappingFilePath: optional explicit mapping file produced by the mapping skeleton flow.")] string mappingFilePath = "")
        {
            try
            {
                var data = HmiTemplatePlcSyncPrecheckSuite.Run(templateDirectory, plcXmlPath, reportDirectory, mappingFilePath);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "HMI template PLC sync precheck suite completed" : "HMI template PLC sync precheck suite found blocking issues",
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
                throw McpError.WithRecovery(ex, $"Unexpected error running HMI template PLC sync precheck suite: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "BuildUnifiedHmiTemplateApplyDesignJson"), Description("[L2][HMI-Unified]Offline-only helper: convert one Unified HMI template JSON file into the execution JSON accepted by ApplyUnifiedHmiScreenDesignJson, with layout QA attached. It does not connect to TIA Portal or modify projects.")]
#endif
        public static ResponseJsonReport BuildUnifiedHmiTemplateApplyDesignJson(
            [Description("templateFile: full path to a Unified HMI template JSON file")] string templateFile,
            [Description("fallbackWidth: width used only if the template omits Screen.Width")] int fallbackWidth = 800,
            [Description("fallbackHeight: height used only if the template omits Screen.Height")] int fallbackHeight = 480)
        {
            try
            {
                var layout = HmiTemplateLayoutAnalyzer.AnalyzeFile(templateFile, path =>
                {
                    var templateRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                    var expectedItems = (templateRoot?["Items"] as JsonArray ?? templateRoot?["items"] as JsonArray ?? new JsonArray()).Count;
                    var design = HmiTemplateDesignJsonBuilder.BuildApplyDesign(path, fallbackWidth, fallbackHeight);
                    return design["items"] is JsonArray executionItems && executionItems.Count == expectedItems;
                });
                var designJson = HmiTemplateDesignJsonBuilder.BuildApplyDesign(templateFile, fallbackWidth, fallbackHeight);
                var ok = string.Equals(layout["status"]?.ToString(), "pass", StringComparison.OrdinalIgnoreCase);
                var itemCount = (designJson["items"] as JsonArray)?.Count ?? 0;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Unified HMI template execution design JSON built offline" : "Unified HMI template execution design JSON built with blocking layout findings",
                    Data = new JsonObject
                    {
                        ["format"] = "tia-unified-hmi-template-apply-design-offline-v1",
                        ["timestamp"] = DateTime.Now.ToString("O"),
                        ["offlineOnly"] = true,
                        ["templateFile"] = templateFile,
                        ["ok"] = ok,
                        ["itemCount"] = itemCount,
                        ["layoutQa"] = layout,
                        ["applyDesign"] = designJson
                    },
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true,
                        ["itemCount"] = itemCount
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building Unified HMI template execution design JSON: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "BuildUnifiedHmiTemplateApplyDesignManifest"), Description("[L2][HMI-Unified]Offline-only helper: build a directory-level manifest for Unified HMI templates. It summarizes layout QA and execution-design readiness for every unified_*.json template without returning full apply payloads. It does not connect to TIA Portal or modify projects.")]
#endif
        public static ResponseJsonReport BuildUnifiedHmiTemplateApplyDesignManifest(
            [Description("templateDirectory: directory containing Unified HMI JSON templates")] string templateDirectory,
            [Description("fallbackWidth: width used only if a template omits Screen.Width")] int fallbackWidth = 800,
            [Description("fallbackHeight: height used only if a template omits Screen.Height")] int fallbackHeight = 480)
        {
            try
            {
                var files = Directory.Exists(templateDirectory)
                    ? Directory.GetFiles(templateDirectory, "*.json", SearchOption.TopDirectoryOnly)
                        .Where(path => Path.GetFileName(path).StartsWith("unified_", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Array.Empty<string>();
                var rows = new JsonArray();
                var referenceAnalysis = HmiTemplateReferenceAnalyzer.Analyze(templateDirectory, "", "");
                var referenceRows = (referenceAnalysis["templates"] as JsonArray ?? new JsonArray())
                    .OfType<JsonObject>()
                    .ToDictionary(
                        x => x["templateName"]?.ToString() ?? "",
                        x => x,
                        StringComparer.OrdinalIgnoreCase);
                var referenceRowsByFile = (referenceAnalysis["templates"] as JsonArray ?? new JsonArray())
                    .OfType<JsonObject>()
                    .Where(x => !string.IsNullOrWhiteSpace(x["file"]?.ToString()))
                    .ToDictionary(
                        x => Path.GetFullPath(x["file"]?.ToString() ?? ""),
                        x => x,
                        StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    var templateName = Path.GetFileNameWithoutExtension(file);
                    referenceRowsByFile.TryGetValue(Path.GetFullPath(file), out var referenceRow);
                    if (referenceRow == null)
                    {
                        referenceRows.TryGetValue(templateName, out referenceRow);
                    }
                    rows.Add(BuildUnifiedHmiTemplateApplyDesignManifestRow(file, fallbackWidth, fallbackHeight, referenceRow));
                }

                var failed = rows.OfType<JsonObject>().Count(x => x["ok"]?.GetValue<bool>() != true);
                var totalItems = rows.OfType<JsonObject>().Sum(x => x["itemCount"]?.GetValue<int>() ?? 0);
                var root = new JsonObject
                {
                    ["format"] = "tia-unified-hmi-template-apply-design-manifest-v1",
                    ["timestamp"] = DateTime.Now.ToString("O"),
                    ["offlineOnly"] = true,
                    ["templateDirectory"] = templateDirectory,
                    ["templateCount"] = files.Length,
                    ["failed"] = failed,
                    ["totalItems"] = totalItems,
                    ["ok"] = failed == 0,
                    ["policy"] = new JsonObject
                    {
                        ["fullPayloadTool"] = "BuildUnifiedHmiTemplateApplyDesignJson",
                        ["applyTool"] = "ApplyUnifiedHmiScreenDesignJson",
                        ["rule"] = "Use this manifest as a pre-apply gate; inspect a full payload for any template before writing it to TIA."
                    },
                    ["templates"] = rows
                };

                var ok = failed == 0;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Unified HMI template execution design manifest built offline" : "Unified HMI template execution design manifest has blocking findings",
                    Data = root,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true,
                        ["templateCount"] = files.Length,
                        ["failed"] = failed,
                        ["totalItems"] = totalItems
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building Unified HMI template execution design manifest: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        private static JsonObject BuildUnifiedHmiTemplateApplyDesignManifestRow(string templateFile, int fallbackWidth, int fallbackHeight, JsonObject? referenceRow)
        {
            var layout = HmiTemplateLayoutAnalyzer.AnalyzeFile(templateFile, path =>
            {
                var templateRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                var expectedItems = (templateRoot?["Items"] as JsonArray ?? templateRoot?["items"] as JsonArray ?? new JsonArray()).Count;
                var design = HmiTemplateDesignJsonBuilder.BuildApplyDesign(path, fallbackWidth, fallbackHeight);
                return design["items"] is JsonArray executionItems && executionItems.Count == expectedItems;
            });
            var designJson = HmiTemplateDesignJsonBuilder.BuildApplyDesign(templateFile, fallbackWidth, fallbackHeight);
            var items = designJson["items"] as JsonArray ?? new JsonArray();
            var errors = layout["errors"] as JsonArray ?? new JsonArray();
            var warnings = layout["warnings"] as JsonArray ?? new JsonArray();
            var ok = string.Equals(layout["status"]?.ToString(), "pass", StringComparison.OrdinalIgnoreCase);
            var eventReadiness = BuildUnifiedHmiTemplateEventReadiness(referenceRow);
            var row = new JsonObject
            {
                ["templateFile"] = templateFile,
                ["templateName"] = layout["templateName"]?.DeepClone(),
                ["ok"] = ok,
                ["status"] = layout["status"]?.DeepClone(),
                ["width"] = designJson["width"]?.DeepClone(),
                ["height"] = designJson["height"]?.DeepClone(),
                ["itemCount"] = items.Count,
                ["errorCount"] = errors.Count,
                ["warningCount"] = warnings.Count,
                ["errors"] = new JsonArray(errors.Select(x => x?.DeepClone()).ToArray()),
                ["warnings"] = new JsonArray(warnings.Select(x => x?.DeepClone()).ToArray()),
                ["layoutDensity"] = layout["layoutDensity"]?.DeepClone(),
                ["applyDesignReady"] = layout["executionJsonChecked"]?.DeepClone(),
                ["requiredTagCount"] = GetManifestInt(referenceRow, "requiredTagCount"),
                ["dynamizationCount"] = GetManifestInt(referenceRow, "dynamizationCount"),
                ["actionCount"] = GetManifestInt(referenceRow, "actionCount"),
                ["eventReadiness"] = eventReadiness,
                ["recommendedNextAction"] = ok
                    ? "Inspect full payload with BuildUnifiedHmiTemplateApplyDesignJson, then apply in a temporary TIA project before using a real project."
                    : "Fix blocking layout/template findings before applying to TIA."
            };
            row["eventRecommendedNextAction"] = eventReadiness["recommendedNextAction"]?.DeepClone();
            return row;
        }

        private static JsonObject BuildUnifiedHmiTemplateEventReadiness(JsonObject? referenceRow)
        {
            if (referenceRow == null)
            {
                return new JsonObject
                {
                    ["status"] = "not-analyzed",
                    ["effectiveActionCount"] = 0,
                    ["safeDeterministicActionCount"] = 0,
                    ["apiDiscoveryRequiredCount"] = 0,
                    ["highRiskActionCount"] = 0,
                    ["todoActionCount"] = 0,
                    ["missingTargetCount"] = 0,
                    ["duplicateActionCount"] = 0,
                    ["commandActionCount"] = 0,
                    ["navigationActionCount"] = 0,
                    ["recommendedNextAction"] = "Run HmiTemplateReferenceAnalyzer first; event and binding readiness could not be joined for this template."
                };
            }

            var summary = referenceRow["actionRecipeSummary"] as JsonObject ?? new JsonObject();
            var effectiveRecipes = summary["effectiveRecipes"] as JsonArray ?? new JsonArray();
            var generated = new JsonArray();
            var safeDeterministic = 0;
            var apiDiscovery = 0;
            var todo = 0;
            var blocked = 0;
            var command = 0;
            var navigation = 0;

            foreach (var recipeNode in effectiveRecipes.OfType<JsonObject>())
            {
                var targetTags = (recipeNode["targetTags"] as JsonArray ?? new JsonArray()).Select(x => x?.ToString() ?? "");
                var built = HmiActionScriptRecipeBuilder.Build(
                    recipeNode["recipeKind"]?.ToString() ?? "",
                    recipeNode["event"]?.ToString() ?? "",
                    targetTags,
                    recipeNode["targetScreen"]?.ToString() ?? "",
                    recipeNode["targetPopup"]?.ToString() ?? "");
                generated.Add(built);
                var kind = built["recipeKind"]?.ToString() ?? "";
                var safety = built["safetyLevel"]?.ToString() ?? "";
                if (string.Equals(safety, "command", StringComparison.OrdinalIgnoreCase)) command++;
                if (string.Equals(safety, "navigation", StringComparison.OrdinalIgnoreCase)) navigation++;
                if (built["requiresApiDiscovery"]?.GetValue<bool>() == true) apiDiscovery++;
                if (built["applyBlocked"]?.GetValue<bool>() == true || !string.IsNullOrWhiteSpace(built["applyBlockedReason"]?.ToString())) blocked++;
                if ((built["script"]?.ToString() ?? "").IndexOf("TODO", StringComparison.OrdinalIgnoreCase) >= 0) todo++;
                if (built["ok"]?.GetValue<bool>() == true
                    && built["requiresApiDiscovery"]?.GetValue<bool>() != true
                    && (kind.Equals("set-bit", StringComparison.OrdinalIgnoreCase)
                        || kind.Equals("reset-bit", StringComparison.OrdinalIgnoreCase)
                        || kind.Equals("toggle-bit", StringComparison.OrdinalIgnoreCase)))
                {
                    safeDeterministic++;
                }
            }

            var missingTargets = summary["missingTargets"] as JsonArray ?? new JsonArray();
            var duplicateActions = summary["duplicateActions"] as JsonArray ?? new JsonArray();
            var highRisk = GetManifestInt(summary, "highRiskWrites");
            var missingRequiredTags = summary["missingRequiredTags"] as JsonArray ?? new JsonArray();
            var status = "ready-for-temp-project-validation";
            var recommended = "Generate safe deterministic scripts, then verify HMI tags, PLC-side symbols, TIA SyntaxCheck, and readback in a temporary project.";

            if (missingRequiredTags.Count > 0 || missingTargets.Count > 0 || duplicateActions.Count > 0)
            {
                status = "needs-template-fix";
                recommended = "Fix missing action tags, missing target screens/popups, or duplicate actions before applying events.";
            }
            else if (highRisk > 0)
            {
                status = "blocked-by-high-risk-actions";
                recommended = "High-risk value writes require explicit operator confirmation, range validation, SyntaxCheck, and readback before any apply path is enabled.";
            }
            else if (apiDiscovery > 0 || todo > 0)
            {
                status = "needs-api-discovery";
                recommended = "Keep API-discovery actions blocked until the exact WinCC Unified V21 event/navigation/popup API is verified from TIA readback.";
            }

            return new JsonObject
            {
                ["status"] = status,
                ["requiredTagCount"] = GetManifestInt(referenceRow, "requiredTagCount"),
                ["dynamizationCount"] = GetManifestInt(referenceRow, "dynamizationCount"),
                ["actionCount"] = GetManifestInt(referenceRow, "actionCount"),
                ["effectiveActionCount"] = GetManifestInt(summary, "effectiveActionCount"),
                ["safeDeterministicActionCount"] = safeDeterministic,
                ["apiDiscoveryRequiredCount"] = apiDiscovery,
                ["blockedActionCount"] = blocked,
                ["highRiskActionCount"] = highRisk,
                ["todoActionCount"] = todo,
                ["missingRequiredTagCount"] = missingRequiredTags.Count,
                ["missingTargetCount"] = missingTargets.Count,
                ["duplicateActionCount"] = duplicateActions.Count,
                ["commandActionCount"] = command,
                ["navigationActionCount"] = navigation,
                ["recommendedNextAction"] = recommended
            };
        }

#if !TIA_V18
        [McpServerTool(Name = "BindUnifiedHmiButtonPressedTag"), Description("[L2][HMI-Unified]Bind a Unified HMI button PressedStateTags entry to an HMI tag (momentary press behavior, best-effort).")]
#endif
        public static ResponseMessage BindUnifiedHmiButtonPressedTag(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("tagName: HMI tag name to write while pressed")] string tagName)
        {
            try
            {
                return Portal.BindUnifiedHmiButtonPressedTag(hmiSoftwarePath, screenName, buttonName, tagName);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error binding button '{buttonName}' to tag '{tagName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "ListUnifiedHmiApiTypes"), Description("[L2][HMI-Unified]List loaded WinCC Unified HMI API types/enums by name filter, useful for discovering event and dynamization types.")]
#endif
        public static ResponseStringList ListUnifiedHmiApiTypes(
            [Description("nameContains: case-insensitive substring filter, e.g. Dynamization or EventType")] string nameContains = "",
            [Description("limit: max returned type lines")] int limit = 500)
        {
            try
            {
                var items = Portal.ListUnifiedHmiApiTypes(nameContains, limit);
                return new ResponseStringList
                {
                    Message = $"Unified HMI API types listed (filter='{nameContains}')",
                    Items = items,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing Unified HMI API types: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiButtonEventHandler"), Description("[L2][HMI-Unified]Ensure a Unified HMI button event handler exists and return its API shape. eventType must match HmiButtonEventType.")]
#endif
        public static ResponseMessage EnsureUnifiedHmiButtonEventHandler(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: HmiButtonEventType value, e.g. Tapped, Down, Up; use ListUnifiedHmiApiTypes('HmiButtonEventType') to inspect")] string eventType)
        {
            try
            {
                return Portal.EnsureUnifiedHmiButtonEventHandler(hmiSoftwarePath, screenName, buttonName, eventType);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring button event handler '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "DescribeUnifiedHmiButtonEventScript"), Description("[L2][HMI-Unified]Describe a Unified HMI button event handler Script property and its current object members/attributes.")]
#endif
        public static ResponseObjectDescribe DescribeUnifiedHmiButtonEventScript(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: enum value, e.g. Tapped, Down, Up")] string eventType,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeUnifiedHmiButtonEventScript(hmiSoftwarePath, screenName, buttonName, eventType, maxMembers);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (res.Members != null && res.Members.Any()) };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error describing button event script '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "SetUnifiedHmiButtonEventScriptCode"), Description("[L2][HMI-Unified]Set ScriptCode on a Unified HMI button event ScriptDynamization and run SyntaxCheck.")]
#endif
        public static ResponseMessage SetUnifiedHmiButtonEventScriptCode(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: enum value, e.g. Tapped, Down, Up")] string eventType,
            [Description("scriptCode: JavaScript code for the event")] string scriptCode,
            [Description("globalDefinitionAreaScriptCode: optional global definitions for the script")] string globalDefinitionAreaScriptCode = "",
            [Description("async: whether the script is async")] bool async = false)
        {
            try
            {
                return Portal.SetUnifiedHmiButtonEventScriptCode(hmiSoftwarePath, screenName, buttonName, eventType, scriptCode, globalDefinitionAreaScriptCode, async);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error setting button event script '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "BuildUnifiedHmiButtonActionScript"), Description("[L2][HMI-Unified]Build a safe Unified HMI button action script from a high-level action recipe without connecting to TIA.")]
#endif
        public static ResponseMessage BuildUnifiedHmiButtonActionScript(
            [Description("actionKind: set-bit, reset-bit, toggle-bit, open-popup, goto-screen, confirm-write")] string actionKind,
            [Description("eventType: HmiButtonEventType value, e.g. Down (press), Up (release), Tapped — NOT Pressed/Released")] string eventType,
            [Description("targetTag: target HMI tag for set/reset/toggle actions")] string targetTag = "",
            [Description("targetScreen: target screen for goto-screen actions")] string targetScreen = "",
            [Description("targetPopup: target popup for open-popup actions")] string targetPopup = "")
        {
            try
            {
                var tags = string.IsNullOrWhiteSpace(targetTag)
                    ? Array.Empty<string>()
                    : new[] { targetTag };
                var recipe = HmiActionScriptRecipeBuilder.Build(actionKind, eventType, tags, targetScreen, targetPopup);
                return new ResponseMessage
                {
                    Message = recipe["ok"]?.GetValue<bool>() == true
                        ? "Unified HMI button action script recipe built."
                        : "Unified HMI button action script recipe has validation errors.",
                    Meta = recipe
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error building Unified HMI button action script: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

// [DISABLED-A]         [McpServerTool(Name = "RunHmiActionScriptRecipeSafetySelfTest"), Description("[L2][Diagnostics]Offline-only helper: prove deterministic HMI button action scripts are allowed only for safe set/reset/toggle bit recipes, while high-risk writes and unverified navigation/popup recipes are blocked.")]
        public static ResponseJsonReport RunHmiActionScriptRecipeSafetySelfTest()
        {
            try
            {
                var data = HmiActionScriptRecipeBuilder.RunSafetySelfTest();
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "HMI action script recipe safety self-test passed" : "HMI action script recipe safety self-test failed",
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
                throw McpError.WithRecovery(ex, $"Unexpected error running HMI action script recipe safety self-test: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiButtonAction"), Description("[L2][HMI-Unified]Generate and apply a deterministic Unified HMI button action. Only set-bit/reset-bit/toggle-bit are applied; high-risk or TODO recipes are rejected.")]
#endif
        public static ResponseMessage EnsureUnifiedHmiButtonAction(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: HmiButtonEventType value, e.g. Down (press), Up (release), Tapped — NOT Pressed/Released")] string eventType,
            [Description("actionKind: set-bit, reset-bit, or toggle-bit")] string actionKind,
            [Description("targetTag: verified target HMI tag")] string targetTag)
        {
            try
            {
                var recipe = HmiActionScriptRecipeBuilder.Build(actionKind, eventType, new[] { targetTag });
                var kind = recipe["recipeKind"]?.ToString() ?? "";
                var script = recipe["script"]?.ToString() ?? "";
                var allowed = new[] { "set-bit", "reset-bit", "toggle-bit" };
                if (!allowed.Contains(kind, StringComparer.OrdinalIgnoreCase))
                {
                    recipe["applyStatus"] = "rejected";
                    recipe["applyReason"] = "Only set-bit/reset-bit/toggle-bit recipes can be applied by this safe high-level tool.";
                    return new ResponseMessage { Message = "Unified HMI button action rejected by safety policy.", Meta = recipe };
                }
                if (recipe["ok"]?.GetValue<bool>() != true || string.IsNullOrWhiteSpace(script) || script.IndexOf("TODO", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    recipe["applyStatus"] = "rejected";
                    recipe["applyReason"] = "Recipe has errors, empty script, or TODO placeholder.";
                    return new ResponseMessage { Message = "Unified HMI button action rejected because the generated script is not directly applicable.", Meta = recipe };
                }

                var ensure = Portal.EnsureUnifiedHmiButtonEventHandler(hmiSoftwarePath, screenName, buttonName, eventType);
                var set = Portal.SetUnifiedHmiButtonEventScriptCode(hmiSoftwarePath, screenName, buttonName, eventType, script, "", false);
                recipe["applyStatus"] = set.Meta?["success"]?.GetValue<bool>() == true ? "applied" : "apply-failed";
                recipe["ensureMessage"] = ensure.Message ?? "";
                recipe["setMessage"] = set.Message ?? "";
                recipe["setMeta"] = set.Meta?.DeepClone();
                return new ResponseMessage
                {
                    Message = "Unified HMI button action applied via generated recipe.",
                    Meta = recipe
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error applying Unified HMI button action '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "EnsureUnifiedHmiDynamization"), Description("[L2][HMI-Unified]Ensure a Unified HMI item property dynamization exists using a concrete dynamization type and return its API shape.")]
#endif
        public static ResponseMessage EnsureUnifiedHmiDynamization(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("itemName: HMI screen item name")] string itemName,
            [Description("propertyName: target property name, e.g. BackColor or Visible")] string propertyName,
            [Description("dynamizationType: type short name/full name; empty tries common candidates and returns errors if unsupported")] string dynamizationType = "")
        {
            try
            {
                return Portal.EnsureUnifiedHmiDynamization(hmiSoftwarePath, screenName, itemName, propertyName, dynamizationType);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error ensuring dynamization '{itemName}.{propertyName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "BindUnifiedHmiTagDynamization"), Description("[L2][HMI-Unified]Ensure a Unified HMI TagDynamization exists for an item property and bind it to an HMI tag.")]
#endif
        public static ResponseMessage BindUnifiedHmiTagDynamization(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("itemName: HMI screen item name")] string itemName,
            [Description("propertyName: target property name, e.g. BackColor or Visible")] string propertyName,
            [Description("tagName: HMI tag name used as the dynamic source")] string tagName,
            [Description("dataType: tag data type, e.g. Bool, Int, Real")] string dataType = "Bool",
            [Description("plcTag: optional PLC tag/path")] string plcTag = "",
            [Description("address: optional absolute address")] string address = "")
        {
            try
            {
                return Portal.BindUnifiedHmiTagDynamization(hmiSoftwarePath, screenName, itemName, propertyName, tagName, dataType, plcTag, address);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error binding tag dynamization '{itemName}.{propertyName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetHmiScreens"), Description("[L2][HMI] List all screen names in an HMI (Classic or Unified). Requires: Connect + OpenProject. softwarePath from GetProjectTree. Use before EnsureUnifiedHmiScreen/ExportHmiScreen to confirm which screens exist.")]
        public static ResponseStringList GetHmiScreens(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetHmiScreens(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI screens listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing HMI screens for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetHmiTagTables"), Description("[L2][HMI]List HMI tag table names (Classic/Unified, best-effort)")]
        public static ResponseStringList GetHmiTagTables(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetHmiTagTables(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI tag tables listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing HMI tag tables for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetHmiTags"), Description("[L2][HMI]List HMI tag names (best-effort). If tagTableName empty, returns tags found at root collection if available.")]
        public static ResponseStringList GetHmiTags(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("tagTableName: optional tag table name to list tags from")] string tagTableName = "")
        {
            try
            {
                var items = Portal.GetHmiTags(softwarePath, tagTableName);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI tags listed for '{softwarePath}' (table='{tagTableName}')",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing HMI tags for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetHmiConnections"), Description("[L2][HMI]List HMI connection names (Classic/Unified, best-effort)")]
        public static ResponseStringList GetHmiConnections(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetHmiConnections(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI connections listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing HMI connections for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportHmiScreen"), Description("[L2][HMI]Export one HMI screen to a file (best-effort; requires Openness export support)")]
        public static ResponseExportFile ExportHmiScreen(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: the screen name to export")] string screenName,
            [Description("exportPath: full file path to write to (e.g. C:\\\\temp\\\\screen.xml)")] string exportPath)
        {
            try
            {
                Portal.ExportHmiScreen(softwarePath, screenName, exportPath);
                return new ResponseExportFile
                {
                    Message = $"HMI screen '{screenName}' exported",
                    ExportPath = exportPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed exporting HMI screen '{screenName}' from '{softwarePath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error exporting HMI screen '{screenName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportHmiTagTable"), Description("[L2][HMI]Export one HMI tag table to a file (best-effort; requires Openness export support)")]
        public static ResponseExportFile ExportHmiTagTable(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("tagTableName: the tag table name to export")] string tagTableName,
            [Description("exportPath: full file path to write to (e.g. C:\\\\temp\\\\tagtable.xml)")] string exportPath)
        {
            try
            {
                Portal.ExportHmiTagTable(softwarePath, tagTableName, exportPath);
                return new ResponseExportFile
                {
                    Message = $"HMI tag table '{tagTableName}' exported",
                    ExportPath = exportPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed exporting HMI tag table '{tagTableName}' from '{softwarePath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error exporting HMI tag table '{tagTableName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportHmiConnection"), Description("[L2][HMI]Export one HMI connection to a file (best-effort; Classic/Unified via reflection)")]
        public static ResponseExportFile ExportHmiConnection(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("connectionName: the HMI connection name to export")] string connectionName,
            [Description("exportPath: full file path to write to (e.g. C:\\\\temp\\\\connection.xml)")] string exportPath)
        {
            try
            {
                Portal.ExportHmiConnection(softwarePath, connectionName, exportPath);
                return new ResponseExportFile
                {
                    Message = $"HMI connection '{connectionName}' exported",
                    ExportPath = exportPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed exporting HMI connection '{connectionName}' from '{softwarePath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error exporting HMI connection '{connectionName}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportHmiProgram"), Description("[L2][HMI]Batch export HMI screens/tagtables into a directory (best-effort)")]
        public static ResponseBatchExport ExportHmiProgram(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("exportDir: directory to write exported files into")] string exportDir,
            [Description("exportScreens: default true")] bool exportScreens = true,
            [Description("exportTagTables: default true")] bool exportTagTables = true)
        {
            try
            {
                var res = Portal.ExportHmiProgram(softwarePath, exportDir, exportScreens, exportTagTables);
                if (res != null)
                {
                    return new ResponseBatchExport
                    {
                        Message = $"HMI program exported to '{exportDir}'",
                        Exported = res.Value.Exported,
                        Failed = res.Value.Failed,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }
                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error exporting HMI program: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportHmiScreen"), Description("[L2][HMI]Import one HMI screen XML file into an HMI program (best-effort; Classic/Unified via reflection)")]
        public static ResponseMessage ImportHmiScreen(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional screen group path inside HMI (use empty for root)")] string folderPath,
            [Description("importPath: full file path of exported screen XML")] string importPath)
        {
            try
            {
                Portal.ImportHmiScreen(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"HMI screen imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed importing HMI screen from '{importPath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing HMI screen: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportHmiTagTable"), Description("[L2][HMI]Import one HMI tag table XML file into an HMI program (best-effort; Classic/Unified via reflection)")]
        public static ResponseMessage ImportHmiTagTable(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional tag table group path inside HMI (use empty for root)")] string folderPath,
            [Description("importPath: full file path of exported tag table XML")] string importPath)
        {
            try
            {
                Portal.ImportHmiTagTable(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"HMI tag table imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed importing HMI tag table from '{importPath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing HMI tag table: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportHmiConnection"), Description("[L2][HMI]Import one HMI connection XML file into an HMI program (best-effort; Classic/Unified via reflection)")]
        public static ResponseMessage ImportHmiConnection(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("importPath: full file path of exported HMI connection XML")] string importPath)
        {
            try
            {
                Portal.ImportHmiConnection(softwarePath, importPath);
                return new ResponseMessage
                {
                    Message = $"HMI connection imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed importing HMI connection from '{importPath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing HMI connection: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportHmiScreensFromDirectory"), Description("[L2][HMI]Batch import HMI screen .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportHmiScreensFromDirectory(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional screen group path inside HMI (use empty for root)")] string folderPath,
            [Description("dir: directory containing exported screen XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportHmiScreensFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} HMI screens from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing HMI screens from '{dir}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportHmiTagTablesFromDirectory"), Description("[L2][HMI]Batch import HMI tag table .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportHmiTagTablesFromDirectory(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional tag table group path inside HMI (use empty for root)")] string folderPath,
            [Description("dir: directory containing exported tag table XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportHmiTagTablesFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} HMI tag tables from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing HMI tag tables from '{dir}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "AnalyzeHmiTemplateReference"), Description("[L2][HMI-Library]Analyze local Unified HMI JSON templates against reference-project/runtime/global-library hints offline. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport AnalyzeHmiTemplateReference(
            [Description("templateDirectory: directory containing Unified HMI JSON templates")] string templateDirectory,
            [Description("referenceProjectPath: reference TIA project folder containing HMI runtime export/currentConfiguration")] string referenceProjectPath,
            [Description("referenceGlobalLibraryPath: reference global library folder or .al* file")] string referenceGlobalLibraryPath)
        {
            try
            {
                var data = HmiTemplateReferenceAnalyzer.Analyze(templateDirectory, referenceProjectPath, referenceGlobalLibraryPath);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "HMI template/reference offline analysis completed" : "HMI template/reference offline analysis completed with findings",
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
                throw McpError.WithRecovery(ex, $"Unexpected error analyzing HMI template/reference assets: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

#if !TIA_V18
        [McpServerTool(Name = "AnalyzeUnifiedHmiTemplateLayout"), Description("[L2][HMI-Library]Offline-only QA for Unified HMI JSON templates. Checks theme metadata, screen bounds, duplicate item names, size issues, layout overlap warnings, density, and execution JSON shape. It does not connect to TIA Portal or modify projects.")]
#endif
        public static ResponseJsonReport AnalyzeUnifiedHmiTemplateLayout(
            [Description("templateDirectory: directory containing Unified HMI JSON templates")] string templateDirectory)
        {
            try
            {
                var data = HmiTemplateLayoutAnalyzer.AnalyzeDirectory(templateDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Unified HMI template layout offline QA completed" : "Unified HMI template layout offline QA found blocking issues",
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
                throw McpError.WithRecovery(ex, $"Unexpected error analyzing Unified HMI template layout: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        #endregion
    }
}
