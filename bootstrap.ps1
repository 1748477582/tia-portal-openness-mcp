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
    [string]$Ref = 'master',
    [string]$Source
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Repo   = '1748477582/tia-portal-openness-mcp'
$ZipUrl = "https://github.com/$Repo/archive/refs/heads/$Ref.zip"
$RawUrl = "https://raw.githubusercontent.com/$Repo/$Ref/bootstrap.ps1"
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
    foreach ($v in $supported) {
        Say ("    V{0}: powershell -NoProfile -ExecutionPolicy Bypass -Command `"Invoke-WebRequest -UseBasicParsing '{1}' -OutFile .\tia-bootstrap.ps1; & .\tia-bootstrap.ps1 -Version {0}`"" -f $v, $RawUrl)
    }
    Say ("    跟随当前打开的 TIA: powershell -NoProfile -ExecutionPolicy Bypass -Command `"Invoke-WebRequest -UseBasicParsing '{0}' -OutFile .\tia-bootstrap.ps1; & .\tia-bootstrap.ps1 -FollowOpenTia`"" -f $RawUrl)
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
    Say "  下载交付包：$ZipUrl"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $zip = Join-Path $env:TEMP 'tia-mcp-bootstrap.zip'
    Invoke-WebRequest -UseBasicParsing -Uri $ZipUrl -OutFile $zip
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
