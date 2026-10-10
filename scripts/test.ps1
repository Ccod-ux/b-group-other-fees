param([switch]$SkipBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
Initialize-OtherFeesSdk
Push-Location $taskRoot
try {
    & $taskSdk test OtherFees.sln -c Release --no-build --no-restore --nologo --logger 'trx;LogFileName=mvp1.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Pop-Location }
