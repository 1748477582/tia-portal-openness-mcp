# TIA Portal Openness MCP

[中文](README.md) · [English](README.en.md) · [Documentation index](docs/INDEX.md) · [Changelog](CHANGELOG.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)
[![TIA Portal](https://img.shields.io/badge/TIA_Portal-V18%20%7C%20V20%20%7C%20V21-009999.svg?style=flat-square)](https://www.siemens.com)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4.svg?style=flat-square)](https://dotnet.microsoft.com)
[![MCP](https://img.shields.io/badge/MCP-stdio-000000.svg?style=flat-square)](https://modelcontextprotocol.io)
[![Tools](https://img.shields.io/badge/tools-232%20%2F%20204-2f8f46.svg?style=flat-square)](#tools)
[![Version](https://img.shields.io/badge/version-2.6.3-blue.svg?style=flat-square)](CHANGELOG.md)

An MCP server built on Siemens Openness. It lets MCP clients and AI agents (WorkBuddy, Claude Desktop,
Cursor, VS Code, …) read and write TIA Portal projects: inspect structure, export blocks, generate PLC code,
edit hardware configuration, compile, and run project audits.

Three builds are maintained — **V18 / V20 / V21** — behind **one connector** that picks the engine from the
project you open. Pre-built runtimes are included; unzip and go.

---

## Tools

232 tools in the V20 build, in three layers: `L0` bootstrap 7 · `L1` everyday 57 · `L2` specialised 168.
The V18 build hides 28 capabilities V18 does not have and exposes 204.

| Area | Representative tools | Notes |
|---|---|---|
| Connect / project | `ConnectIsolated` `OpenProject` `AttachToOpenProject` `SaveProject` `CloseProject` `GetState` | Headless vs visible instance; project lifecycle |
| Structure | `GetProjectTree` `GetSoftwareTree` `GetBlocksWithHierarchy` `GetTypes` | Project tree, software tree, block hierarchy with consistency/protection flags |
| Blocks | `PlcBuildAndImport` `ImportBlock` `DeletePlcBlock` `SetBlockNumber` `MoveBlocksToGroup` `RegenerateBlockFromSource` | Create / modify / delete / renumber / regroup |
| Reading logic | `DescribeBlockLogic` `GetBlockInfo` `AnalyzeBlockImpact` `GetCallChain` | LAD/SCL as text; blast radius before editing |
| Types & tag tables | `ImportType` `ImportPlcTagTable` `GetPlcTagTables` `ImportPlcExternalSource` | UDTs, tag tables, external sources |
| Hardware & network | `AddDevice` `PlugDeviceItem` `SetDeviceItemIoAddress` `EnsureSubnet` `ConnectDeviceNodesToProfinetSubnet` `SetDriveParameter` | Devices, modules, I/O, subnets, drives |
| HMI | `EnsureUnifiedHmiScreen` `ApplyUnifiedHmiLayout` `EnsureClassicHmiConnection` `ImportHmiScreen` `ImportAlarmClasses` | Classic / Comfort / Unified |
| Online & diagnostics | `GoOnline` `GoOffline` `DownloadToPlc` `GetPlcLiveValuesS7` `CompileAndDiagnosePlc` `GetCrossReferences` | Download, live values, compile messages, cross references |
| Export | `ExportBlocks` `ExportTypes` `ExportBlockSourceUtf8` `SaveExport` `GetExport` | Batch/single export; parked responses saved to disk |

**Workflow prompts** (not tools — they fix the ordering and stop gates for the agent):
`Workflow_AuditProject` · `Workflow_ExportForReview` · `Workflow_EditBlock`

---

## Write safety

A TIA project is a production asset. Four constraints apply to every write path:

- **Writing requires a window.** `ConnectIsolated(withUserInterface=false)` is for reading only. The **65 write tools**
  are refused on a headless instance, with a message telling you to `Disconnect` and reconnect with
  `withUserInterface=true`.
- **Only self-started things are closed.** Attached user projects are never auto-closed. Before switching versions the
  engine calls `CloseProject(saveBeforeClose=true)`; if the save fails the switch is aborted rather than forced.
- **The disk decides, not "the tool said OK".** Exports report
  `filesWritten / emptyFiles / unparsableFiles / totalBytes / diskVerified`; zero new files or any bad file makes
  `success=false`. `SaveExport` refuses drive roots, system/program folders and TIA installation folders.
- **No password guessing on know-how protected blocks.** The tool stops and asks; no dictionaries, no enumeration.

---

## Architecture

```
MCP client
   │  stdio
   ▼
tia_mcp_launcher.bat        locates TIA on every fixed drive (filesystem is the authority, not the registry)
   │
   ▼
tia_router.py               one MCP server
   ├── OpenProject(.ap18) ──► child engine TiaMcpServer.exe (bin-v18)  204 tools
   └── OpenProject(.ap20) ──► child engine TiaMcpServer.exe (bin-v20)  232 tools
```

Openness assemblies of one TIA version can only be loaded into one process, so switching version means switching
the child engine. The open project is saved and closed first. Multiple TIA processes of the same version can each
be served by one engine; two engines competing for the same TIA process are still refused
(mutex `TiaMcpServer.SingleAttach.V{major}.P{pid}`).

---

## Quick start

### 1. Prerequisites

| Item | Requirement | If missing |
|---|---|---|
| Windows | 10 1903+ / 11, x64 | — |
| TIA Portal | V18 / V20 / V21 with **Openness** selected during setup | Not detected; V16/V17/V19 unsupported |
| .NET Framework | 4.8 | Engine cannot start |
| User group | current user in `Siemens TIA Openness` | Connect is refused (sign out and back in) |
| Disk | ~40 MB | — |

### 2. Install

```powershell
# See what it finds first (read-only, writes no config)
powershell -File tools\tiaportal-mcp\detect_tia_version.ps1 -List

# Interactive install: lists local TIA installs -> you confirm -> writes MCP config
powershell -ExecutionPolicy Bypass -File .\install-mcp.ps1
```

Manual wiring (register **one** entry):

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "cmd",
      "args": ["/c", "<extract-dir>\\tools\\tiaportal-mcp\\tia_mcp_launcher.bat"],
      "env": { "TIA_MCP_ATTACH_TIMEOUT_MS": "8000" }
    }
  }
}
```

### 3. First call

```
ConnectIsolated(withUserInterface=false)      # read-only, no window
OpenProject("D:\\Work\\Line1\\Line1.ap20")    # routed to the V20 engine automatically
GetProjectTree()                              # get softwarePath
GetBlocksWithHierarchy(softwarePath)          # blocks + consistency + protection flags
```

To modify the program, reconnect with `ConnectIsolated(withUserInterface=true)`; otherwise write tools refuse.

---

## Compatibility

| Build | Output | TIA | Tools | Notes |
|---|---|---|---|---|
| V18 | `bin-v18` | V18 | 204 | 23 Unified HMI + 5 VersionControl tools hidden |
| V20 | `bin-v20` | V20 | 232 | Full tool set |
| V21 | `bin` | V21 | 232 | Upstream build |

---

## Project audit

Offline rule engine, CI friendly, no TIA required for the analysis step.

```powershell
python scripts\tia_project_audit.py --collect --project "<project.apXX>" --software <plcName> --out report.md
python scripts\tia_project_audit.py --skeleton s.json --hierarchy h.json --out report.md
```

| Rule | Meaning |
|---|---|
| R0 / R0b | **Source reconciliation** between skeleton and hierarchy (block count, per-type counts); mismatch is P1 |
| R1 / R3 / R5 | Loose root-level blocks, unpartitioned numbering, sparse number space |
| R7 | Blocks not compiled (`isConsistent=false`) |
| R8 | Know-how protected blocks |
| R11 / R12 | Empty groups, language distribution |
| R13 / R13b | Suspected orphans (heuristic) + call-graph coverage statement |

Naming conventions are not checked by default (they belong to your own framework); pass `--naming-check` to enable.

---

## Documentation

| File | Contents |
|---|---|
| [docs/INDEX.md](docs/INDEX.md) | Index of every doc/script/manifest (generated) |
| [docs/user-directives.md](docs/user-directives.md) | Hard constraints (verification, safety, process) |
| [docs/lessons-and-shortcuts.md](docs/lessons-and-shortcuts.md) | Pitfalls and shortcuts (symptom → cause → fix → evidence) |
| [CHANGELOG.md](CHANGELOG.md) | Per-release changes |
| [AGENTS.md](AGENTS.md) | First thing to read when taking over the repo |

---

## Other tools

Comparable open-source projects (queried 2026-10-29 via GitHub API):

| Project | Latest | Character |
|---|---|---|
| [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) | v4.2.0 | .NET 10 · TIA V21 · few composite tools + preview→apply |
| [asckye/TIA_Portal_Openness_MCP](https://github.com/asckye/TIA_Portal_Openness_MCP) | v3.3.0 | V14 SP1–V21 · Workbench + MCP |
| [renanlido/tia-openness](https://github.com/renanlido/tia-openness) | app-v1.7.0 | V21 · HTTP/JSON engine + bridge |
| [a4webdev/tiacommander-mcp](https://github.com/a4webdev/tiacommander-mcp) | v3.42.0 | 16 tools / 166 operations · V15.1–V21 |
| **This project** | **2.6.3** | V18/V20/V21 builds + auto-routing · window gate on writes · offline audit |

Rule of thumb: single version, few tools, preview tokens → Czarnak. Several TIA versions on one machine,
fine-grained control, built-in audit → this project.

---

## Contributing

Issues and suggestions via GitHub Issues. When changing tools, keep `manifest/tools-list.json` and
`docs/tool-capability-matrix.md` in sync, and run `scripts/Check-VersionConsistency.py`,
`Check-ManifestConsistency.py`, `Check-DeadToolReferences.py` plus the offline suite (currently 228 passed)
before submitting.

---

## License and disclaimer

MIT License — see [LICENSE](LICENSE).

This tool **really writes to TIA projects**. AI-generated PLC code must be reviewed by a human before production use.
Back up before writing (`ArchiveProject` or version control) and validate on a machine that is not running the plant.
