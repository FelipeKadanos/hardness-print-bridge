# Hardness Print Bridge

<div align="center">

### Microserviço Windows para impressão automática de `.etq` via RAW spooler

[![Status](https://img.shields.io/badge/status-MVP%20em%20desenvolvimento-0a7ea4?style=for-the-badge)](#status)
[![Plataforma](https://img.shields.io/badge/plataforma-Windows-0078D4?style=for-the-badge&logo=windows)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet)](#setup-rápido)

</div>

---

## Visão rápida

O **Hardness Print Bridge** conecta o Hardness às impressoras do cliente via fila local:

`inbox -> processing -> printed/error`

## Setup rápido

```powershell
git clone <url-do-repo>
cd hardness-print-bridge
dotnet restore Hardness.PrintBridge.slnx
dotnet build Hardness.PrintBridge.slnx -c Debug
dotnet run --project src/Hardness.PrintBridge.Agent
```

## Configuração local

```powershell
Copy-Item src/Hardness.PrintBridge.Agent/appsettings.Local.example.json src/Hardness.PrintBridge.Agent/appsettings.Local.json
```

Ajuste no `appsettings.Local.json`:

- `WatchPath`, `ProcessingPath`, `PrintedPath`, `ErrorPath`
- `DefaultPrinterName`
- `HardnessCallbackUrl`, `HardnessCallbackToken` (se aplicável)

## Publicação (Release)

```powershell
dotnet publish src/Hardness.PrintBridge.Agent/Hardness.PrintBridge.Agent.csproj -c Release -o .\publish
```

## Instalação como Windows Service

Executar PowerShell como Administrador:

```powershell
.\scripts\install-service.ps1 -ExecutablePath .\publish\Hardness.PrintBridge.Agent.exe
```

Remover serviço:

```powershell
.\scripts\uninstall-service.ps1
```

Comandos úteis:

```powershell
Get-Service HardnessPrintBridgeAgent
Start-Service HardnessPrintBridgeAgent
Stop-Service HardnessPrintBridgeAgent
Restart-Service HardnessPrintBridgeAgent
```

## Permissões recomendadas da conta do serviço

A conta que executa o serviço precisa de:

- leitura/escrita em `WatchPath`, `ProcessingPath`, `PrintedPath`, `ErrorPath`
- leitura/escrita em `logs/` (ou no diretório de log configurado)
- acesso às impressoras alvo no Windows

## Estrutura

```txt
src/
  Hardness.PrintBridge.Agent/
    Configuration/
    Domain/
    Application/
    Infrastructure/
      Callback/
      Printing/
```

## Status

Fases 0 a 5 implementadas no backlog técnico.  
Referências:

- [mvp_servico_impressao.md](./mvp_servico_impressao.md)
- [backlog.md](./backlog.md)
