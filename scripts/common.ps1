$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSdk = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
function Initialize-OtherFeesSdk {
    if (-not (Test-Path -LiteralPath $taskSdk)) { & (Join-Path $PSScriptRoot 'bootstrap.ps1') | Out-Null }
    if (-not (Test-Path -LiteralPath $taskSdk)) { throw 'Portable SDK is missing.' }
}
