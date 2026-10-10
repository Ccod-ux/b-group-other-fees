param()
. (Join-Path $PSScriptRoot 'common.ps1')
Initialize-OtherFeesSdk
Push-Location $taskRoot
try {
    & $taskSdk restore OtherFees.sln --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $taskSdk build OtherFees.sln -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
} finally { Pop-Location }
