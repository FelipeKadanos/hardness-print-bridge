param(
    [string]$ServiceName = "HardnessPrintBridgeAgent"
)

$ErrorActionPreference = "Stop"

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $existingService) {
    Write-Host "Service '$ServiceName' not found. Nothing to remove."
    exit 0
}

if ($existingService.Status -ne "Stopped") {
    Write-Host "Stopping service '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
}

sc.exe delete $ServiceName | Out-Null
Write-Host "Service '$ServiceName' removed successfully."
