# SUMMARY: 单文件安装器：探测本机 TIA（≥V18）→ 选构建/启动器 → 写进已装客户端配置 → 体检；幂等、改前备份
# WHEN: 新机器或同事的机器要装这套 MCP 时（先 -DryRun 看计划）
# ENV: python
<#
.SYNOPSIS
    一键把 TIA Portal Openness MCP 装到本机并注册进 AI 客户端（WorkBuddy / Claude / Cursor / VS Code）。

.DESCRIPTION
    这个脚本是给「拿到压缩包但不想逐步配置」的同事用的：跑一次就把引擎就位、把 MCP 注册写进
    客户端配置、最后体检。幂等，可重复执行；改配置前自动备份为 *.bak。

    相比仓库里自带的 配置MCP.bat / 配置MCP-v20.bat，它多做三件事：
      1. 自动探测 V18 / V20 / V21（原来只有 V21 与 V20 两个 bat，没有 V18）；
      2. 支持 WorkBuddy（~/.workbuddy/mcp.json）—— exe 自带的 config 命令还没列这个宿主；
      3. 本机没有解压包时，可以直接从 GitHub 拉。

.PARAMETER Client
    要注册的客户端：workbuddy / claude / claude-code / cursor / vscode / all（默认 all = 所有已装的 + WorkBuddy）。

.PARAMETER Lite
    只暴露约 40 个核心工具。模型较弱或客户端限制工具数量（如 VS Code 上限 128）时用。

.PARAMETER DryRun
    只探测与打印计划，不写任何文件。

.PARAMETER Source
    已解压的交付包目录（脚本所在目录会自动尝试）。不给就从 GitHub 下载。

.PARAMETER ConfigPath
    直接指定配置文件路径（用于自定义宿主或测试）。会覆盖 -Client 的推断。

.PARAMETER TiaMajorVersion
    指定 TIA 版本（默认取本机最高版本）。

    默认的"启动器模式"下，它会写进 env.TIA_MCP_VERSION 把这个版本钉住；
    不传则交给启动器按"你当前打开的 TIA"自动探测。

.PARAMETER Direct
    不用启动器，直接把配置指向某个版本编译出的 exe。
    适合只固定用一个版本的场景，能省掉启动器每次约 1.3 秒的版本探测。
    注意：这样配置就锁死了那一份构建 —— 换版本要重跑安装器，
    而且这份 exe 放到只装了其它 TIA 版本的机器上会启动失败（程序集版本不匹配）。

.PARAMETER Remove
    从目标配置里移除 tia-portal 条目（其余内容保持不变）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install-mcp.ps1
    powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -Client workbuddy
    powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -TiaMajorVersion 18
    powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -Direct -TiaMajorVersion 20
    powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -Lite
    powershell -ExecutionPolicy Bypass -File install-mcp.ps1 -DryRun

.NOTES
    不需要管理员权限。装完请重启 AI 客户端。
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'workbuddy', 'claude', 'claude-code', 'cursor', 'vscode')]
    [string]$Client = 'all',
    [switch]$Lite,
    [switch]$DryRun,
    [switch]$Remove,
    [switch]$SkipDoctor,
    [switch]$Direct,
    [string]$Source,
    [string]$ConfigPath,
    [int]$TiaMajorVersion = 0
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # 让 Invoke-WebRequest 在管道里安静些
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$RepoZip = 'https://github.com/1748477582/tia-portal-openness-mcp/archive/refs/heads/master.zip'
$ServerKey = 'tia-portal'

function Say  { param($m) Write-Host $m }
function Step { param($m) Write-Host "`n==> $m" -ForegroundColor Cyan }
function Ok   { param($m) Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn { param($m) Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Die  { param($m) Write-Host "`n[x] $m" -ForegroundColor Red; exit 1 }

# ── 1. 探测已安装的 TIA Portal ────────────────────────────────────────────────
function Find-TiaPortal {
    $found = @{}
    # a) 环境变量优先（同事机器上最明确的指定方式）
    $envLoc = [Environment]::GetEnvironmentVariable('TiaPortalLocation')
    if ($envLoc -and (Test-Path (Join-Path $envLoc 'PublicAPI'))) {
        $ver = Get-ChildItem (Join-Path $envLoc 'PublicAPI') -Directory -ErrorAction SilentlyContinue |
               ForEach-Object { $_.Name } | Where-Object { $_ -match '^V(\d+)$' } |
               ForEach-Object { [int]$Matches[1] } | Sort-Object -Descending | Select-Object -First 1
        if ($ver) { $found[$ver] = $envLoc }
    }
    # b) 常见安装根目录（含 D 盘 —— V20 常装在这里）
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

# ── 2. 定位引擎 exe（挑与 TIA 版本对应的构建）───────────────────────────────
function Find-Engine {
    param([string]$Root, [int]$TiaVer)
    if (-not $Root) { return $null }
    $candidates = @(
        @{ v = 18; rel = 'tools\tiaportal-mcp\src\TiaMcpServer\bin-v18\Release\net48\TiaMcpServer.exe' },
        @{ v = 20; rel = 'tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48\TiaMcpServer.exe' },
        @{ v = 21; rel = 'tools\tiaportal-mcp\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe' }
    )
    foreach ($c in $candidates) {
        if ($TiaVer -gt 0 -and $c.v -ne $TiaVer) { continue }
        $p = Join-Path $Root $c.rel
        if (Test-Path $p) { return [pscustomobject]@{ Path = $p; Version = $c.v } }
    }
    # 版本没对上就退而求其次：给一个存在的最高版本构建（引擎启动时会自路由）
    foreach ($c in $candidates) {
        $p = Join-Path $Root $c.rel
        if (Test-Path $p) { return [pscustomobject]@{ Path = $p; Version = $c.v } }
    }
    return $null
}

# ── 3. 必要时从 GitHub 拉取 ────────────────────────────────────────────────
function Get-BundleFromGitHub {
    $dest = Join-Path $env:USERPROFILE ('tia-mcp-' + (Get-Date -Format 'yyyyMMdd'))
    if (Test-Path (Join-Path $dest 'tools\tiaportal-mcp')) { return $dest }
    $zip = Join-Path $env:TEMP 'tia-mcp-master.zip'
    Step "从 GitHub 下载交付包"
    Say "     $RepoZip"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $RepoZip -OutFile $zip -UseBasicParsing
    $tmp = Join-Path $env:TEMP ('tia-mcp-unzip-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $inner = Get-ChildItem $tmp -Directory | Select-Object -First 1
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Move-Item $inner.FullName $dest
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Ok "已解压到 $dest"
    return $dest
}

# ── 4. 客户端配置定位 ──────────────────────────────────────────────────────
function Get-TargetConfig {
    param([string]$Which)
    $home_ = $env:USERPROFILE
    $appdata = $env:APPDATA
    switch ($Which) {
        'workbuddy'    { @{ style = 'mcpServers'; path = (Join-Path $home_ '.workbuddy\mcp.json') } }
        'claude'       { @{ style = 'mcpServers'; path = (Join-Path $appdata 'Claude\claude_desktop_config.json') } }
        'claude-code'  { @{ style = 'mcpServers'; path = (Join-Path $home_ '.claude.json') } }
        'cursor'       { @{ style = 'mcpServers'; path = (Join-Path $home_ '.cursor\mcp.json') } }
        'vscode'       { @{ style = 'servers';    path = (Join-Path $appdata 'Code\User\mcp.json') } }
        default        { $null }
    }
}

# 只在「确实装了」时才动它的配置 —— 别给没装的应用凭空造一个配置文件
function Test-ClientInstalled {
    param([string]$Which)
    switch ($Which) {
        'workbuddy'   { return (Test-Path (Join-Path $env:USERPROFILE '.workbuddy')) -or
                              (Test-Path (Join-Path $env:APPDATA 'WorkBuddy')) }
        'claude'      { return (Test-Path (Join-Path $env:APPDATA 'Claude')) }
        'cursor'      { return (Test-Path (Join-Path $env:USERPROFILE '.cursor')) }
        'vscode'      { return (Test-Path (Join-Path $env:APPDATA 'Code\User')) }
        default       { return $true }   # claude-code: ~/.claude.json
    }
}

# ── 5. 写入 / 移除配置项（保留其它内容，先备份）────────────────────────────
function Write-JsonNoBom {
    param([string]$Path, [object]$Object)
    $json = ConvertTo-Json $Object -Depth 20
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Set-McpEntry {
    param([string]$Path, [string]$Style, [string]$Exe, [int]$Ver, [bool]$IsLite, [bool]$DoRemove,
          [bool]$UseLauncher, [string]$LauncherBat, [int]$PinVer, [string]$TiaLocation)

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $root = New-Object PSObject
    if (Test-Path $Path) {
        $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
        if ($raw.Trim()) {
            try { $root = $raw | ConvertFrom-Json } catch { Die "已有配置不是合法 JSON，请先修好：$Path" }
        }
        Copy-Item -LiteralPath $Path -Destination ($Path + '.bak') -Force
    }

    if ($Style -eq 'servers') { $rootKey = 'servers' } else { $rootKey = 'mcpServers' }

    # 注意：PSObject.Properties[$k] 返回的是**属性包装器**，不是值对象。
    # 早先直接往包装器上写，配置里就什么都没有 —— 而退出码仍是 0，其他条目也完好，
    # 属于最不容易被发现的一类 bug。所以这里显式取 .Value。
    $prop = $root.PSObject.Properties[$rootKey]
    if ($null -ne $prop -and $null -ne $prop.Value) {
        $servers = $prop.Value
    } else {
        $servers = New-Object PSObject
        $root | Add-Member -NotePropertyName $rootKey -NotePropertyValue $servers -Force
    }

    if ($DoRemove) {
        if ($null -ne $servers.PSObject.Properties[$ServerKey]) {
            $servers.PSObject.Properties.Remove($ServerKey)
            Write-JsonNoBom -Path $Path -Object $root
            return "removed $ServerKey from $Path"
        }
        return "absent  $ServerKey in $Path"
    }

    $entry = New-Object PSObject
    if ($Style -eq 'servers') { $entry | Add-Member -NotePropertyName 'type' -NotePropertyValue 'stdio' }

    if ($UseLauncher) {
        # 启动器模式：一份配置切版本。tia_mcp_launcher.bat 读 env.TIA_MCP_VERSION 选构建，
        # 不设就自动探测 —— 这与 exe 自带 installer 的直连写法不同，但正是多版本机器该用的方式。
        $entry | Add-Member -NotePropertyName 'command' -NotePropertyValue 'cmd'
        $entry | Add-Member -NotePropertyName 'args' -NotePropertyValue @('/c', $LauncherBat)
    } else {
        $entry | Add-Member -NotePropertyName 'command' -NotePropertyValue $Exe
        $entry | Add-Member -NotePropertyName 'args' -NotePropertyValue @('--tia-major-version', "$Ver")
    }

    $envObj = New-Object PSObject
    if ($UseLauncher) {
        # 只在"显式钉版本"时才写 env；不钉就两项都不写，让启动器自己去探测版本与安装路径，
        # 否则我们等于把探测结果又固化回配置，和自动探测的用意相反。
        if ($PinVer -gt 0) {
            $envObj | Add-Member -NotePropertyName 'TIA_MCP_VERSION' -NotePropertyValue "$PinVer"
            if ($TiaLocation) { $envObj | Add-Member -NotePropertyName 'TIA_MCP_PORTAL_LOCATION' -NotePropertyValue $TiaLocation }
        }
    }
    if ($IsLite) { $envObj | Add-Member -NotePropertyName 'TIA_MCP_PROFILE' -NotePropertyValue 'lite' }
    if ($envObj.PSObject.Properties.Count -gt 0) { $entry | Add-Member -NotePropertyName 'env' -NotePropertyValue $envObj }

    $had = $null -ne $servers.PSObject.Properties[$ServerKey]
    # PS 5.1 不支持 `$obj.PSObject.Properties['k'] = $v` 这种赋值（报「无法对
    # PSMemberInfoIntegratingCollection 进行索引」）。Add-Member -Force 是可靠写法，存在即覆盖。
    # 另：写出必须无 BOM —— 与 exe 自带 installer（File.WriteAllText）保持一致。
    $servers | Add-Member -NotePropertyName $ServerKey -NotePropertyValue $entry -Force
    Write-JsonNoBom -Path $Path -Object $root
    return ("$(if ($had) { 'updated' } else { 'wrote  ' }) $ServerKey -> $Path")
}

# ── 主流程 ──────────────────────────────────────────────────────────────────
Step '探测 TIA Portal 安装'
$tia = Find-TiaPortal
if ($tia.Count -eq 0) {
    Die @"
未找到 TIA Portal。请先安装 V18/V20/V21（含 Openness 选项），或设置环境变量指向安装目录：
  setx TiaPortalLocation "C:\Program Files\Siemens\Automation\Portal V18"
"@
}
$supported = @($tia.Keys | Where-Object { $_ -ge 18 } | Sort-Object)
$tooOld   = @($tia.Keys | Where-Object { $_ -lt 18 }  | Sort-Object)
foreach ($k in ($tia.Keys | Sort-Object)) {
    if ($k -lt 18) { Warn "V$k  $($tia[$k])  （低于 V18，本 MCP 不支持，仅作提示）" }
    else { Ok "V$k  $($tia[$k])" }
}
if ($supported.Count -eq 0) {
    Die "只找到 V$($tooOld -join '/V')，本 MCP 需要 **V18 或更高**（V16/V17 没有 Openness 这条路）。"
}

if ($TiaMajorVersion -gt 0) {
    if (-not $tia.ContainsKey($TiaMajorVersion)) {
        Die "指定了 V$TiaMajorVersion，但本机装的是 V$((($tia.Keys) | Sort-Object) -join '/V')"
    }
    $tiaVer = $TiaMajorVersion
} else {
    $tiaVer = ($tia.Keys | Sort-Object -Descending)[0]
}
Ok "将使用 V$tiaVer"
$tiaLocation = if ($tia.ContainsKey($tiaVer)) { $tia[$tiaVer] } else { '' }
if ($supported.Count -gt 1) { Say "     若要指定其它版本，加参数 -TiaMajorVersion 18（可选：$($supported -join ' / ')）" }

# 单版本机器必须钉住版本。原因：启动器的探测顺序是「env → 当前运行的 TIA → 兜底 V18」，
# 而兜底写死的是 V18。若本机只装了 V20、又没开 TIA，启动器会去加载 bin-v18 构建，
# 而 V18 的 PublicAPI 目录根本不存在 —— 表现为"装了却用不了"，且报错不直观。
if ($TiaMajorVersion -eq 0 -and $supported.Count -eq 1) {
    $TiaMajorVersion = $supported[0]
    Ok "本机只装了 V$($supported[0]) —— 已自动钉住，避免启动器在未开 TIA 时兜底到 V18"
} elseif ($TiaMajorVersion -eq 0 -and $supported.Count -gt 1) {
    Say '     本机有多个版本：不钉版本，由启动器按"当前打开的 TIA"选择；若它选错，加 -TiaMajorVersion 钉住'
}

Step '定位引擎 exe'
$root = $null
foreach ($cand in @($Source, $PSScriptRoot, (Split-Path -Parent $MyInvocation.MyCommand.Path))) {
    if ($cand -and (Test-Path (Join-Path $cand 'tools\tiaportal-mcp'))) { $root = (Resolve-Path $cand).Path; break }
}
if (-not $root -and -not $DryRun) { $root = Get-BundleFromGitHub }
$engine = if ($root) { Find-Engine -Root $root -TiaVer $tiaVer } else { $null }
if (-not $engine) {
    if ($DryRun) { Warn '引擎 exe 未找到（DryRun 不下载）。实际执行时会自动从 GitHub 获取。' }
    else { Die "引擎 exe 未找到。根目录：$root" }
} else {
    Ok "$($engine.Path)  (V$($engine.Version) 构建)"
}

# 启动器（多版本机器的正确姿势：一份配置切版本）
$launcherBat = $null
if ($root) {
    # 用正斜杠：Windows 同样接受，且不会被任何转义规则改写
    $cand = Join-Path $root 'tools/tiaportal-mcp/tia_mcp_launcher.bat'
    if (Test-Path $cand) { $launcherBat = (Resolve-Path $cand).Path }
}
$useLauncher = (-not $Direct) -and $launcherBat
if ($Direct) {
    Write-Host '  模式: 直连 exe（-Direct，配置锁死这一份构建）' -ForegroundColor DarkGray
} elseif ($launcherBat) {
    Ok "启动器: $launcherBat"
    if ($TiaMajorVersion -gt 0) { Say "     版本: 钉住 V$TiaMajorVersion（env.TIA_MCP_VERSION）" }
    else { Say '     版本: 不钉，由启动器按你当前打开的 TIA 自动探测' }
} else {
    Warn '包内没有 tia_mcp_launcher.bat，退回直连 exe 模式'
}

Step '目标客户端'
$targets = @()
if ($ConfigPath) {
    $style = if ($PSCmdlet.ParameterName -contains 'ConfigPath' -and $ConfigPath -match 'Code') { 'servers' } else { 'mcpServers' }
    $targets = @(@{ name = (Split-Path -Leaf $ConfigPath); style = $style; path = $ConfigPath })
} elseif ($Client -eq 'all') {
    foreach ($c in @('workbuddy', 'claude', 'cursor', 'vscode', 'claude-code')) {
        if (Test-ClientInstalled $c) {
            $t = Get-TargetConfig $c
            if ($t) { $targets += @{ name = $c; style = $t.style; path = $t.path } }
        }
    }
    if ($targets.Count -eq 0) {
        Warn '没检测到已安装的客户端，只处理 WorkBuddy。'
        $t = Get-TargetConfig 'workbuddy'
        $targets = @(@{ name = 'workbuddy'; style = $t.style; path = $t.path })
    }
} else {
    $t = Get-TargetConfig $Client
    if (-not $t) { Die "未知客户端：$Client" }
    $targets = @(@{ name = $Client; style = $t.style; path = $t.path })
}
foreach ($t in $targets) { Say ("  {0,-12} {1}" -f $t.name, $t.path) }

Step $(if ($Remove) { '移除配置' } else { '写入配置' })
if ($DryRun) {
    foreach ($t in $targets) { Warn "[DryRun] 本会改动：$($t.path)" }
} elseif (-not $engine) {
    Die '没有引擎 exe，无法写入配置'
} else {
    foreach ($t in $targets) {
        try { Ok (Set-McpEntry -Path $t.path -Style $t.style -Exe $engine.Path -Ver $engine.Version -IsLite $Lite.IsPresent -DoRemove $Remove.IsPresent -UseLauncher ([bool]$useLauncher) -LauncherBat $launcherBat -PinVer $TiaMajorVersion -TiaLocation $tiaLocation) }
        catch { Warn "写 $($t.name) 失败：$($_.Exception.Message)" }
    }
}

if (-not $Remove -and -not $DryRun -and $engine -and -not $SkipDoctor) {
    Step '体检（doctor）'
    try {
        $out = & $engine.Path doctor 2>&1 | Out-String
        ($out -split "`r?`n") | Where-Object { $_ -match '\S' } | Select-Object -Last 14 | ForEach-Object { Say "  $_" }
    } catch { Warn "doctor 未跑成功：$($_.Exception.Message)（不影响已写入的配置）" }

    Step '收尾'
    Say '  1) 重启 AI 客户端（配置在启动时读取）'
    Say '  2) 让它调用 tia-portal 的 Bootstrap 验证连通性'
    if ($useLauncher) { Say '  换 TIA 版本：改配置里的 env.TIA_MCP_VERSION（18 / 20 / 21），或删掉这一行让它自动探测' }
    else { Say '  换 TIA 版本：重跑本脚本并加 -TiaMajorVersion 18（或 20）' }
    Say ('  3) 有问题先跑：' + $engine.Path + ' doctor')
}

if ($DryRun) { Say "`n完成。（DryRun：未写任何文件）" -ForegroundColor Green } else { Say "`n完成。" -ForegroundColor Green }