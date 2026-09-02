# TIA Portal Openness MCP — Multi-Version (V18 / V20 / V21)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) ![TIA Portal](https://img.shields.io/badge/TIA%20Portal-V18%20%2F%20V20%20%2F%20V21-blue.svg) ![MCP Tools](https://img.shields.io/badge/MCP%20Tools-166-green.svg)

> **TIA Portal Openness MCP** — an independently maintained, MIT-licensed MCP server for **Siemens TIA Portal V18 / V20 / V21**.

On **Windows + TIA Portal V18 / V20 / V21**, drive TIA Portal through **MCP (stdio)**: create projects, add hardware, generate PLC objects (Tag / UDT / DB / SCL / LAD), build **Classic / Comfort / Unified HMI** screens and tags, compile-and-diagnose, and save. The bundle ships **prebuilt runtimes**, a Skill spec, templates, a capability matrix, and a manual. **No source-clone required** — download the zip at the repository root, unzip, and go.

## Quick start (3 steps)

1. **Prepare**: install **TIA Portal V18** + **.NET Framework 4.8**; add your Windows user to the local **`Siemens TIA Openness`** group and log off/on once.
2. **Download & unzip**: grab `TIA_Portal_Openness_MCP.zip` from the `master` branch root and unzip it anywhere.
3. **Mount the MCP**: in your MCP client (WorkBuddy / Cursor / VS Code / Claude Desktop, etc.), point `command` at
   `tools\tiaportal-mcp\src\TiaMcpServer\bin-v18\Release\net48\TiaMcpServer.exe`,
   pass `args` `["--tia-major-version","18","--logging","0"]`, trust the connector, and restart the client. The connector exposes **166 safe V18 tools**.

---

## Version notes (V18 / V20 / V21)

This repository maintains **V18 / V20 / V21** builds (three csproj files producing the same `TiaMcpServer.exe`, distinguished by output directory `bin-v18` / `bin-v20` / `bin`). The **V18 build** applies the following compatibility handling:

- **WinCC Unified HMI is unavailable in V18**: a full V18 Openness install does not include `Siemens.Engineering.HmiUnified`. The **23 Unified HMI tools** are hidden in the V18 build via `#if !TIA_V18` guards (bodies kept, simply not registered as MCP tools).
- **Exposed tool count**: the connector reports **166 safe V18 tools** (baseline 189 − 23 Unified); V20/V21 builds expose the full set.
- **Audit result**: apart from those 23 Unified tools, every exposed tool is safe and usable under V18; V20-only document tools (`Export/Import*Documents`) are exposed in V18 only as guided hints and are not callable.
- **HMI automation path**: under V18 use the **Classic / Comfort HMI** tool family; Unified requires the V20/V21 build.

---

## Setup

1. **Environment**
   - Install **.NET Framework 4.8** and **TIA Portal V18**;
   - Add the current user to the local **`Siemens TIA Openness`** group and re-login;
   - Locate the TIA install root (one of):
     a) pass `--tia-portal-location "C:/Program Files/Siemens/Automation/Portal V18"` at launch (recommended for non-default installs);
     b) set the `TiaPortalLocation` user environment variable;
     c) let it auto-read from the registry.
   - Authorize **Openness** in the TIA popup on first connect.

2. **Mount the MCP (manual config, most reliable)**
   Point `command` at the V18 exe inside the bundle:

   ```json
   {
     "mcpServers": {
       "tia-portal": {
         "command": "C:/path/to/unzip/tools/tiaportal-mcp/src/TiaMcpServer/bin-v18/Release/net48/TiaMcpServer.exe",
         "args": ["--tia-major-version", "18", "--logging", "0", "--with-ui"],
         "env": { "TiaPortalLocation": "C:/Program Files/Siemens/Automation/Portal V18" }
       }
     }
   }
   ```

   (On a managed MCP client, set `disabled` to `true` in the connector config to release the file lock before upgrading `TiaMcpServer.exe`, then restore it after copying. The same exe also supports `TiaMcpServer.exe config` to auto-discover and write host config — manual config is more reliable.)

3. **First-call sequence**
   - `Bootstrap` → `Connect` → `OpenProject` (or `CreateProject`) → `GetProjectTree`, then read the real `PLC_xxx` / `HMI_RT_xxx` paths from the tree before continuing.

---

## Capabilities & boundaries

**Can do**: project & hardware configuration, PROFINET, declarative PLC import (Tag/UDT/DB/SCL/LAD), Classic / Comfort HMI connections/tags/screens, cross-reference & impact analysis, batch grouping & auto-classification, compile-and-diagnose, save.

**Not available under V18**:
- WinCC **Unified** HMI (23 tools hidden; V18 Openness lacks the `HmiUnified` assembly) — requires V20/V21.
- S7DCL human-readable SCL text export (`Export/Import*Documents` is V20+ only; exposed as a guided hint only in this build). Use `ExportBlockSourceUtf8` + `RegenerateBlockFromSource` instead (SimaticML XML, UTF-8+BOM).

**Not bundled**: Siemens install media, field projects, business-specific technology.

---

## Version comparison

| Capability | V18 build | V20 / V21 build |
|------|-----------|------------------------|
| Common PLC / Classic HMI tools | ✅ | ✅ |
| WinCC Unified HMI tools | ❌ (guarded/hidden) | ✅ |
| Document import/export (`*Documents` / S7DCL) | ⚠️ guided hint only | ✅ callable |
| Exposed tool count | **166** | ~180–189 |

---

## Build (if compiling from source)

Three csproj files map to the three TIA versions, distinguished by output directory:

```bat
:: V18 (Unified HMI hidden, 166 tools)
dotnet build TiaMcpServer.V18.csproj -c Release ^
  -p:TiaPortalLocation="C:/Program Files/Siemens/Automation/Portal V18" ^
  -p:BaseOutputPath=bin-v18/ -p:BaseIntermediateOutputPath=obj-v18/

:: V20 (monolithic session assembly)
dotnet build TiaMcpServer.V20.csproj -c Release ^
  -p:TiaPortalLocation="D:/Program Files/Siemens/Automation/Portal V20" ^
  -p:BaseOutputPath=bin-v20/ -p:BaseIntermediateOutputPath=obj-v20/

:: V21 (split assemblies)
dotnet build TiaMcpServer.csproj -c Release
```

`TiaMcpServer.V18.csproj` defines the `TIA_V18` compile symbol, which automatically hides the Unified HMI tools.

---

## What's delivered

The `master` branch root provides:

```
TIA_Portal_Openness_MCP.zip   ← full toolkit (~26 MB, 663 files)
```

The archive contains: prebuilt runtime `bin-v18/`, source `src/`, docs `docs/`, templates `templates/`, a Skill, the capability matrix `manifest/`, config `.mcp.json`, plus a more detailed `README.md` and `LICENSE`. Everything needed is inside the archive — no separate source clone required.

> This repository is delivered as a packaged bundle and does not separately commit the source tree, to avoid mixing it with build artifacts. For source, unzip the archive.

---

## Documentation map (inside the archive)

| Path | What |
|------|------|
| `tools/tiaportal-mcp/skill/SKILL.md` | Primary spec: tool layers, parameter traps, HMI schema, LAD/SCL boundaries |
| `manifest/tools-list.json` | Static tool names/layers (runtime authority is `tools/list` after connect) |
| `docs/tool-capability-matrix.md` | Capability matrix |
| `docs/scl-instruction-library.md` / `docs/lad-instruction-library.md` | SCL / LAD instruction libraries |
| `docs/hmi-plc-tag-binding-and-addressing.md` | HMI↔PLC binding & addressing |
| `templates/plc/` · `templates/hmi/` | PLC / HMI template index |
| `手册/` | Quick start, Openness limitations, error model, etc. |

---

## FAQ

**Q: The connector only shows some tools / a tool is missing?**
This is a client tool-descriptor/cache issue, not a trimmed bundle — restart the client or clear the tool cache. The runtime authority is `tools/list`.

**Q: Why no WinCC Unified?**
TIA Portal V18's full Openness install does not include `Siemens.Engineering.HmiUnified`; the Unified HMI tools are hidden in the V18 build via `#if !TIA_V18`. Use the Classic / Comfort HMI family under V18; use V20/V21 for Unified.

**Q: "File in use" when upgrading `TiaMcpServer.exe`?**
The managed MCP connector's process is hosted and respawns immediately. Set `disabled` to `true` in the connector config to release the file lock before upgrading, then restore it.

**Q: Is it IDE-independent?**
Yes. Any MCP-capable client (WorkBuddy, Cursor, VS Code, Claude Desktop, custom HTTP clients, etc.) can use the same `TiaMcpServer.exe`.

---

## License

Released under the **MIT License** — see `LICENSE`.
