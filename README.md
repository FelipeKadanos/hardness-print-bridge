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

Arquitetura atual:

- `Hardness.PrintBridge.Agent`: nucleo de impressao, autonomo, executavel sozinho e instalavel como Windows Service
- `Hardness.PrintBridge.App`: aplicativo Windows com tray, configuracoes locais, checagem de atualizacao e orquestracao leve
- `Hardness.PrintBridge.Updater`: executavel separado para aplicar atualizacoes com backup e rollback

## Setup rápido

```powershell
git clone <url-do-repo>
cd hardness-print-bridge
dotnet restore Hardness.PrintBridge.slnx
dotnet build Hardness.PrintBridge.slnx -c Debug
dotnet run --project src/Hardness.PrintBridge.Agent
```

## Logs do Agent

- todos os logs do `Agent` são gravados em `DIR\logs\agent.log`
- em desenvolvimento pelo repositório, o caminho fica em `hardness-print-bridge\logs\agent.log`
- fora do repositório, `DIR` = pasta onde o `Hardness.PrintBridge.Agent.exe` está rodando
- o caminho do log não é configurável
- quando `agent.log` passa de `10 MB`, o próprio arquivo é truncado e continua sendo reutilizado

## Configuração local

```powershell
Copy-Item src/Hardness.PrintBridge.Agent/appsettings.Local.example.json src/Hardness.PrintBridge.Agent/appsettings.Local.json
```

### Modo 1: fila local (filesystem)

- `WatchPath`, `ProcessingPath`, `PrintedPath`, `ErrorPath`
- `DefaultPrinterName`

Exemplo de paths padrão usados nos arquivos de configuração de desenvolvimento:

```json
"WatchPath": "C:\\Hardness-Print-Brige\\print-agent\\inbox",
"ProcessingPath": "C:\\Hardness-Print-Brige\\print-agent\\processing",
"PrintedPath": "C:\\Hardness-Print-Brige\\print-agent\\printed",
"ErrorPath": "C:\\Hardness-Print-Brige\\print-agent\\error",
"DefaultPrinterName": "Microsoft Print to PDF"
```

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
"RemoteDownloadUrlTemplate": "http://localhost/api/rel/select_file?API_AUTH=REPLACE_ME&file={fileName}"
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

Build completo da solucao:

```powershell
dotnet build Hardness.PrintBridge.slnx -c Debug
```

Execucao independente do `Agent`:

```powershell
dotnet run --project src/Hardness.PrintBridge.Agent
```

Execucao do `App` desktop/tray:

```powershell
dotnet run --project src/Hardness.PrintBridge.App
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
- a aba `Logs` do `App` acompanha em tempo real o arquivo fixo `DIR\logs\agent.log`

## Status do Agent

- o `Agent` publica snapshots de status em `ProgramData\HardnessPrintBridge\status\agent-status.json`
- o `App` consome esse status via `IAgentStatusSource`
- a implementacao atual usa JSON, mas a leitura ficou abstraida para futura troca por Named Pipes

## Atualizacao automatica

- o `App` verifica novas releases do GitHub ao iniciar
- o `App` revalida periodicamente a cada 6 horas
- a instalacao da atualizacao acontece somente com confirmacao do usuario
- o `Updater` aplica o pacote fora do processo principal, com backup e tentativa de rollback

## Versionamento e releases

- a fonte unica da verdade da versao e a Git Tag semantica (`v1.2.3`)
- o GitHub Actions injeta a versao da tag no build
- a GitHub Release e criada automaticamente a partir da mesma tag

## Instalador

- tecnologia escolhida: `Inno Setup`
- script do instalador: [installer/HardnessPrintBridge.iss](./installer/HardnessPrintBridge.iss)
- pipeline de release: [.github/workflows/release.yml](./.github/workflows/release.yml)

## Estrutura

```txt
src/
  Hardness.PrintBridge.Contracts/
  Hardness.PrintBridge.App/
  Hardness.PrintBridge.Agent/
    Configuration/
    Domain/
    Application/
    Infrastructure/
      Callback/
      Printing/
      Queue/
  Hardness.PrintBridge.Updater/
```

## Status

MVP implementado com fluxo local + coleta remota por API dedicada.

Referências:

- [mvp_servico_impressao.md](./mvp_servico_impressao.md)
- [backlog.md](./backlog.md)
