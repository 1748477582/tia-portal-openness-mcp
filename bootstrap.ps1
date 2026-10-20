# SUMMARY: 一行式安装入口：可直接从 GitHub 拉取并调用 install-mcp.ps1；不带版本时只探测并给出下一条命令（不猜）
# WHEN: 别人要把本 MCP 装到自己机器上时，复制 README 里那条一行命令即可
# ENV: python
<#
.SYNOPSIS
    TIA Portal Openness MCP 的一行式安装入口（bootstrap）。

.DESCRIPTION
    设计目标是"复制一条命令就能装"，同时**不替用户决定 TIA 版本**：

      · 不带 -Version / -FollowOpenTia  → 只探测本机装了哪些 TIA，打印列表，并给出**下一条可直接复制的命令**（填好版本号）
      · 带 -Version 20                  → 下载交付包到 %USERPROFILE%\tia-mcp，然后调用 install-mcp.ps1 -TiaMajorVersion 20
      · 带 -FollowOpenTia               → 同上，但不钉版本，由启动器跟随当前打开的 TIA

    典型用法（复制这一整行，把版本号换成你的）：

      powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/1748477582/tia-portal-openness-mcp/master/bootstrap.ps1' -OutFile .\tia-bootstrap.ps1; & .\tia-bootstrap.ps1 -Version 20"

    注意：脚本只做"取包 + 转调"，真正的探测/确认/写配置都在 install-mcp.ps1 里。

.PARAMETER Version
    要固定使用的 TIA 主版本（18 / 20 / 21）。

.PARAMETER FollowOpenTia
    不钉版本，由启动器跟随当前打开的 TIA。

.PARAMETER Lite
    只暴露约 40 个核心工具（透传给 install-mcp.ps1）。

.PARAMETER DryRun
    只看计划，不写任何文件（透传）。

.PARAMETER Direct
    配置直连某个版本的 exe，不用启动器（透传）。

.PARAMETER Ref
    拉取哪个分支/标签，默认 master。

.PARAMETER Source
    已解压的交付包目录；给了就不下载。

.EXAMPLE
    & .\bootstrap.ps1
    & .\bootstrap.ps1 -Version 20
    & .\bootstrap.ps1 -FollowOpenTia -Lite
#>
[CmdletBinding()]
param(
    [int]$Version = 0,
    [switch]$FollowOpenTia,
    [switch]$Lite,
    [switch]$DryRun,
    [switch]$Direct,
    [switch]$Check,
    [string]$Ref = 'master',
    [string]$Source
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Repo   = '1748477582/tia-portal-openness-mcp'
$RawUrl = "https://raw.githubusercontent.com/$Repo/$Ref/bootstrap.ps1"
# 下载通道按可用性排序：github.com/archive 在部分企业网/国内网络会被拦（本机实测就是），
# codeload 与 api zipball 通常仍可用。逐个尝试，全失败再给人工指引。
$ZipUrls = @(
    "https://codeload.github.com/$Repo/zip/refs/heads/$Ref",
    "https://api.github.com/repos/$Repo/zipball/$Ref",
    "https://github.com/$Repo/archive/refs/heads/$Ref.zip"
)
$Dest   = Join-Path $env:USERPROFILE 'tia-mcp'

function Say  { param($m) Write-Host $m }
function Ok   { param($m) Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn { param($m) Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Die  { param($m) Write-Host "`n[x] $m" -ForegroundColor Red; exit 1 }

# ── 本机版本探测（与 install-mcp.ps1 / switch-version.ps1 同一口径）─────────
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

# ── -Check：安装前自检（不改任何东西）──────────────────────────────────────
if ($Check) {
    Say ''
    Say '  安装前自检（只读，不改任何东西）'
    Say ''

    # 1) PowerShell 版本
    $psv = $PSVersionTable.PSVersion
    $psOk = $psv.Major -ge 5
    if ($psOk) { Ok  "PowerShell $psv" } else { Warn "PowerShell $psv —— 需要 5.0 或更高" }

    # 2) .NET Framework 4.8（Win10 1903+/Win11 自带）
    $rel = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
    if ($rel -ge 528040) { Ok "\.NET Framework 4.8（Release=$rel）" }
    elseif ($rel)        { Warn ".NET Framework 版本偏低（Release=$rel）—— 引擎需要 4.8；Windows 10 1903+ / 11 自带" }
    else                 { Warn '.NET Framework 4.x 未检测到（罕见；Windows 10/11 应自带 4.8）' }

    # 3) 执行策略
    $ep = Get-ExecutionPolicy -Scope CurrentUser
    Say ("  执行策略（CurrentUser）：$ep" + $(if ($ep -in @('Restricted','AllSigned')) { '  ← 安装命令里已带 -ExecutionPolicy Bypass，通常不受影响' } else { '' }))

    # 4) TIA 版本
    $tia = Find-TiaPortal
    $supported = @($tia.Keys | Where-Object { $_ -ge 18 } | Sort-Object)
    if ($supported.Count -gt 0) { Ok ("TIA Portal：V" + ($supported -join ' / V')) }
    else { Warn '未检测到 V18+ —— 需要 TIA Portal V18/V20/V21（含 Openness 选项）' }
    foreach ($k in ($tia.Keys | Where-Object { $_ -lt 18 } | Sort-Object)) {
        Say ("     V$k  $($tia[$k])   [低于 V18，不支持]")
    }

    # 5) 磁盘空间（包约 17 MB，解压后约 40 MB）
    $drive = (Get-Item $env:USERPROFILE).PSDrive
    $freeGB = [math]::Round($drive.Free / 1GB, 1)
    if ($drive.Free -gt 200MB) { Ok "磁盘剩余：$freeGB GB（安装目录 $env:USERPROFILE\tia-mcp）" }
    else { Warn "磁盘剩余仅 $freeGB GB —— 建议至少留 200 MB" }

    # 6) 网络可达性（决定用哪条下载通道）
    Say ''
    Say '  下载通道可达性（HEAD 探测，10 秒超时）：'
    $probe = @(
        @{ n = 'codeload（首选·整包）'; u = "https://codeload.github.com/$Repo/zip/refs/heads/$Ref" },
        @{ n = 'api（备选·zipball）  '; u = "https://api.github.com/repos/$Repo/zipball/$Ref" },
        @{ n = 'github（备选·archive）'; u = "https://github.com/$Repo/archive/refs/heads/$Ref.zip" },
        @{ n = 'raw（一行脚本用）     '; u = $RawUrl }
    )
    $reachable = @()
    foreach ($p in $probe) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        try {
            Invoke-WebRequest -UseBasicParsing -Method Head -Uri $p.u -TimeoutSec 10 | Out-Null
            $reachable += $p.n
            Ok ("{0} {1,5} ms" -f $p.n, $sw.ElapsedMilliseconds)
        } catch {
            Warn ("{0} 不可达（{1}）" -f $p.n, $sw.ElapsedMilliseconds + ' ms')
        }
    }

    # 7) 已注册的客户端（安装会改这些文件，改前会备份 .bak）
    Say ''
    Say '  已注册 tia-portal 的客户端（安装时会更新，改前备份 .bak）：'
    $home_ = $env:USERPROFILE; $app = $env:APPDATA
    $cfgs = @(
        @{ n = 'workbuddy';   p = (Join-Path $home_ '.workbuddy\mcp.json') },
        @{ n = 'claude';      p = (Join-Path $app 'Claude\claude_desktop_config.json') },
        @{ n = 'claude-code'; p = (Join-Path $home_ '.claude.json') },
        @{ n = 'cursor';      p = (Join-Path $home_ '.cursor\mcp.json') },
        @{ n = 'vscode';      p = (Join-Path $app 'Code\User\mcp.json') }
    )
    $any = $false
    foreach ($c in $cfgs) {
        if (-not (Test-Path $c.p)) { continue }
        $has = $false
        try { $j = Get-Content $c.p -Raw -Encoding UTF8 | ConvertFrom-Json; $has = ($null -ne $j.mcpServers.'tia-portal') } catch { }
        Say ("    {0,-12} {1}" -f $c.n, $(if ($has) { '已注册（会被更新）' } else { '存在，但未注册 tia-portal' }))
        $any = $true
    }
    if (-not $any) { Say '    （没有找到任何客户端配置——先用客户端打开一次，再安装）' }

    # 8) 结论
    Say ''
    if ($psOk -and $supported.Count -gt 0 -and $reachable.Count -gt 0) {
        Say '  结论：可以安装。下一步（把 20 换成你要用的版本）：'
        Say ''
        Say "    powershell -NoProfile -ExecutionPolicy Bypass -Command `"& '$PSCommandPath' -Version 20`""
        Say ''
    } else {
        Say '  结论：有前提条件未满足，见上面 [warn] 行。'
        if ($supported.Count -eq 0) { Say '    · 先装 TIA Portal V18+（含 Openness 选项），或设 TiaPortalLocation 指向安装目录' }
        if ($reachable.Count -eq 0) { Say '    · 网络到 GitHub 全不通：请用离线方式（在能上网的机器下 ZIP，拷过来解压后跑 install-mcp.ps1）' }
        Say ''
    }
    exit 0
}

# ── 没给版本：只探测 + 给出下一条命令（不猜）────────────────────────────────
if ($Version -eq 0 -and -not $FollowOpenTia) {
    $tia = Find-TiaPortal
    $supported = @($tia.Keys | Where-Object { $_ -ge 18 } | Sort-Object)

    Say ''
    Say '  TIA Portal Openness MCP —— 一行式安装入口'
    Say ''
    Say '  本机探测到的 TIA Portal：'
    if ($tia.Count -eq 0) {
        Say '    （未探测到）若确认装了，请先设 TiaPortalLocation 再重试：'
        Say '      setx TiaPortalLocation "D:\Siemens\Automation\Portal V20"'
    }
    foreach ($k in ($tia.Keys | Sort-Object)) {
        $tag = if ($k -ge 18) { '可用' } else { '低于 V18，本 MCP 不支持' }
        Say ("    V{0,-2}  {1}   [{2}]" -f $k, $tia[$k], $tag)
    }

    if ($supported.Count -eq 0) {
        Die '本 MCP 需要 V18 或更高。'
    }

    Say ''
    Say '  下一步：从下面选一条复制执行（把版本换成你要用的那个）'
    Say ''
    # 若当前就在交付包内（已解压），直接给本地命令——不必再下载，也不依赖 raw 是否可达。
    $localInstaller = Join-Path $PSScriptRoot 'install-mcp.ps1'
    $inPackage = Test-Path $localInstaller
    foreach ($v in $supported) {
        if ($inPackage) {
            Say ("    V{0}: powershell -NoProfile -ExecutionPolicy Bypass -File `"{1}`" -TiaMajorVersion {0}" -f $v, $localInstaller)
        } else {
            Say ("    V{0}: powershell -NoProfile -ExecutionPolicy Bypass -Command `"Invoke-WebRequest -UseBasicParsing '{1}' -OutFile .\tia-mcp.zip; Expand-Archive .\tia-mcp.zip -DestinationPath .\tia-mcp-pkg -Force; & .\tia-mcp-pkg\tia-portal-openness-mcp-master\install-mcp.ps1 -TiaMajorVersion {0}`"" -f $v, $ZipUrls[0])
        }
    }
    if ($inPackage) {
        Say ("    跟随当前打开的 TIA: powershell -NoProfile -ExecutionPolicy Bypass -File `"{0}`" -FollowOpenTia" -f $localInstaller)
    } else {
        Say ("    跟随当前打开的 TIA: powershell -NoProfile -ExecutionPolicy Bypass -Command `"Invoke-WebRequest -UseBasicParsing '{0}' -OutFile .\tia-mcp.zip; Expand-Archive .\tia-mcp.zip -DestinationPath .\tia-mcp-pkg -Force; & .\tia-mcp-pkg\tia-portal-openness-mcp-master\install-mcp.ps1 -FollowOpenTia`"" -f $ZipUrls[0])
    }
    Say ''
    Say '  （装完想换版本：在安装目录里运行 switch-version.ps1）'
    Say ''
    exit 0
}

if ($Version -gt 0 -and $FollowOpenTia) {
    Die 'Version 与 FollowOpenTia 只能选一个。'
}
if ($Version -gt 0 -and $Version -lt 18) {
    Die "V$Version 低于 V18，本 MCP 不支持。"
}

# ── 取包 ────────────────────────────────────────────────────────────────────
$pkg = $null
if ($Source) {
    if (-not (Test-Path (Join-Path $Source 'install-mcp.ps1'))) { Die "Source 里没有 install-mcp.ps1：$Source" }
    $pkg = (Resolve-Path $Source).Path
} elseif ((Test-Path (Join-Path $Dest 'install-mcp.ps1'))) {
    $pkg = $Dest
    Ok "复用已有交付包：$pkg（要强制更新，删掉这个目录再跑）"
} else {
    Say ''
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $zip = Join-Path $env:TEMP 'tia-mcp-bootstrap.zip'
    $proxy = if ($env:HTTPS_PROXY) { $env:HTTPS_PROXY } elseif ($env:HTTP_PROXY) { $env:HTTP_PROXY } else { $null }

    $downloaded = $false
    foreach ($u in $ZipUrls) {
        Say "  下载交付包：$u"
        try {
            $iwr = @{ UseBasicParsing = $true; Uri = $u; OutFile = $zip; TimeoutSec = 300 }
            if ($proxy) { $iwr['Proxy'] = $proxy }
            if (Test-Path $zip) { Remove-Item $zip -Force }
            Invoke-WebRequest @iwr
            if ((Get-Item $zip).Length -gt 1024) { $downloaded = $true; break }
        } catch {
            Warn ("  这个通道不可用：" + $_.Exception.Message.Split("`n")[0])
        }
    }
    if (-not $downloaded) {
        Die @"
下载失败：三个通道都不通。请改用下面任一办法后重跑：

  1) 用浏览器打开（或让同事转发）：
     https://github.com/$Repo/archive/refs/heads/$Ref.zip
     解压到 %USERPROFILE%\tia-mcp，然后运行其中的 install-mcp.ps1 -TiaMajorVersion <你的版本>

  2) 若公司网络要走代理，先设好再重跑：
     set HTTPS_PROXY=http://<代理地址>:<端口>
"@
    }
    Ok ("已下载 {0:N0} 字节" -f (Get-Item $zip).Length)
    $tmp = Join-Path $env:TEMP ('tia-mcp-unzip-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $inner = Get-ChildItem $tmp -Directory | Select-Object -First 1
    if (-not $inner) { Die '下载的压缩包是空的。' }
    if (Test-Path $Dest) { Remove-Item $Dest -Recurse -Force }
    Move-Item $inner.FullName $Dest
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    $pkg = $Dest
    Ok "已解压到 $pkg"
}

$installer = Join-Path $pkg 'install-mcp.ps1'
if (-not (Test-Path $installer)) { Die "包里没有 install-mcp.ps1：$pkg" }

# ── 转调安装器（版本已确定，因此不会触发交互提问）──────────────────────────
# 用哈希表 splatting：数组 splat 会被当"位置参数"，`-TiaMajorVersion` 这类名字会被当成值塞给第一个参数
# （实际踩过：'-TiaMajorVersion' 被绑到 -Client，报 ValidateSet 不匹配）。
$installArgs = @{}
if ($Version -gt 0) { $installArgs['TiaMajorVersion'] = $Version } else { $installArgs['FollowOpenTia'] = $true }
if ($Lite.IsPresent)   { $installArgs['Lite'] = $true }
if ($DryRun.IsPresent) { $installArgs['DryRun'] = $true }
if ($Direct.IsPresent) { $installArgs['Direct'] = $true }

Say ''
Say ("  调用安装器：install-mcp.ps1 " + (($installArgs.GetEnumerator() | ForEach-Object {
    if ($_.Value -is [bool]) { "-$($_.Key)" } else { "-$($_.Key) $($_.Value)" }
}) -join ' '))
Say ''
& $installer @installArgs
exit $LASTEXITCODE
