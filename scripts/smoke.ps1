param([switch]$SkipBuild, [int]$Port = 5091, [string]$ApiDirectory)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
Add-Type -AssemblyName System.Net.Http
if (-not $ApiDirectory) { $ApiDirectory = Join-Path $taskRoot 'src/OtherFees.Api/bin/Release/net48' }
$taskExe = (Resolve-Path -LiteralPath (Join-Path $ApiDirectory 'OtherFees.Api.exe')).Path
$taskRunDirectory = Join-Path $taskRoot ('artifacts/smoke/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRunDirectory -Force | Out-Null
$taskData = Join-Path $taskRunDirectory 'smoke.sqlite'
$taskBaseUrl = 'http://localhost:' + $Port + '/'
$taskReport = New-Object System.Collections.Generic.List[object]
$taskProcess = $null
$taskClient = New-Object System.Net.Http.HttpClient
$taskClient.Timeout = [TimeSpan]::FromSeconds(10)

function Start-SmokeApi {
    $script:taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--url', $taskBaseUrl, '--database', ('"' + $taskData + '"')) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $taskRunDirectory ('stdout-' + [Guid]::NewGuid().ToString('N') + '.log')) -RedirectStandardError (Join-Path $taskRunDirectory ('stderr-' + [Guid]::NewGuid().ToString('N') + '.log'))
    for ($taskAttempt = 0; $taskAttempt -lt 40; $taskAttempt++) {
        if ($taskProcess.HasExited) { throw ('API exited. Logs: ' + $taskRunDirectory) }
        try {
            $taskHealth = $taskClient.GetAsync($taskBaseUrl + 'api/health').GetAwaiter().GetResult()
            try { if ([int]$taskHealth.StatusCode -eq 200) { return } } finally { $taskHealth.Dispose() }
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    throw 'API did not become ready.'
}
function Stop-SmokeApi {
    if ($null -ne $taskProcess) {
        if (-not $taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id; $taskProcess.WaitForExit() }
        $taskProcess.Dispose()
        $script:taskProcess = $null
    }
}
function Send-SmokeRequest([string]$Method, [string]$Path, [string]$Json, [int]$ExpectedStatus, [string]$Check) {
    $taskRequest = New-Object System.Net.Http.HttpRequestMessage -ArgumentList @((New-Object System.Net.Http.HttpMethod -ArgumentList $Method), ($taskBaseUrl + $Path))
    if ($Json) { $taskRequest.Content = New-Object System.Net.Http.StringContent -ArgumentList @($Json, [Text.Encoding]::UTF8, 'application/json') }
    try {
        $taskResponse = $taskClient.SendAsync($taskRequest).GetAwaiter().GetResult()
        try {
            $taskText = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $taskReport.Add([PSCustomObject]@{ Check = $Check; ExpectedStatus = $ExpectedStatus; ActualStatus = [int]$taskResponse.StatusCode; Body = $taskText })
            if ([int]$taskResponse.StatusCode -ne $ExpectedStatus) { throw ($Check + ': ' + $taskText) }
            if ($taskText) { return (ConvertFrom-Json -InputObject $taskText) }
        } finally { $taskResponse.Dispose() }
    } finally { $taskRequest.Dispose() }
}
try {
    Start-SmokeApi
    $taskReset = Send-SmokeRequest 'POST' 'api/reset' '' 200 'reset'
    if ($taskReset.ProjectCount -ne 2 -or $taskReset.FixedWorkItemCount -ne 12 -or $taskReset.DesignFeeCount -ne 2 -or $taskReset.OtherFeeCount -ne 0) { throw 'Seed counts differ.' }
    $taskOriginal = '{"ProjectId":"7-51231123","Name":"测试其他费用","IncomePrice":521.00,"CostPrice":520.00}'
    $taskCreated = Send-SmokeRequest 'POST' 'api/other-fees' $taskOriginal 201 'create-original'
    if ($taskCreated.Profit -ne 1 -or $taskCreated.ProfitRatePercent -ne 0.19) { throw 'Original calculation differs.' }
    $null = Send-SmokeRequest 'POST' 'api/other-fees' $taskOriginal 409 'duplicate-create'
    Stop-SmokeApi
    Start-SmokeApi
    $taskList = @(Send-SmokeRequest 'GET' 'api/projects/7-51231123/other-fees' '' 200 'restart-preserves-record')
    if ($taskList.Count -ne 1 -or $taskList[0].Id -ne $taskCreated.Id) { throw 'Restart lost data.' }
    $taskUpdated = Send-SmokeRequest 'PUT' ('api/other-fees/' + $taskCreated.Id) '{"Name":"测试其他费用","IncomePrice":50,"CostPrice":70}' 200 'update'
    if ($taskUpdated.Id -ne $taskCreated.Id -or $taskUpdated.Profit -ne -20 -or $taskUpdated.ProfitRatePercent -ne -40) { throw 'Update calculation differs.' }
    $taskInvalid = Send-SmokeRequest 'POST' 'api/other-fees' '{"ProjectId":"demo-zero","Name":"bad","CostPrice":-0.001}' 400 'negative-cost'
    if ($taskInvalid.Code -ne 'COST_NEGATIVE') { throw 'Wrong validation error.' }
    $null = Send-SmokeRequest 'DELETE' ('api/other-fees/' + $taskCreated.Id) '' 204 'delete'
    $null = Send-SmokeRequest 'DELETE' ('api/other-fees/' + $taskCreated.Id) '' 404 'delete-missing'
    $null = Send-SmokeRequest 'POST' 'api/reset' '' 200 'reset-again'
    $taskOutput = Join-Path $taskRunDirectory 'http-smoke.json'
    $taskReport.ToArray() | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $taskOutput -Encoding UTF8
    Write-Output ('PASS: ' + $taskReport.Count + ' HTTP checks; ' + $taskOutput)
} finally {
    Stop-SmokeApi
    $taskClient.Dispose()
}
