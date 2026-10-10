param(
    [Parameter(Mandatory = $true)][string]$File,
    [string]$SdkPath,
    [string]$Revision = 'HEAD'
)
. (Join-Path $PSScriptRoot 'common.ps1')
$taskOriginal = (Resolve-Path -LiteralPath $File).Path
if (-not $SdkPath) { Initialize-OtherFeesSdk; $SdkPath = $taskSdk }
$taskBuildSdk = (Resolve-Path -LiteralPath $SdkPath).Path
$taskClean = Join-Path $taskRoot ('artifacts/final-verification/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskClean -Force | Out-Null
$taskCommit = (& git -C $taskRoot rev-parse ($Revision + '^{commit}')).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Revision does not resolve to a commit.' }
& git -C $taskRoot archive $taskCommit -o (Join-Path $taskClean 'source.zip')
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
$taskSource = Join-Path $taskClean 'source'
Expand-Archive -LiteralPath (Join-Path $taskClean 'source.zip') -DestinationPath $taskSource
$taskPreviousOriginal = $env:OTHERFEES_ORIGINAL_IMPORT_XLSX
$taskHash = (Get-FileHash -LiteralPath $taskOriginal).Hash
Push-Location $taskSource
try {
    Start-Transcript -LiteralPath (Join-Path $taskClean 'verification.log') | Out-Null
    $env:OTHERFEES_ORIGINAL_IMPORT_XLSX = $taskOriginal
    & $taskBuildSdk restore OtherFees.sln --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Clean locked restore failed.' }
    & $taskBuildSdk build OtherFees.sln -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Clean build failed.' }
    & $taskBuildSdk test OtherFees.sln -c Release --no-build --no-restore --nologo --logger 'trx;LogFileName=mvp4.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Clean tests failed.' }
    & ./scripts/smoke.ps1 -SkipBuild
    & ./scripts/smoke-import.ps1 -SkipBuild -File $taskOriginal
    & ./scripts/smoke-export.ps1 -SkipBuild -File $taskOriginal
    if ((Get-FileHash -LiteralPath $taskOriginal).Hash -ne $taskHash) { throw 'Original workbook changed.' }
    [xml]$taskTrx = Get-Content -LiteralPath artifacts/test-results/mvp4.trx -Raw
    $taskCounters = $taskTrx.SelectSingleNode('//*[local-name()="Counters"]')
    $taskEvidence = [PSCustomObject]@{
        SourceCommit = $taskCommit
        VerifiedAt = (Get-Date).ToString('o')
        Restore = 'locked-mode, machine NuGet cache permitted'
        BuildSdk = $taskBuildSdk
        Build = 'Release, net48, x64'
        TestTotal = [int]$taskCounters.total
        TestPassed = [int]$taskCounters.passed
        TestFailed = [int]$taskCounters.failed
        OriginalSha256 = $taskHash
        HttpChecks = 32
        ApiDirectory = (Join-Path $taskSource 'src/OtherFees.Api/bin/Release/net48')
    }
    $taskEvidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskClean 'verification.json') -Encoding UTF8
    Write-Output ('CLEAN_PASS: ' + $taskClean)
} finally {
    Stop-Transcript -ErrorAction SilentlyContinue | Out-Null
    $env:OTHERFEES_ORIGINAL_IMPORT_XLSX = $taskPreviousOriginal
    Pop-Location
}
