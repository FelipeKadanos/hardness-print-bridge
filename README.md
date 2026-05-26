# Hardness Print Bridge

<div align="center">

### Microserviço Windows para impressão automática de `.etq` via RAW spooler

[![Status](https://img.shields.io/badge/status-MVP%20em%20desenvolvimento-0a7ea4?style=for-the-badge)](#status)
[![Plataforma](https://img.shields.io/badge/plataforma-Windows-0078D4?style=for-the-badge&logo=windows)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet)](#setup-rápido)

</div>

---

## Visão rápida

O **Hardness Print Bridge** conecta o Hardness às impressoras do cliente com dois modos de entrada:

- **Fila local**: `inbox -> processing -> printed/error`
- **Coleta remota**: o agente busca `.etq` via API HTTP, salva na `inbox` e usa o mesmo pipeline local

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

### Modo 1: fila local (filesystem)

- `WatchPath`, `ProcessingPath`, `PrintedPath`, `ErrorPath`
- `DefaultPrinterName`

### Modo 2: coleta remota (API dedicada)

- `RemoteSourceEnabled = true`
- `RemoteListUrl`
- `RemoteDownloadUrlTemplate` (usa `{fileName}`)
- `RemotePollIntervalMs`, `RemoteTimeoutMs`, `RemoteMaxFilesPerCycle`
- `RemoteSeenCachePath` (cache de dedupe local)

Contrato de referência dos endpoints Hardness:
- [exemplo_endpoints.php](./exemplo_endpoints.php)

Exemplo com endpoints reais:

```json
"RemoteSourceEnabled": true,
"RemoteListUrl": "http://localhost/api/rel/list_files?API_AUTH=REPLACE_ME",
"RemoteDownloadUrlTemplate": "http://localhost/api/rel/select_file?API_AUTH=REPLACE_ME&arquivo={fileName}"
```

### Callback para Hardness

- `HardnessCallbackUrl`
- Payload enviado pelo agente (JSON):
  - `arquivo`
  - `acao` (valor padrão: `impressao`)
  - `status` (`success` ou `error`)
  - `mensagem` (detalhe de sucesso/erro)

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

## Operação e deduplicação

- arquivos remotos são baixados para `inbox` com escrita atômica
- arquivos já existentes em `inbox/processing/printed/error` são ignorados
- cache local de vistos (`RemoteSeenCachePath`) evita reingestão

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
      Queue/
```

## Status

MVP implementado com fluxo local + coleta remota por API dedicada.

Referências:

- [mvp_servico_impressao.md](./mvp_servico_impressao.md)
- [backlog.md](./backlog.md)
