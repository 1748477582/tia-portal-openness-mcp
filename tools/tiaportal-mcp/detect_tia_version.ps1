# Detect which TIA Portal version to serve.
# Priority:
#   1. Explicit override via env var TIA_MCP_VERSION (e.g. 18 / 20)
#   2. A currently running Siemens.Automation.Portal process -> its major version
#   3. Fallback: 18 (the user's daily driver)
# Outputs ONLY the major version number (e.g. "20") or nothing.
try {
    if ($env:TIA_MCP_VERSION -match '^\s*(\d{2})\s*$') {
        [int]$env.TIA_MCP_VERSION
        exit 0
    }
    $procs = Get-Process -Name "Siemens.Automation.Portal" -ErrorAction SilentlyContinue
    $ver = $null
    foreach ($p in $procs) {
        if ($p.Path -match 'Portal V(\d+)') {
            $v = [int]$matches[1]
            if ($ver -eq $null -or $v -gt $ver) { $ver = $v }
        }
    }
    if ($ver -ne $null) { Write-Output $ver }
} catch { }
