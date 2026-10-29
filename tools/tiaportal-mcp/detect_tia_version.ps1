# TIA Portal installation discovery for tia_mcp_launcher.bat
#
# WHY THIS EXISTS
#   The old script only looked at (1) env TIA_MCP_VERSION and (2) a RUNNING TIA process,
#   then the launcher silently fell back to V18 and a hard-coded install path. On a fresh
#   machine (no TIA running, V20 installed on another drive) that produced
#   "install path not found" or a wrong-version engine. This rewrite fixes the discovery.
#
# WHAT COUNTS AS "INSTALLED" (disk is the only authority)
#   <root>\Portal V<XX>\bin\Siemens.Automation.Portal.exe         <- the portal itself
#   <root>\Portal V<XX>\PublicAPI\V<XX>\Siemens.Engineering.dll    <- this install serves Openness for its own version
#   BOTH must exist, and <XX> must equal the folder name.
#
# WHAT DOES *NOT* COUNT
#   - PublicAPI\V<YY> folders: a Portal V20 install ships PublicAPI for V17..V20, and a
#     Portal V18 install ships V15.1..V18. Their presence proves nothing about installed portals.
#   - HKLM\SOFTWARE\Siemens\Automation\Openness\<ver>: registers those bundled client DLLs,
#     so it lists versions that were never installed and survives incomplete uninstalls.
#     Verified on a real machine: registry showed 16/17/18/19/20 while disk had only V18 + V20.
#
# SELECTION RULE (user requirement, 2026-10-28)
#   - exactly one usable version            -> use it
#   - a running TIA process                 -> use its version/path (closest to user intent)
#   - TIA_MCP_VERSION set                   -> honour it, but fail loudly if unusable
#   - several usable versions, no hint      -> status=ambiguous, DO NOT GUESS
#     (the user picks once; the installer/launcher writes TIA_MCP_VERSION + TIA_MCP_PORTAL_LOCATION)
#
# MODES
#   (default)  print the major version only, nothing when not uniquely resolvable  (legacy contract)
#   -Full      print one pipe-delimited line:  status|ver|root|source|candidates|unsupported
#   -List      print JSON: every discovered install with its usability flags
#
# OUTPUT IS ASCII-ONLY ON PURPOSE: cmd.exe reads it through the OEM code page.

[CmdletBinding()]
param(
    [switch]$Full,
    [switch]$List
)

$ErrorActionPreference = 'Continue'

$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

function Get-EnumeratedInstall {
    # Walk every FIXED drive; network/removable drives are skipped on purpose (slow / unrelated).
    $results = @()
    $drives = @()
    try {
        $drives = Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" |
            Select-Object -ExpandProperty DeviceID
    } catch { $drives = @() }
    if (-not $drives -or $drives.Count -eq 0) {
        foreach ($d in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
            if ($d.Root) { $drives += $d.Root.TrimEnd('\') }
        }
    }

    foreach ($d in $drives) {
        foreach ($base in @('Program Files\Siemens\Automation',
                            'Program Files (x86)\Siemens\Automation',
                            'Siemens\Automation')) {
            $p = Join-Path ($d + '\') $base
            if (-not (Test-Path -LiteralPath $p)) { continue }
            foreach ($dir in (Get-ChildItem -LiteralPath $p -Directory -Filter 'Portal V*' -ErrorAction SilentlyContinue)) {
                $ver = $dir.Name -replace '^Portal V', ''
                if ($ver -notmatch '^\d+(\.\d+)?$') { continue }
                $major = [int]($ver.Split('.')[0])

                $portalExe   = Join-Path $dir.FullName 'bin\Siemens.Automation.Portal.exe'
                $opennessDll = Join-Path $dir.FullName ("PublicAPI\V{0}\Siemens.Engineering.dll" -f $ver)

                $results += [pscustomobject]@{
                    Version       = $major
                    VersionFolder = $ver
                    Root          = $dir.FullName
                    HasPortalExe  = Test-Path -LiteralPath $portalExe
                    HasOpenness   = Test-Path -LiteralPath $opennessDll
                }
            }
        }
    }
    return $results
}

function Get-BuildAvailable {
    param([int]$Version)
    $a = Join-Path $scriptDir ("src\TiaMcpServer\bin-v{0}\Release\net48\TiaMcpServer.exe" -f $Version)
    $b = Join-Path $scriptDir ("..\..\runtime\v{0}\TiaMcpServer.exe" -f $Version)
    return ((Test-Path -LiteralPath $a) -or (Test-Path -LiteralPath $b))
}

function Get-RunningPortal {
    $best = $null
    foreach ($p in (Get-Process -Name 'Siemens.Automation.Portal' -ErrorAction SilentlyContinue)) {
        try {
            $path = $p.Path
            if (-not $path) { continue }
            if ($path -match 'Portal V(\d+)') {
                $v = [int]$matches[1]
                # root = two levels above ...\Portal VXX\bin\Siemens.Automation.Portal.exe
                $root = Split-Path -Parent (Split-Path -Parent $path)
                if (-not $best -or $v -gt $best.Version) {
                    $best = [pscustomobject]@{ Version = $v; Root = $root; Pid = $p.Id }
                }
            }
        } catch { }
    }
    return $best
}

# ---- discover ------------------------------------------------------------------
$install = Get-EnumeratedInstall
$usable = @()
foreach ($i in $install) {
    if ($i.HasPortalExe -and $i.HasOpenness) {
        $usable += [pscustomobject]@{
            Version = $i.Version
            Root    = $i.Root
            Build   = (Get-BuildAvailable -Version $i.Version)
        }
    }
}

# one entry per version; prefer Program Files over (x86), then the shortest path
$byVersion = @{}
foreach ($u in $usable) {
    if (-not $byVersion.ContainsKey($u.Version)) { $byVersion[$u.Version] = $u; continue }
    $cur = $byVersion[$u.Version]
    $curX86 = $cur.Root -match '\(x86\)'
    $newX86 = $u.Root -match '\(x86\)'
    if ($curX86 -and -not $newX86) { $byVersion[$u.Version] = $u }
    elseif ($curX86 -eq $newX86 -and $u.Root.Length -lt $cur.Root.Length) { $byVersion[$u.Version] = $u }
}

$supported = @($byVersion.Values | Where-Object { $_.Build } | Sort-Object Version -Descending)
$noBuild   = @($byVersion.Values | Where-Object { -not $_.Build } | Sort-Object Version -Descending)

$running = Get-RunningPortal
$pinRaw  = $env:TIA_MCP_VERSION
$pin     = $null
if ($pinRaw -match '^\s*(\d{1,2})\s*$') { $pin = [int]$matches[1] }
$pinRoot = $env:TIA_MCP_PORTAL_LOCATION

# ---- decide --------------------------------------------------------------------
$status = 'none'; $ver = $null; $root = $null; $source = ''

if ($pin -ne $null) {
    $hit = $supported | Where-Object { $_.Version -eq $pin } | Select-Object -First 1
    if ($hit) {
        $ver = $hit.Version; $root = $hit.Root; $source = 'env'
        if ($pinRoot) {
            # A bogus path must NOT throw and must NOT be silently replaced by another install:
            # Join-Path raises DriveNotFoundException for a missing drive, so probe defensively.
            $pinExe = $null
            $pinRootExists = $false
            try { $pinRootExists = Test-Path -LiteralPath $pinRoot } catch { $pinRootExists = $false }
            if ($pinRootExists) {
                try { $pinExe = Join-Path $pinRoot 'bin\Siemens.Automation.Portal.exe' } catch { $pinExe = $null }
            }
            $pinExeExists = $false
            if ($pinExe) {
                try { $pinExeExists = Test-Path -LiteralPath $pinExe } catch { $pinExeExists = $false }
            }
            if ($pinExeExists) {
                $root = $pinRoot; $source = 'env-location'
            } else {
                $status = 'bad-location'; $root = $pinRoot; $source = 'env-location'
            }
        }
        if ($status -ne 'bad-location') { $status = 'ok' }
    } else {
        $status = 'pinned-unusable'; $ver = $pin
    }
}
elseif ($running) {
    $hit = $supported | Where-Object { $_.Version -eq $running.Version } | Select-Object -First 1
    if ($hit) { $status = 'ok'; $ver = $hit.Version; $root = $running.Root; $source = 'running' }
    else { $status = 'running-unsupported'; $ver = $running.Version }
}
elseif ($supported.Count -eq 1) {
    $status = 'ok'; $ver = $supported[0].Version; $root = $supported[0].Root; $source = 'disk'
}
elseif ($supported.Count -gt 1) {
    $status = 'ambiguous'
}
elseif ($noBuild.Count -gt 0) {
    $status = 'unsupported'
}

$cand  = ($supported | ForEach-Object { '{0}@{1}' -f $_.Version, $_.Root }) -join ';'
$unsup = ($noBuild   | ForEach-Object { '{0}@{1}' -f $_.Version, $_.Root }) -join ';'

# ---- emit ----------------------------------------------------------------------
if ($List) {
    $json = @{
        status           = $status
        chosen           = @{ version = $ver; root = $root; source = $source }
        supported        = @($supported | ForEach-Object { @{ version = $_.Version; root = $_.Root } })
        installedNoBuild = @($noBuild   | ForEach-Object { @{ version = $_.Version; root = $_.Root } })
        running          = if ($running) { @{ version = $running.Version; root = $running.Root; pid = $running.Pid } } else { $null }
        env              = @{ version = $pin; portalLocation = $pinRoot }
    }
    $json | ConvertTo-Json -Depth 6
    exit 0
}

if ($Full) {
    # KEY=VALUE lines, NOT pipe-delimited: cmd's for /f collapses consecutive delimiters,
    # so an empty field (e.g. no root when ambiguous) silently shifts every later token.
    Write-Output ("STATUS=" + $status)
    Write-Output ("VER=" + $(if ($ver -ne $null) { $ver } else { '' }))
    Write-Output ("ROOT=" + $(if ($root) { $root } else { '' }))
    Write-Output ("SOURCE=" + $source)
    Write-Output ("CANDIDATES=" + $cand)
    Write-Output ("UNSUPPORTED=" + $unsup)
    exit 0
}

# legacy contract: version only, and only when uniquely resolvable
if ($status -eq 'ok' -and $ver -ne $null) { Write-Output $ver }
exit 0
