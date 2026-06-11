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
    $ConfigPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\appsettings.json"))
}

$queueRootPath = $QueueRootPath.Trim().TrimEnd('\', '/')
if ([string]::IsNullOrWhiteSpace($queueRootPath)) {
    throw "QueueRootPath cannot be empty."
}

$configDirectory = Split-Path -Parent $ConfigPath
if (-not [string]::IsNullOrWhiteSpace($configDirectory)) {
    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
}

$document = [ordered]@{}
if (Test-Path -LiteralPath $ConfigPath) {
    $existing = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json -AsHashtable
    if ($existing) {
        $document = [ordered]@{}
        foreach ($key in $existing.Keys) {
            $document[$key] = $existing[$key]
        }
    }
}

$existingPrintBridge = $null
if ($document.ContainsKey("PrintBridge")) {
    $existingPrintBridge = $document["PrintBridge"]
}

$printBridge = [ordered]@{
    QueueRootPath = $queueRootPath
    WatchPath = Join-Path $queueRootPath "inbox"
    ProcessingPath = Join-Path $queueRootPath "processing"
    PrintedPath = Join-Path $queueRootPath "printed"
    ErrorPath = Join-Path $queueRootPath "error"
    PrinterName = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("PrinterName")) { $existingPrintBridge["PrinterName"] } else { "" }
    DefaultPrinterName = $DefaultPrinterName
    PollIntervalMs = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("PollIntervalMs")) { $existingPrintBridge["PollIntervalMs"] } else { 10000 }
    RemoteListUrl = $RemoteListUrl
    RemoteDownloadUrlTemplate = $RemoteDownloadUrlTemplate
    RemotePollIntervalMs = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("RemotePollIntervalMs")) { $existingPrintBridge["RemotePollIntervalMs"] } else { 10000 }
    RemoteTimeoutMs = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("RemoteTimeoutMs")) { $existingPrintBridge["RemoteTimeoutMs"] } else { 10000 }
    RemoteMaxFilesPerCycle = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("RemoteMaxFilesPerCycle")) { $existingPrintBridge["RemoteMaxFilesPerCycle"] } else { 20 }
    RemoteAllowInsecureTls = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("RemoteAllowInsecureTls")) { $existingPrintBridge["RemoteAllowInsecureTls"] } else { $true }
    RemoteSeenCachePath = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("RemoteSeenCachePath")) { $existingPrintBridge["RemoteSeenCachePath"] } else { "meta\remote-seen.json" }
    LogLevel = if ($existingPrintBridge -and $existingPrintBridge.ContainsKey("LogLevel")) { $existingPrintBridge["LogLevel"] } else { "Information" }
    HardnessCallbackUrl = $HardnessCallbackUrl
    ApiAuthToken = $ApiAuthToken
    RemoteSourceEnabled = $RemoteSourceEnabled
}

$document["PrintBridge"] = $printBridge

if (-not $document.ContainsKey("App")) {
    $document["App"] = [ordered]@{
        InstallPath = [System.IO.Path]::GetDirectoryName($ConfigPath)
        StartWithWindows = $true
        CheckForUpdatesOnStartup = $true
        UpdateCheckIntervalHours = 6
        MinimizeToTrayOnClose = $true
    }
}

$json = $document | ConvertTo-Json -Depth 5
Set-Content -LiteralPath $ConfigPath -Value $json -Encoding UTF8

Write-Host "Agent configuration written to '$ConfigPath'."
