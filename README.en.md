# TIA Portal Openness MCP (V18 / V20 / V21)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![TIA Portal](https://img.shields.io/badge/TIA%20Portal-V18%20%2F%20V20%20%2F%20V21-blue)
![Tools](https://img.shields.io/badge/tools-232%20(V20)%20%2F%20204%20(V18)-green)

Drive TIA Portal from an AI agent over **MCP (stdio)**: read structure, export blocks, generate PLC code,
edit hardware configuration, and run offline project audits. The repository ships **pre-built runtimes** —
unzip and go; you do not need to compile anything.

---

## 1. What it does

| Area | Capabilities |
|---|---|
| **Read** | Project tree, software tree, block hierarchy, block interfaces, tag tables, UDTs, cross references, call chains, online values |
| **Export** | Single / batch blocks and types → SimaticML XML, **with disk facts reported** (files written, empty files, unparsable XML, bytes) |
| **Write** | Create / delete / move blocks, renumber, import UDTs / tag tables / technology objects, import from external sources, regenerate from SCL |
| **Hardware** | Add devices, plug modules, set I/O addresses, configure PROFINET subnets, drive parameters and telegrams |
| **HMI** | Classic / Comfort / Unified screens, tags, connections, button actions |
| **Diagnostics** | Compile and collect messages, broken-link and address-consistency checks, list of inconsistent blocks |
| **Audit** | Offline rule engine (see §5), CI friendly |

Layers: `L0` bootstrap 7 · `L1` everyday 57 · `L2` specialised 168.
The V20 build exposes **232** tools; the V18 build hides 28 capabilities V18 does not have
(23 Unified HMI + 5 VersionControl) and exposes **204**.

---

## 2. Connect in 60 seconds

**Prerequisites**: Windows x64, **TIA Portal V18/V20/V21 with Openness selected during setup**, .NET Framework 4.8,
and the current Windows user in the local group `Siemens TIA Openness` (sign out and back in after changing it).

### Option A — one-line install (recommended)

```powershell
powershell -ExecutionPolicy Bypass -File .\install-mcp.ps1
```

The installer **scans every fixed drive** for TIA installations (no guessed default), lists what it found,
lets you confirm, then writes the MCP configuration.

### Option B — wire it up manually

Point your MCP client at the launcher inside the extracted folder:

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

Register **one entry only**. The version is chosen automatically from the project you open (see §3).

---

## 3. Three design decisions that matter

### 3.1 One connector, engine chosen by project version

The Openness assemblies of a given TIA version can only be loaded into one process, therefore:

- `tia_mcp_launcher.bat` **scans all fixed drives** and treats the filesystem as the only authority
  (`Portal V<XX>\bin\Siemens.Automation.Portal.exe` **and** `PublicAPI\V<XX>\Siemens.Engineering.dll`).
  **The registry is not trusted** — incomplete uninstalls leave keys behind, and a `Portal V20\PublicAPI`
  folder legitimately contains client DLLs for V17–V20.
- `tia_router.py` is **one** MCP server in front of per-version child engines. On `OpenProject` it reads the
  project extension (`.ap18` / `.ap20`) and switches to the matching engine. Before switching it **saves and
  closes** the open project; if the save fails the switch is aborted — it never force-closes your work.
- Multiple versions installed? It **does not guess**: it reports `ambiguous`, lists the candidates, and
  remembers your one-time choice in the config.

### 3.2 Headless for reading, a window for writing

- `ConnectIsolated(withUserInterface=false)` — no window; for reading structure, blocks, exports, audits.
- `ConnectIsolated(withUserInterface=true)` — visible TIA window; for anything that changes the project.
- **Write gate**: 65 write tools are refused outright on a headless instance, with a message telling you to
  `Disconnect` and reconnect with `withUserInterface=true`. The tool never edits your program in a window
  you cannot see.
- Only instances and projects **this server opened** are ever closed; attached user projects are left alone.

### 3.3 "The tool said OK" is not evidence — the disk is

Export, compile and write operations **read back the actual result**: export responses carry
`filesWritten / emptyFiles / unparsableFiles / totalBytes / diskVerified`. Zero new files, or any empty /
unparsable file, makes `success=false`, and the message says
`disk shows N new file(s)` or `treat this export as FAILED`.

---

## 4. Three workflows (for the agent, not new tools)

Available as MCP Prompts after mounting. They fix the ordering, the stop gates and the "never do this" list:

| Prompt | Purpose |
|---|---|
| `Workflow_AuditProject` | Read-only audit: identity gate → tree → software tree → block hierarchy → consistency → types → export reconciliation → offline rules → report |
| `Workflow_ExportForReview` | Export and **prove** the disk artefacts before any offline parsing |
| `Workflow_EditBlock` | Impact analysis → backup → edit → re-import → compile → verify → save |

---

## 5. Project audit (offline, CI friendly)

```powershell
# Collect (attaches to TIA, read-only)
python scripts\tia_project_audit.py --collect --project "<project.apXX>" --software <plcName> --out report.md

# Analyse collected data only (no TIA)
python scripts\tia_project_audit.py --skeleton s.json --hierarchy h.json --out report.md
```

Rules enabled by default (all are **fact checks**, no naming style):

| Rule | Meaning |
|---|---|
| R0 / R0b | **Source reconciliation** between skeleton and hierarchy (block count, per-type counts); mismatch is P1 |
| R1 / R3 / R5 | Loose root-level blocks, unpartitioned numbering, sparse number space |
| R7 | Blocks not compiled (`isConsistent=false`) — export and download will be refused |
| R8 | Know-how protected blocks (logic not readable; needs manual unlock) |
| R11 / R12 | Empty groups, programming-language distribution |
| R13 / R13b | Suspected orphans (no callers, heuristic) + **call-graph coverage statement** |

Naming conventions (group prefixes, block prefixes, special characters in names) are **not checked by default** —
naming belongs to your own framework. Pass `--naming-check` to enable them.

---

## 6. Versions and tool counts

| Build | Output | TIA | Tools | Notes |
|---|---|---|---|---|
| V18 | `bin-v18` | V18 | 204 | 28 capabilities hidden (not available in V18) |
| V20 | `bin-v20` | V20 | 232 | Full tool set |
| V21 | `bin` | V21 | 232 | Upstream build |

V16 / V17 / V19 are not supported: no usable Openness channel there (or no build shipped here).
When an unsupported version is detected, the tool **says so** instead of silently degrading.

---

## 7. How this differs from similar projects

The most active comparable open-source project is [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp)
(**v4.2.0**, released 2026-10-25; .NET 10; TIA V21; 14 read-write / 4 read-only tools according to its v3.0.0 discussion).

| Dimension | This project (2.6.2) | Czarnak/tia-portal-mcp (v4.2.0) |
|---|---|---|
| Tool granularity | **Many small tools** (232, composable) | **Few composite tools** (preview → apply) |
| TIA versions | V18 / V20 / V21 builds, **routed by project** | V21 only |
| Runtime | .NET Framework 4.8 (built into Windows) | .NET 10 (install runtime or use the self-contained zip) |
| Write safety | **Window gate** (headless instances refuse writes) + only closes self-started instances | Access modes (read-only hides write tools) + preview tokens + audit JSONL |
| Audit | Built-in offline rule engine (reconciliation / uncompiled / protected / orphans) | No separate audit script |
| Distribution | Runtimes included in the repo, unzip and go | Self-contained zip / NuGet global tool |

Different optimisations: this project targets "one engineer, several TIA versions on one machine, every step explainable".

---

## 8. Repository layout

```
tools/tiaportal-mcp/
  tia_mcp_launcher.bat      # version detection + start the matching build
  detect_tia_version.ps1    # scan all fixed drives (filesystem is the authority)
  tia_router.py             # single connector, routes by project version
  src/TiaMcpServer/         # engine source (three csproj: V18 / V20 / upstream)
scripts/                    # audit, manifest generation, release gates, version switch
manifest/                   # tools-list.json / package-manifest.json
docs/                       # index, user directives, lessons learned
runtime/                    # pre-built runtimes used by the distribution
```

---

## 9. FAQ

**"Install path not found", or it always uses V18 on a new machine?**
Fixed in 2.5.5: detection scans every fixed drive, trusts the filesystem, and fails loudly instead of
falling back to V18. To see what it found:
`powershell -File tools\tiaportal-mcp\detect_tia_version.ps1 -List`.

**I have both V18 and V20 — how do I switch?**
You do not. With one connector, `.ap18` goes to V18 and `.ap20` goes to V20.
To pin a default: `scripts\Set-TiaVersion.ps1 -Version 18 -Apply`.

**`AttachToOpenProject` does not attach?**
On some machines `Attach()` hangs (reproduced twice here). Use `ConnectIsolated` + `OpenProject` instead.

**Export "succeeded" but the folder is empty?**
Not since 2.6.1: exports report `filesWritten` and friends; `success=false` means engine and disk disagree.

**V20 compiles slowly?**
Normal with 100+ technology objects — 60 s or more. Give `timeoutSeconds` 600.

---

## 10. Changelog and license

Per-release notes: [CHANGELOG.md](CHANGELOG.md). Current version **2.6.2**.

MIT License — see [LICENSE](LICENSE).
