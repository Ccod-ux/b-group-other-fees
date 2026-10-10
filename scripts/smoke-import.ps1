param(
    [Parameter(Mandatory = $true)][string]$File,
    [switch]$SkipBuild,
    [string]$ApiDirectory,
    [int]$Port = 5092
)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression
$taskSource = (Resolve-Path -LiteralPath $File).Path
$taskOriginalHash = (Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash
if (-not $ApiDirectory) { $ApiDirectory = Join-Path $taskRoot 'src/OtherFees.Api/bin/Release/net48' }
$taskExe = (Resolve-Path -LiteralPath (Join-Path $ApiDirectory 'OtherFees.Api.exe')).Path
$taskRunDirectory = Join-Path $taskRoot ('artifacts/import-smoke/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRunDirectory -Force | Out-Null
$taskData = Join-Path $taskRunDirectory 'import.sqlite'
$taskBaseUrl = 'http://localhost:' + $Port + '/'
$taskReport = New-Object System.Collections.Generic.List[object]
$taskProcess = $null
$taskClient = New-Object System.Net.Http.HttpClient
$taskClient.Timeout = [TimeSpan]::FromSeconds(15)

function Start-ImportApi {
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
function Stop-ImportApi {
    if ($null -ne $taskProcess) {
        if (-not $taskProcess.HasExited) {
            if ($taskProcess.Path -ne $taskExe) { throw 'Unexpected API process identity.' }
            Stop-Process -Id $taskProcess.Id
            $taskProcess.WaitForExit()
        }
        $taskProcess.Dispose()
        $script:taskProcess = $null
    }
}
function Send-ImportRequest([string]$Path, [string]$InputFile, [int]$Expected, [string]$Check, [switch]$JsonBody) {
    $taskRequest = New-Object System.Net.Http.HttpRequestMessage
    $taskRequest.RequestUri = $taskBaseUrl + $Path
    $taskRequest.Method = [Net.Http.HttpMethod]::Get
    if ($InputFile) {
        $taskRequest.Method = [Net.Http.HttpMethod]::Post
        $taskMultipart = New-Object System.Net.Http.MultipartFormDataContent
        $taskBytes = [IO.File]::ReadAllBytes($InputFile)
        $taskPart = [Net.Http.ByteArrayContent]::new($taskBytes)
        $taskMultipart.Add($taskPart, 'file', [IO.Path]::GetFileName($InputFile))
        $taskRequest.Content = $taskMultipart
    }
    if ($JsonBody) {
        $taskRequest.Method = [Net.Http.HttpMethod]::Post
        $taskRequest.Content = [Net.Http.StringContent]::new('{}', [Text.Encoding]::UTF8, 'application/json')
    }
    try {
        $taskResponse = $taskClient.SendAsync($taskRequest).GetAwaiter().GetResult()
        try {
            $taskBody = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $taskReport.Add([PSCustomObject]@{ Check = $Check; ExpectedStatus = $Expected; ActualStatus = [int]$taskResponse.StatusCode; Body = ($taskBody | ConvertFrom-Json) })
            if ([int]$taskResponse.StatusCode -ne $Expected) { throw ($Check + ': ' + $taskBody) }
            return ($taskBody | ConvertFrom-Json)
        } finally { $taskResponse.Dispose() }
    } finally { $taskRequest.Dispose() }
}
function Assert-ImportReport($Report, [int]$Success, [int]$Created, [int]$Updated, [int]$Failed) {
    if ($Report.SuccessCount -ne $Success -or $Report.CreatedCount -ne $Created -or $Report.UpdatedCount -ne $Updated -or $Report.FailureCount -ne $Failed) { throw 'Import counts differ.' }
}
function New-DirtyImportWorkbook([string]$Path, [string]$OriginalName) {
    $taskHeaders = '["\u9879\u76eeId","\u8d39\u7528\u540d\u79f0","\u6536\u5165\u4ef7","\u6210\u672c\u4ef7","\u63cf\u8ff0"]' | ConvertFrom-Json
    $taskRows = @(
        $taskHeaders,
        @('7-51231123', $OriginalName, 999, -1, 'invalid duplicate'),
        @('demo-zero', 'wrong-project', 1, 0, $null),
        @('7-51231123', 'null-income', $null, 25, $null),
        @('7-51231123', 'missing-cost', 10, $null, $null),
        @('7-51231123', 'later-valid', 521, 520, $null)
    )
    $taskXmlRows = [Text.StringBuilder]::new()
    for ($taskIndex = 0; $taskIndex -lt $taskRows.Count; $taskIndex++) {
        $null = $taskXmlRows.Append('<row r="' + ($taskIndex + 1) + '">')
        for ($taskColumn = 0; $taskColumn -lt 5; $taskColumn++) {
            $taskValue = $taskRows[$taskIndex][$taskColumn]
            if ($null -eq $taskValue) { continue }
            $taskReference = [string][char](65 + $taskColumn) + ($taskIndex + 1)
            if ($taskValue -is [string]) {
                $null = $taskXmlRows.Append('<c r="' + $taskReference + '" t="inlineStr"><is><t>' + [Security.SecurityElement]::Escape($taskValue) + '</t></is></c>')
            } else {
                $null = $taskXmlRows.Append('<c r="' + $taskReference + '"><v>' + $taskValue.ToString([Globalization.CultureInfo]::InvariantCulture) + '</v></c>')
            }
        }
        $null = $taskXmlRows.Append('</row>')
    }
    $taskParts = @{
        '[Content_Types].xml' = '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>'
        '_rels/.rels' = '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'
        'xl/workbook.xml' = '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="&#x9879;&#x76ee;&#x6c47;&#x603b;&#x6e05;&#x5355;" sheetId="1" r:id="rId1"/></sheets></workbook>'
        'xl/_rels/workbook.xml.rels' = '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'
        'xl/worksheets/sheet1.xml' = '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>' + $taskXmlRows.ToString() + '</sheetData></worksheet>'
    }
    $taskStream = [IO.File]::Create($Path)
    $taskZip = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskPartName in $taskParts.Keys) {
            $taskWriter = [IO.StreamWriter]::new($taskZip.CreateEntry($taskPartName).Open(), [Text.UTF8Encoding]::new($false))
            try { $taskWriter.Write($taskParts[$taskPartName]) } finally { $taskWriter.Dispose() }
        }
    } finally { $taskZip.Dispose(); $taskStream.Dispose() }
}
try {
    Start-ImportApi
    $taskImportPath = 'api/projects/7-51231123/other-fees/import'
    $taskListPath = 'api/projects/7-51231123/other-fees'
    Assert-ImportReport (Send-ImportRequest $taskImportPath $taskSource 200 'original-first') 2 1 1 0
    $taskList = @(Send-ImportRequest $taskListPath '' 200 'original-final-values')
    if ($taskList.Count -ne 1 -or $taskList[0].IncomePrice -ne 50 -or $taskList[0].CostPrice -ne 70) { throw 'Original file final values differ.' }
    $taskId = $taskList[0].Id
    $taskName = $taskList[0].Name
    Assert-ImportReport (Send-ImportRequest $taskImportPath $taskSource 200 'original-second') 2 0 2 0
    $taskDirtyFile = Join-Path $taskRunDirectory 'dirty-fixture.xlsx'
    New-DirtyImportWorkbook $taskDirtyFile $taskName
    $taskDirtyReport = Send-ImportRequest $taskImportPath $taskDirtyFile 200 'mixed-valid-invalid-rows'
    Assert-ImportReport $taskDirtyReport 2 2 0 3
    if (($taskDirtyReport.Errors.RowNumber -join ',') -ne '2,3,5') { throw 'Wrong Excel error row numbers.' }
    Stop-ImportApi
    Start-ImportApi
    $taskList = @(Send-ImportRequest $taskListPath '' 200 'restart-preserves-imports')
    if ($taskList.Count -ne 3 -or $taskList[0].Id -ne $taskId -or $taskList[0].IncomePrice -ne 50 -or $taskList[0].CostPrice -ne 70 -or $null -ne $taskList[1].IncomePrice -or $taskList[2].Profit -ne 1 -or $taskList[2].ProfitRatePercent -ne 0.19) { throw 'Restart, null income, failed overwrite or final calculation differs.' }
    $taskBroken = Join-Path $taskRunDirectory 'broken.xlsx'
    [IO.File]::WriteAllBytes($taskBroken, [byte[]]@(1, 2, 3))
    $taskError = Send-ImportRequest $taskImportPath $taskBroken 400 'corrupted-workbook'
    if ($taskError.Code -ne 'IMPORT_FILE_INVALID') { throw 'Wrong corrupted file response.' }
    $null = Send-ImportRequest $taskImportPath '' 400 'wrong-content-type' -JsonBody
    $taskOther = @(Send-ImportRequest 'api/projects/demo-zero/other-fees' '' 200 'project-isolation')
    if ($taskOther.Count -ne 0) { throw 'Import changed another project.' }
    if ((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -ne $taskOriginalHash) { throw 'Original workbook changed.' }
    $taskReport.ToArray() | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskRunDirectory 'import-http-smoke.json') -Encoding UTF8
    Write-Output ('PASS: ' + $taskReport.Count + ' HTTP checks; original SHA256 ' + $taskOriginalHash + '; report ' + $taskRunDirectory)
} finally {
    Stop-ImportApi
    $taskClient.Dispose()
}
