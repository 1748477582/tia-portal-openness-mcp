using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Model-facing guidance, shipped inside the server so EVERY MCP client benefits —
    /// including hosts that never load SKILL.md (VS Code, Cursor, LobeChat, third-party
    /// agents). Two delivery channels:
    ///   1. <see cref="ServerInstructions"/> — returned in the MCP initialize handshake;
    ///      most hosts inject it into the model's system context automatically.
    ///   2. <see cref="Topic(string)"/> — on-demand cheat sheets via the GetAuthoringGuide
    ///      tool, for syntax details too large for the handshake.
    /// All facts here are verified against live TIA V20/V21 machines; do not add
    /// speculative syntax.
    /// </summary>
    public static class McpGuides
    {
        // Common header + golden paths + tool quick-pick, shared by ALL builds (V18/V20/V21).
        // The quick-pick table is the fix for "picking a tool got slower" after the V20
        // build grew to ~200 tools: it lets the model jump straight to the right tool
        // instead of scanning the whole tool list (esp. the 23 Unified HMI tools whose
        // names resemble the Classic HMI tools).
        private const string ServerInstructionsCommon =
@"TIA Portal MCP server (Siemens PLC/HMI engineering via Openness). How to work well:

FIRST CALL: Bootstrap — returns environment status, connection state, the recommended next tool, and operating rules. Do this before anything else. If the environment itself seems broken (TIA missing, group membership, nothing connects), call Doctor for a plain-language diagnosis with exact fixes.

GOLDEN PATHS (pick one, do not improvise):
- Whole new project → ScaffoldProject with ONE JSON spec (PLC + blocks + HMI + compile + save in a single call). The DEFAULT call is a dry run (offline spec validation, nothing created); when it reports clean, call again with dryRun=false to actually create.
- Add/modify code in an existing project → write SCL or S7DCL text, then import. PREFERRED path is ImportFromDocuments (.s7dcl); or GenerateBlocksFromExternalSource (.scl). NEVER hand-write SimaticML FlgNet XML for ladder logic — it is fragile (UId bookkeeping, XML entities) and the #1 cause of failed imports. Use S7DCL ladder text instead (GetAuthoringGuide topic 'lad').
- Read/understand a project → GetProjectTree, GetBlocksWithHierarchy. To READ ONE BLOCK'S LOGIC use DescribeBlockLogic — it returns readable LADDER rungs (series ' · ', parallel ' + ') and inline SCL, and flags contacts wired to a constant (a disabled/forced rung). Far faster and more accurate than exporting and reading FlgNet XML by hand. Do NOT hand-parse ladder XML.

TOOL QUICK-PICK (this build exposes ~200 tools — use this table instead of scanning the full list to choose):
- Environment / first step → Bootstrap, Doctor
- Project structure → GetProjectTree, GetSoftwareTree, GetBlocksWithHierarchy
- Read ONE block's logic → DescribeBlockLogic (readable LADDER rungs + inline SCL; fastest, do not export XML)
- Block callers / impact / interface → AnalyzeBlockImpact (Openness has no reverse call-index; this IS your global view)
- Compile → CompileAndDiagnosePlc, CompileSoftware; read structured diagnostics with GetCompileDiagnostics
- Export editable SCL/XML → ExportBlockSourceUtf8 (UTF-8+BOM), ExportBlock
- Import SCL → ImportPlcExternalSource + GenerateBlocksFromExternalSource; or ImportFromDocuments for .s7dcl documents
- Renumber / move / delete blocks → SetBlockNumber, MoveBlocksToGroup, AutoClassifyBlocks, DeleteBlock
- External SCL sources → GetPlcExternalSources, DeletePlcExternalSource (unlock a block before renumber/export)
- PLC-HMI CLASSIC / Comfort → GetHmi* / EnsureHmi* tools (screens, tags, connections, tag tables)
- PLC-HMI WINCC UNIFIED (V20-only, 23 tools) → GetUnifiedHmi* / EnsureUnifiedHmi* / ApplyUnifiedHmi* tools
- HMI connection (Unified) → EnsureUnifiedHmiConnection (single connection auto-selects the driver)

BEFORE WRITING CODE call GetAuthoringGuide with topic 'scl' or 'lad' — it returns the exact verified syntax and encoding rules. Most quality problems come from skipping this.

ENCODING (breaks Chinese text if wrong):
- RegenerateBlockFromSource / ImportBlock force UTF-8 WITH BOM for you — just hand files to those tools; do NOT pre-strip the BOM.
- .s7dcl / .s7res and ALL block/UDT/tag-table XML: UTF-8 WITH BOM.

DISCIPLINE:
- After ANY write: CompileSoftware (or CompileAndDiagnosePlc), then SaveProject. Nothing persists automatically.
- Names are exact: if a path/name is rejected, read the real names with GetProjectTree / GetBlocks — do not guess variants.
- On error: the message names the recovery tool; call it. Do not retry the same call unchanged and do not switch tools at random.
- Prefer one big declarative call (ScaffoldProject / PlcBuildAndImport) over dozens of small calls — it is faster and far less error-prone.";

        // V18-only addendum: the 23 Unified HMI tools are compiled out; no .s7dcl text export.
        private const string ServerInstructionsV18Only =
@"

*** THIS BUILD TARGETS TIA PORTAL V18. The 23 WINCC UNIFIED HMI tools (GetUnifiedHmi* / EnsureUnifiedHmi* / ApplyUnifiedHmi*) ARE COMPILED OUT and are NOT callable — they require TIA V20+. On V18 use the Classic/Comfort HMI tools (GetHmi* / EnsureHmi*). ***
V18 HOUSE RULES (these OVERRIDE any generic V20/V21 guidance above):
- To GET an editable export: use ExportBlockSourceUtf8 (SimaticML XML, UTF-8+BOM), edit, then RegenerateBlockFromSource to re-import. (These two tools are the V18 substitute for the V20 'ImportFromDocuments' flow.)
- THERE IS NO .s7dcl human-readable SCL TEXT export on V18. If you need it, tell the user to switch to the V20 build — do NOT call ExportAsDocuments / ImportFromDocuments (they are disabled here).
- BEFORE editing / renumbering / deleting ANY block: call AnalyzeBlockImpact(softwarePath, blockName) first — it reports the block's callers + interface.
- GLOBAL-BEFORE-LOCAL: understand a project from GetBlocksWithHierarchy / GetSoftwareTree first. A single exported block file is a transport artifact, NOT the source of truth.";

        // V20+ addendum: all ~200 tools available, including the 23 Unified HMI tools and .s7dcl export.
        private const string ServerInstructionsV20Plus =
@"

*** THIS BUILD TARGETS TIA PORTAL V20. ALL ~200 tools are enabled, including the 23 WINCC UNIFIED HMI tools (GetUnifiedHmi* / EnsureUnifiedHmi* / ApplyUnifiedHmi*) and the .s7dcl document export/import flow (ExportAsDocuments / ImportFromDocuments). Classic/Comfort HMI tools (GetHmi* / EnsureHmi*) remain available for Comfort panels. ***
V20 HOUSE RULES:
- Export editable SCL: ExportBlockSourceUtf8 (UTF-8+BOM) + RegenerateBlockFromSource, OR the document flow ExportAsDocuments / ImportFromDocuments with .s7dcl.
- BEFORE editing / renumbering / deleting ANY block: call AnalyzeBlockImpact(softwarePath, blockName) first — it reports the block's callers + interface.
- GLOBAL-BEFORE-LOCAL: understand a project from GetBlocksWithHierarchy / GetSoftwareTree first.";

        public const string ServerInstructions =
            ServerInstructionsCommon
#if TIA_V18
            + ServerInstructionsV18Only
#else
            + ServerInstructionsV20Plus
#endif
            ;

        /// <summary>Cheat-sheet topics for the GetAuthoringGuide tool.</summary>
        public static readonly IReadOnlyDictionary<string, string> Topics = new Dictionary<string, string>
        {
            ["workflow"] =
@"WORKFLOW (verified order):
Connect → (OpenProject | AttachToOpenProject | CreateProject) → GetProjectTree → read/write → CompileSoftware → SaveProject.
- ScaffoldProject: one JSON spec builds PLC + tag tables + UDT/DB + SCL/LAD blocks + HMI screens + compile + save. dryRun=true validates offline (block shapes, file existence) without touching TIA. Use it for anything bigger than a single block.
- PlcBuildAndImport: batch-import block set with compileAfter; also supports dryRun.
- softwarePath is the PLC SOFTWARE name (e.g. '5T车', 'PLC_1'), NOT the device/station name. When rejected, GetProjectTree shows the real one; fuzzy matching exists but exact is faster.
- Openness export does not work while online: tools auto GoOffline where safe; if you see 'not supported in online mode', call GoOffline(softwarePath) and retry.
- Cold start is slow (TIA launch). If many operations are planned, keep one session; do not Disconnect between calls.",

            ["scl"] =
@"SCL AUTHORING (verified):
Preferred import (V20+): ImportFromDocuments / ImportBlocksFromScl with .s7dcl files (UTF-8 WITH BOM). Alternative on V20+: GenerateBlocksFromExternalSource with .scl external source (UTF-8 WITHOUT BOM — a BOM makes it fail at line 0). On THIS V18 build, prefer RegenerateBlockFromSource (it forces UTF-8+BOM for you) instead of hand-making a .scl for GenerateBlocksFromExternalSource.
Skeleton (block names in English; Chinese OK in comments/titles):
  FUNCTION_BLOCK ""FB_Name""
  { S7_Optimized_Access := 'TRUE' }
  VERSION : 0.1
  VAR_INPUT
      Enable : Bool;   // comment
  END_VAR
  VAR_OUTPUT
      Done : Bool;
  END_VAR
  VAR
      state : Int;
  END_VAR
  BEGIN
      IF #Enable THEN
          #Done := TRUE;
      END_IF;
  END_FUNCTION_BLOCK
Rules that prevent 90% of compile errors:
- Every local reference uses '#', every global uses double quotes: #state, ""GlobalDB"".value.
- Statements end with ';'. IF needs THEN and END_IF; CASE needs END_CASE.
- Literals: TRUE/FALSE, 16#00FF (hex), T#500ms (time), 'text' (string).
- Declare EVERY variable in a VAR section before use; give explicit datatypes.
- FC with return: FUNCTION ""FC_Name"" : Bool ... assign #FC_Name := ...;
- After import always CompileSoftware and read the diagnostics; fix and re-import the SAME block name (it overwrites).",

            ["lad"] =
@"LADDER (LAD) — READING & AUTHORING (verified):
READING/ANALYZING existing LAD: call DescribeBlockLogic(softwarePath, blockPath). It reconstructs each rung as a readable expression (series contacts = ' · ', parallel = ' + ', NC shown as '/operand'), lists coils ( )/(S)/(R) and MOVE/compare/timer boxes with operands, and FLAGS a contact wired to a literal constant ('⟨恒断·禁用本行⟩' = a NO contact on FALSE that silently disables its rung). Use it instead of exporting XML and tracing wires by hand — it is the accurate, fast path.
AUTHORING: DO NOT hand-write SimaticML FlgNet XML — UId bookkeeping and entity escaping make it fail constantly. The reliable path is S7DCL ladder TEXT imported with ImportBlocksFromScl(importPath=directory) / ImportFromDocuments. Files: Block.s7dcl (+ optional Block.s7res for Chinese texts), both UTF-8 WITH BOM.
S7DCL ladder essentials (from real V20/V21 exports):
- A network is a RUNG; series contacts chain, parallel branches use shared wire labels (wire#w1, wire#w2) to fork and rejoin.
- Elements: Contact (NO), negated contact (NC), Coil, S_Coil (set), R_Coil (reset), timer/counter/compare boxes via templates (e.g. GT_Contact + {S7_Templates}), Move/Add boxes with EN/ENO.
- Operands: #local for interface vars, ""Tag_Name"" for global tags, ""DB"".member for DB access.
- Easiest way to learn the exact dialect: ExportBlocksAsScl on ANY existing LAD block and copy its .s7dcl structure.
When only a plain FC/FB CALL network is needed, BuildFlgNetCallXml / ComposePlcLadFcBlockXml are safe (they generate the XML for you).
Mixed LAD+SCL blocks are supported by .s7dcl. After import: CompileSoftware, then SaveProject.",

            ["db"] =
@"DB / UDT / TAG TABLES (verified):
- Global DB: BuildPlcGlobalDbXml → ImportBlock (XML, UTF-8 WITH BOM). Members need Name + Datatype (+ optional StartValue).
- UDT: BuildPlcUdtXml → ImportType. A UDT with no members is invalid (dryRun catches it).
- Tag tables: BuildPlcTagTableXml → ImportPlcTagTable; logical addresses like %I0.0 / %Q0.1 / %MW10.
- Instance DBs are created automatically when a FB call is compiled — do not author them by hand.
- Reading live values: ReadPlcLiveValuesS7 needs PUT/GET enabled and non-optimized access for absolute addressing; check GetPutGetAccess first. Optimized-block symbolic live read is NOT possible over classic S7 — do not promise it.",

            ["hmi"] =
@"HMI (WinCC Unified, verified):
Order matters: create/complete the PLC side FIRST (tags/DB must exist), then HMI.
- Connection: EnsureUnifiedHmiConnection (single connection auto-selects the driver).
- Tags: EnsureUnifiedHmiTag bound SYMBOLICALLY to PLC tags (not absolute addresses); set acquisition cycle.
- Screens: EnsureUnifiedHmiScreen + EnsureUnifiedHmiScreenItem, or ApplyUnifiedHmiScreenDesignJson with a design JSON (only use schema keys you have seen in BuildUnifiedHmiLayoutDesignJson output — invented keys are silently ignored or rejected).
- Buttons: EnsureUnifiedHmiButtonAction / EnsureUnifiedHmiButtonEventHandler for press handlers.
- HMI software path is usually 'HMI_RT_1'; ScaffoldProject auto-resolves it.
- These tools verify after write (AbsoluteVerified in the response) — check it instead of re-reading.",

            ["errors"] =
@"COMMON ERRORS → EXACT FIX (all seen on real machines):
- 'Block not found' → name mismatch. GetBlocks/GetProjectTree for real names; root-level blocks may be addressed with or without the 'Program blocks' prefix.
- 'The engineering version Vxx is not supported' → importing XML from another TIA version; the server normalizes this automatically on ImportBlock/ImportType — if you built the XML yourself, do not write <Engineering version> at all, or re-import through the provided Build*Xml tools.
- Chinese text becomes '???' → wrong encoding. XML/.s7dcl need UTF-8 WITH BOM. On THIS V18 build, .scl must ALSO be UTF-8 WITH BOM (our RegenerateBlockFromSource/ImportBlock force it) — a BOM-less .scl FAILS at line 0 here, contrary to generic docs.
- 'not supported in online mode' → GoOffline(softwarePath), retry the export/import.
- 'PLC_1 NotFound' → softwarePath must be the PLC software name from GetProjectTree, not 'PLC_1' guessed, not the station name.
- Compile errors after import → CompileAndDiagnosePlc returns structured diagnostics; fix the source text and re-import the same block (overwrite), do not create renamed copies.
- Connect hangs / security error → an orphan TIA process is stuck; ask the user to close TIA instances (or kill Siemens.Automation.Portal.exe) and retry.
- Long waits are normal on FIRST launch only (headless TIA cold start); subsequent calls are fast. Never spam-retry a slow call — you will spawn extra TIA instances.",
        };

        /// <summary>Get a topic text, or null. Case-insensitive.</summary>
        public static string? Topic(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return Topics.TryGetValue(name.Trim().ToLowerInvariant(), out var t) ? t : null;
        }

        public static string TopicList => string.Join(", ", Topics.Keys);
    }
}
