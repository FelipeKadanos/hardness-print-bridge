# Backlog Técnico v0 — MVP Microserviço de Impressão

## Fase 0 — Base do projeto
- [x] Inicializar repositório do serviço Windows (estrutura de pastas e projeto).
- [x] Definir arquivo de configuração (`.env` ou `appsettings.json`) com todas as chaves do MVP.
- [x] Criar bootstrap da aplicação com leitura de config + validação de configuração obrigatória.
- [x] Estruturar logging (console + arquivo com rotação).

## Fase 1 — Núcleo da fila
- [x] Implementar scanner/poller da `inbox` com intervalo configurável.
- [x] Implementar `move` atômico `inbox -> processing` antes de qualquer leitura.
- [x] Implementar controle de idempotência por nome de arquivo (evitar reimpressão).
- [x] Implementar movimentação final para `printed` e `error`.

## Fase 2 — Domínio de impressão
- [ ] Definir contrato interno de `PrintJob` (arquivo, conteúdo, impressora solicitada, metadados).
- [ ] Implementar parser `.etq` (string de números -> `byte[]`) com validações.
- [ ] Implementar resolvedor de impressora (solicitada vs padrão, sem fallback silencioso).
- [ ] Implementar adaptador de impressão RAW para Windows spooler.
- [ ] Implementar tratamento de exceções de impressão com mensagens úteis.

## Fase 3 — Integração Hardness
- [ ] Implementar cliente HTTP de callback (`success/error`).
- [ ] Incluir autenticação por token no callback (se configurado).
- [ ] Garantir payload mínimo: arquivo, status, impressora solicitada, utilizada, erro.
- [ ] Implementar retry simples de callback (ex.: 3 tentativas com backoff curto) sem reimprimir.

## Fase 4 — Resiliência operacional
- [ ] Garantir retomada segura após restart (processar pendentes em `processing`).
- [ ] Criar política para arquivo inválido (`error` + log + callback).
- [ ] Garantir que falha de callback não altere resultado da impressão (estado de impressão prevalece).
- [ ] Implementar health logs de ciclo (polling ativo, contagem processada, falhas).

## Fase 5 — Serviço Windows e entrega
- [ ] Empacotar para execução como Windows Service com auto-start.
- [ ] Criar script de instalação/remoção do serviço.
- [ ] Documentar configuração de diretórios e permissões da conta do serviço.
- [ ] Documentar procedimento de operação e troubleshooting básico.

## Fase 6 — Testes de aceite do MVP
- [ ] Teste: `.etq` válido sem impressora especificada -> imprime na padrão + `printed` + callback success.
- [ ] Teste: `.etq` válido com impressora existente -> imprime na solicitada + `printed`.
- [ ] Teste: impressora inexistente/offline -> `error` + callback error.
- [ ] Teste: `.etq` inválido -> `error` + log + callback error.
- [ ] Teste: restart do serviço sem perder pendências.
- [ ] Teste: inicialização automática com Windows.
