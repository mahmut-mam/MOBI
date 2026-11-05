param([switch]$ServerOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dotnetExe = Join-Path (Split-Path $projectRoot -Parent) '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
$serverDll = Join-Path $projectRoot 'src\MobiStore.Server\bin\Release\net10.0\MobiStore.Server.dll'
$desktopDll = Join-Path $projectRoot 'src\MobiStore.Desktop\bin\Release\net10.0-windows\MobiStore.dll'
$releaseRoot = Join-Path $projectRoot 'artifacts\releases\v0.2'
if (Test-Path -LiteralPath (Join-Path $releaseRoot 'server\MobiStore.Server.dll')) {
    $serverDll = Join-Path $releaseRoot 'server\MobiStore.Server.dll'
    $desktopDll = Join-Path $releaseRoot 'desktop\MobiStore.dll'
}
if (-not (Test-Path -LiteralPath $serverDll)) { throw 'Run scripts\build.ps1 first' }
$logDir = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$running = $false
try { $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5188/health' -TimeoutSec 2; $running = $health.product -eq 'MobiStore' } catch { }
if (-not $running) {
    $process = Start-Process -FilePath $dotnetExe -ArgumentList @($serverDll) -WorkingDirectory (Join-Path $projectRoot 'src\MobiStore.Server') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'server.log') -RedirectStandardError (Join-Path $logDir 'server-error.log') -PassThru
    $process.Id | Set-Content -LiteralPath (Join-Path $logDir 'server.pid')
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { Invoke-RestMethod -Uri 'http://127.0.0.1:5188/health' -TimeoutSec 1 | Out-Null; $running = $true; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $running) { throw "Server did not start. See $logDir\server-error.log" }
}
if (-not $ServerOnly) { Start-Process -FilePath $dotnetExe -ArgumentList @($desktopDll) -WorkingDirectory $projectRoot -WindowStyle Hidden }
