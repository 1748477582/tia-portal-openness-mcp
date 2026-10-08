using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>What a host may assume about one tool: the MCP ToolAnnotations hints, plus the
    /// questions this server answers for itself - does it need the shared TIA Portal handle, does it
    /// reach outside the station, and must the user see it by name.</summary>
    public sealed class ToolSafetyInfo
    {
        public bool ReadOnly { get; set; }
        public bool Destructive { get; set; }
        public bool Idempotent { get; set; }
        /// <summary>Talks to something outside the engineering station: a physical CPU over the
        /// network, PLCSIM, an OPC UA server.</summary>
        public bool OpenWorld { get; set; }
        /// <summary>Uses the process-wide Openness handle, so it must not overlap another call.</summary>
        public bool TouchesPortal { get; set; }
        /// <summary>Changes a running CPU, deletes engineering data, or closes/replaces the open
        /// project - so it has to be called by its own name and the user must see it. FindTools
        /// flags these; if a proxy ever calls tools by name, it must refuse them.</summary>
        public bool DirectOnly { get; set; }
    }

    /// <summary>
    /// One table behind FindTools' risk annotations, so a tool that "looks like a read" cannot quietly
    /// change a CPU or drop engineering data. Ported from upstream
    /// bulaofen0036-coder/TIA_Portal_Openness_MCP (ToolSafety.cs, commit 53731356), with the tool
    /// names this fork actually registers.
    ///
    /// Anything not matched is treated as a mutating, non-destructive, non-idempotent, closed-world
    /// call that needs the portal: the conservative default.
    /// </summary>
    public static class ToolSafety
    {
        /// <summary>Changes a running CPU, deletes engineering data, or closes/replaces the open
        /// project. Kept to the ones that can lose work or touch hardware, because a proxy that hides
        /// them behind its own name would hide that from the user.</summary>
        private static readonly HashSet<string> DirectOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            // touches a running CPU
            "DownloadToPlc", "GoOnline", "SetWatchTableModifyValue",
            // deletes engineering data
            "DeletePlcBlock", "DeleteBlock", "DeletePlcType", "DeletePlcTagTable", "DeletePlcExternalSource",
            // closes or replaces the open project - unsaved edits are lost. This fork hit that on
            // 2026-09-24, which is why the ownership flags exist; keep these visible.
            "CloseProject", "Disconnect",
            // overwrites blocks wholesale
            "RegenerateBlockFromSource", "GenerateBlocksFromExternalSource",
            "PlcBuildAndImport", "ImportBlocksFromDirectory", "ImportPlcProgramFromDirectory",
        };

        public static IReadOnlyCollection<string> DirectOnlyTools => DirectOnly;

        // Proven not to touch the TIA Portal handle: tool search, the authoring guide, the export store.
        private static readonly HashSet<string> PortalFree = new HashSet<string>(StringComparer.Ordinal)
        {
            "FindTools", "GetAuthoringGuide",
            "GetExport", "ListExports", "SaveExport", "DeleteExport", "ClearExports",
        };

        // Verbs whose tools only read (the project, files, or a CPU) and return data.
        private static readonly string[] ReadOnlyPrefixes =
        {
            "Get", "Describe", "List", "Find", "Search", "Analyze", "Probe", "Check", "Compare",
            "Trace", "Read", "Sample", "Monitor", "Dump", "Plan", "Validate", "Build", "Compose",
        };

        private static readonly HashSet<string> ReadOnlyExtra = new HashSet<string>(StringComparer.Ordinal)
        {
            "RunOnlineMonitoringSafetySelfTest", "RunHmiActionScriptRecipeSafetySelfTest",
        };

        // Prefix matches that are NOT read-only despite the verb.
        private static readonly HashSet<string> NotReadOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            "SaveExport",        // writes the export into a caller-chosen file
        };

        // Verbs that overwrite or remove existing engineering data.
        private static readonly string[] DestructivePrefixes =
        {
            "Delete", "Import", "Move", "Set", "Apply", "Sync", "Repair", "Seed",
        };

        private static readonly HashSet<string> DestructiveExtra = new HashSet<string>(StringComparer.Ordinal)
        {
            // Can invoke anything, so it inherits the worst case. They stay usable read-only, which is
            // why they are not DirectOnly - but they need allowWrite=true to change anything.
            "InvokeObject", "InvokeService",
            // Close (or replace) the open project; unsaved edits are lost.
            "CloseProject", "OpenProject", "CreateProject", "ScaffoldProject", "Disconnect",
            // Drop live online sessions, including one the user opened in the TIA UI.
            "GoOffline", "GoOfflineAll",
            "DownloadToPlc", "PlcBuildAndImport", "GenerateBlocksFromExternalSource",
            "RegenerateBlockFromSource", "ClearExports",
            // Writes the project itself, and moves blocks between groups - neither reads as a write
            // from the verb alone.
            "SaveProject", "SaveAsProject", "AutoClassifyBlocks",
        };

        private static readonly HashSet<string> OpenWorldTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "DownloadToPlc", "GoOnline", "GoOffline", "GoOfflineAll", "GetOnlineState",
            "CompareSoftwareToOnline", "GetPlcRunStateS7", "ProbeS7CpuIdentity",
            "ReadPlcLiveValuesS7", "ReadPlcLiveValuesOpcUa", "SamplePlcLiveValuesS7",
            "MonitorWatchTableLiveS7", "TraceTagCauseLive",
        };

        // Repeating the call with the same arguments leaves the same end state.
        private static readonly string[] IdempotentPrefixes =
        {
            "Ensure", "Bind", "Set", "Export", "Save", "Compile", "Connect", "Attach", "GoOffline",
        };

        private static readonly HashSet<string> NotIdempotent = new HashSet<string>(StringComparer.Ordinal)
        {
            "ConnectIsolated",   // starts one more headless TIA Portal instance on every call
        };

        public static ToolSafetyInfo Classify(string? toolName)
        {
            string name = toolName ?? "";
            bool readOnly = !NotReadOnly.Contains(name)
                && (ReadOnlyExtra.Contains(name) || StartsWithAny(name, ReadOnlyPrefixes));
            bool destructive = !readOnly
                && (DestructiveExtra.Contains(name) || StartsWithAny(name, DestructivePrefixes));
            bool idempotent = readOnly
                || (!NotIdempotent.Contains(name) && StartsWithAny(name, IdempotentPrefixes));

            return new ToolSafetyInfo
            {
                ReadOnly = readOnly,
                Destructive = destructive,
                Idempotent = idempotent,
                OpenWorld = OpenWorldTools.Contains(name),
                TouchesPortal = !PortalFree.Contains(name),
                DirectOnly = DirectOnly.Contains(name),
            };
        }

        /// <summary>Short human label for the risk annotations FindTools prints. The wording is
        /// deliberate: a tool can be read-only against the TIA project and still write a file, and
        /// calling that "mutating" contradicted the tool's own description and taught the reader not
        /// to trust the label.</summary>
        public static string RiskLabel(ToolSafetyInfo info)
        {
            var parts = new List<string>();
            if (info.DirectOnly) parts.Add("RISK changes CPU/deletes data/closes project - call by name");
            else if (info.Destructive) parts.Add("writes or overwrites engineering data");
            else if (!info.ReadOnly) parts.Add("reads the project, writes a file");
            else parts.Add("read-only");
            if (info.OpenWorld) parts.Add("touches a live CPU");
            if (!info.Idempotent) parts.Add("not idempotent");
            return string.Join("; ", parts);
        }

        /// <summary>A verb prefix only counts at a word boundary: "Settle" is not "Set".</summary>
        private static bool StartsWithAny(string name, string[] prefixes)
        {
            foreach (var p in prefixes)
            {
                if (name.Length > p.Length
                    && name.StartsWith(p, StringComparison.Ordinal)
                    && char.IsUpper(name[p.Length]))
                    return true;
                if (name.Length == p.Length && name == p) return true;
            }
            return false;
        }
    }
}
