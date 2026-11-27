# Custódia: Local REST API Documentation and AI Agent Integration

> 🌐 **Language / Idioma:** [🇧🇷 Português](../API.md) | **🇺🇸 English (current)**

- API Version: `v1` (`4.0.0`)
- Default address: `http://127.0.0.1:5080` (local loopback only)
- OpenAPI specification: `GET http://127.0.0.1:5080/api/v1/openapi.json`
- PDF copy: `Custodia-API.en.pdf` (in the project root and linked in Settings)

---

## 1. Overview and Security

Custódia runs an embedded **ASP.NET Core Minimal API** server inside the desktop application, bound exclusively to `127.0.0.1` (never `0.0.0.0`).

This allows local scripts, AI agents, and automations (n8n, cron, CLI, MCP wrappers) to operate Google Drive and synced folders without directly handling OAuth2, token refreshes, or the SQLite database. The application itself manages credentials (`0600`), records transfers in the visual queue, and writes everything to Activity Logs.

### How to enable
1. Open Custódia and go to **Settings > Local REST API (Loopback)**.
2. Keep **Enable local REST server on 127.0.0.1** checked.
3. Adjust the port if needed (default `5080`, range `1024` to `65535`; requires restarting the app if changed).

### Security rules
- Only local processes on the same machine can access `127.0.0.1:{port}`.
- Delete operations use **Google Drive Trash** by default (`POST /api/v1/files/{id}/trash` or `DELETE /api/v1/files/{id}`).
- Permanent removal only occurs when `?permanent=true` is explicitly passed to `DELETE`.

---

## 2. Full Endpoint Catalog (`/api/v1`)

| Category | Method | Route | Description |
|---|---|---|---|
| System | `GET` | `/api/v1/status` | Connected account, storage usage, active transfers, and synced folders |
| Discovery | `GET` | `/api/v1/openapi.json` | OpenAPI 3.0.3 schema for route auto-discovery |
| Sync | `POST` | `/api/v1/sync/trigger` | Triggers sync for all folders or a specific folder by `folderId` |
| Search | `GET` | `/api/v1/files/search` | Searches local SQLite index and optionally remote Google Drive |
| Listing | `GET` | `/api/v1/files/list` | Lists files and subfolders inside a Google Drive folder |
| Trash | `GET` | `/api/v1/files/trash` | Lists items currently in Google Drive Trash |
| Folders | `POST` | `/api/v1/folders` | Creates a new folder in Google Drive |
| Upload | `POST` | `/api/v1/files/upload` | Uploads a local disk file to Google Drive |
| Download | `POST` | `/api/v1/files/download` | Downloads a Google Drive file (auto-exports Docs/Sheets/Slides) |
| Organization | `POST` | `/api/v1/files/{remoteFileId}/move` | Moves to another folder and/or renames a file or folder |
| Trash | `POST` | `/api/v1/files/{remoteFileId}/trash` | Moves a file or folder to Google Drive Trash |
| Trash | `POST` | `/api/v1/files/{remoteFileId}/restore` | Restores an item from Google Drive Trash |
| Deletion | `DELETE` | `/api/v1/files/{remoteFileId}` | Moves to Trash (default) or deletes permanently with `?permanent=true` |
| Queue | `GET` | `/api/v1/transfers` | Lists active or recent uploads and downloads in the queue |
| Logs | `GET` | `/api/v1/logs` | Returns sync and executed action log history |

---

## 3. Detailed Reference

### 3.1 System status (`GET /api/v1/status`)
Returns the active Google account, storage used and limit, number of active transfers, and list of configured local folders.

```bash
curl -s http://127.0.0.1:5080/api/v1/status
```

Example response (`200 OK`):
```json
{
  "serviceName": "Custodia",
  "boundEndpoint": "http://127.0.0.1:5080",
  "isAccountConnected": true,
  "accountEmail": "user@gmail.com",
  "isSyncRunning": false,
  "storageUsageBytes": 10535132323,
  "storageLimitBytes": 5497558138880,
  "activeTransferCount": 0,
  "syncFolders": [
    {
      "id": "70671fe0e6564dbca233a904e478a96b",
      "localPath": "/home/user/Documents/",
      "remoteFolderId": "root",
      "remoteFolderName": "My Drive",
      "syncMode": "TwoWay",
      "state": "Idle",
      "isPaused": false,
      "lastSyncedAtUtc": "2026-11-18T12:08:40Z",
      "trackedFileCount": 142
    }
  ]
}
```

### 3.2 OpenAPI specification (`GET /api/v1/openapi.json`)
Returns the OpenAPI 3.0.3 JSON document describing all 15 available endpoints. Can be pointed directly to API clients or agents that read OpenAPI on startup.

```bash
curl -s http://127.0.0.1:5080/api/v1/openapi.json
```

### 3.3 Trigger sync (`POST /api/v1/sync/trigger`)
Triggers synchronization for all active folders or a specific folder. The JSON body is optional; calling without a body or with `{}` syncs all active folders.

Optional JSON parameters:
- `folderId` (string): folder ID obtained from `/api/v1/status`.
- `waitForCompletion` (boolean, default `false`): if `true`, the request waits until synchronization finishes before responding.

```bash
# All folders (background)
curl -X POST http://127.0.0.1:5080/api/v1/sync/trigger

# Specific folder waiting for completion
curl -X POST http://127.0.0.1:5080/api/v1/sync/trigger \
  -H "Content-Type: application/json" \
  -d '{"folderId":"70671fe0e6564dbca233a904e478a96b","waitForCompletion":true}'
```

HTTP codes: `200 OK`, `404 Not Found` (folder not found), `503 Service Unavailable` (account not connected).

### 3.4 Search files (`GET /api/v1/files/search`)
Searches the local SQLite database and, when `includeRemote=true` and `q` is non-empty, also searches remote Google Drive (deduplicating by `remoteFileId`).

Query params:
- `q` (string, default `""`): filename or relative path substring.
- `folderId` (string, optional): filters by a `syncFolderId`.
- `includeRemote` (boolean, default `false`): includes remote results (`source: "GoogleDriveRemote"`).
- `limit` (int, default `50`, range `1` to `100`): max local and remote items.

```bash
curl -s "http://127.0.0.1:5080/api/v1/files/search?q=contract&includeRemote=true&limit=20"
```

### 3.5 List folder contents (`GET /api/v1/files/list`)
Lists direct children of a Google Drive folder.

Query params:
- `folderId` (string, default `"root"`): Google Drive folder ID.
- `foldersOnly` (boolean, default `false`): if `true`, returns only folders.

```bash
curl -s "http://127.0.0.1:5080/api/v1/files/list?folderId=root"
```

### 3.6 List Trash items (`GET /api/v1/files/trash`)
Lists files and folders currently in Google Drive Trash (`limit` from `1` to `200`, default `50`).

```bash
curl -s "http://127.0.0.1:5080/api/v1/files/trash?limit=50"
```

### 3.7 Create folder (`POST /api/v1/folders`)
Creates a new folder in Google Drive inside `parentFolderId` (if omitted, creates under root `"root"`).

```bash
curl -X POST http://127.0.0.1:5080/api/v1/folders \
  -H "Content-Type: application/json" \
  -d '{"name":"AI_Reports","parentFolderId":"root"}'
```

### 3.8 Upload local file (`POST /api/v1/files/upload`)
Uploads an existing file from the PC to Google Drive and records the transfer in the Transfer Queue and Logs.

JSON fields:
- `localPath` (string, required): full file path on the computer.
- `remoteFolderId` (string, optional, default `"root"`): destination folder ID in Drive.

```bash
curl -X POST http://127.0.0.1:5080/api/v1/files/upload \
  -H "Content-Type: application/json" \
  -d '{"localPath":"/tmp/summary.pdf","remoteFolderId":"root"}'
```

HTTP codes: `200 OK`, `400 Bad Request` (empty `localPath`), `404 Not Found` (local file missing), `503 Service Unavailable`.

### 3.9 Download file (`POST /api/v1/files/download`)
Downloads a file from Google Drive by `remoteFileId`. Native Google Workspace documents (Docs, Sheets, Slides) are automatically exported to Office or PDF format.

JSON fields:
- `remoteFileId` (string, required): remote file ID.
- `destinationPath` (string, optional): full path on disk. If omitted, saves to the download folder configured in **Settings**.

```bash
curl -X POST http://127.0.0.1:5080/api/v1/files/download \
  -H "Content-Type: application/json" \
  -d '{"remoteFileId":"1abc...xyz","destinationPath":"/tmp/downloaded.pdf"}'
```

HTTP codes: `200 OK`, `400 Bad Request` (empty ID or target is a folder), `404 Not Found`, `503 Service Unavailable`.

### 3.10 Move and/or rename (`POST /api/v1/files/{remoteFileId}/move`)
Moves a file or folder to another parent directory (`targetFolderId`) and/or changes its name (`newName`). Provide at least one of the two fields.

```bash
curl -X POST "http://127.0.0.1:5080/api/v1/files/1abc...xyz/move" \
  -H "Content-Type: application/json" \
  -d '{"targetFolderId":"1folder...id","newName":"final-report.pdf"}'
```

### 3.11 Send to Trash (`POST /api/v1/files/{remoteFileId}/trash`)
Marks the file or folder as `trashed = true` in Google Drive (reversible action).

```bash
curl -X POST "http://127.0.0.1:5080/api/v1/files/1abc...xyz/trash"
```

### 3.12 Restore from Trash (`POST /api/v1/files/{remoteFileId}/restore`)
Removes the file or folder from Trash (`trashed = false`), returning it to its original location.

```bash
curl -X POST "http://127.0.0.1:5080/api/v1/files/1abc...xyz/restore"
```

### 3.13 Delete (`DELETE /api/v1/files/{remoteFileId}`)
- `DELETE /api/v1/files/{id}`: safely moves to Trash (`action: "trashed"`).
- `DELETE /api/v1/files/{id}?permanent=true`: permanently deletes from Google Drive (`action: "deleted_permanently"`).

```bash
curl -X DELETE "http://127.0.0.1:5080/api/v1/files/1abc...xyz?permanent=true"
```

### 3.14 Transfer queue and logs (`GET /api/v1/transfers` and `GET /api/v1/logs`)
Query ongoing or completed uploads/downloads and audit activity logs (with optional filter `?q=...&limit=50`).

```bash
curl -s http://127.0.0.1:5080/api/v1/transfers
curl -s "http://127.0.0.1:5080/api/v1/logs?q=Upload&limit=20"
```

---

## 4. AI Agent Integration Capabilities

Any agent capable of making local HTTP requests or running Python/shell scripts can use Custódia to operate local and cloud files:

- **Compatible tools:** agents with OpenAI/Gemini/Claude Function Calling, LangChain, CrewAI, local MCP servers, Ollama/vLLM with tool-use, local n8n, and automated scripts.
- **Recommended workflow for an agent:**
  1. Call `GET /api/v1/status` to confirm the account is connected (`isAccountConnected: true`) and inspect synced folders.
  2. Query `GET /api/v1/files/search?q=...&includeRemote=true` or `GET /api/v1/files/list` before uploading or downloading files to avoid duplicates.
  3. Create a working subfolder with `POST /api/v1/folders` when the agent will generate multiple files.
  4. Use `POST /api/v1/files/{id}/trash` instead of `DELETE ?permanent=true` for automated deletions, preserving recovery.

### 4.1 Tool JSON Schema (Function Calling)

```json
[
  {
    "name": "custodia_status",
    "description": "Checks whether the Google account is connected, storage usage, and configured folders.",
    "parameters": { "type": "object", "properties": {} }
  },
  {
    "name": "custodia_search_files",
    "description": "Searches locally synced files and remote Google Drive by name or path.",
    "parameters": {
      "type": "object",
      "properties": {
        "q": { "type": "string", "description": "Search query" },
        "includeRemote": { "type": "boolean", "description": "Include remote results from Drive" },
        "limit": { "type": "integer", "description": "Max items (1 to 100)" }
      },
      "required": ["q"]
    }
  },
  {
    "name": "custodia_list_folder",
    "description": "Lists files and folders inside a Google Drive folder.",
    "parameters": {
      "type": "object",
      "properties": {
        "folderId": { "type": "string", "description": "Folder ID or 'root'" },
        "foldersOnly": { "type": "boolean" }
      }
    }
  },
  {
    "name": "custodia_create_folder",
    "description": "Creates a folder in Google Drive.",
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
    "description": "Uploads a file from the PC to a Google Drive folder.",
    "parameters": {
      "type": "object",
      "properties": {
        "localPath": { "type": "string", "description": "Absolute path on local disk" },
        "remoteFolderId": { "type": "string", "description": "Destination folder ID or 'root'" }
      },
      "required": ["localPath"]
    }
  },
  {
    "name": "custodia_download_file",
    "description": "Downloads a file from Google Drive to the PC.",
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
    "description": "Moves a file or folder to another Drive directory and/or renames it.",
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
    "description": "Moves a file or folder to Google Drive Trash (reversible).",
    "parameters": {
      "type": "object",
      "properties": { "remoteFileId": { "type": "string" } },
      "required": ["remoteFileId"]
    }
  },
  {
    "name": "custodia_restore_file",
    "description": "Restores an item from Google Drive Trash.",
    "parameters": {
      "type": "object",
      "properties": { "remoteFileId": { "type": "string" } },
      "required": ["remoteFileId"]
    }
  }
]
```

### 4.2 Ready-to-Use Python Client for Agents and Automations

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
