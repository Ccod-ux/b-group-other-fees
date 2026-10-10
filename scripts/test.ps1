param([switch]$SkipBuild, [string]$OutputPath, [string]$OriginalImportFile)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -OutputPath $OutputPath }
Initialize-OtherFeesSdk
Push-Location $taskRoot
$taskPreviousFixture = $env:OTHERFEES_ORIGINAL_IMPORT_XLSX
try {
    if ($OriginalImportFile) { $env:OTHERFEES_ORIGINAL_IMPORT_XLSX = (Resolve-Path -LiteralPath $OriginalImportFile).Path }
    $taskTestArguments = @('test', 'OtherFees.sln', '-c', 'Release', '--no-build', '--no-restore', '--nologo', '--logger', 'trx;LogFileName=mvp3.trx', '--results-directory', 'artifacts/test-results')
    if ($OutputPath) {
        $taskOutput = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $taskRoot $OutputPath }
        $taskTestArguments += ('-p:BaseOutputPath=' + [IO.Path]::GetFullPath($taskOutput).Replace('\', '/') + '/')
    }
    & $taskSdk @taskTestArguments
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { $env:OTHERFEES_ORIGINAL_IMPORT_XLSX = $taskPreviousFixture; Pop-Location }
