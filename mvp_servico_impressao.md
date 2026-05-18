# MVP Microserviço de Impressão (Ponte Hardness)

## 1) Objetivo
Criar um microserviço local (Windows) que funcione como **ponte de impressão** entre o Hardness e impressoras instaladas no cliente.

Este MVP deve resolver:
- Impressão automática sem depender de navegador.
- Processamento robusto de fila.
- Separação clara entre sucesso e erro.
- Uso de impressora padrão configurada no agente.
- Possibilidade de o Hardness solicitar impressão em impressora específica.
- Retorno de status para o Hardness quando a impressão falhar.

---

## 2) Decisões do MVP

### 2.1 Origem da fila
**MVP v1: leitura por pasta compartilhada** (pull local), com metadados no arquivo/nome para roteamento.

Pasta de entrada (ERP):  
`dados_usuarios/{empresa}/tmp/impressao/`

Observação:
- Endpoint HTTP de envio pode entrar no v2, se necessário.
- No v1, a devolutiva de status para o Hardness será por callback HTTP (somente resultado), sem envio do conteúdo da impressão.

### 2.2 Formato do payload
Manter formato atual do `.etq` do Hardness para etiquetas:
- conteúdo em bytes representados por números separados por espaço;
- o microserviço converte para `byte[]` e envia em modo RAW para impressora.

Extensibilidade:
- O agente deve ser preparado para outros tipos de impressão no futuro (ex.: texto bruto/PDF), mas no MVP a implementação obrigatória é `.etq` RAW.

### 2.3 Estratégia de confirmação
Após tentativa de impressão:
- **Sucesso**: mover arquivo para `impresso/`.
- **Falha**: mover arquivo para `erro/`.

No MVP, além do estado do arquivo, o agente deve notificar o Hardness via callback HTTP:
- `status=success` quando imprimir;
- `status=error` quando falhar;
- informar impressora solicitada, impressora utilizada e mensagem de erro.

### 2.4 Seleção de impressora
- O agente terá uma **impressora padrão** configurada.
- O Hardness pode informar uma **impressora específica** por job.
- Regra:
  1. Se vier impressora no job, tentar essa impressora.
  2. Se não vier, usar impressora padrão.
  3. Se a impressora solicitada não existir/offline, marcar erro e retornar ao Hardness (sem fallback silencioso).

### 2.5 Execução no cliente
Rodar como **serviço Windows** (sem interface gráfica), com inicialização automática.

---

## 3) Estrutura de diretórios (no cliente)

Base local do serviço (exemplo):
`C:\hardness\print-agent\`

Subpastas:
- `inbox\` (entrada monitorada; pode apontar para pasta compartilhada do ERP)
- `processing\`
- `printed\`
- `error\`
- `logs\`
- `meta\` (opcional no MVP, para registrar metadados do job quando necessário)

Observação:
- Se a entrada for a pasta do ERP, o fluxo deve fazer move atômico para `processing` antes de imprimir.

---

## 4) Fluxo operacional

1. Serviço varre `inbox` a cada N segundos (ex.: 2s).
2. Para cada job de impressão:
   - move para `processing`;
   - lê conteúdo e metadados (incluindo impressora solicitada, se houver);
   - resolve impressora (solicitada ou padrão);
   - converte para bytes (caso `.etq`);
   - envia para impressora.
3. Se imprimir:
   - move para `printed`;
   - registra log de sucesso;
   - envia callback de sucesso para o Hardness.
4. Se falhar:
   - move para `error`;
   - registra motivo no log;
   - envia callback de falha para o Hardness.

---

## 5) Regras de segurança e consistência

- Nunca imprimir arquivo direto da `inbox`.
- Sempre mover para `processing` antes de processar (evita dupla execução).
- Não apagar arquivo em caso de erro.
- Garantir idempotência por nome de arquivo (não reimprimir o mesmo arquivo sem ação manual).
- Se impressora especificada não for encontrada, não substituir automaticamente por padrão.

---

## 6) Logs mínimos obrigatórios

Para cada arquivo:
- timestamp;
- nome do arquivo;
- impressora solicitada;
- impressora utilizada;
- status final (`printed` ou `error`);
- mensagem de erro (quando houver).

Arquivo de log:
- rotação diária ou por tamanho (definir na implementação).

---

## 7) Configuração do serviço (arquivo .env/.json)

Parâmetros mínimos:
- `watch_path`
- `processing_path`
- `printed_path`
- `error_path`
- `printer_name`
- `default_printer_name`
- `poll_interval_ms`
- `log_level`
- `hardness_callback_url`
- `hardness_callback_token` (se aplicável)

---

## 8) Critérios de aceite do MVP

1. Ao colocar um `.etq` válido na fila sem impressora especificada, ele imprime na padrão e vai para `printed`.
2. Ao colocar um `.etq` válido com impressora especificada existente, imprime nessa impressora e vai para `printed`.
3. Se a impressora especificada não existir/offline, o job vai para `error` e o Hardness recebe callback com falha.
4. Ao colocar um `.etq` inválido, ele vai para `error` com log e callback explicando a falha.
5. Reiniciar o serviço não causa perda de arquivos pendentes.
6. Serviço sobe automaticamente com o Windows.

---

## 9) Fora do escopo (MVP)

- Painel web de administração.
- API pública de impressão (entrada de jobs via HTTP).
- Reprocessamento automático com política avançada.

---

## 10) Próximo passo para codificação

Com este MVP fechado, o próximo passo é criar o repositório com:
- esqueleto do serviço;
- módulo de watcher de arquivos;
- módulo de roteamento de impressora (padrão vs especificada);
- módulo de impressão RAW;
- módulo de callback para Hardness;
- módulo de logs e movimentação de fila;
- arquivo de configuração e instruções de instalação como serviço Windows.
