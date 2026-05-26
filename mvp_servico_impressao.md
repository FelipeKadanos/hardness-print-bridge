# MVP Microserviço de Impressão (Ponte Hardness)

## 1) Objetivo
Criar um agente local Windows que faça a ponte entre Hardness e impressoras do cliente, com:

- impressão automática sem navegador
- fila robusta com estados claros
- callback de sucesso/erro para o Hardness

## 2) Origem dos jobs (etapa atual)

O agente suporta dois modos:

1. **Fila local (filesystem)**  
   Lê `.etq` da `inbox` local/compartilhada.

2. **Coleta remota via API dedicada (pull HTTP)**  
   Busca lista de arquivos no servidor Hardness, baixa os `.etq`, salva na `inbox` local e processa no mesmo pipeline.

Endpoints remotos (exemplo):
- Listagem: `GET /api/rel/list_files?...`
- Download: `GET /api/rel/select_file?...&arquivo={fileName}`

Referência de contrato (exemplo real do Hardness):
- [exemplo_endpoints.php](./exemplo_endpoints.php)

## 3) Formato de payload

`.etq` com bytes numéricos separados por espaço.  
O agente converte para `byte[]` e envia em RAW para spooler Windows.

## 4) Fluxo operacional

1. (Opcional) coleta remota via API e grava arquivos novos na `inbox`.
2. Move arquivo para `processing` (atômico).
3. Faz parse `.etq` + resolve impressora (solicitada/padrão).
4. Envia RAW para impressora.
5. Sucesso: move para `printed` + callback `success`.
6. Falha: move para `error` + callback `error`.

## 5) Regras de consistência

- nunca imprimir direto da `inbox`
- sempre passar por `processing`
- não perder arquivo em erro
- dedupe por nome/localização (`inbox/processing/printed/error`)
- sem fallback silencioso de impressora
- no modo remoto, usar cache local de vistos para evitar reingestão

## 6) Seleção de impressora

1. se vier impressora no job, tentar ela
2. se não vier, usar padrão
3. se não existir/offline/paused/erro, job vai para `error`

## 7) Callback para Hardness

Payload mínimo:
- `arquivo`
- `acao` (valor padrão no agente: `impressao`)
- `status` (`success`/`error`)
- `mensagem` (mensagem final de sucesso/erro)

Com retry simples no envio HTTP.

## 8) Configuração mínima

- `WatchPath`, `ProcessingPath`, `PrintedPath`, `ErrorPath`
- `DefaultPrinterName`
- `PollIntervalMs`
- `HardnessCallbackUrl`

Modo remoto:
- `RemoteSourceEnabled`
- `RemoteListUrl`
- `RemoteDownloadUrlTemplate`
- `RemotePollIntervalMs`
- `RemoteTimeoutMs`
- `RemoteMaxFilesPerCycle`
- `RemoteAllowInsecureTls` (homolog)
- `RemoteSeenCachePath`

## 9) Critérios de aceite MVP

1. `.etq` válido sem impressora específica -> `printed` + callback success
2. `.etq` válido com impressora específica existente -> `printed` + callback success
3. impressora inexistente/offline -> `error` + callback error
4. `.etq` inválido -> `error` + callback error
5. reinício do serviço recupera pendentes de `processing`
6. serviço inicia com Windows

## 10) Fora de escopo (MVP)

- painel administrativo
- entrada de jobs por API pública do agente (push direto do ERP)
- políticas avançadas de reprocessamento
