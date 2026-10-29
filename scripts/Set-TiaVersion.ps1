# Set-TiaVersion.ps1
#
# Switch the ONE 'tia-portal' connector between installed TIA Portal versions.
#
# Why one connector and not one per version: the user wants a single entry in the MCP list.
# A TIA engine binds to one Openness version per process, so switching version = rewriting the
# connector's env and reconnecting. This script does exactly that and nothing else.
#
# USAGE
#   powershell -ExecutionPolicy Bypass -File Set-TiaVersion.ps1            # show current + available
#   powershell -ExecutionPolicy Bypass -File Set-TiaVersion.ps1 -Version 18
#   powershell -ExecutionPolicy Bypass -File Set-TiaVersion.ps1 -Version 18 -Apply
#
# SAFETY
#   Dry run by default. With -Apply it writes a timestamped backup first and only touches
#   mcpServers['tia-portal'].env.TIA_MCP_VERSION / TIA_MCP_PORTAL_LOCATION.
#
# OUTPUT IS ASCII-ONLY (cmd/OEM code page safety).

[CmdletBinding()]
param(
    [int]$Version = 0,
    [string]$PortalLocation = '',
    [switch]$Apply,
    [string]$McpJson = (Join-Path $env:USERPROFILE '.workbuddy\mcp.json')
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$detect = [System.IO.Path]::GetFullPath((Join-Path $here '..\tools\tiaportal-mcp\detect_tia_version.ps1'))

if (-not (Test-Path -LiteralPath $detect)) { throw "detect_tia_version.ps1 not found: $detect" }
if (-not (Test-Path -LiteralPath $McpJson)) { throw "MCP config not found: $McpJson" }

$raw = & powershell -NoProfile -ExecutionPolicy Bypass -File $detect -List
$info = ($raw -join "`n") | ConvertFrom-Json
$supported = @($info.supported)
$noBuild = @($info.installedNoBuild)

$cfg = Get-Content -LiteralPath $McpJson -Raw -Encoding UTF8 | ConvertFrom-Json
$entry = $cfg.mcpServers.'tia-portal'
if (-not $entry) { throw "mcpServers['tia-portal'] is missing in $McpJson" }

$curVer = $entry.env.TIA_MCP_VERSION
$curLoc = $entry.env.TIA_MCP_PORTAL_LOCATION
Write-Output "config    : $McpJson"
Write-Output "current   : TIA_MCP_VERSION=$curVer  PORTAL_LOCATION=$(if ($curLoc) { $curLoc } else { '(auto)' })"
Write-Output ""
Write-Output "available (installed AND this build supports it):"
foreach ($s in $supported) { Write-Output ("  V{0}  {1}" -f $s.version, $s.root) }
if ($noBuild.Count -gt 0) {
    Write-Output "installed but unsupported by this delivery (ignored):"
    foreach ($s in $noBuild) { Write-Output ("  V{0}  {1}" -f $s.version, $s.root) }
}

if ($Version -le 0) {
    Write-Output ""
    Write-Output "Nothing changed. Pass -Version <n> to switch (add -Apply to write)."
    exit 0
}

$hit = $supported | Where-Object { $_.version -eq $Version } | Select-Object -First 1
if (-not $hit) {
    Write-Output ""
    Write-Output "ERROR: V$Version is not usable on this machine (not installed, or this delivery has no V$Version build)."
    Write-Output "Pick one of: $(($supported | ForEach-Object { 'V' + $_.version }) -join ', ')"
    exit 4
}

$newLoc = if ($PortalLocation) { $PortalLocation } else { $hit.root }
if (-not (Test-Path -LiteralPath (Join-Path $newLoc 'bin\Siemens.Automation.Portal.exe'))) {
    throw "not a TIA Portal install (missing bin\Siemens.Automation.Portal.exe): $newLoc"
}

Write-Output ""
Write-Output "planned change (mcpServers['tia-portal'].env):"
Write-Output ("  TIA_MCP_VERSION         : {0} -> {1}" -f $curVer, $Version)
Write-Output ("  TIA_MCP_PORTAL_LOCATION : {0} -> {1}" -f $(if ($curLoc) { $curLoc } else { '(auto)' }), $newLoc)

if (-not $Apply) {
    Write-Output ""
    Write-Output "DRY RUN. Re-run with -Apply to write."
    exit 0
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "$McpJson.bak-$stamp"
Copy-Item -LiteralPath $McpJson -Destination $backup -Force
Write-Output ""
Write-Output "backup    : $backup"

$envObj = [ordered]@{}
foreach ($p in $entry.env.PSObject.Properties) { $envObj[$p.Name] = $p.Value }
$envObj['TIA_MCP_VERSION'] = "$Version"
$envObj['TIA_MCP_PORTAL_LOCATION'] = $newLoc

$entry.env = $envObj
$json = $cfg | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($McpJson, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "written   : $McpJson"
Write-Output "Reconnect the 'tia-portal' connector for the change to take effect."
