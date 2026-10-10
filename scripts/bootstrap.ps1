param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskTools = Join-Path $taskRoot '.tools'
$taskSdkDir = Join-Path $taskTools 'dotnet'
$taskSdkExe = Join-Path $taskSdkDir 'dotnet.exe'
if (Test-Path -LiteralPath $taskSdkExe) {
    Write-Output $taskSdkExe
    exit 0
}
$taskVersion = '8.0.425'
$taskUrl = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.425/dotnet-sdk-8.0.425-win-x64.zip'
$taskHash = 'f0b6f15bf6f1a0507205c0cb102ab99e1dee875c4682c8ed94665be1d580186a06b21455e83b3a01a0ff7f4cd887b67420f2e2fe09ed985534a4cea488ae1af9'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskZip = Join-Path $taskTools ('dotnet-sdk-' + $taskVersion + '.zip')
Write-Host 'Downloading the portable build SDK...'
& curl.exe --fail --location --retry 2 --silent --show-error --output $taskZip $taskUrl
if ($LASTEXITCODE -ne 0) { throw 'SDK download failed.' }
if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash.ToLowerInvariant() -ne $taskHash) {
    throw 'SDK SHA512 does not match the official release metadata.'
}
New-Item -ItemType Directory -Path $taskSdkDir -Force | Out-Null
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskSdkDir -Force
Write-Output $taskSdkExe
