# SUMMARY: 切换 MCP 使用的 TIA 版本：列出本机已装版本 → 让用户选 → 改写配置里的 TIA_MCP_VERSION（只改这一项）
# WHEN: 想换一个 TIA 版本工作、或安装时选了"跟随当前 TIA"现在想固定下来
# ENV: python
<#
.SYNOPSIS
    切换 TIA Portal Openness MCP 使用的 TIA 版本。

.DESCRIPTION
    做法是改写已注册配置里 tia-portal 条目的 env.TIA_MCP_VERSION：
      · 指定版本    -> 写入 "18"/"20"/"21"，启动器加载对应构建
      · 跟随当前 TIA -> 删掉该变量，由启动器按"当前打开的 TIA"探测
    只动这一个环境变量，不碰命令、参数和其它 MCP 条目；改前备份为 *.bak。
    改完需要重启 AI 客户端（配置在启动时读取）。

    不带参数时交互询问；非交互环境必须显式给 -TiaMajorVersion 或 -FollowOpenTia，
    不会替你猜。

.PARAMETER TiaMajorVersion
    固定使用该版本（例如 18 / 20）。

.PARAMETER FollowOpenTia
    删除 TIA_MCP_VERSION，改为跟随当前打开的 TIA。

.PARAMETER Show
    只显示当前状态与本机已装版本，不做修改。

.PARAMETER Client
    workbuddy | claude | claude-code | cursor | vscode | all（默认 all = 所有已注册的）

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File switch-version.ps1
    powershell -ExecutionPolicy Bypass -File switch-version.ps1 -Show
    powershell -ExecutionPolicy Bypass -File switch-version.ps1 -TiaMajorVersion 18
    powershell -ExecutionPolicy Bypass -File switch-version.ps1 -FollowOpenTia
#>
[CmdletBinding()]
param(
    [int]$TiaMajorVersion = 0,
    [switch]$FollowOpenTia,
    [switch]$Show,
    [switch]$NonInteractive,
    [string]$ConfigPath,
    [ValidateSet('all', 'workbuddy', 'claude', 'claude-code', 'cursor', 'vscode')]
    [string]$Client = 'all'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Say  { param($m) Write-Host $m }
function Ok   { param($m) Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn { param($m) Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Die  { param($m) Write-Host "`n[x] $m" -ForegroundColor Red; exit 2 }

# ── 本机已装版本（与 install-mcp.ps1 同一套探测口径）────────────────────────
function Find-TiaPortal {
    $found = @{}
    $envLoc = [Environment]::GetEnvironmentVariable('TiaPortalLocation')
    if ($envLoc -and (Test-Path (Join-Path $envLoc 'PublicAPI'))) {
        $ver = Get-ChildItem (Join-Path $envLoc 'PublicAPI') -Directory -ErrorAction SilentlyContinue |
               ForEach-Object { $_.Name } | Where-Object { $_ -match '^V(\d+)$' } |
               ForEach-Object { [int]$Matches[1] } | Sort-Object -Descending | Select-Object -First 1
        if ($ver) { $found[$ver] = $envLoc }
    }
    foreach ($root in @("$env:ProgramFiles\Siemens\Automation", "${env:ProgramFiles(x86)}\Siemens\Automation", 'D:\Program Files\Siemens\Automation')) {
        if (-not $root -or -not (Test-Path $root)) { continue }
        Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^Portal V(\d+)$' } | ForEach-Object {
                $v = [int]($_.Name -replace '^Portal V', '')
                if (-not $found.ContainsKey($v)) { $found[$v] = $_.FullName }
            }
    }
    $found
}

function Get-Targets {
    if ($ConfigPath) { return @(@{ name = (Split-Path -Leaf $ConfigPath); path = $ConfigPath }) }
    $home_ = $env:USERPROFILE
    $app = $env:APPDATA
    $all = @(
        @{ name = 'workbuddy';   path = (Join-Path $home_ '.workbuddy\mcp.json') },
        @{ name = 'claude';      path = (Join-Path $app 'Claude\claude_desktop_config.json') },
        @{ name = 'claude-code'; path = (Join-Path $home_ '.claude.json') },
        @{ name = 'cursor';      path = (Join-Path $home_ '.cursor\mcp.json') },
        @{ name = 'vscode';      path = (Join-Path $app 'Code\User\mcp.json') }
    )
    if ($Client -eq 'all') { return @($all | Where-Object { Test-Path $_.path }) }
    return @($all | Where-Object { $_.name -eq $Client })
}

function Read-Json($path) {
    $raw = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if (-not $raw.Trim()) { return $null }
    try { return ($raw | ConvertFrom-Json) } catch { Die "配置不是合法 JSON：$path" }
}

function Write-JsonNoBom($path, $obj) {
    $json = ConvertTo-Json $obj -Depth 20
    [System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

# ── 探测 ────────────────────────────────────────────────────────────────────
$tia = Find-TiaPortal
$supported = @($tia.Keys | Where-Object { $_ -ge 18 } | Sort-Object)

Say ''
Say '  本机探测到的 TIA Portal：'
if ($tia.Count -eq 0) { Say '    （未探测到；若确认装了，请设 TiaPortalLocation）' }
foreach ($k in ($tia.Keys | Sort-Object)) {
    $tag = if ($k -ge 18) { '可用' } else { '低于 V18，不支持' }
    Say ("    V{0,-2}  {1}   [{2}]" -f $k, $tia[$k], $tag)
}
if ($supported.Count -eq 0) { Die '本 MCP 需要 V18 或更高。' }

$targets = Get-Targets
if ($targets.Count -eq 0) { Die '没有找到任何已注册 tia-portal 的客户端配置。请先运行 install-mcp.ps1。' }

Say ''
Say '  已注册的客户端：'
foreach ($t in $targets) {
    $root = Read-Json $t.path
    $entry = $root.mcpServers.'tia-portal'
    if ($null -eq $entry) { Say ("    {0,-12} {1}   （没有 tia-portal 条目，跳过）" -f $t.name, $t.path); continue }
    $pinned = $entry.env.TIA_MCP_VERSION
    $mode = if ($pinned) { "固定 V$pinned" } else { '跟随当前打开的 TIA' }
    Say ("    {0,-12} {1}   当前：{2}" -f $t.name, $t.path, $mode)
}

if ($Show) { Say ''; exit 0 }

# ── 选择 ────────────────────────────────────────────────────────────────────
$choice = $null
if ($TiaMajorVersion -gt 0) {
    if ($supported -notcontains $TiaMajorVersion) {
        Die "本机没有 V$TiaMajorVersion。可用：V$($supported -join ' / V')"
    }
    $choice = @{ Mode = 'pin'; Ver = $TiaMajorVersion }
} elseif ($FollowOpenTia) {
    $choice = @{ Mode = 'auto'; Ver = 0 }
} else {
    $interactive = (-not $NonInteractive) -and (-not [Console]::IsInputRedirected)
    if (-not $interactive) {
        Die @"
非交互运行，但没有指定 —— 不会替你猜。
  -TiaMajorVersion 18|20|21    固定该版本
  -FollowOpenTia               跟随当前打开的 TIA
本机可用：V$($supported -join ' / V')
"@
    }
    Say ''
    Say '  改为哪个？'
    for ($i = 0; $i -lt $supported.Count; $i++) { Say ("    [{0}] V{1}" -f ($i + 1), $supported[$i]) }
    $autoIdx = $supported.Count + 1
    Say ("    [{0}] 跟随当前打开的 TIA" -f $autoIdx)
    $sel = Read-Host '  请输入序号'
    $n = 0
    if (-not [int]::TryParse($sel, [ref]$n) -or $n -lt 1 -or $n -gt $autoIdx) { Die "序号无效：$sel" }
    if ($n -eq $autoIdx) { $choice = @{ Mode = 'auto'; Ver = 0 } }
    else { $choice = @{ Mode = 'pin'; Ver = $supported[$n - 1] } }
}

# ── 写入（只改 env.TIA_MCP_VERSION）─────────────────────────────────────────
$changed = 0
foreach ($t in $targets) {
    $root = Read-Json $t.path
    $servers = $root.PSObject.Properties['mcpServers']
    if ($null -eq $servers -or $null -eq $servers.Value) { continue }
    $entry = $servers.Value.PSObject.Properties['tia-portal']
    if ($null -eq $entry) { continue }
    $e = $entry.Value

    Copy-Item -LiteralPath $t.path -Destination ($t.path + '.bak') -Force

    $envProp = $e.PSObject.Properties['env']
    if ($choice.Mode -eq 'pin') {
        if ($null -eq $envProp -or $null -eq $envProp.Value) {
            $e | Add-Member -NotePropertyName 'env' -NotePropertyValue (New-Object PSObject) -Force
            $envProp = $e.PSObject.Properties['env']
        }
        $envProp.Value | Add-Member -NotePropertyName 'TIA_MCP_VERSION' -NotePropertyValue "$($choice.Ver)" -Force
    } else {
        if ($null -ne $envProp -and $null -ne $envProp.Value) {
            $envProp.Value.PSObject.Properties.Remove('TIA_MCP_VERSION')
        }
    }
    Write-JsonNoBom $t.path $root
    $changed++
    Ok ("{0} -> {1}" -f $t.name, $(if ($choice.Mode -eq 'pin') { "固定 V$($choice.Ver)" } else { '跟随当前打开的 TIA' }))
}

if ($changed -eq 0) { Warn '没有可修改的条目（可能都还没注册 tia-portal）。先运行 install-mcp.ps1。'; exit 1 }
Say ''
Say '  已改配置；重启 AI 客户端后生效。'
