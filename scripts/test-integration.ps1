param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dotnetExe = Join-Path (Split-Path $projectRoot -Parent) '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet).Source }
$env:DOTNET_CLI_HOME = Join-Path (Split-Path $projectRoot -Parent) '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path (Split-Path $projectRoot -Parent) '.tools\nuget'
$logDir = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$database = 'MobiStore_Test_' + (Get-Date -Format 'yyyyMMdd_HHmmss')
$database | Set-Content -LiteralPath (Join-Path $logDir 'test-database.txt')
$serverDll = Join-Path $projectRoot "src\MobiStore.Server\bin\$Configuration\net10.0\MobiStore.Server.dll"
if ($Configuration -eq 'Release' -and (Test-Path -LiteralPath (Join-Path $projectRoot 'artifacts\releases\v0.2\server\MobiStore.Server.dll'))) { $serverDll = Join-Path $projectRoot 'artifacts\releases\v0.2\server\MobiStore.Server.dll' }
$connection = "Server=lpc:.\SQLEXPRESS;Database=$database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$arguments = @($serverDll, '--urls', 'http://127.0.0.1:5190', '--ConnectionStrings:Store', ('"' + $connection + '"'), '--DatabaseCatalogFile', (Join-Path $logDir ($database + '-catalog.json')))
$process = Start-Process -FilePath $dotnetExe -ArgumentList $arguments -WorkingDirectory (Join-Path $projectRoot 'src\MobiStore.Server') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'test-server.log') -RedirectStandardError (Join-Path $logDir 'test-server-error.log') -PassThru
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($process.HasExited) { throw 'Test server exited; inspect artifacts/test-server-error.log' }
        try { Invoke-RestMethod -Uri 'http://127.0.0.1:5190/health' -TimeoutSec 1 | Out-Null; $ready = $true; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Test server did not become ready' }
    & $dotnetExe run --project (Join-Path $projectRoot 'tests\MobiStore.Tests\MobiStore.Tests.csproj') -c $Configuration --no-build -- 'http://127.0.0.1:5190' | Tee-Object -FilePath (Join-Path $logDir 'integration-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed' }
    Write-Output "Isolated test database retained for inspection: $database"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
