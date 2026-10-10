param(
    [Parameter(Mandatory = $true)][string]$File,
    [switch]$SkipBuild,
    [string]$ApiDirectory,
    [int]$Port = 5093
)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression
$taskSource = (Resolve-Path -LiteralPath $File).Path
$taskOriginalHash = (Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash
if (-not $ApiDirectory) { $ApiDirectory = Join-Path $taskRoot 'src/OtherFees.Api/bin/Release/net48' }
$taskExe = (Resolve-Path -LiteralPath (Join-Path $ApiDirectory 'OtherFees.Api.exe')).Path
$taskRunDirectory = Join-Path $taskRoot ('artifacts/export-smoke/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRunDirectory -Force | Out-Null
$taskData = Join-Path $taskRunDirectory 'export.sqlite'
$taskBaseUrl = 'http://localhost:' + $Port + '/'
$taskReport = New-Object System.Collections.Generic.List[object]
$taskProcess = $null
$taskClient = New-Object System.Net.Http.HttpClient
$taskClient.Timeout = [TimeSpan]::FromSeconds(15)
function Start-ExportApi {
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
function Stop-ExportApi {
    if ($null -ne $taskProcess) {
        if (-not $taskProcess.HasExited) {
            if ($taskProcess.Path -ne $taskExe) { throw 'Unexpected API process identity.' }
            Stop-Process -Id $taskProcess.Id
            $taskProcess.WaitForExit()
        }
        $taskProcess.Dispose(); $script:taskProcess = $null
    }
}
function Send-ExportRequest([string]$Method, [string]$Path, [int]$Expected, [string]$Check, [string]$InputFile = '', [string]$Json = '', [string]$SaveFile = '') {
    $taskRequest = New-Object System.Net.Http.HttpRequestMessage
    $taskRequest.Method = [Net.Http.HttpMethod]::new($Method)
    $taskRequest.RequestUri = $taskBaseUrl + $Path
    if ($InputFile) {
        $taskMultipart = New-Object System.Net.Http.MultipartFormDataContent
        $taskPart = [Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($InputFile))
        $taskMultipart.Add($taskPart, 'file', [IO.Path]::GetFileName($InputFile)); $taskRequest.Content = $taskMultipart
    } elseif ($Json) { $taskRequest.Content = [Net.Http.StringContent]::new($Json, [Text.Encoding]::UTF8, 'application/json') }
    try {
        $taskResponse = $taskClient.SendAsync($taskRequest).GetAwaiter().GetResult()
        try {
            if ([int]$taskResponse.StatusCode -ne $Expected) { throw ($Check + ': ' + $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()) }
            if ($SaveFile) {
                if ($taskResponse.Content.Headers.ContentType.MediaType -ne 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' -or $taskResponse.Content.Headers.ContentDisposition.DispositionType -ne 'attachment') { throw 'Wrong download headers.' }
                [IO.File]::WriteAllBytes($SaveFile, $taskResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
                $taskBody = [PSCustomObject]@{ File = [IO.Path]::GetFileName($SaveFile); Sha256 = (Get-FileHash -LiteralPath $SaveFile).Hash }
            } else {
                $taskText = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $taskBody = if ($taskText) { $taskText | ConvertFrom-Json } else { $null }
            }
            $taskReport.Add([PSCustomObject]@{ Check = $Check; Status = [int]$taskResponse.StatusCode; Body = $taskBody })
            return $taskBody
        } finally { $taskResponse.Dispose() }
    } finally { $taskRequest.Dispose() }
}
function Read-ZipXml($Zip, [string]$Name) {
    $taskEntry = $Zip.GetEntry($Name)
    if ($null -eq $taskEntry) { return $null }
    $taskReader = [IO.StreamReader]::new($taskEntry.Open())
    try { return [xml]$taskReader.ReadToEnd() } finally { $taskReader.Dispose() }
}
function Read-ExportCells([string]$Path) {
    $taskStream = [IO.File]::OpenRead($Path)
    $taskZip = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        $taskSharedXml = Read-ZipXml $taskZip 'xl/sharedStrings.xml'
        $taskStrings = @()
        if ($taskSharedXml) { $taskStrings = @($taskSharedXml.SelectNodes('//*[local-name()="si"]') | ForEach-Object { ($_.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join '' }) }
        $taskSheet = Read-ZipXml $taskZip 'xl/worksheets/sheet1.xml'
        $taskCells = @{}
        foreach ($taskCell in $taskSheet.SelectNodes('//*[local-name()="sheetData"]/*[local-name()="row"]/*[local-name()="c"]')) {
            $taskValue = $taskCell.SelectSingleNode('./*[local-name()="v"]')
            if ($taskCell.t -eq 's') { $taskCells[$taskCell.r] = $taskStrings[[int]$taskValue.InnerText] }
            elseif ($taskCell.t -eq 'inlineStr') { $taskCells[$taskCell.r] = $taskCell.InnerText }
            elseif ($taskValue) { $taskCells[$taskCell.r] = [decimal]::Parse($taskValue.InnerText, [Globalization.CultureInfo]::InvariantCulture) }
        }
        return $taskCells
    } finally { $taskZip.Dispose(); $taskStream.Dispose() }
}
function Assert-ExportAmounts($Cells, [int]$Row, [decimal]$Income, [decimal]$Cost, [decimal]$Profit) {
    if ($Cells['C' + $Row] -ne $Income -or $Cells['D' + $Row] -ne $Cost -or $Cells['E' + $Row] -ne $Profit) { throw ('Wrong exported amounts at row ' + $Row) }
}
try {
    Start-ExportApi
    $taskImport = 'api/projects/7-51231123/other-fees/import'
    $taskExport = 'api/projects/7-51231123/summary/export'
    $taskZeroExport = 'api/projects/demo-zero/summary/export'
    $taskReportBody = Send-ExportRequest POST $taskImport 200 'original-import' -InputFile $taskSource
    if ($taskReportBody.SuccessCount -ne 2 -or $taskReportBody.CreatedCount -ne 1 -or $taskReportBody.UpdatedCount -ne 1 -or $taskReportBody.FailureCount -ne 0) { throw 'Original import counts differ.' }
    $taskBaseline = Join-Path $taskRunDirectory 'original-import-summary.xlsx'
    $null = Send-ExportRequest GET $taskExport 200 'import-to-export' -SaveFile $taskBaseline
    $taskCells = Read-ExportCells $taskBaseline
    Assert-ExportAmounts $taskCells 10 50 70 -20
    Assert-ExportAmounts $taskCells 12 34558.40 122765.73 -88207.33
    Assert-ExportAmounts $taskCells 13 3110.26 11048.92 -7938.66
    Assert-ExportAmounts $taskCells 14 37668.66 133814.65 -96145.99
    if ($taskCells.A10 -ne 7 -or $taskCells.I10 -ne -0.4) { throw 'Wrong sequence or ratio.' }
    Stop-ExportApi
    Start-ExportApi
    $taskRestart = Join-Path $taskRunDirectory 'restart-summary.xlsx'
    $null = Send-ExportRequest GET $taskExport 200 'restart-export' -SaveFile $taskRestart
    Assert-ExportAmounts (Read-ExportCells $taskRestart) 14 37668.66 133814.65 -96145.99
    $taskZero = Join-Path $taskRunDirectory 'zero-summary.xlsx'
    $null = Send-ExportRequest GET $taskZeroExport 200 'project-isolation' -SaveFile $taskZero
    Assert-ExportAmounts (Read-ExportCells $taskZero) 13 0 0 0
    $taskCreated = Send-ExportRequest POST 'api/other-fees' 201 'create-521-520' -Json '{"ProjectId":"demo-zero","Name":"manual","IncomePrice":521,"CostPrice":520}'
    $null = Send-ExportRequest GET $taskZeroExport 200 'manual-to-export' -SaveFile $taskZero
    $taskCells = Read-ExportCells $taskZero
    Assert-ExportAmounts $taskCells 13 46.89 46.80 0.09
    if ([math]::Abs([double]$taskCells.I10 - 1.0 / 521) -gt 0.000000001) { throw 'Wrong small-profit ratio.' }
    $taskNull = Send-ExportRequest POST 'api/other-fees' 201 'create-null-income' -Json '{"ProjectId":"demo-zero","Name":"empty","IncomePrice":null,"CostPrice":25}'
    $null = Send-ExportRequest GET $taskZeroExport 200 'null-income-export' -SaveFile $taskZero
    $taskCells = Read-ExportCells $taskZero
    if ($taskCells.ContainsKey('C11') -or $taskCells.E11 -ne -25 -or $taskCells.I11 -ne 0) { throw 'Null income was not preserved.' }
    $null = Send-ExportRequest PUT ('api/other-fees/' + $taskCreated.Id) 200 'update-persisted-fee' -Json '{"Name":"renamed","IncomePrice":50,"CostPrice":70,"Remark":"updated"}'
    $null = Send-ExportRequest GET $taskZeroExport 200 'updated-export' -SaveFile $taskZero
    $taskCells = Read-ExportCells $taskZero
    Assert-ExportAmounts $taskCells 10 50 70 -20
    if ($taskCells.B10 -ne 'renamed' -or $taskCells.J10 -ne 'updated') { throw 'Updated fields missing.' }
    $null = Send-ExportRequest DELETE ('api/other-fees/' + $taskCreated.Id) 204 'delete-persisted-fee'
    $null = Send-ExportRequest GET $taskZeroExport 200 'deleted-export' -SaveFile $taskZero
    $taskCells = Read-ExportCells $taskZero
    if ($taskCells.B10 -ne 'empty' -or $taskCells.A10 -ne 7) { throw 'Deleted fee still exported or sequence not continuous.' }
    $taskMissing = Send-ExportRequest GET 'api/projects/missing/summary/export' 404 'missing-project'
    if ($taskMissing.Code -ne 'PROJECT_NOT_FOUND') { throw 'Wrong missing-project response.' }
    $taskRoundtrip = Send-ExportRequest POST $taskImport 200 'export-to-import' -InputFile $taskBaseline
    if ($taskRoundtrip.SuccessCount -ne 1 -or $taskRoundtrip.UpdatedCount -ne 1 -or $taskRoundtrip.FailureCount -ne 0) { throw 'Roundtrip failed.' }
    $null = Send-ExportRequest GET $taskExport 200 'roundtrip-export' -SaveFile (Join-Path $taskRunDirectory 'roundtrip-summary.xlsx')
    if ((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -ne $taskOriginalHash) { throw 'Original file changed.' }
    $taskReport.ToArray() | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskRunDirectory 'export-http-smoke.json') -Encoding UTF8
    Write-Output ('PASS: ' + $taskReport.Count + ' HTTP checks; original SHA256 ' + $taskOriginalHash + '; report ' + $taskRunDirectory)
} finally { Stop-ExportApi; $taskClient.Dispose() }
