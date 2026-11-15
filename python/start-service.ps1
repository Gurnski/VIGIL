<#
Starts the VIGIL analysis service on the stored scan, and starts it again if it stops.

  python\start-service.cmd             this machine only
  python\start-service.cmd -Network    other devices on the network can reach it, which Present needs
  python\start-service.cmd -Port 8001  another port

It finds the scan in data\scan and the Detective's key in ANTHROPIC_API_KEY: in this terminal,
or saved for your user with setx, or in python\.env, which git ignores. The key is never shown.
Anything already set in the terminal (VIGIL_SNAPSHOT, VIGIL_SCAN_DATA, the key) is left as it is.
#>
param(
    [switch]$Network,
    [int]$Port = 8000
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $here
Set-Location $here

$python = Join-Path $here '.venv\Scripts\python.exe'
if (-not (Test-Path $python)) {
    Write-Host 'There is no virtual environment at python\.venv. python\README.md says how to make one.'
    exit 1
}

# The scan, and the gridded data it was made from. Zones, Recompute and the years need the second.
if (-not $env:VIGIL_SNAPSHOT) {
    $found = Join-Path $repo 'data\scan\snapshot.json'
    if (Test-Path $found) { $env:VIGIL_SNAPSHOT = $found }
}
if (-not $env:VIGIL_SCAN_DATA) {
    $found = Join-Path $repo 'data\scan\scan-data.npz'
    if (Test-Path $found) { $env:VIGIL_SCAN_DATA = $found }
}

# The Detective's key: this terminal, then the one saved for the user, then python\.env.
if (-not $env:ANTHROPIC_API_KEY) {
    $saved = [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY', 'User')
    $file = Join-Path $here '.env'
    if (-not $saved -and (Test-Path $file)) {
        foreach ($line in Get-Content $file) {
            if ($line -match '^\s*ANTHROPIC_API_KEY\s*=\s*"?([^"\s]+)"?\s*$') { $saved = $Matches[1] }
        }
    }
    if ($saved) { $env:ANTHROPIC_API_KEY = $saved }
}

$address = "http://127.0.0.1:$Port"
try { $there = Invoke-RestMethod "$address/health" -TimeoutSec 2 } catch { $there = $null }
if ($there -and $there.status -eq 'ok') {
    Write-Host "A service is already answering at $address, holding $($there.run_id). Close that one first to start another."
    exit 1
}

if ($env:VIGIL_SNAPSHOT) { Write-Host "Scan:          $env:VIGIL_SNAPSHOT" }
else { Write-Host 'Scan:          none found in data\scan, so the service will hold the made-up fixture' }
if ($env:VIGIL_SCAN_DATA) { Write-Host "Gridded data:  $env:VIGIL_SCAN_DATA" }
else { Write-Host 'Gridded data:  none, so zones, Recompute and the years will not work' }
if ($env:ANTHROPIC_API_KEY) { Write-Host 'Detective:     a key is set' }
else { Write-Host 'Detective:     no key, so it will not answer. Save one with: setx ANTHROPIC_API_KEY "your key", then start this again' }
if ($Network) { Write-Host "Listening on:  every network address, port $Port. Windows may ask to let Python through the firewall: say yes." }
else { Write-Host "Listening on:  this machine only, port $Port. Add -Network so other devices can follow a presentation." }
Write-Host 'Close this window, or press Ctrl+C, to stop the service.'
Write-Host ''

$bind = if ($Network) { '0.0.0.0' } else { '127.0.0.1' }
$quick = 0
while ($true) {
    $started = Get-Date
    & $python -m uvicorn vigil.api.app:app --host $bind --port $Port
    $code = $LASTEXITCODE
    if ($code -eq 0) { break }

    # A service that dies as soon as it starts will not be cured by starting it again.
    if (((Get-Date) - $started).TotalSeconds -lt 10) { $quick++ } else { $quick = 0 }
    if ($quick -ge 3) {
        Write-Host ''
        Write-Host "The service stopped three times as soon as it started (exit code $code). The lines above say why."
        exit 1
    }
    Write-Host ''
    Write-Host "The service stopped (exit code $code). Starting it again in 2 seconds."
    Start-Sleep -Seconds 2
}
