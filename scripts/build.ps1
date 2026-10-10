param([string]$OutputPath)
. (Join-Path $PSScriptRoot 'common.ps1')
Initialize-OtherFeesSdk
Push-Location $taskRoot
try {
    & $taskSdk restore OtherFees.sln --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    $taskBuildArguments = @('build', 'OtherFees.sln', '-c', 'Release', '--no-restore', '--nologo')
    if ($OutputPath) {
        $taskOutput = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $taskRoot $OutputPath }
        $taskBuildArguments += ('-p:BaseOutputPath=' + [IO.Path]::GetFullPath($taskOutput).Replace('\', '/') + '/')
    }
    & $taskSdk @taskBuildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
} finally { Pop-Location }
