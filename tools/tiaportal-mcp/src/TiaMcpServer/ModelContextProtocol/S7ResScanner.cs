using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Scanner for SIMATIC SD resource files (.s7res) — the per-block multi-language
    /// text store that ships next to a .s7dcl.
    ///
    /// 🔴 **Real files come in TWO shapes, and they are version-dependent:**
    ///
    /// **V20 — XML** (verified on a real export, 2026-09-24; `OP_00_Main.s7res`, 254 B):
    /// <code>
    /// &lt;root&gt;
    ///   &lt;Comment Id="MLC_34j"&gt;
    ///     &lt;MultiLanguageText Lang="de-DE"&gt;&lt;/MultiLanguageText&gt;
    ///     &lt;MultiLanguageText Lang="en-US"&gt;Call the Manual  Logic&lt;/MultiLanguageText&gt;
    ///     &lt;MultiLanguageText Lang="zh-CN"&gt;&lt;/MultiLanguageText&gt;
    ///   &lt;/Comment&gt;
    /// &lt;/root&gt;
    /// </code>
    /// The .s7dcl side references such an id, e.g. <c>S7_NetworkTitle := "MLC_34j"</c>.
    ///
    /// **V21 — YAML** (shape this scanner was originally written for; upstream targets V21):
    /// <code>
    /// MultiLingualTexts:
    ///   - id: MLC_start
    ///     zh-CN: 启动
    ///     en-US: Start
    /// </code>
    ///
    /// The previous version only understood the YAML shape and treated "not YAML" as
    /// "nothing to warn about" — so on V20 it silently returned an empty list for every
    /// real file and the missing-en-US pre-check never fired. That is why the detector
    /// below dispatches on the actual content instead of assuming a version.
    ///
    /// Dependency-free on purpose: the offline check suite compiles this file for net8.0
    /// with no Siemens assemblies, so the parse must be plain string/regex work.
    /// </summary>
    internal static class S7ResScanner
    {
        private const string EnUsKey = "en-US";

        /// <summary>
        /// Returns the MultiLingualText ids that have no non-empty <c>en-US</c> entry.
        /// Empty list when the file does not exist, or when its shape is unrecognizable
        /// (unknown shape is reported as "nothing to warn about", never as "everything is missing").
        /// </summary>
        public static List<string> GetMissingEnUsIds(string directory, string baseName)
        {
            var path = Path.Combine(directory, baseName + ".s7res");
            if (!File.Exists(path))
            {
                return new List<string>();
            }

            return GetMissingEnUsIdsFromText(File.ReadAllText(path));
        }

        /// <summary>
        /// Dispatch on content: XML if the first non-whitespace character is '&lt;', YAML otherwise.
        /// Kept as a text-level entry point so both shapes can be exercised without a file.
        /// </summary>
        public static List<string> GetMissingEnUsIdsFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            var body = text!.TrimStart('\uFEFF').TrimStart();
            if (body.Length > 0 && body[0] == '<')
            {
                return GetMissingEnUsIdsFromXml(body);
            }

            return GetMissingEnUsIdsFromLines(text.Split('\n'));
        }

        // ── V20: XML shape ────────────────────────────────────────────────

        // Any element that carries an Id attribute owns a multi-language text block.
        // Matches opening tags only (a closing tag starts with '/', which the name group forbids),
        // so nested elements inside a body are still discovered on their own.
        private static readonly Regex OpenTag =
            new Regex("<(?<tag>[A-Za-z_][A-Za-z0-9_.\\-]*)(?<attrs>[^>]*)>", RegexOptions.Singleline);

        private static readonly Regex IdAttr =
            new Regex("Id\\s*=\\s*[\"'](?<id>[^\"']*)[\"']", RegexOptions.IgnoreCase);

        /// <summary>Ids that carry no non-empty text for <paramref name="lang"/> inside the given body.</summary>
        private static bool HasNonEmptyLang(string body, string lang)
        {
            var rx = new Regex("Lang\\s*=\\s*[\"']" + Regex.Escape(lang) + "[\"']\\s*/?>\\s*(?<t>.*?)\\s*</",
                               RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var m = rx.Match(body);
            return m.Success && m.Groups["t"].Value.Trim().Length > 0;
        }

        public static List<string> GetMissingEnUsIdsFromXml(string xml)
        {
            var missing = new List<string>();
            var sawIdElement = false;

            foreach (Match open in OpenTag.Matches(xml))
            {
                var attrs = open.Groups["attrs"].Value;
                // A self-closing tag (<x ... />) has no body to inspect.
                if (attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                var idm = IdAttr.Match(attrs);
                if (!idm.Success)
                {
                    continue;
                }

                var tag = open.Groups["tag"].Value;
                var closeTag = "</" + tag + ">";
                var close = xml.IndexOf(closeTag, open.Index + open.Length, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    continue;   // malformed / truncated — skip rather than guess
                }

                sawIdElement = true;
                var id = idm.Groups["id"].Value;
                var body = xml.Substring(open.Index + open.Length, close - (open.Index + open.Length));
                if (!HasNonEmptyLang(body, EnUsKey))
                {
                    missing.Add(id.Length == 0 ? "<unnamed>" : id);
                }
            }

            // No id-bearing element at all: `<root />` (empty block) or an unknown shape.
            return sawIdElement ? missing : new List<string>();
        }

        // ── V21: YAML shape (unchanged) ───────────────────────────────────

        /// <summary>Line-level scan of the YAML shape; separated out so it can be exercised without a file.</summary>
        public static List<string> GetMissingEnUsIdsFromLines(IReadOnlyList<string> lines)
        {
            var missing = new List<string>();
            var sawContainer = false;
            string? currentId = null;
            var currentHasEnUs = false;

            void Flush()
            {
                if (currentId != null && !currentHasEnUs)
                {
                    missing.Add(currentId);
                }
                currentId = null;
                currentHasEnUs = false;
            }

            foreach (var raw in lines)
            {
                // Strip a UTF-8 BOM on the first line and any trailing whitespace.
                var line = raw.TrimStart('\uFEFF').TrimEnd();
                var trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                if (trimmed.StartsWith("MultiLingualTexts:", StringComparison.OrdinalIgnoreCase))
                {
                    sawContainer = true;
                    continue;
                }

                // New list item: "- id: MLC_xxx"
                if (trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    var item = trimmed.Substring(1).TrimStart();
                    if (TryReadValue(item, "id", out var id))
                    {
                        Flush();
                        currentId = id.Length == 0 ? "<unnamed>" : id;
                        continue;
                    }
                }

                if (currentId != null && TryReadValue(trimmed, EnUsKey, out var enUs) && enUs.Length > 0)
                {
                    currentHasEnUs = true;
                }
            }

            Flush();

            return sawContainer ? missing : new List<string>();
        }

        /// <summary>Matches "key: value" case-insensitively on the key; unquotes the value.</summary>
        private static bool TryReadValue(string text, string key, out string value)
        {
            value = "";
            if (!text.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = text.Substring(key.Length).TrimStart();
            if (rest.Length == 0 || rest[0] != ':')
            {
                return false;
            }

            value = rest.Substring(1).Trim().Trim('"', '\'');
            return true;
        }
    }
}
