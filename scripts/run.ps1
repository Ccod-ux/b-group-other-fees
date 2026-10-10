param([string]$Url = 'http://localhost:5080/', [switch]$ResetDisabled)
. (Join-Path $PSScriptRoot 'common.ps1')
$taskExe = Join-Path $taskRoot 'src/OtherFees.Api/bin/Release/net48/OtherFees.Api.exe'
if (-not (Test-Path -LiteralPath $taskExe)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$taskArguments = @('--url', $Url, '--database', (Join-Path $taskRoot 'App_Data/other-fees.sqlite'))
if ($ResetDisabled) { $taskArguments += '--reset-disabled' }
& $taskExe @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'API stopped with an error; inspect the console output.' }
