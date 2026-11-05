param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path (Split-Path $projectRoot -Parent) '.tools\dotnet\dotnet.exe'
$dotnetExe = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path (Split-Path $projectRoot -Parent) '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path (Split-Path $projectRoot -Parent) '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
foreach ($name in @('MobiStore.Server','MobiStore.Desktop')) {
    $part = if ($name -eq 'MobiStore.Server') { 'server' } else { 'desktop' }
    $destination = Join-Path $projectRoot "artifacts\releases\v0.2\$part\"
    & $dotnetExe build (Join-Path $projectRoot "src\$name\$name.csproj") -c $Configuration -p:OutputPath=$destination -p:UseSharedCompilation=false -m:1
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
}
& $dotnetExe run --project (Join-Path $projectRoot 'tests\MobiStore.Tests\MobiStore.Tests.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
