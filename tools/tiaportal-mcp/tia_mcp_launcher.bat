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

:: Version resolution, fastest path first:
::   1. TIA_MCP_VERSION env var (set by the host, e.g. in mcp.json) -> used
::      as-is and the probe process below is NOT started at all (saves ~1.3-1.5 s
::      per cold start);
::   2. otherwise ask detect_tia_version.ps1 (running-Portal probe, then fallback).
:: NOTE: pinning TIA_MCP_VERSION means this launcher no longer follows whichever
::       TIA version you actually have open - clear it to restore auto-detection.
set "VER="
if defined TIA_MCP_VERSION set "VER=%TIA_MCP_VERSION%"
if defined VER set "VER=%VER: =%"
if defined VER goto :ver_ready

:: NOTE: keep this for/f at TOP level. Wrapping it in an "if ( ... )" block breaks
:: the 2^>nul escaping and silently yields an empty VER (i.e. the V18 fallback).
for /f "delims=" %%v in ('powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0detect_tia_version.ps1" 2^>nul') do set "VER=%%v"

:ver_ready
if "%VER%"=="" set "VER=18"

if "%VER%"=="20" (
    set "TIA_LOC=D:\Program Files\Siemens\Automation\Portal V20"
    set "TIA_BIN=D:\Program Files\Siemens\Automation\Portal V20\Bin"
) else (
    set "TIA_LOC=C:\Program Files\Siemens\Automation\Portal V18"
    set "TIA_BIN=C:\Program Files\Siemens\Automation\Portal V18\Bin"
)

:: Prepend the version-specific TIA Bin + common Siemens native paths.
set "PATH=%TIA_BIN%;C:\Program Files\Common Files\Siemens\Automation\Simatic OAM\bin;C:\Program Files (x86)\Common Files\Siemens\Bin;C:\Program Files (x86)\Common Files\Siemens\CommonArchiving;C:\Program Files (x86)\Common Files\Siemens\ACE\Bin;%PATH%"

:: Launch the version-matched build. All forwarded args + the resolved version.
"%~dp0src\TiaMcpServer\bin-v%VER%\Release\net48\TiaMcpServer.exe" --tia-major-version %VER% --tia-portal-location "%TIA_LOC%" %*

endlocal
