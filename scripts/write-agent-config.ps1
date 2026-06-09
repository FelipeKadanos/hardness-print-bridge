param(
    [string]$ConfigPath = "",
    [string]$QueueRootPath = "C:\Hardness-Print-Brige\print-agent",
    [string]$DefaultPrinterName = "Microsoft Print to PDF",
    [string]$RemoteListUrl = "http://localhost/api/rel/list_files?API_AUTH=REPLACE_ME",
    [string]$RemoteDownloadUrlTemplate = "http://localhost/api/rel/select_file?API_AUTH=REPLACE_ME&file={fileName}",
    [string]$HardnessCallbackUrl = "http://localhost/api/rel/callback?API_AUTH=REPLACE_ME",
    [string]$ApiAuthToken = "",
    [bool]$RemoteSourceEnabled = $true
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $programData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    $ConfigPath = Join-Path $programData "HardnessPrintBridge\config\agent-settings.json"
}

$queueRootPath = $QueueRootPath.Trim().TrimEnd('\', '/')
if ([string]::IsNullOrWhiteSpace($queueRootPath)) {
    throw "QueueRootPath cannot be empty."
}

$configDirectory = Split-Path -Parent $ConfigPath
if (-not [string]::IsNullOrWhiteSpace($configDirectory)) {
    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
}

$printBridge = [ordered]@{
    QueueRootPath = $queueRootPath
    WatchPath = Join-Path $queueRootPath "inbox"
    ProcessingPath = Join-Path $queueRootPath "processing"
    PrintedPath = Join-Path $queueRootPath "printed"
    ErrorPath = Join-Path $queueRootPath "error"
    DefaultPrinterName = $DefaultPrinterName
    RemoteListUrl = $RemoteListUrl
    RemoteDownloadUrlTemplate = $RemoteDownloadUrlTemplate
    HardnessCallbackUrl = $HardnessCallbackUrl
    ApiAuthToken = $ApiAuthToken
    RemoteSourceEnabled = $RemoteSourceEnabled
}

$document = [ordered]@{
    PrintBridge = $printBridge
}

$json = $document | ConvertTo-Json -Depth 5
Set-Content -LiteralPath $ConfigPath -Value $json -Encoding UTF8

Write-Host "Agent configuration written to '$ConfigPath'."
