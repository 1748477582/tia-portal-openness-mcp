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
    // Partial: PLC software core (blocks / types / tag-tables / external sources / compile). Split out of McpServer.PlcSoftware.cs (god-file split); behavior unchanged.
    public static partial class McpServer
    {
        #region plc software

        [McpServerTool(Name = "GetSoftwareInfo"), Description("[L1][PLC-Software] Get PLC software properties (language, version, block counts). Requires: Connect + OpenProject. softwarePath comes from GetProjectTree (e.g. 'PLC_1'). Use GetSoftwareTree for the full block hierarchy.")]
        public static ResponseSoftwareInfo GetSoftwareInfo(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath)
        {
            // Openness compliance: enumerate software attributes on the STA thread.
            return Portal.RunOnSta(() =>
            {
                try
                {
                    var software = Portal.GetPlcSoftware(softwarePath);
                    if (software != null)
                    {

                        var attributes = Helper.GetAttributeList(software);

                        return new ResponseSoftwareInfo
                        {
                            Message = $"Software info retrieved from '{softwarePath}'",
                            Name = software.Name,
                            Attributes = attributes,
                            Description = software.ToString(),
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException($"Software not found at '{softwarePath}'", McpErrorCode.InternalError);
                    }
                }
                catch (Exception ex) when (ex is not McpException)
                {
                    throw McpError.WithRecovery(ex, $"Unexpected error retrieving software info from '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
                }
            });
        }

        [McpServerTool(Name = "DescribeObjectProperty"), Description("[L2][Reflection]Describe an object's nested property via reflection (members list). propertyPath supports dotted path.")]
        public static ResponseObjectDescribe DescribeObjectProperty(
            [Description("objectKind: Project|Portal|Device|DeviceItem|Software|Block|Type")] string objectKind,
            [Description("objectPath: object path")] string objectPath,
            [Description("propertyPath: dotted property path, e.g. 'Connections' or 'PressedStateTags'")] string propertyPath,
            [Description("softwarePath: required for Block/Type")] string softwarePath = "",
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeObjectProperty(objectKind, objectPath, propertyPath, softwarePath, maxMembers);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (res.Members != null && res.Members.Any()) };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error describing property '{propertyPath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "BuildPlcUdtXml"), Description("[L2][PLC-Builders][Offline] Build a TIA V21 PLC UDT/PlcStruct XML document from structured JSON. Input: {members:[{name,datatype,externalWritable?,commentZhCn?}]}. It only returns XML; it does not connect to TIA Portal, import types, write files, or modify projects.")]
        public static ResponseXmlBuild BuildPlcUdtXml(
            [Description("udtJson: JSON object with members[]. Required member fields: name, datatype. Optional: externalWritable, commentZhCn/comment.")] string udtJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.BuildUdt(udtJson), "PLC UDT XML built offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid PLC UDT builder input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "BuildPlcTagTableXml"), Description("[L2][PLC-Builders][Offline] Build a TIA V21 PLC tag table XML document from structured JSON. Input: {tableName,tags:[{name,dataTypeName,logicalAddress}]}. It only returns XML; it does not connect to TIA Portal, import tag tables, write files, or modify projects.")]
        public static ResponseXmlBuild BuildPlcTagTableXml(
            [Description("tagTableJson: JSON object with tableName/name and tags[]. Required tag fields: name, dataTypeName/datatype, logicalAddress/address.")] string tagTableJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.BuildTagTable(tagTableJson), "PLC tag table XML built offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid PLC tag table builder input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "BuildPlcGlobalDbXml"), Description("[L2][PLC-Builders][Offline] Build a TIA V21 PLC GlobalDB XML document from structured JSON. Input: {dbName,dbNumber,staticMembers:[{name,datatype,externalWritable?,commentZhCn?,startValue?}]}. It only returns XML; it does not connect to TIA Portal, import blocks, write files, or modify projects.")]
        public static ResponseXmlBuild BuildPlcGlobalDbXml(
            [Description("globalDbJson: JSON object with dbName/name, dbNumber/number, and staticMembers[] or members[].")] string globalDbJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.BuildGlobalDb(globalDbJson), "PLC GlobalDB XML built offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid PLC GlobalDB builder input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "BuildStructuredTextXml"), Description("[L2][PLC-Builders][Offline] Build a TIA V21 StructuredText/v4 XML fragment from operation JSON. Input: {operations:[{op:'if'|'else'|'endif'|'assignment'|'token'|'blank'|'newline', ...}]}. It only returns XML; it does not connect to TIA Portal, import blocks, write files, or modify projects.")]
        public static ResponseXmlBuild BuildStructuredTextXml(
            [Description("structuredTextJson: JSON object with operations[]. assignment uses target + literalValue/value; if uses condition/variable; token uses text.")] string structuredTextJson,
            [Description("innerOnly: true returns only inner XML for embedding into a block composer; false returns <StructuredText>.")] bool innerOnly = false)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.BuildStructuredText(structuredTextJson, innerOnly), "PLC StructuredText XML built offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid StructuredText builder input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "BuildFlgNetCallXml"), Description("[L2][PLC-Builders][Offline] NARROW SCOPE: builds ONLY a LAD network that calls one FC with parameters. For general ladder (contacts/coils/SR/compare/Move/math) author S7DCL text and import with ImportBlocksFromDocuments — there is no XML builder for those, and hand-written FlgNet XML is the usual cause of import errors. Build a TIA V21 LAD FlgNet/v5 FC call network XML from structured JSON. Input: {callName,parameters:[{name,section,dataType,sourceKind?,symbolPath?|symbol?|value?}]}. It only returns XML; it does not connect to TIA Portal, import blocks, write files, or modify projects.")]
        public static ResponseXmlBuild BuildFlgNetCallXml(
            [Description("flgNetJson: JSON object with callName/name and parameters[]. Global parameters use symbolPath[] or dotted symbol; constants use sourceKind='constant' and value.")] string flgNetJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.BuildFlgNetCall(flgNetJson), "PLC FlgNet call XML built offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid FlgNet call builder input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "ComposePlcFcBlockXml"), Description("[L2][PLC-Builders][Offline] Compose a TIA V21 SCL FC block XML from interface JSON and StructuredText content. Input: {blockName,blockNumber,inputs:[{name,datatype}],outputs:[{name,datatype}],structuredTextInnerXml? or structuredText:{operations:[]}}. It only returns XML; it does not connect to TIA Portal, import blocks, write files, or modify projects.")]
        public static ResponseXmlBuild ComposePlcFcBlockXml(
            [Description("fcBlockJson: JSON object with blockName/name, blockNumber/number, inputs[], outputs[], and structuredTextInnerXml or structuredText.operations[].")] string fcBlockJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.ComposeFcBlock(fcBlockJson), "PLC FC block XML composed offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid PLC FC composer input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "ComposePlcFbBlockXml"), Description("[L2][PLC-Builders][Offline] Compose a TIA V21 SCL FB block XML from interface JSON and StructuredText content. Input: {blockName,blockNumber,inputs?,outputs?,inouts?,statics?,temps?,structuredTextInnerXml? or structuredText:{operations:[]}}. It only returns XML; it does not connect to TIA Portal, import blocks, write files, create instance DBs, or modify projects.")]
        public static ResponseXmlBuild ComposePlcFbBlockXml(
            [Description("fbBlockJson: JSON object with blockName/name, blockNumber/number, optional inputs/outputs/inouts/statics/temps arrays, and structuredTextInnerXml or structuredText.operations[].")] string fbBlockJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.ComposeFbBlock(fbBlockJson), "PLC FB block XML composed offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid PLC FB composer input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "ComposePlcLadFcBlockXml"), Description("[L2][PLC-Builders][Offline] NARROW SCOPE: every network must be an FC call; this cannot emit contacts/coils/SR/compare/Move/math. For general ladder, author S7DCL text (.s7dcl + .s7res) and import with ImportBlocksFromDocuments instead. Compose a TIA V21 LAD FC block XML containing one or more FlgNet/v5 FC-call networks. Each network is an FC call described as { callJson: { callName, parameters[] }, titleZhCn?, commentZhCn? }. Top-level: blockName, blockNumber, optional inputs/outputs members, optional commentZhCn / titleZhCn. Returns XML only; does not connect to TIA Portal or import. Pair with ImportBlock.")]
        public static ResponseXmlBuild ComposePlcLadFcBlockXml(
            [Description("ladFcBlockJson: JSON object with blockName, blockNumber, networks[] (each with callJson{callName,parameters[]}, optional titleZhCn/commentZhCn), optional inputs[]/outputs[] interface members with commentZhCn, optional commentZhCn/titleZhCn block-level.")] string ladFcBlockJson)
        {
            try
            {
                return BuildOfflineXmlBuilderReport(PlcBuilderToolJson.ComposeLadFcBlock(ladFcBlockJson), "PLC LAD FC block XML composed offline");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid PLC LAD FC composer input: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "PlcBuildAndImport"), Description("[L1][PLC-Software] MAIN tool for creating new PLC blocks from natural language. Build one PLC artifact (UDT/tag table/GlobalDB/FC/FB) from structured JSON, then optionally import and compile. Use dryRun=true first to validate. Workflow: describe block in JSON → dryRun → review → dryRun=false to import. Replaces the multi-step Build*Xml + ImportBlock sequence.")]
        public static ResponsePlcProgramImport PlcBuildAndImport(
            [Description("softwarePath: PLC software path, e.g. 'PLC_1'. Required only when dryRun=false.")] string softwarePath,
            [Description("kind: udt|tagtable|globaldb|fc|fb")] string kind,
            [Description("json: structured JSON matching the corresponding BuildPlc* tool.")] string json,
            [Description("typeGroupPath: PLC data type group path for kind=udt.")] string typeGroupPath = "",
            [Description("tagFolderPath: PLC tag table group path for kind=tagtable.")] string tagFolderPath = "",
            [Description("blockGroupPath: PLC block group path for kind=globaldb|fc.")] string blockGroupPath = "",
            [Description("compileAfter: compile PLC after import when dryRun=false.")] bool compileAfter = true,
            [Description("dryRun: true builds XML and returns the import plan without importing/compiling.")] bool dryRun = true)
        {
            var failed = new List<ImportFailure>();
            var importedTypes = new List<string>();
            var importedTagTables = new List<string>();
            var importedBlocks = new List<string>();
            ResponseCompile? compile = null;

            try
            {
                var normalizedKind = NormalizePlcBuildKind(kind);
                var capability = AnalyzePlcBuildCapability(normalizedKind, json);
                var build = BuildPlcArtifact(normalizedKind, json);
                var xml = build["xml"]?.ToString() ?? "";
                var objectName = ResolveBuiltPlcObjectName(xml);
                var tempDir = Path.Combine(Path.GetTempPath(), "tia_mcp_plc_build_import_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
                Directory.CreateDirectory(tempDir);
                var fileName = MakeSafeFileName(string.IsNullOrWhiteSpace(objectName) ? normalizedKind : objectName) + ".xml";
                var xmlPath = Path.Combine(tempDir, fileName);
                File.WriteAllText(xmlPath, xml, System.Text.Encoding.UTF8);

                var classifiedKind = ClassifyPlcXml(xmlPath, out var subKind, out var classifiedObjectName);
                if (classifiedKind == "unknown")
                {
                    failed.Add(new ImportFailure { Path = xmlPath, Error = "Generated XML could not be classified as a supported PLC XML artifact." });
                }

                var discoveredTypes = classifiedKind == "type" ? new List<string> { classifiedObjectName } : new List<string>();
                var discoveredTagTables = classifiedKind == "tagtable" ? new List<string> { classifiedObjectName } : new List<string>();
                var discoveredBlocks = classifiedKind == "block" ? new List<string> { classifiedObjectName } : new List<string>();

                if (!dryRun && failed.Count == 0)
                    compile = PlcBuildAndImportApply(softwarePath, typeGroupPath, tagFolderPath, blockGroupPath, compileAfter, xmlPath, classifiedKind, classifiedObjectName, importedTypes, importedTagTables, importedBlocks, failed);

                var response = BuildPlcProgramImportResponse(
                    tempDir,
                    dryRun,
                    discoveredTypes,
                    discoveredTagTables,
                    new List<string>(),
                    discoveredBlocks,
                    importedTypes,
                    importedTagTables,
                    new List<string>(),
                    importedBlocks,
                    failed,
                    compile);
                response.BuildKind = normalizedKind;
                response.CapabilityDecision = capability.Decision;
                response.CapabilityWarnings = capability.Warnings;
                response.RecommendedNextActions = capability.NextActions;
                response.GeneratedDirectory = tempDir;
                response.WrittenFiles = new[] { xmlPath };
                response.Message = dryRun
                    ? $"PLC build/import dry-run kind={normalizedKind}: generated '{xmlPath}', classified={classifiedKind}/{subKind}, failed={failed.Count}"
                    : $"PLC build/import kind={normalizedKind}: generated '{xmlPath}', importedTypes={importedTypes.Count}, importedTagTables={importedTagTables.Count}, importedBlocks={importedBlocks.Count}, failed={failed.Count}, compileState={compile?.State ?? "-"}";
                response.Meta ??= new JsonObject();
                response.Meta["offlineBuildOk"] = build["ok"]?.GetValue<bool>() == true;
                response.Meta["classifiedKind"] = classifiedKind;
                response.Meta["classifiedSubKind"] = subKind;
                response.Meta["capabilityDecision"] = capability.Decision;
                response.Meta["capabilityWarnings"] = new JsonArray(capability.Warnings.Select(x => (JsonNode)x).ToArray());
                response.Meta["recommendedNextActions"] = new JsonArray(capability.NextActions.Select(x => (JsonNode)x).ToArray());
                return response;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error running PlcBuildAndImport: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        private static ResponseCompile? PlcBuildAndImportApply(
            string softwarePath,
            string typeGroupPath,
            string tagFolderPath,
            string blockGroupPath,
            bool compileAfter,
            string xmlPath,
            string classifiedKind,
            string classifiedObjectName,
            List<string> importedTypes,
            List<string> importedTagTables,
            List<string> importedBlocks,
            List<ImportFailure> failed)
        {
            if (classifiedKind == "type")
            {
                try { Portal.ImportType(softwarePath, typeGroupPath, xmlPath); importedTypes.Add(classifiedObjectName); }
                catch (PortalException pex) { failed.Add(new ImportFailure { Path = xmlPath, Error = pex.Message }); }
            }
            else if (classifiedKind == "tagtable")
            {
                try { Portal.ImportPlcTagTable(softwarePath, tagFolderPath, xmlPath); importedTagTables.Add(classifiedObjectName); }
                catch (PortalException pex) { failed.Add(new ImportFailure { Path = xmlPath, Error = pex.Message }); }
            }
            else if (classifiedKind == "block")
            {
                try { Portal.ImportBlock(softwarePath, blockGroupPath, xmlPath); importedBlocks.Add(classifiedObjectName); }
                catch (PortalException pex) { failed.Add(new ImportFailure { Path = xmlPath, Error = pex.Message }); }
            }
            else
            {
                failed.Add(new ImportFailure { Path = xmlPath, Error = "Unsupported classified kind: " + classifiedKind });
            }

            if (!compileAfter || failed.Count != 0)
                return null;

            try
            {
                var result = Portal.CompileSoftware(softwarePath);
                return BuildCompileResponse(softwarePath, result);
            }
            catch (PortalException pex)
            {
                failed.Add(new ImportFailure { Path = softwarePath, Error = $"[{pex.Code}] {pex.Message}" });
                return null;
            }
        }

        private static ResponseXmlBuild BuildOfflineXmlBuilderReport(JsonObject data, string successMessage)
        {
            var ok = data["ok"]?.GetValue<bool>() == true;
            var xml = data["xml"]?.GetValue<string>();

            // Builders use one of two error shapes:
            //   PlcBuilderToolJson:        ["error"] = string?
            //   ClassicHmi*XmlBuilder:     ["errors"] = JsonArray of string
            string[]? errorList = null;
            if (data["errors"] is JsonArray errArr)
            {
                errorList = errArr.Where(e => e != null).Select(e => e!.GetValue<string>()).ToArray();
                if (errorList.Length == 0) errorList = null;
            }
            else
            {
                var singleError = data["error"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(singleError))
                    errorList = new[] { singleError! };
            }

            string[]? warningList = null;
            if (data["warnings"] is JsonArray warnArr)
            {
                warningList = warnArr.Where(w => w != null).Select(w => w!.GetValue<string>()).ToArray();
                if (warningList.Length == 0) warningList = null;
            }

            return new ResponseXmlBuild
            {
                Ok = ok,
                Message = ok ? successMessage : successMessage + " with validation findings",
                Data = data,
                Xml = xml,
                Errors = errorList,
                Warnings = warningList,
                Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = ok,
                    ["offlineOnly"] = true
                }
            };
        }

        private static string NormalizePlcBuildKind(string kind)
        {
            var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
            return normalized switch
            {
                "udt" => "udt",
                "type" => "udt",
                "plcstruct" => "udt",
                "tagtable" => "tagtable",
                "plctagtable" => "tagtable",
                "globaldb" => "globaldb",
                "db" => "globaldb",
                "fc" => "fc",
                "function" => "fc",
                "fb" => "fb",
                "functionblock" => "fb",
                _ => throw new ArgumentException("Unsupported PLC build kind. Supported values: udt|tagtable|globaldb|fc|fb.")
            };
        }

        private static JsonObject BuildPlcArtifact(string kind, string json)
        {
            return kind switch
            {
                "udt" => PlcBuilderToolJson.BuildUdt(json),
                "tagtable" => PlcBuilderToolJson.BuildTagTable(json),
                "globaldb" => PlcBuilderToolJson.BuildGlobalDb(json),
                "fc" => PlcBuilderToolJson.ComposeFcBlock(json),
                "fb" => PlcBuilderToolJson.ComposeFbBlock(json),
                _ => throw new ArgumentException("Unsupported PLC build kind: " + kind)
            };
        }

        private sealed class PlcBuildCapabilityDecision
        {
            public string Decision { get; set; } = "xml-dsl";
            public List<string> Warnings { get; } = new();
            public List<string> NextActions { get; } = new();
        }

        private static PlcBuildCapabilityDecision AnalyzePlcBuildCapability(string kind, string json)
        {
            var result = new PlcBuildCapabilityDecision();
            if (kind != "fc" && kind != "fb")
            {
                result.Decision = "declaration-xml";
                result.NextActions.Add("Import generated declaration XML in dependency order, then run CompileAndDiagnosePlc.");
                return result;
            }

            JsonObject? root = null;
            try
            {
                root = JsonNode.Parse(json) as JsonObject;
            }
            catch
            {
                result.Warnings.Add("PLC JSON could not be parsed for capability analysis; builder will still validate the schema.");
                result.NextActions.Add("Fix JSON parsing/schema errors before importing into TIA Portal.");
                return result;
            }

            var structuredText = root?["structuredText"] as JsonObject;
            var operations = structuredText?["operations"] as JsonArray;
            if (operations == null)
            {
                if (root?["structuredTextInnerXml"] != null || root?["structuredTextXml"] != null || root?["sclInnerXml"] != null)
                {
                    result.Decision = "raw-structuredtext-xml";
                    result.Warnings.Add("Raw StructuredText XML was supplied. This path is only safe when cloned from a TIA export or generated by a verified builder.");
                    result.NextActions.Add("Dry-run first, import into a disposable project, then require CompileAndDiagnosePlc errors=0.");
                }
                return result;
            }

            var risky = new List<string>();
            for (var i = 0; i < operations.Count; i++)
            {
                if (operations[i] is not JsonObject op) continue;
                foreach (var name in new[] { "condition", "source", "sym", "name" })
                {
                    if (op[name] is JsonNode n && LooksLikeSclExpression(n.ToString()))
                        risky.Add($"$.structuredText.operations[{i}].{name}='{n}'");
                }
            }

            if (risky.Count > 0)
            {
                result.Decision = "external-scl-recommended";
                result.Warnings.Add("The PLC XML DSL is intentionally narrow. Complex SCL expressions were detected: " + string.Join("; ", risky.Take(8)));
                result.NextActions.Add("Prefer a native .scl/.s7dcl external source and import via ImportFromDocuments/ImportBlocksFromDocuments, or use a verified SCL template from templates/plc/scl-examples.");
                result.NextActions.Add("If you still use XML DSL, split expressions into verified primitive operations and run dryRun=true plus CompileAndDiagnosePlc.");
            }
            else
            {
                result.NextActions.Add("Run dryRun=true first, then import with compileAfter=true and require CompileAndDiagnosePlc errors=0.");
            }

            return result;
        }

        private static bool LooksLikeSclExpression(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var s = value.Trim();
            if (s.StartsWith("\"", StringComparison.Ordinal) && s.EndsWith("\"", StringComparison.Ordinal)) return false;
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
            if (string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase)) return true;
            return s.Any(IsComplexSclToken);
        }

        private static bool IsComplexSclToken(char ch)
        {
            return char.IsWhiteSpace(ch) || ch == '(' || ch == ')' || ch == '+' || ch == '-' || ch == '*' || ch == '/' ||
                   ch == '<' || ch == '>' || ch == '=' || ch == ':' || ch == ';' || ch == ',';
        }

        private static string ResolveBuiltPlcObjectName(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var obj = doc.Root?.Elements().FirstOrDefault(e =>
                    e.Name.LocalName.StartsWith("SW.Types.", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.LocalName.StartsWith("SW.Tags.", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.OrdinalIgnoreCase));
                var attrs = obj?.Element("AttributeList");
                var name = attrs?.Element("Name")?.Value;
                return string.IsNullOrWhiteSpace(name) ? "" : name!.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (string.IsNullOrWhiteSpace(name) ? "plc_artifact" : name.Trim())
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray();
            var safe = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "plc_artifact" : safe;
        }

        private static ResponseCompile BuildCompileResponse(string softwarePath, object result)
        {
            // Every CompilerResult member is a COM property that must be read on the PortalSta
            // thread; CollectCompilerResultOnSta does that and returns a plain snapshot.
            var snap = Portal.CollectCompilerResultOnSta(result);

            return new ResponseCompile
            {
                Message = $"Software '{softwarePath}' compiled. State={snap.State} Errors={snap.ErrorCount} Warnings={snap.WarningCount}",
                State = snap.State,
                ErrorCount = snap.ErrorCount,
                WarningCount = snap.WarningCount,
                Messages = snap.Raw,
                Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = !snap.State.Equals("Error", StringComparison.OrdinalIgnoreCase),
                    ["errorDetailCount"] = snap.Errors.Count,
                    ["warningDetailCount"] = snap.Warnings.Count
                }
            };
        }

        private static int ReadIntProperty(object value, string propertyName)
        {
            var raw = value.GetType().GetProperty(propertyName)?.GetValue(value);
            if (raw is int i) return i;
            return int.TryParse(raw?.ToString(), out var parsed) ? parsed : 0;
        }

        [McpServerTool(Name = "GetCrossReferences"), Description("[L2][PLC-Software]Get cross references for a Step7 block/type (best-effort). Requires applicable object and Openness support.")]
        public static ResponseCrossReferences GetCrossReferences(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("objectPath: blockPath or typePath inside the PLC software")] string objectPath,
            [Description("objectKind: Block or Type")] string objectKind = "Block",
            [Description("filter: CrossReferenceFilter enum name (e.g. AllObjects, ObjectsWithReferences, UnusedObjects)")] string filter = "AllObjects")
        {
            try
            {
                var items = Portal.GetCrossReferences(softwarePath, objectPath, objectKind, filter);
                if (items != null)
                {
                    return new ResponseCrossReferences
                    {
                        Message = $"Cross references retrieved for {objectKind} '{objectPath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"Cross reference service not available for {objectKind} '{objectPath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error retrieving cross references: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetPlcExternalSources"), Description("[L2][PLC-Software]List PLC external source names (best-effort)")]
        public static ResponseStringList GetPlcExternalSources(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetPlcExternalSources(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"PLC external sources listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"PLC software not found at '{softwarePath}'.{Portal.AvailablePlcPathsSuffix()}", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing PLC external sources: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GetPlcTagTables"), Description("[L2][PLC-Software] List all PLC tag table names. Requires: Connect + OpenProject. softwarePath from GetProjectTree (e.g. 'PLC_1'). Use before ExportPlcTagTable to get exact table names, or before ImportPlcTagTable to check for conflicts.")]
        public static ResponseStringList GetPlcTagTables(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetPlcTagTables(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"PLC tag tables listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"PLC software not found at '{softwarePath}'.{Portal.AvailablePlcPathsSuffix()}", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error listing PLC tag tables: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ExportPlcTagTable"), Description("[L2][PLC-Software]Export one PLC tag table (PlcTagTable) to XML file")]
        public static ResponseExportFile ExportPlcTagTable(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("tagTableName: PLC tag table name")] string tagTableName,
            [Description("exportPath: full file path to write to")] string exportPath)
        {
            try
            {
                var ok = Portal.ExportPlcTagTable(softwarePath, tagTableName, exportPath);
                if (ok)
                {
                    return new ResponseExportFile
                    {
                        Message = $"PLC tag table '{tagTableName}' exported",
                        ExportPath = exportPath,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"Failed exporting PLC tag table '{tagTableName}' from '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error exporting PLC tag table: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportPlcTagTable"), Description("[L1][PLC-Software]Import one PLC tag table XML file into PLC software (best-effort)")]
        public static ResponseMessage ImportPlcTagTable(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional tag table group path (use empty for root)")] string folderPath,
            [Description("importPath: full file path of PLC tag table XML")] string importPath)
        {
            try
            {
                Portal.ImportPlcTagTable(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"PLC tag table imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed importing PLC tag table from '{importPath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing PLC tag table: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ImportPlcTagTablesFromDirectory"), Description("[L2][PLC-Software]Batch import PLC tag table .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportPlcTagTablesFromDirectory(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional tag table group path (use empty for root)")] string folderPath,
            [Description("dir: directory containing PLC tag table XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportPlcTagTablesFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} PLC tag tables from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing PLC tag tables from '{dir}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "ProbePlcMonitorOnlineCapabilities"), Description("[L2][PLC-Online]Read-only probe for PLC online/offline/watch/monitor API surfaces. It does not go online/offline, change watch tables, write values, or touch restricted safety APIs.")]
        public static ResponseJsonReport ProbePlcMonitorOnlineCapabilities(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var result = Portal.ProbePlcMonitorOnlineCapabilities(softwarePath);
                result.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = result.Ok == true };
                return result;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error probing PLC monitor/online capabilities: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "PlanOnlineReadOnlyMonitoring"), Description("[L2][PLC-Online] Validate an online-monitoring request shape without connecting to TIA Portal. Read-only preflight only: no go-online/offline, no watch-table modification, no value write, and no force operation.")]
        public static ResponseJsonReport PlanOnlineReadOnlyMonitoring(
            [Description("softwarePath: PLC software path resolved from GetProjectTree/ValidateAutomationContext.")] string softwarePath,
            [Description("tagPathsJson: JSON array of symbolic PLC tag/member paths, for example [\"DB_HMI.MotorRun\",\"DB_HMI.SpeedSet\"]. Do not pass guessed M bits.")] string tagPathsJson,
            [Description("mode: current-values or watch-table-export-plan. Both are read-only planning modes.")] string mode = "current-values")
        {
            try
            {
                var warnings = new JsonArray();
                var acceptedTags = new JsonArray();
                var rejectedTags = new JsonArray();
                var policy = new JsonArray();
                foreach (var policyLine in GetOnlineMonitoringSafetyPolicy())
                {
                    policy.Add(policyLine);
                }
                var normalizedMode = (mode ?? string.Empty).Trim();
                var allowedModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "current-values",
                    "watch-table-export-plan"
                };

                if (!allowedModes.Contains(normalizedMode))
                {
                    return BuildOnlineMonitoringPlanResponse(false, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, $"Unsupported mode '{mode}'. Supported values: current-values, watch-table-export-plan.");
                }

                if (string.IsNullOrWhiteSpace(softwarePath))
                {
                    warnings.Add("softwarePath is empty. Resolve the PLC software path from GetProjectTree before real online monitoring.");
                }

                JsonNode? parsed;
                try
                {
                    parsed = JsonNode.Parse(tagPathsJson);
                }
                catch (Exception ex)
                {
                    return BuildOnlineMonitoringPlanResponse(false, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, "tagPathsJson must be a JSON array of symbolic PLC paths. Parse error: " + ex.Message);
                }

                if (parsed is not JsonArray tagArray)
                {
                    return BuildOnlineMonitoringPlanResponse(false, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, "tagPathsJson must be a JSON array.");
                }

                foreach (var item in tagArray)
                {
                    var tag = item?.GetValue<string>()?.Trim() ?? string.Empty;
                    var rejectReason = GetOnlineMonitoringTagRejectReason(tag);
                    if (rejectReason == null)
                    {
                        acceptedTags.Add(tag);
                    }
                    else
                    {
                        rejectedTags.Add(new JsonObject
                        {
                            ["tagPath"] = tag,
                            ["reason"] = rejectReason
                        });
                    }
                }

                if (acceptedTags.Count == 0)
                {
                    warnings.Add("No accepted tag paths. Real online monitoring requires at least one declared PLC symbol or DB member.");
                }

                var ok = rejectedTags.Count == 0 && acceptedTags.Count > 0;
                var message = ok
                    ? "Online read-only monitoring plan validated. This preflight did not connect to TIA Portal."
                    : "Online read-only monitoring plan rejected. Fix rejected tag paths before any real online workflow.";

                return BuildOnlineMonitoringPlanResponse(ok, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error planning online read-only monitoring: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "PlanOnlineReadOnlyDataProvider"), Description("[L2][PLC-Online] Plan the commercial current-value path through an external read-only data provider such as opcua or s7-readonly. This is a preflight only: it does not connect, write PLC values, modify watch tables, go online/offline through TIA, or use force operations.")]
        public static ResponseJsonReport PlanOnlineReadOnlyDataProvider(
            [Description("provider: opcua or s7-readonly. opcua is preferred for commercial symbolic readback.")] string provider,
            [Description("endpoint: OPC UA endpoint URL or PLC endpoint/IP. It is validated only for shape and is not opened.")] string endpoint,
            [Description("tagPathsJson: JSON array of declared symbolic PLC tags/DB members. Guessed M bits and unsafe intent names are rejected.")] string tagPathsJson,
            [Description("optionsJson: optional JSON object such as {\"pollMs\":1000,\"source\":\"watch-table-export\"}.")] string optionsJson = "{}")
        {
            try
            {
                var normalizedProvider = (provider ?? "").Trim().ToLowerInvariant();
                var allowedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "opcua",
                    "s7-readonly"
                };

                var policy = new JsonArray(GetOnlineMonitoringSafetyPolicy().Select(x => JsonValue.Create(x)).ToArray());
                var warnings = new JsonArray();
                var acceptedTags = new JsonArray();
                var rejectedTags = new JsonArray();
                var options = ParseJsonObjectOrEmpty(optionsJson, "optionsJson");

                if (!allowedProviders.Contains(normalizedProvider))
                {
                    return BuildReadOnlyProviderPlan(false, normalizedProvider, endpoint, acceptedTags, rejectedTags, warnings, policy, options, $"Unsupported provider '{provider}'. Supported providers: opcua, s7-readonly.");
                }

                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    warnings.Add("endpoint is empty. Real read-only providers require an OPC UA endpoint URL or PLC endpoint/IP before execution.");
                }

                JsonNode? parsed;
                try
                {
                    parsed = JsonNode.Parse(tagPathsJson);
                }
                catch (Exception ex)
                {
                    return BuildReadOnlyProviderPlan(false, normalizedProvider, endpoint, acceptedTags, rejectedTags, warnings, policy, options, "tagPathsJson must be a JSON array. Parse error: " + ex.Message);
                }

                if (parsed is not JsonArray tagArray)
                {
                    return BuildReadOnlyProviderPlan(false, normalizedProvider, endpoint, acceptedTags, rejectedTags, warnings, policy, options, "tagPathsJson must be a JSON array.");
                }

                foreach (var item in tagArray)
                {
                    var tag = item?.GetValue<string>()?.Trim() ?? "";
                    var rejectReason = GetOnlineMonitoringTagRejectReason(tag);
                    if (rejectReason == null)
                    {
                        acceptedTags.Add(tag);
                    }
                    else
                    {
                        rejectedTags.Add(new JsonObject
                        {
                            ["tagPath"] = tag,
                            ["reason"] = rejectReason
                        });
                    }
                }

                if (normalizedProvider == "s7-readonly")
                {
                    warnings.Add("s7-readonly must be implemented as a read-only adapter with no Write/Force API surface exposed by MCP.");
                }

                var ok = acceptedTags.Count > 0 && rejectedTags.Count == 0;
                return BuildReadOnlyProviderPlan(
                    ok,
                    normalizedProvider,
                    endpoint,
                    acceptedTags,
                    rejectedTags,
                    warnings,
                    policy,
                    options,
                    ok
                        ? "Read-only data provider plan validated. This preflight did not open a network connection."
                        : "Read-only data provider plan rejected. Fix rejected tags/provider settings before any real read workflow.");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error planning read-only data provider: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        private static ResponseJsonReport BuildReadOnlyProviderPlan(bool ok, string provider, string endpoint, JsonArray acceptedTags, JsonArray rejectedTags, JsonArray warnings, JsonArray policy, JsonObject options, string message)
        {
            return new ResponseJsonReport
            {
                Ok = ok,
                Message = message,
                Data = new JsonObject
                {
                    ["provider"] = provider,
                    ["endpoint"] = endpoint ?? "",
                    ["implementationPath"] = provider.Equals("opcua", StringComparison.OrdinalIgnoreCase)
                        ? "Use OPC UA read/subscribe as the preferred commercial current-value channel."
                        : "Use a strictly read-only S7 adapter for address/symbol reads when OPC UA is unavailable.",
                    ["status"] = "planned-read-only-provider",
                    ["usesTiaOpennessForCurrentValues"] = false,
                    ["usesTiaOpennessForTagDiscovery"] = true,
                    ["readOnly"] = true,
                    ["connectsNow"] = false,
                    ["writesPlcValues"] = false,
                    ["modifiesWatchTables"] = false,
                    ["usesForce"] = false,
                    ["acceptedTags"] = acceptedTags,
                    ["rejectedTags"] = rejectedTags,
                    ["warnings"] = warnings,
                    ["policy"] = policy,
                    ["options"] = options
                },
                Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = ok
                }
            };
        }

        private static JsonObject ParseJsonObjectOrEmpty(string json, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
            try
            {
                return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                throw new McpException(parameterName + " must be a JSON object. Parse error: " + ex.Message, ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "WritePlcSclSourceFile"), Description("[L1][PLC-Software][Offline] Write SCL source text to a local .scl external-source file (UTF-8 WITH BOM, so Chinese comments are not imported as mojibake/乱码). This tool does NOT connect to TIA Portal and does NOT import anything — it only writes the file to disk and returns the path plus manual-import instructions. Use it as the robust fallback when XML block import is rejected (e.g. a TIA V20 portal rejecting V21 SimaticML tokens: 'Cannot create SW.Blocks.CompileUnit... token not supported'): the user imports the .scl manually in TIA via project tree → 'External source files' → 'Add new external file', then right-clicks the source → 'Generate blocks from source'. The sclContent must be a complete source, e.g. FUNCTION_BLOCK \"Name\" ... END_FUNCTION_BLOCK. SECURITY: path traversal (..) is blocked; outputPath must be a valid file path.")]
        public static ResponseMessage WritePlcSclSourceFile(
            [Description("sclContent: the full SCL source text (complete FUNCTION_BLOCK / FUNCTION / DATA_BLOCK / TYPE declarations). This is written verbatim.")] string sclContent,
            [Description("outputPath: target .scl file path. If a directory is given (or the path has no extension), the file is named after the first block found in the source. Empty means a temp file under %TEMP%\\tia_mcp_scl. Path traversal (..) is rejected.")] string outputPath = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sclContent))
                    throw new McpException("sclContent is empty — provide the full SCL source text to write.", McpErrorCode.InvalidParams);

                // SECURITY: Reject path traversal attempts (..) in user-provided paths
                if (!string.IsNullOrWhiteSpace(outputPath) && outputPath.Contains(".."))
                {
                    throw new McpException("Path traversal denied: outputPath must not contain '..' components.", McpErrorCode.InvalidParams);
                }

                // Derive a default file name from the first block declaration in the source.
                var nameMatch = Regex.Match(sclContent,
                    "(?:FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE)\\s+\"?([A-Za-z_][A-Za-z0-9_]*)\"?",
                    RegexOptions.IgnoreCase);
                var defaultName = MakeSafeFileName(nameMatch.Success ? nameMatch.Groups[1].Value : "MCP_Source");

                string finalPath;

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    // Default to temp directory
                    var dir = Path.Combine(Path.GetTempPath(), "tia_mcp_scl");
                    finalPath = Path.Combine(dir, defaultName + ".scl");
                }
                else if (Directory.Exists(outputPath) ||
                         outputPath.EndsWith("\\", StringComparison.Ordinal) ||
                         outputPath.EndsWith("/", StringComparison.Ordinal))
                {
                    // User specified a directory
                    finalPath = Path.Combine(outputPath, defaultName + ".scl");
                }
                else
                {
                    // User specified a file path
                    finalPath = string.IsNullOrEmpty(Path.GetExtension(outputPath))
                        ? outputPath + ".scl"
                        : outputPath;
                }

                // Normalize to full path
                finalPath = Path.GetFullPath(finalPath);

                var parent = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                // UTF-8 WITH BOM: TIA reads BOM-less UTF-8 SCL with Chinese comments as mojibake.
                File.WriteAllText(finalPath, sclContent, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                // P0① SCL lint pre-check (offline, no TIA needed): surface compile-breakers before generate.
                var lint = SclLinter.Lint(sclContent);
                var lintArr = SclLinter.ToJson(lint);
                var lintSummary = lint.Count == 0
                    ? " (SCL lint: clean — no issues found)"
                    : $" (SCL lint: {lint.Count} potential issue(s) — see Meta.lint for line numbers)";

                return new ResponseMessage
                {
                    Message =
                        $"SCL source written to '{finalPath}'. To import in TIA Portal: project tree → " +
                        "'External source files' → 'Add new external file' → select this .scl → " +
                        "right-click the source → 'Generate blocks from source'. " +
                        "(Or call ImportPlcExternalSource then GenerateBlocksFromExternalSource if connected.)" +
                        lintSummary,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["path"] = finalPath,
                        ["blockName"] = nameMatch.Success ? nameMatch.Groups[1].Value : null,
                        ["bytes"] = new System.IO.FileInfo(finalPath).Length,
                        ["lintCount"] = lint.Count,
                        ["lint"] = lintArr
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error writing SCL source file: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "LintPlcSclSource"), Description("[L1][PLC-Software][Offline] Static pre-check of SCL source text — no TIA Portal connection required. Catches 4 common compile-breakers before 'Generate blocks from source': duplicate formal parameters in a call (SCL001), invalid \"DB\".DB(...) access (SCL002), FB multi-instance declared in an OB (SCL003), and HW_*/PORT/CONN_* types declared in VAR_TEMP (SCL004). Findings carry 1-based source line numbers in Meta.lint. Non-blocking: it only reports, it never modifies anything.")]
        public static ResponseMessage LintPlcSclSource(
            [Description("sclContent: the full SCL source text to lint")] string sclContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sclContent))
                    throw new McpException("sclContent is empty — provide the full SCL source text to lint.", McpErrorCode.InvalidParams);

                var lint = SclLinter.Lint(sclContent);
                var lintArr = SclLinter.ToJson(lint);
                int errors = lint.Count(f => f.Severity == SclLintSeverity.Error);

                return new ResponseMessage
                {
                    Message = lint.Count == 0
                        ? "SCL lint passed: no issues found."
                        : $"SCL lint found {lint.Count} issue(s) ({errors} error-level). See Meta.lint for line numbers and fixes.",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["lintCount"] = lint.Count,
                        ["lintErrors"] = errors,
                        ["lint"] = lintArr
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error during SCL lint: {ex.Message}");
            }
        }

        [McpServerTool(Name = "ImportPlcExternalSource"), Description("[L2][PLC-Software]Import one PLC external source file into a group (best-effort)")]
        public static ResponseMessage ImportPlcExternalSource(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("groupPath: external source group path (use empty for root)")] string groupPath,
            [Description("filePath: path to external source file (.scl, etc.)")] string filePath)
        {
            try
            {
                Portal.ImportPlcExternalSource(softwarePath, groupPath, filePath);
                return new ResponseMessage
                {
                    Message = "PLC external source imported",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed importing PLC external source [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error importing PLC external source: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "DeletePlcExternalSource"), Description("[L2][PLC-Software]Delete a PLC external source by name so ImportPlcExternalSource can replace it (idempotent). Name may include or omit .scl.")]
        public static ResponseMessage DeletePlcExternalSource(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("externalSourceName: name from GetPlcExternalSources (e.g. MCPVerify_FC_SCL_v3.scl)")] string externalSourceName)
        {
            try
            {
                Portal.DeletePlcExternalSource(softwarePath, externalSourceName);
                return new ResponseMessage
                {
                    Message = $"PLC external source '{externalSourceName}' deleted or was not present",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed deleting PLC external source '{externalSourceName}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error deleting PLC external source: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "GenerateBlocksFromExternalSource"), Description("[L2][PLC-Software]Generate blocks from a PLC external source by name (best-effort)")]
        public static ResponseMessage GenerateBlocksFromExternalSource(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("externalSourceName: name from GetPlcExternalSources")] string externalSourceName)
        {
            try
            {
                Portal.GenerateBlocksFromExternalSource(softwarePath, externalSourceName);
                return new ResponseMessage
                {
                    Message = $"Blocks generated from external source '{externalSourceName}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed generating blocks from external source '{externalSourceName}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error generating blocks from external source: {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        [McpServerTool(Name = "CompileSoftware"), Description("[L1][PLC-Software] Compile all blocks in the PLC software. Requires: Connect + OpenProject. Returns basic success/failure. For structured error/warning details use CompileAndDiagnosePlc instead. Must compile before ExportBlock if any blocks are inconsistent. After adding new blocks via import, always compile to catch type/interface mismatches.")]
        public static ResponseCompile CompileSoftware(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("password: the password to access adminsitration, default: no password")] string password = "")
        {
            try
            {
                // CompilerResult COM reads must happen on the PortalSta thread.
                var compiled = WithAutoOffline(() => Portal.CompileSoftware(softwarePath, password));
                var snap = Portal.CollectCompilerResultOnSta(compiled);

                return new ResponseCompile
                {
                    Message = $"Software '{softwarePath}' compiled. State={snap.State} Errors={snap.ErrorCount} Warnings={snap.WarningCount}",
                    State = snap.State,
                    ErrorCount = snap.ErrorCount,
                    WarningCount = snap.WarningCount,
                    Messages = snap.Raw,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = !snap.State.Equals("Error", StringComparison.OrdinalIgnoreCase),
                        ["errorDetailCount"] = snap.Errors.Count,
                        ["warningDetailCount"] = snap.Warnings.Count
                    }
                };
            }
            catch (PortalException pex)
            {
                throw McpError.WithRecovery(pex, $"Failed compiling software '{softwarePath}' [{pex.Code}]: {pex.Message}");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error compiling software '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        internal static T WithAutoOffline<T>(Func<T> op)
        {
            try { return op(); }
            catch (Exception ex) when (IsOnlineModeError(ex))
            {
                try { Portal.GoOfflineAll(); } catch { }
                return op(); // retry once, now fully offline
            }
        }

        internal sealed class CompilerMessageCollectResult
        {
            public List<string> Raw { get; } = new List<string>();
            public List<string> Errors { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
            public List<string> Info { get; } = new List<string>();
        }

        internal static CompilerMessageCollectResult CollectCompilerMessages(object? messagesRoot)
        {
            var collected = new CompilerMessageCollectResult();
            if (messagesRoot is System.Collections.IEnumerable enumerable && messagesRoot is not string)
            {
                foreach (var message in enumerable)
                    WalkCompilerMessageNode(message, collected);
            }
            return collected;
        }

        private static void WalkCompilerMessageNode(object? message, CompilerMessageCollectResult collected)
        {
            if (message == null) return;

            var formatted = FormatCompilerMessage(message);
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                collected.Raw.Add(formatted!);
                ClassifyCompilerMessage(message, formatted!, collected);
            }

            if (!TryGetCompilerMessageChildren(message, out var children)) return;
            foreach (var child in children)
                WalkCompilerMessageNode(child, collected);
        }

        private static void ClassifyCompilerMessage(object message, string formatted, CompilerMessageCollectResult collected)
        {
            var state = ReadCompilerMessageState(message);
            var description = ReadCompilerMessageProperty(message, "Description") ?? string.Empty;
            var hasChildren = HasCompilerMessageChildren(message);

            if (IsCompilerSummaryDescription(description))
                return;

            if (IsCompilerErrorState(state))
            {
                if (!hasChildren || !string.IsNullOrWhiteSpace(description))
                    AddUniqueCompilerLine(collected.Errors, formatted);
                return;
            }

            if (IsCompilerWarningState(state))
            {
                if (!hasChildren || !string.IsNullOrWhiteSpace(description))
                    AddUniqueCompilerLine(collected.Warnings, formatted);
                return;
            }

            if (!string.IsNullOrWhiteSpace(description))
                AddUniqueCompilerLine(collected.Info, formatted);
        }

        private static bool HasCompilerMessageChildren(object message)
        {
            return TryGetCompilerMessageChildren(message, out var children) && children.Count > 0;
        }

        private static bool TryGetCompilerMessageChildren(object message, out List<object> children)
        {
            children = new List<object>();
            try
            {
                var messagesValue = message.GetType().GetProperty("Messages")?.GetValue(message);
                if (messagesValue is System.Collections.IEnumerable enumerable && messagesValue is not string)
                {
                    foreach (var child in enumerable)
                    {
                        if (child != null)
                            children.Add(child);
                    }
                }
            }
            catch
            {
                // best effort only
            }

            return children.Count > 0;
        }

        private static string? ReadCompilerMessageProperty(object message, string propertyName)
        {
            try
            {
                var value = message.GetType().GetProperty(propertyName)?.GetValue(message);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadCompilerMessageState(object message)
        {
            return ReadCompilerMessageProperty(message, "State") ?? string.Empty;
        }

        private static bool IsCompilerErrorState(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            return state.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                || state.IndexOf("fehler", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCompilerWarningState(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            return state.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0
                || state.IndexOf("warnung", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCompilerSummaryDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;
            var text = description!.Trim();
            return text.StartsWith("Compiling finished", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Compilation finished", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Kompilierung beendet", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddUniqueCompilerLine(List<string> target, string line)
        {
            if (!target.Contains(line))
                target.Add(line);
        }

        private static void AppendCompilerEngineeringAttributes(object message, List<string> parts)
        {
            var getAttribute = message.GetType().GetMethod(
                "GetAttribute",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            if (getAttribute == null) return;

            foreach (var attrName in new[]
            {
                "Line", "Column", "BlockName", "Severity", "ErrorCode", "Message", "Text", "ObjectPath"
            })
            {
                if (parts.Any(p => p.StartsWith(attrName + "=", StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                {
                    var value = getAttribute.Invoke(message, new object[] { attrName });
                    if (value == null) continue;
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        parts.Add($"{attrName}={text}");
                }
                catch
                {
                    // attribute not supported on this message type
                }
            }
        }

        private static string? FormatCompilerMessage(object? message)
        {
            if (message == null) return null;

            try
            {
                var t = message.GetType();
                var parts = new List<string>();

                foreach (var name in new[]
                {
                    "State", "Severity", "ErrorCode", "Message", "Description", "Text",
                    "Path", "ObjectPath", "BlockName", "Line", "Column", "DateTime"
                })
                {
                    var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null || p.GetIndexParameters().Length != 0) continue;

                    object? value = null;
                    try { value = p.GetValue(message); } catch { }
                    if (value == null) continue;

                    var s = value.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        parts.Add($"{name}={s}");
                }

                AppendCompilerEngineeringAttributes(message, parts);

                if (parts.Count == 0)
                {
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (p.GetIndexParameters().Length != 0) continue;
                        if (string.Equals(p.Name, "Messages", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.Name, "Parent", StringComparison.OrdinalIgnoreCase))
                            continue;

                        object? value = null;
                        try { value = p.GetValue(message); } catch { }
                        if (value == null) continue;
                        var s = value.ToString();
                        if (!string.IsNullOrWhiteSpace(s) && s != t.FullName)
                            parts.Add($"{p.Name}={s}");
                    }
                }

                return parts.Count > 0 ? string.Join("; ", parts) : message.ToString();
            }
            catch
            {
                return message.ToString();
            }
        }

        [McpServerTool(Name = "GetSoftwareTree"), Description("[L1][PLC-Software] Get the full PLC block/type/external-source hierarchy as ASCII tree. Requires: Connect + OpenProject. softwarePath from GetProjectTree (e.g. 'PLC_1'). ALWAYS call before ExportBlock/ImportBlock to get exact group paths (e.g. 'Program blocks/FBs/FB_Motor'). Returns OB/FB/FC/GlobalDB/UDT/ExternalSource blocks with group hierarchy.")]
        public static ResponseSoftwareTree GetSoftwareTree(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath)
        {
            try
            {
                var tree = Portal.GetSoftwareTree(softwarePath);

                if (!string.IsNullOrEmpty(tree))
                {
                    return new ResponseSoftwareTree
                    {
                        Message = $"Software tree retrieved from '{softwarePath}'",
                        Tree = "```\n" + tree + "\n```",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed retrieving software tree from '{softwarePath}'", McpErrorCode.InternalError);
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw McpError.WithRecovery(ex, $"Unexpected error retrieving software tree from '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}");
            }
        }

        #endregion
    }
}
