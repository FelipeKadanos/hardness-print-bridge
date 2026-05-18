param(
    [string]$ServiceName = "HardnessPrintBridgeAgent",
    [string]$DisplayName = "Hardness Print Bridge Agent",
    [string]$Description = "Microservico de impressao do Hardness (fila local para spooler Windows).",
    [string]$ExecutablePath = ".\publish\Hardness.PrintBridge.Agent.exe",
    [string]$StartupType = "Automatic"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Executable not found at '$ExecutablePath'. Publish the project first."
}

$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    throw "Service '$ServiceName' already exists. Remove it first with uninstall-service.ps1."
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName "`"$resolvedExecutablePath`"" `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType $StartupType

Write-Host "Service '$ServiceName' installed successfully."
Write-Host "Starting service..."
Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' is running."
