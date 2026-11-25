# Custódia: Documentação da API REST Local e Integração com Agentes de IA

- Versão da API: `v1` (`3.0.5`)
- Endereço padrão: `http://127.0.0.1:5080` (somente loopback local)
- Especificação OpenAPI: `GET http://127.0.0.1:5080/api/v1/openapi.json`
- Cópia em PDF: `Custodia-API.pdf` (na raiz do projeto e acoplada em Configurações)

---

## 1. Visão Geral e Segurança

O Custódia roda um servidor **ASP.NET Core Minimal API** dentro do aplicativo desktop, vinculado exclusivamente ao endereço `127.0.0.1` (nunca em `0.0.0.0`).

Com isso, scripts locais, agentes de IA e automações (n8n, cron, CLI, wrappers MCP) conseguem operar o Google Drive e as pastas sincronizadas sem lidar diretamente com OAuth2, refresh de tokens ou banco SQLite. O próprio aplicativo gerencia as credenciais (`0600`), registra as transferências na fila visual e grava tudo nos Logs de Atividade.

### Como habilitar
1. Abra o Custódia e vá em **Configurações > API REST Local (Loopback)**.
2. Deixe marcada a opção **Habilitar servidor REST local em 127.0.0.1**.
3. Ajuste a porta se precisar (padrão `5080`, intervalo `1024` a `65535`; exige reiniciar o app caso mude a porta).

### Regras de segurança
- Apenas processos locais da mesma máquina conseguem acessar `127.0.0.1:{porta}`.
- Operações de exclusão usam a **Lixeira do Google Drive** por padrão (`POST /api/v1/files/{id}/trash` ou `DELETE /api/v1/files/{id}`).
- A remoção permanente só ocorre quando `?permanent=true` é passado explicitamente no `DELETE`.

---

## 2. Catálogo Completo de Endpoints (`/api/v1`)

| Categoria | Método | Rota | O que faz |
|---|---|---|---|
| Sistema | `GET` | `/api/v1/status` | Conta conectada, uso de armazenamento, transferências ativas e pastas sincronizadas |
| Descoberta | `GET` | `/api/v1/openapi.json` | Esquema OpenAPI 3.0.3 para autodescoberta de rotas |
| Sincronização | `POST` | `/api/v1/sync/trigger` | Inicia sincronização de todas as pastas ou de uma pasta pelo `folderId` |
| Busca | `GET` | `/api/v1/files/search` | Pesquisa arquivos no índice SQLite local e opcionalmente no Google Drive remoto |
| Listagem | `GET` | `/api/v1/files/list` | Lista arquivos e subpastas dentro de uma pasta no Google Drive |
| Lixeira | `GET` | `/api/v1/files/trash` | Lista os itens atualmente na Lixeira do Google Drive |
| Pastas | `POST` | `/api/v1/folders` | Cria uma nova pasta no Google Drive |
| Upload | `POST` | `/api/v1/files/upload` | Envia um arquivo do disco local para o Google Drive |
| Download | `POST` | `/api/v1/files/download` | Baixa um arquivo do Google Drive (exporta Docs/Sheets/Slides automaticamente) |
| Organização | `POST` | `/api/v1/files/{remoteFileId}/move` | Move para outra pasta e/ou renomeia um arquivo ou pasta |
| Lixeira | `POST` | `/api/v1/files/{remoteFileId}/trash` | Move um arquivo ou pasta para a Lixeira do Google Drive |
| Lixeira | `POST` | `/api/v1/files/{remoteFileId}/restore` | Restaura um item da Lixeira do Google Drive |
| Exclusão | `DELETE` | `/api/v1/files/{remoteFileId}` | Move para Lixeira (padrão) ou apaga definitivamente com `?permanent=true` |
| Fila | `GET` | `/api/v1/transfers` | Lista uploads e downloads ativos ou recentes na fila |
| Logs | `GET` | `/api/v1/logs` | Retorna o histórico de logs de sincronização e ações executadas |

---

## 3. Referência Detalhada

### 3.1 Estado do sistema (`GET /api/v1/status`)
Retorna a conta Google ativa, espaço usado e limite, quantidade de transferências em execução e lista das pastas locais configuradas.

```bash
curl -s http://127.0.0.1:5080/api/v1/status
```

Exemplo de resposta (`200 OK`):
```json
{
  "serviceName": "Custodia",
  "boundEndpoint": "http://127.0.0.1:5080",
  "isAccountConnected": true,
  "accountEmail": "usuario@gmail.com",
  "isSyncRunning": false,
  "storageUsageBytes": 10535132323,
  "storageLimitBytes": 5497558138880,
  "activeTransferCount": 0,
  "syncFolders": [
    {
      "id": "70671fe0e6564dbca233a904e478a96b",
      "localPath": "/home/usuario/Documentos/",
      "remoteFolderId": "root",
      "remoteFolderName": "Meu Drive",
      "syncMode": "TwoWay",
      "state": "Idle",
      "isPaused": false,
      "lastSyncedAtUtc": "2026-11-18T12:08:40Z",
      "trackedFileCount": 142
    }
  ]
}
```

### 3.2 Especificação OpenAPI (`GET /api/v1/openapi.json`)
Devolve o documento OpenAPI 3.0.3 em JSON com os 15 endpoints disponíveis. Pode ser apontado diretamente em clientes de API ou agentes que leem OpenAPI na inicialização.

```bash
curl -s http://127.0.0.1:5080/api/v1/openapi.json
```

### 3.3 Disparar sincronização (`POST /api/v1/sync/trigger`)
Dispara a sincronização de todas as pastas ativas ou de uma pasta específica. O corpo JSON é opcional; chamar sem corpo ou com `{}` sincroniza todas as pastas ativas.

Parâmetros JSON opcionais:
- `folderId` (string): ID da pasta obtido em `/api/v1/status`.
- `waitForCompletion` (boolean, padrão `false`): se `true`, a requisição espera o término da sincronização antes de responder.

```bash
# Todas as pastas (em segundo plano)
curl -X POST http://127.0.0.1:5080/api/v1/sync/trigger

# Pasta específica aguardando concluir
curl -X POST http://127.0.0.1:5080/api/v1/sync/trigger \
  -H "Content-Type: application/json" \
  -d '{"folderId":"70671fe0e6564dbca233a904e478a96b","waitForCompletion":true}'
```

Códigos HTTP: `200 OK`, `404 Not Found` (pasta não encontrada), `503 Service Unavailable` (conta não conectada).

### 3.4 Buscar arquivos (`GET /api/v1/files/search`)
Busca no banco local SQLite e, quando `includeRemote=true` e `q` não está vazio, busca também no Google Drive remoto (evitando duplicados pelo `remoteFileId`).

Query params:
- `q` (string, padrão `""`): trecho do nome ou caminho relativo.
- `folderId` (string, opcional): filtra por um `syncFolderId`.
- `includeRemote` (boolean, padrão `false`): inclui resultados remotos (`source: "GoogleDriveRemote"`).
- `limit` (int, padrão `50`, intervalo `1` a `100`): máximo de itens locais e remotos.

```bash
curl -s "http://127.0.0.1:5080/api/v1/files/search?q=contrato&includeRemote=true&limit=20"
```

### 3.5 Listar arquivos de uma pasta (`GET /api/v1/files/list`)
Lista os filhos diretos de uma pasta do Google Drive.

Query params:
- `folderId` (string, padrão `"root"`): ID da pasta no Google Drive.
- `foldersOnly` (boolean, padrão `false`): se `true`, retorna apenas pastas.

```bash
curl -s "http://127.0.0.1:5080/api/v1/files/list?folderId=root"
```

### 3.6 Listar itens na Lixeira (`GET /api/v1/files/trash`)
Lista arquivos e pastas que estão na Lixeira do Google Drive (`limit` de `1` a `200`, padrão `50`).

```bash
curl -s "http://127.0.0.1:5080/api/v1/files/trash?limit=50"
```

### 3.7 Criar pasta (`POST /api/v1/folders`)
Cria uma nova pasta no Google Drive dentro de `parentFolderId` (se omitido, cria na raiz `"root"`).

```bash
curl -X POST http://127.0.0.1:5080/api/v1/folders \
  -H "Content-Type: application/json" \
  -d '{"name":"Relatorios_IA","parentFolderId":"root"}'
```

### 3.8 Upload de arquivo local (`POST /api/v1/files/upload`)
Envia um arquivo existente no PC para o Google Drive e registra o envio na Fila de Transferências e nos Logs.

Campos JSON:
- `localPath` (string, obrigatório): caminho completo do arquivo no computador.
- `remoteFolderId` (string, opcional, padrão `"root"`): ID da pasta destino no Drive.

```bash
curl -X POST http://127.0.0.1:5080/api/v1/files/upload \
  -H "Content-Type: application/json" \
  -d '{"localPath":"/tmp/resumo.pdf","remoteFolderId":"root"}'
```

Códigos HTTP: `200 OK`, `400 Bad Request` (`localPath` vazio), `404 Not Found` (arquivo local inexistente), `503 Service Unavailable`.

### 3.9 Download de arquivo (`POST /api/v1/files/download`)
Baixa um arquivo do Google Drive pelo `remoteFileId`. Documentos nativos do Google Workspace (Docs, Sheets, Slides) são exportados automaticamente em formato Office ou PDF.

Campos JSON:
- `remoteFileId` (string, obrigatório): ID remoto do arquivo.
- `destinationPath` (string, opcional): caminho completo no disco. Se omitido, salva na pasta de downloads configurada em **Configurações**.

```bash
curl -X POST http://127.0.0.1:5080/api/v1/files/download \
  -H "Content-Type: application/json" \
  -d '{"remoteFileId":"1abc...xyz","destinationPath":"/tmp/baixado.pdf"}'
```

Códigos HTTP: `200 OK`, `400 Bad Request` (ID vazio ou alvo é uma pasta), `404 Not Found`, `503 Service Unavailable`.

### 3.10 Mover e/ou renomear (`POST /api/v1/files/{remoteFileId}/move`)
Move um arquivo ou pasta para outro diretório pai (`targetFolderId`) e/ou altera o nome (`newName`). Informe pelo menos um dos dois campos.

```bash
curl -X POST "http://127.0.0.1:5080/api/v1/files/1abc...xyz/move" \
  -H "Content-Type: application/json" \
  -d '{"targetFolderId":"1pasta...id","newName":"relatorio-final.pdf"}'
```

### 3.11 Enviar para a Lixeira (`POST /api/v1/files/{remoteFileId}/trash`)
Marca o arquivo ou pasta como `trashed = true` no Google Drive (ação reversível).

```bash
curl -X POST "http://127.0.0.1:5080/api/v1/files/1abc...xyz/trash"
```

### 3.12 Restaurar da Lixeira (`POST /api/v1/files/{remoteFileId}/restore`)
Tira o arquivo ou pasta da Lixeira (`trashed = false`), devolvendo-o ao local original.

```bash
curl -X POST "http://127.0.0.1:5080/api/v1/files/1abc...xyz/restore"
```

### 3.13 Excluir (`DELETE /api/v1/files/{remoteFileId}`)
- `DELETE /api/v1/files/{id}`: move com segurança para a Lixeira (`action: "trashed"`).
- `DELETE /api/v1/files/{id}?permanent=true`: apaga definitivamente do Google Drive (`action: "deleted_permanently"`).

```bash
curl -X DELETE "http://127.0.0.1:5080/api/v1/files/1abc...xyz?permanent=true"
```

### 3.14 Fila de transferências e logs (`GET /api/v1/transfers` e `GET /api/v1/logs`)
Permitem consultar uploads/downloads em andamento ou concluídos e auditar os logs de atividade (com filtro opcional `?q=...&limit=50`).

```bash
curl -s http://127.0.0.1:5080/api/v1/transfers
curl -s "http://127.0.0.1:5080/api/v1/logs?q=Upload&limit=20"
```

---

## 4. Capacidade de Integração com Agentes de IA

Qualquer agente capaz de executar requisições HTTP locais ou chamar scripts Python/shell consegue usar o Custódia para operar arquivos locais e em nuvem:

- **Ferramentas compatíveis:** agentes com OpenAI/Gemini/Claude Function Calling, LangChain, CrewAI, servidores MCP locais, Ollama/vLLM com tool-use, n8n local e scripts automatizados.
- **Fluxo recomendado para um agente:**
  1. Chamar `GET /api/v1/status` para confirmar que a conta está conectada (`isAccountConnected: true`) e conhecer as pastas sincronizadas.
  2. Consultar `GET /api/v1/files/search?q=...&includeRemote=true` ou `GET /api/v1/files/list` antes de enviar ou baixar arquivos, evitando duplicações.
  3. Criar uma subpasta de trabalho com `POST /api/v1/folders` quando o agente for gerar múltiplos arquivos.
  4. Usar `POST /api/v1/files/{id}/trash` em vez de `DELETE ?permanent=true` para exclusões automáticas, preservando a possibilidade de restauração.

### 4.1 Esquema JSON de Tools (Function Calling)

```json
[
  {
    "name": "custodia_status",
    "description": "Verifica se a conta Google esta conectada, espaco usado e pastas configuradas.",
    "parameters": { "type": "object", "properties": {} }
  },
  {
    "name": "custodia_search_files",
    "description": "Busca arquivos sincronizados localmente e no Google Drive remoto por nome ou caminho.",
    "parameters": {
      "type": "object",
      "properties": {
        "q": { "type": "string", "description": "Termo de busca" },
        "includeRemote": { "type": "boolean", "description": "Incluir resultados remotos no Drive" },
        "limit": { "type": "integer", "description": "Maximo de itens (1 a 100)" }
      },
      "required": ["q"]
    }
  },
  {
    "name": "custodia_list_folder",
    "description": "Lista arquivos e pastas dentro de uma pasta do Google Drive.",
    "parameters": {
      "type": "object",
      "properties": {
        "folderId": { "type": "string", "description": "ID da pasta ou 'root'" },
        "foldersOnly": { "type": "boolean" }
      }
    }
  },
  {
    "name": "custodia_create_folder",
    "description": "Cria uma pasta no Google Drive.",
    "parameters": {
      "type": "object",
      "properties": {
        "name": { "type": "string" },
        "parentFolderId": { "type": "string" }
      },
      "required": ["name"]
    }
  },
  {
    "name": "custodia_upload_file",
    "description": "Envia um arquivo do PC para uma pasta do Google Drive.",
    "parameters": {
      "type": "object",
      "properties": {
        "localPath": { "type": "string", "description": "Caminho absoluto no disco local" },
        "remoteFolderId": { "type": "string", "description": "ID da pasta destino ou 'root'" }
      },
      "required": ["localPath"]
    }
  },
  {
    "name": "custodia_download_file",
    "description": "Baixa um arquivo do Google Drive para o PC.",
    "parameters": {
      "type": "object",
      "properties": {
        "remoteFileId": { "type": "string" },
        "destinationPath": { "type": "string" }
      },
      "required": ["remoteFileId"]
    }
  },
  {
    "name": "custodia_move_file",
    "description": "Move um arquivo ou pasta para outro diretorio no Drive e/ou renomeia.",
    "parameters": {
      "type": "object",
      "properties": {
        "remoteFileId": { "type": "string" },
        "targetFolderId": { "type": "string" },
        "newName": { "type": "string" }
      },
      "required": ["remoteFileId"]
    }
  },
  {
    "name": "custodia_trash_file",
    "description": "Move um arquivo ou pasta para a Lixeira do Google Drive (reversivel).",
    "parameters": {
      "type": "object",
      "properties": { "remoteFileId": { "type": "string" } },
      "required": ["remoteFileId"]
    }
  },
  {
    "name": "custodia_restore_file",
    "description": "Restaura um item da Lixeira do Google Drive.",
    "parameters": {
      "type": "object",
      "properties": { "remoteFileId": { "type": "string" } },
      "required": ["remoteFileId"]
    }
  }
]
```

### 4.2 Cliente Python pronto para agentes e automações

```python
import requests

class CustodiaClient:
    def __init__(self, base_url: str = "http://127.0.0.1:5080/api/v1"):
        self.base_url = base_url.rstrip("/")

    def status(self):
        return requests.get(f"{self.base_url}/status", timeout=10).json()

    def search(self, q: str, include_remote: bool = True, limit: int = 20):
        return requests.get(
            f"{self.base_url}/files/search",
            params={"q": q, "includeRemote": include_remote, "limit": limit},
            timeout=15,
        ).json()

    def list_folder(self, folder_id: str = "root", folders_only: bool = False):
        return requests.get(
            f"{self.base_url}/files/list",
            params={"folderId": folder_id, "foldersOnly": folders_only},
            timeout=15,
        ).json()

    def list_trash(self, limit: int = 50):
        return requests.get(
            f"{self.base_url}/files/trash",
            params={"limit": limit},
            timeout=15,
        ).json()

    def create_folder(self, name: str, parent_folder_id: str = "root"):
        return requests.post(
            f"{self.base_url}/folders",
            json={"name": name, "parentFolderId": parent_folder_id},
            timeout=15,
        ).json()

    def upload(self, local_path: str, remote_folder_id: str = "root"):
        return requests.post(
            f"{self.base_url}/files/upload",
            json={"localPath": local_path, "remoteFolderId": remote_folder_id},
            timeout=60,
        ).json()

    def download(self, remote_file_id: str, destination_path: str | None = None):
        payload = {"remoteFileId": remote_file_id}
        if destination_path:
            payload["destinationPath"] = destination_path
        return requests.post(f"{self.base_url}/files/download", json=payload, timeout=60).json()

    def move(self, remote_file_id: str, target_folder_id: str | None = None, new_name: str | None = None):
        return requests.post(
            f"{self.base_url}/files/{remote_file_id}/move",
            json={"targetFolderId": target_folder_id, "newName": new_name},
            timeout=20,
        ).json()

    def trash(self, remote_file_id: str):
        return requests.post(f"{self.base_url}/files/{remote_file_id}/trash", timeout=15).json()

    def restore(self, remote_file_id: str):
        return requests.post(f"{self.base_url}/files/{remote_file_id}/restore", timeout=15).json()

    def trigger_sync(self, folder_id: str | None = None, wait: bool = False):
        return requests.post(
            f"{self.base_url}/sync/trigger",
            json={"folderId": folder_id, "waitForCompletion": wait},
            timeout=120 if wait else 10,
        ).json()

    def transfers(self):
        return requests.get(f"{self.base_url}/transfers", timeout=10).json()

    def logs(self, q: str = "", limit: int = 50):
        return requests.get(
            f"{self.base_url}/logs",
            params={"q": q, "limit": limit},
            timeout=10,
        ).json()
```
