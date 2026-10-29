@echo off
setlocal EnableExtensions
:: -----------------------------------------------------------------------------
:: Single MCP launcher for multi-version TIA Portal (V18 / V20 / ...).
:: Picks the correct build (bin-vXX) by version, injects the matching TIA Bin
:: into PATH and passes --tia-major-version + --tia-portal-location so the
:: server loads the right Openness assemblies. stdio (MCP) is inherited clean.
:: -----------------------------------------------------------------------------
:: ASCII-only on purpose: this file is read by cmd.exe with the OEM code page,
:: so non-ASCII bytes in comments can be re-parsed as commands on GBK hosts.
:: -----------------------------------------------------------------------------
:: 2026-10-28 rewrite of version/location resolution:
::   - NO hard-coded install paths (the old V20->D: / other->C: mapping broke on
::     every machine whose layout differed).
::   - NO silent "fall back to V18". An unresolvable or ambiguous setup now stops
::     with an actionable message instead of loading the wrong build.
::   - Detection lives in detect_tia_version.ps1 -Full, which scans every fixed
::     drive and treats the disk as the only authority (registry claims are not
::     trusted: an incomplete uninstall leaves Openness keys behind).
:: -----------------------------------------------------------------------------
:: Output contract of detect_tia_version.ps1 -Full: KEY=VALUE lines
::   STATUS=ok|ambiguous|none|unsupported|pinned-unusable|running-unsupported|bad-location
::   VER=20  ROOT=D:\...\Portal V20  SOURCE=env|running|disk|env-location
::   CANDIDATES=20@D:\...;18@C:\...   UNSUPPORTED=<installed but no build for it>
:: (KEY=VALUE rather than a delimiter-separated line: cmd's for /f collapses
::  consecutive delimiters, so an empty field silently shifts every later token.)
:: -----------------------------------------------------------------------------
:: Diagnostic: set TIA_MCP_LAUNCHER_ECHO_ONLY=1 to print the resolution and exit
:: without starting the engine (used by the install/verify scripts).
:: -----------------------------------------------------------------------------

set "ST="
set "VER="
set "TIA_LOC="
set "SRC="
set "CAND="
set "UNSUP="

for /f "usebackq tokens=1,* delims==" %%a in (`powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0detect_tia_version.ps1" -Full`) do (
    if /i "%%a"=="STATUS" set "ST=%%b"
    if /i "%%a"=="VER" set "VER=%%b"
    if /i "%%a"=="ROOT" set "TIA_LOC=%%b"
    if /i "%%a"=="SOURCE" set "SRC=%%b"
    if /i "%%a"=="CANDIDATES" set "CAND=%%b"
    if /i "%%a"=="UNSUPPORTED" set "UNSUP=%%b"
)

if not "%ST%"=="ok" goto :not_ok

set "TIA_BIN=%TIA_LOC%\Bin"
:: Prepend the resolved TIA Bin + common Siemens native paths.
set "PATH=%TIA_BIN%;C:\Program Files\Common Files\Siemens\Automation\Simatic OAM\bin;C:\Program Files (x86)\Common Files\Siemens\Bin;C:\Program Files (x86)\Common Files\Siemens\CommonArchiving;C:\Program Files (x86)\Common Files\Siemens\ACE\Bin;%PATH%"

:: Launch the version-matched build. All forwarded args + the resolved version.
:: Two layouts are supported:
::   tools\...\bin-vXX\Release\net48\...  (local dev tree, what the original delivery ships)
::   <repo>\runtime\vXX\...              (repo / ZIP distribution: bin-* is not committed)
set "ENGINE=%~dp0src\TiaMcpServer\bin-v%VER%\Release\net48\TiaMcpServer.exe"
if exist "%ENGINE%" goto :engine_ready
set "ENGINE=%~dp0..\..\runtime\v%VER%\TiaMcpServer.exe"
if exist "%ENGINE%" goto :engine_ready
echo [launcher] TiaMcpServer.exe not found for V%VER%. Looked in:
echo [launcher]   %~dp0src\TiaMcpServer\bin-v%VER%\Release\net48\
echo [launcher]   %~dp0..\..\runtime\v%VER%\
echo [launcher] This build does not ship V%VER%. Supported builds are the bin-v* you see there.
endlocal
exit /b 3

:engine_ready
if defined TIA_MCP_LAUNCHER_ECHO_ONLY (
    echo [launcher] status=ok version=%VER% source=%SRC%
    echo [launcher] portal=%TIA_LOC%
    echo [launcher] engine=%ENGINE%
    endlocal
    exit /b 0
)
"%ENGINE%" --tia-major-version %VER% --tia-portal-location "%TIA_LOC%" %*
endlocal
exit /b 0

:not_ok
echo [launcher] Could not decide which TIA Portal to use. status=%ST%
if "%ST%"=="ambiguous" (
    echo [launcher] Several usable installs found: %CAND%
    echo [launcher] Pick one ONCE by setting these two environment variables in the MCP host config:
    echo [launcher]   TIA_MCP_VERSION=18 or 20 or 21
    echo [launcher]   TIA_MCP_PORTAL_LOCATION=^<the Portal VXX folder shown above^>
    echo [launcher] The launcher will not guess.
    endlocal
    exit /b 6
)
if "%ST%"=="bad-location" (
    echo [launcher] TIA_MCP_PORTAL_LOCATION points to a folder that is not a TIA Portal install: %TIA_LOC%
    echo [launcher] It must contain bin\Siemens.Automation.Portal.exe.
    echo [launcher] Usable installs: %CAND%
    endlocal
    exit /b 5
)
if "%ST%"=="pinned-unusable" (
    echo [launcher] TIA_MCP_VERSION=%VER% is pinned but no usable install of it was found.
    echo [launcher] Usable installs: %CAND%
    echo [launcher] Installed but without a matching build: %UNSUP%
    endlocal
    exit /b 4
)
if "%ST%"=="running-unsupported" (
    echo [launcher] The running TIA Portal is V%VER%, which this build cannot serve.
    echo [launcher] Usable installs: %CAND%
    echo [launcher] Installed but without a matching build: %UNSUP%
    endlocal
    exit /b 4
)
if "%ST%"=="unsupported" (
    echo [launcher] TIA Portal found, but this delivery has no build for it: %UNSUP%
    endlocal
    exit /b 4
)
echo [launcher] No TIA Portal installation was found on any fixed drive.
echo [launcher] Looked for ^<drive^>:\Program Files\Siemens\Automation\Portal V*,
echo [launcher]   ^<drive^>:\Program Files ^(x86^)\Siemens\Automation\Portal V* and
echo [launcher]   ^<drive^>:\Siemens\Automation\Portal V*, requiring bin\Siemens.Automation.Portal.exe
echo [launcher]   and PublicAPI\V^<same version^>\Siemens.Engineering.dll.
echo [launcher] Install TIA Portal, or point TIA_MCP_PORTAL_LOCATION at an existing install.
endlocal
exit /b 4
