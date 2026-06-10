# Backlog Técnico v0 — MVP Microserviço de Impressão

## Fase 0 — Base do projeto
- [x] Inicializar repositório do serviço Windows (estrutura de pastas e projeto).
- [x] Definir arquivo de configuração (`.env` ou `appsettings.json`) com todas as chaves do MVP.
- [x] Criar bootstrap da aplicação com leitura de config + validação de configuração obrigatória.
- [x] Estruturar logging (console + arquivo fixo único `logs\agent.log`).

## Fase 1 — Núcleo da fila
- [x] Implementar scanner/poller da `inbox` com intervalo configurável.
- [x] Implementar `move` atômico `inbox -> processing` antes de qualquer leitura.
- [x] Implementar controle de idempotência por nome de arquivo (evitar reimpressão).
- [x] Implementar movimentação final para `printed` e `error`.

## Fase 2 — Domínio de impressão
- [x] Definir contrato interno de `PrintJob` (arquivo, conteúdo, impressora solicitada, metadados).
- [x] Implementar parser `.etq` (string de números -> `byte[]`) com validações.
- [x] Implementar resolvedor de impressora (solicitada vs padrão, sem fallback silencioso).
- [x] Implementar adaptador de impressão RAW para Windows spooler.
- [x] Implementar tratamento de exceções de impressão com mensagens úteis.

## Fase 3 — Integração Hardness
- [x] Implementar cliente HTTP de callback (`success/error`).
- [x] Incluir autenticação por token no callback (se configurado).
- [x] Garantir payload mínimo de callback Hardness: `arquivo`, `acao`, `status`, `mensagem`.
- [x] Implementar retry simples de callback (ex.: 3 tentativas com backoff curto) sem reimprimir.

## Fase 4 — Resiliência operacional
- [x] Garantir retomada segura após restart (processar pendentes em `processing`).
- [x] Criar política para arquivo inválido (`error` + log + callback).
- [x] Garantir que falha de callback não altere resultado da impressão (estado de impressão prevalece).
- [x] Implementar health logs de ciclo (polling ativo, contagem processada, falhas).

## Fase 5 — Serviço Windows e entrega
- [x] Empacotar para execução como Windows Service com auto-start.
- [x] Criar script de instalação/remoção do serviço.
- [x] Documentar configuração de diretórios e permissões da conta do serviço (`C:\Hardness-Print-Brige\print-agent\...`).
- [x] Documentar procedimento de operação e troubleshooting básico.

## Fase 6 — Testes de aceite do MVP
- [x] Teste: `.etq` válido sem impressora especificada -> imprime na padrão + `printed` + callback success.
- [x] Teste: `.etq` válido com impressora existente -> imprime na solicitada + `printed`.
- [x] Teste: impressora inexistente/offline -> `error` + callback error.
- [x] Teste: `.etq` inválido -> `error` + log + callback error.
- [x] Teste: restart do serviço sem perder pendências.
- [x] Teste: inicialização automática com Windows.

## Fase 7 — Coleta remota por API dedicada
- [x] Adicionar `RemoteJobFetcher` para listar e baixar `.etq` de endpoint HTTP.
- [x] Integrar coleta remota no ciclo do worker antes do processamento local.
- [x] Implementar deduplicação local por presença de arquivo e cache `remote-seen.json`.
- [x] Implementar escrita atômica na `inbox` para arquivos baixados.
- [x] Adicionar configuração remota (`Remote*`) e validações de startup.
- [x] Adicionar observabilidade de ciclo remoto (baixados, ignorados, falhas, backoff).

## Fase 8 — App desktop, tray e distribuição
- [x] Adicionar projeto compartilhado para contratos leves entre `Agent`, `App` e `Updater`.
- [x] Publicar status do `Agent` em JSON em `LocalAppData`.
- [x] Adicionar `App` Windows com tray, leitura de status por abstração e preferências locais.
- [x] Adicionar `Updater` separado com backup e tentativa de rollback.
- [x] Adicionar workflow de GitHub Actions baseado em Git Tag como fonte de verdade da versão.
- [x] Adicionar script de instalador Inno Setup.
