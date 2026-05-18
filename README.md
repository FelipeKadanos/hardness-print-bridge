# Hardness Print Bridge

<div align="center">

### Microserviço Windows para impressão automática de `.etq` via RAW spooler

[![Status](https://img.shields.io/badge/status-MVP%20em%20desenvolvimento-0a7ea4?style=for-the-badge)](#status)
[![Plataforma](https://img.shields.io/badge/plataforma-Windows-0078D4?style=for-the-badge&logo=windows)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet)](#setup-rápido)

</div>

---

## Visão rápida

O **Hardness Print Bridge** conecta o Hardness às impressoras do cliente sem depender de navegador.

Fluxo principal:

`inbox -> processing -> printed/error`

## Stack atual

- C# / .NET 10 (Worker Service)
- Serilog (console + arquivo com rotação diária)
- Config via `appsettings.json` + `appsettings.Development.json` + `appsettings.Local.json`

## Setup rápido

Pré-requisitos:

- Windows
- .NET SDK 10 instalado

Clone e execução:

```powershell
git clone <url-do-repo>
cd hardness-print-bridge
dotnet restore Hardness.PrintBridge.slnx
dotnet build Hardness.PrintBridge.slnx -c Debug
dotnet run --project src/Hardness.PrintBridge.Agent
```

## Configuração local

O arquivo local não é versionado. Após clonar, crie assim:

```powershell
Copy-Item src/Hardness.PrintBridge.Agent/appsettings.Local.example.json src/Hardness.PrintBridge.Agent/appsettings.Local.json
```

Depois ajuste no `appsettings.Local.json`:

- paths de fila (`WatchPath`, `ProcessingPath`, `PrintedPath`, `ErrorPath`)
- `DefaultPrinterName`
- `HardnessCallbackUrl` e `HardnessCallbackToken` (se aplicável)

Arquivos:

- versionado: `src/Hardness.PrintBridge.Agent/appsettings.Local.example.json`
- local (ignorado): `src/Hardness.PrintBridge.Agent/appsettings.Local.json`

## Estrutura

```txt
src/
  Hardness.PrintBridge.Agent/
    Configuration/
    Domain/
    Application/
    Infrastructure/
      Queue/
      Printing/
      Callback/
```

## Roadmap

- [x] Fase 0: base do projeto, config, validação e logging
- [ ] Fase 1: núcleo da fila (`inbox -> processing -> printed/error`)
- [ ] Fase 2: parser `.etq`, resolução de impressora e impressão RAW
- [ ] Fase 3: callback HTTP com retry
- [ ] Fase 4: resiliência e retomada
- [ ] Fase 5: empacotamento como Windows Service
- [ ] Fase 6: testes de aceite

## Status

Projeto em desenvolvimento do MVP, com bootstrap e base técnica prontos.

Documentos de referência:

- [mvp_servico_impressao.md](./mvp_servico_impressao.md)
- [backlog.md](./backlog.md)
