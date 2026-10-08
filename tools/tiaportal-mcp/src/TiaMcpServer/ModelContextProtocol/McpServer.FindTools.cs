using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;

namespace TiaMcpServer.ModelContextProtocol
{
    // Tool search over the full roster.
    //
    // This server lists every tool it has, which is the right default for a host that can carry them
    // (the descriptions cost roughly 30-45k tokens per turn). FindTools is the escape hatch for the
    // other case: when the model does not remember a tool exists, it can search for it and get the
    // exact name, the parameter signature and the risk flags - without the whole roster being loaded
    // up front. It is purely additive: nothing about the advertised tool list changes.
    //
    // Risk flags come from ToolSafety so that "looks like a read" cannot quietly mean "overwrites
    // blocks" or "closes the project the user has open".
    //
    // Pattern follows upstream bulaofen0036-coder/TIA_Portal_Openness_MCP (McpServer.ToolBridge.cs,
    // commit 53731356), reduced to the search half - this fork does not ship a tool proxy.
    public static partial class McpServer
    {
        private static Dictionary<string, MethodInfo>? _allToolMethods;

        private static Dictionary<string, MethodInfo> AllToolMethods()
        {
            if (_allToolMethods != null) return _allToolMethods;
            var map = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in typeof(McpServer).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = m.GetCustomAttribute<McpServerToolAttribute>();
                if (attr == null) continue;
                map[attr.Name ?? m.Name] = m;
            }
            _allToolMethods = map;
            return map;
        }

        private static string ToolDescription(MethodInfo m)
            => m.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

        /// <summary>Parameters the SDK injects; they are never part of what a caller sends, so they are
        /// not shown. A tool that only takes these has no callable signature.</summary>
        private static bool IsInjectedParameterType(Type t)
            => t == typeof(IMcpServer)
            || t == typeof(RequestContext<CallToolRequestParams>)
            || t == typeof(CancellationToken);

        private static string RenderSignature(string name, MethodInfo m)
        {
            var ps = m.GetParameters().Where(p => !IsInjectedParameterType(p.ParameterType)).ToList();
            if (ps.Count == 0) return name + "()";
            return name + "(" + string.Join(", ", ps.Select(p =>
            {
                var t = p.ParameterType == typeof(string) ? "string"
                    : p.ParameterType == typeof(int) ? "int"
                    : p.ParameterType == typeof(long) ? "long"
                    : p.ParameterType == typeof(bool) ? "bool"
                    : p.ParameterType == typeof(double) ? "double"
                    : p.ParameterType.IsArray ? p.ParameterType.GetElementType()?.Name + "[]"
                    : p.ParameterType.Name;
                var d = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var head = d.Contains('：') ? d.Substring(0, d.IndexOf('：')) : d.Split('.')[0];
                if (head.Length > 28) head = head.Substring(0, 28) + "...";
                return t + " " + p.Name + (p.HasDefaultValue ? " = " + RenderDefault(p) : "")
                       + (head.Length > 0 ? "  // " + head : "");
            })) + ")";
        }

        /// <summary>Defaults the way a caller would write them, not the way C# prints them
        /// (True -> true, "" stays "", a null string shows as null).</summary>
        private static string RenderDefault(ParameterInfo p)
        {
            object? v = p.DefaultValue;
            if (v == null) return p.ParameterType == typeof(string) ? "\"\"" : "null";
            if (v is string s) return "\"" + s + "\"";
            if (v is bool b) return b ? "true" : "false";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        [McpServerTool(Name = "FindTools"), Description(
            "[L0][Meta] Search ALL tools by capability and get the exact name, parameter signature and risk flags. " +
            "USE THIS whenever you are not certain which tool to call, before concluding the server cannot do something, " +
            "or when a tool you half-remember seems to be missing. Search by capability words, not exact names: " +
            "'watch table', 'HMI screen', 'cross reference', 'GSD', 'block comments', 'drive telegram'. " +
            "Every match is annotated: 'read-only', 'reads the project, writes a file', " +
            "'writes or overwrites engineering data', or " +
            "'RISK changes CPU/deletes data/closes project - call by name'. Empty query lists the whole roster.")]
        public static ResponseStringList FindTools(
            [Description("query: space-separated words matched against tool names and descriptions. Empty lists everything.")] string query = "",
            [Description("limit: max tools to return (default 12). Raise it for a broad survey.")] int limit = 12)
        {
            try
            {
                var all = AllToolMethods();
                if (limit <= 0) limit = 12;

                var terms = (query ?? "")
                    .Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => t.Length > 0)
                    .ToArray();

                var scored = new List<KeyValuePair<int, string>>();
                foreach (var kv in all)
                {
                    string lname = kv.Key.ToLowerInvariant();
                    string desc = ToolDescription(kv.Value).ToLowerInvariant();
                    int score = 0;
                    if (terms.Length == 0) score = 1;
                    foreach (var t in terms)
                    {
                        // A name hit outranks a description hit: searching "watch table" should put
                        // ExportPlcWatchTable above every tool that merely mentions it.
                        if (lname == t) score += 100;
                        else if (lname.Contains(t)) score += 20;
                        if (desc.Contains(t)) score += 3;
                    }
                    if (score > 0) scored.Add(new KeyValuePair<int, string>(score, kv.Key));
                }

                if (scored.Count == 0)
                {
                    return new ResponseStringList
                    {
                        Message = "No tool matches '" + query + "'. Try fewer or more general words (e.g. 'watch table' "
                                  + "instead of 'ExportPlcWatchTableToCsv'), or call FindTools with an empty query to list everything.",
                    };
                }

                var hits = scored.OrderByDescending(x => x.Key).ThenBy(x => x.Value, StringComparer.Ordinal)
                                .Take(limit).ToList();
                var lines = new List<string>();
                foreach (var h in hits)
                {
                    var m = all[h.Value];
                    var info = ToolSafety.Classify(h.Value);
                    // Annotation first: the signature can be long, and the risk is the part that must
                    // never be the part that gets cut off.
                    lines.Add("[" + ToolSafety.RiskLabel(info) + "]  " + RenderSignature(h.Value, m));
                    var d = ToolDescription(m);
                    if (d.Length > 0) lines.Add("    " + (d.Length > 260 ? d.Substring(0, 260) + "..." : d));
                }

                return new ResponseStringList
                {
                    Message = hits.Count + " of " + scored.Count + " matching tools (roster: " + all.Count
                              + " total). Annotations: read-only / writes or overwrites / RISK changes CPU, deletes data "
                              + "or closes the open project - those must be called by their own name so the user sees them.",
                    Items = lines,
                };
            }
            catch (Exception ex)
            {
                return new ResponseStringList { Message = "FindTools failed: " + ex.Message };
            }
        }
    }
}
