# Custódia v4.0.0

> 🌐 **Language / Idioma:** [🇧🇷 Português](../../README.md) | **🇺🇸 English (current)**

![Custódia by TundraLab - Your files. Always safe.](../images/custodia-promo-en.jpg)

[![Release](https://img.shields.io/badge/Release-v4.0.0-2563EB?style=flat-square&logo=github&labelColor=1D2024)](https://github.com/TundraLab/Custodia/releases)
[![xUnit Tests](https://img.shields.io/badge/xUnit%20Tests-94%20passed-16A34A?style=flat-square&labelColor=1D2024)](../../tests/Custodia.Tests)
[![Linux](https://img.shields.io/badge/Linux-x86__64%20(Installer%20%7C%20.deb%20%7C%20.rpm%20%7C%20.AppImage)-334155?style=flat-square&logo=linux&logoColor=white&labelColor=1D2024)](#installation)
[![.NET 10](https://img.shields.io/badge/.NET%2010-Avalonia%2011.3.8-3B82F6?style=flat-square&logo=dotnet&logoColor=white&labelColor=1D2024)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-All%20Rights%20Reserved-475569?style=flat-square&labelColor=1D2024)](../../LICENSE)
[![Privacy](https://img.shields.io/badge/Privacy-EN-0F766E?style=flat-square&labelColor=1D2024)](PRIVACY.md)
[![Terms of Use](https://img.shields.io/badge/Terms-EN-1D4ED8?style=flat-square&labelColor=1D2024)](TERMS.md)

> **In development since:** 2026  
> **Status:** stable, with official releases ([GitHub Releases](https://github.com/TundraLab/Custodia/releases))  
> **Platform:** Linux `x86_64`  
> **Goal:** secure and intuitive synchronization with Google Drive

**My files. Always safe.** Native Linux desktop client focused on folder synchronization, local backups, multimedia management, and privacy-first local automation directly with Google Drive.

## Quality

- **Build & distribution:** clean `.NET 10.0` compilation for Linux `x86_64` with native **`.deb`**, **`.rpm`**, **`.AppImage`** packages and a lightweight **`.run`** graphical installer (~30 KB), all verified by SHA-256
- **Test suite:** **94 xUnit unit & UI tests passing (`0` failures)** covering sync engine, transfer queue & retry, SQLite persistence, OAuth2 account isolation, updater, support diagnostics sanitization, terms/privacy acceptance, and lifecycle emails
- **Primary platform:** Linux `x86_64` (Fedora Workstation/KDE, Pop!_OS GNOME/COSMIC, and KDE Plasma 5/6)
- **Technical documentation:** live OpenAPI `3.0.3` (`GET /api/v1/openapi.json`), [API.md](API.md), [Custodia-API.en.pdf](Custodia-API.en.pdf), and full history in [CHANGELOG.md](../../CHANGELOG.md)

## Documentation

> Available in **[🇺🇸 English](README.md)** and **[🇧🇷 Português](../../README.md)**.

| Guide | Contents |
| :--- | :--- |
| **[OAuth2 Setup](OAUTH_SETUP.md)** | Illustrated step-by-step guide to create `credentials.json` in Google Cloud Console and connect your account |
| **[Linux Compatibility](COMPATIBILITY.md)** | Matrix of tested vs. supported features across GNOME, KDE Plasma 5/6, COSMIC, Wayland, and X11 |
| **[Security & Privacy](SECURITY.md)** | BYO-OAuth2 model, `0700`/`0600` permissions, token refresh, and local API surface |
| **[Frequently Asked Questions (FAQ)](FAQ.md)** | Offline mode, conflict policy, Trash recovery, multiple accounts, and complete removal |
| **[Troubleshooting](TROUBLESHOOTING.md)** | Diagnostics for `.rpm`, `.AppImage`/FUSE, GNOME tray icon, D-Bus, `403` error, and previews |
| **[Local REST API](API.md)** | Endpoints on `127.0.0.1:5080`, OpenAPI `3.0.3`, Python examples, and AI agent integration |

## Visual Preview

![Custódia screens preview](../images/preview-showcase.png)

- **[Watch promo video](../videos/custodia-divulgacao.mp4)** (`MP4`, 21 MB)

<details>
<summary>View all 6 individual screens</summary>

| Screen | Preview |
| :--- | :--- |
| **My Files** | ![My Files](../images/preview-01-meu-driver.png) |
| **Sync Folders** | ![Sync Folders](../images/preview-02-pastas.png) |
| **Transfer Queue** | ![Transfer Queue](../images/preview-03-transferencias.png) |
| **Activity Logs** | ![Activity Logs](../images/preview-04-logs.png) |
| **Settings** | ![Settings](../images/preview-05-configuracoes.png) |
| **About** | ![About](../images/preview-06-sobre.png) |

</details>

## Systems and Formats

- Tested on **Fedora**, **Ubuntu**, and **Pop!_OS** (including modest hardware), with support for **GNOME**, **COSMIC**, and **KDE Plasma 5/6** — see the full matrix in **[COMPATIBILITY.md](COMPATIBILITY.md)**.
- **Distributed formats:** lightweight graphical installer **`.run`** (~30 KB, pick version and format), native **`.deb`** package (Ubuntu / Debian / Pop!_OS / Linux Mint), native **`.rpm`** package (Fedora / RHEL / openSUSE), and standalone **`.AppImage` (`x86_64`)** with the `.NET 10` runtime bundled, all verified by **SHA-256** (`SHA256SUMS.txt`).

### System Requirements

| Resource | Minimum | Recommended |
| :--- | :--- | :--- |
| **OS / Architecture** | Linux `x86_64` (Wayland or X11) | Fedora Workstation/KDE, Ubuntu, or Pop!_OS (GNOME/COSMIC) |
| **Processor** | Dual-core `x86_64` | Quad-core `x86_64` |
| **Memory (RAM)** | `2 GB` | `4 GB` or more |
| **Disk space** | `200 MB` free *(+ space for your synced files)* | SSD with headroom for cache and synced files |
| **Extra dependencies** | None for `.deb`/`.rpm`; `libfuse2` for `.AppImage` *(or `--appimage-extract-and-run`)* | `poppler-utils`, `ghostscript`, `ffmpeg`, `libreoffice`, `ImageMagick` *(for full media/doc previews)* |

## What the App Does

- **Clean My Files with Shared With Me and Trash:** browse **My Drive**, **Shared with me**, **Local Folders**, and **Trash** in List or Grid mode with fast background thumbnails, multi-selection (`Ctrl + Click`, `Shift + Click`, `Ctrl + A`), drag-and-drop uploads, **Save As** destination picker, and right-click menus for files, folders, and empty space.
- **Sharing links and permissions:** generate or revoke shareable Google Drive links (`anyone` view or edit) and invite people by email directly from the app.
- **Folder customization (color, symbol, and nickname):** customize directly via right-click in **My Files** or on the **Sync Folders** tab, with colors and icons preserved across sessions.
- **Built-in multimedia preview:** images, videos (including **Google Vids**), audio with built-in player, PDFs with page navigation, Office documents (`DOCX`, `XLSX`, `PPTX`, `ODT`, `ODS`, `ODP`), Google Workspace files, and text/code.
- **Transfer queue with filters, retry, and persistent resume:** filter by all, uploads, downloads, and failures, retry failed transfers with one click, resume interrupted large uploads (`UploadSessionUri`) and partial downloads (`.tundratmp.*` with HTTP `Range`), and set an optional bandwidth limit.
- **Flexible per-folder sync:** **Two-Way (`PC <-> Drive`)** or **One-Way Backup (`PC -> Drive`)** mode, multiple mappings preserving names and empty subfolders, MD5/SHA256 hash validation, conflict policy, ignore patterns, and continuous real-time local change monitoring.
- **Safe Trash:** deletions in synced folders send items to Google Drive Trash (or to the system FreeDesktop Trash compatible with GNOME Files and KDE Dolphin); permanent deletion only happens manually inside Trash.
- **Advanced multi-account management:** dedicated folders per email in `~/.config/custodia/contas/<email>/` (`credentials.json`, `conta.json`, and `pastas-sincronizadas.json`), automatic detection of `client_secret_*.json` in `~/Downloads`, drag-and-drop import in Settings, fast switching, reconnection, and folder preservation when removing/re-adding an account.
- **Instant auto-save Settings, language, zoom, and sound:** changes in Settings save automatically without a manual Save button, with PT-BR and EN-US, zoom from 100% to 300%, periodic RAM cleaner, and synthesized D-Bus notification sounds.
- **Support and updates on the About tab:** scheduled release check with offline protection, unified Bug / Suggestion form, and optional sanitized diagnostics.

## Architecture

```text
  Desktop UI (Avalonia UI — MVVM, System Tray & Notifications)
                          │
                          ▼
   Sync Core (Sync Engine, OAuth2, Hashes & Updater)
     ├──▶ Google Drive API v3 (drive scope directly from the user's machine)
     ├──▶ Local SQLite (~/.local/share/custodia/custodia.db)
     ├──▶ TundraLab Email Service (contato.tundralab@gmail.com)
     └──▶ Local REST API (ASP.NET Core + OpenAPI 3.0.3 on 127.0.0.1:5080)
```

- **Desktop Layer:** Avalonia UI 11.3.8 interface with MVVM pattern, enforced single instance, dynamic tray icon via D-Bus/AppIndicator, and multimedia preview.
- **Sync Core:** two-way and one-way sync engine, conflict resolution, MD5/SHA256 integrity validation, continuous local watcher, per-account OAuth2 vault, and official update checker.
- **Local REST API:** embedded HTTP Minimal API server on local *loopback* (`127.0.0.1`) exposing REST endpoints and an OpenAPI `3.0.3` schema for safe integration with local scripts and AI agents.
- **Data Flow:** local or remote changes are compared against state persisted in SQLite and synced directly with Google Drive API v3, updating the transfer queue and local logs in real time.

## Technical Decisions

- **C# and `.NET 10.0`:** chosen for strong typing, solid controlled async concurrency support, and stable self-contained publishing for Linux `x86_64`.
- **Avalonia UI (`11.3.8`):** chosen to deliver a fluid, consistent UI across GNOME, COSMIC, and KDE Plasma (X11 and Wayland) with low memory footprint and testable MVVM architecture.
- **SQLite + Dapper:** chosen for a lightweight, transactional (ACID), low-latency local relational database without requiring an external service installed on the user's PC.
- **Direct Google Drive API v3:** chosen so files and tokens travel strictly between the user's computer and Google Drive, with no intermediary servers.
- **Native `.deb`, `.rpm`, and `.AppImage` (`x86_64`) + lightweight `.run` installer:** `.deb` and `.rpm` provide native integration on Debian/Ubuntu/Mint/Pop!_OS and Fedora/RHEL/openSUSE, `.AppImage` runs portably across distros, and the ~30 KB graphical `.run` installer lets users choose both version and format.
- **Local API on `127.0.0.1:5080`:** restricts automations and AI agents to the local machine, allowing file operations without exposing OAuth tokens or opening external network ports.

## Sync Modes

Each mapped folder can operate in **Two-Way Mirror (`PC <-> Drive`)** or **One-Way Backup (`PC -> Drive`)**:

| Action taken | Two-Way (`PC <-> Drive`) | One-Way Backup (`PC -> Drive`) |
| :--- | :--- | :--- |
| Deleted on **PC** | Moves to Drive Trash | Moves to Drive Trash |
| Deleted on **Drive** | Moves to PC Trash | Stays saved on PC and is not re-uploaded |
| Added on **Drive** | Downloads to PC | Does not download to PC |
| Added/edited on **PC** | Uploads to Drive | Uploads to Drive |

## Installation

### 1. Lightweight Graphical Installer (`Custódia by TundraLab`)

A lightweight graphical installer (~30 KB, no AppImage or FUSE required) where you pick the release version and package format (`.deb`, `.rpm`, or `.AppImage`):

1. Download the official installer: **[Custodia-Installer-x86_64.run](https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-Installer-x86_64.run)**
2. Right-click the file → **Properties** → allow executing as a program *(or `chmod +x Custodia-Installer-x86_64.run`)*, then open it.
3. Choose the version and format (`.deb`, `.rpm`, or `.AppImage`). The installer downloads the package, verifies **SHA-256**, installs in the background, creates shortcuts, and **auto-deletes itself once finished**.

<details>
<summary>Other installation methods (direct .deb / .rpm / .AppImage packages, terminal, and advanced options)</summary>

#### Direct package download

- **Ubuntu / Debian / Pop!_OS / Linux Mint (`.deb`):**
  ```bash
  curl -fLO https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-4.0.0_amd64.deb
  sudo apt install -y ./Custodia-4.0.0_amd64.deb
  ```
- **Fedora / RHEL / openSUSE (`.rpm`):**
  ```bash
  curl -fLO https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-4.0.0-1.x86_64.rpm
  sudo dnf install -y ./Custodia-4.0.0-1.x86_64.rpm
  ```
- **AppImage (`x86_64`, portable):**
  ```bash
  curl -fLO https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-4.0.0-x86_64.AppImage
  chmod +x Custodia-4.0.0-x86_64.AppImage
  ./Custodia-4.0.0-x86_64.AppImage
  ```

#### Terminal installer (`install-custodia.sh`)

```bash
curl -fsSL https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-Installer-x86_64.sh -o install-custodia.sh
chmod +x install-custodia.sh
./install-custodia.sh
```

- **`install-custodia.sh` flags:**
  - `./install-custodia.sh --gui`: opens the graphical Custódia installer.
  - `./install-custodia.sh --background`: installs or updates silently in the background.
  - `./install-custodia.sh --update`: detects your current installation (`.rpm` or `.AppImage`) and updates to the latest release.
  - `./install-custodia.sh --rpm`: downloads, validates `SHA-256`, and installs the `.rpm`.
  - `./install-custodia.sh --appimage`: downloads, validates `SHA-256`, and installs the user-integrated `.AppImage`.
  - `./install-custodia.sh --download-only`: downloads packages and `SHA256SUMS.txt` into the current directory.
  - `./install-custodia.sh --uninstall`: removes the local or `.rpm` installation while keeping your settings.
- **AppImage without FUSE:** `./Custodia-4.0.0-x86_64.AppImage --appimage-extract-and-run`
- **Optional packages for full preview (PDF, Office, audio, and video):**
  - Fedora: `sudo dnf install poppler-utils ghostscript ffmpeg libreoffice ImageMagick`
  - Pop!_OS / Ubuntu: `sudo apt install poppler-utils ghostscript ffmpeg libreoffice imagemagick`
- **Cloud icon on the GNOME Workstation top bar** *(works natively on KDE Plasma and Pop!_OS)*:
  ```bash
  sudo dnf install gnome-shell-extension-appindicator
  gsettings set org.gnome.shell enabled-extensions "['appindicatorsupport@rgcjonas.gmail.com']"
  ```
  *(Log out and log back in to GNOME after enabling for the first time.)*

</details>

## How to Configure Google Drive (`credentials.json`)

To connect your account directly to Google without intermediary servers, create your Desktop OAuth2 credentials in Google Cloud Console:

1. Enable the **Google Drive API** in your Google Cloud Console project.
2. Configure the **OAuth consent screen** (*Google Auth Platform* → **External**).
3. Add your email under **Audience → Test users** *(required while the app is in Testing mode)*.
4. Under **Clients → + Create client**, choose **Desktop app** and download the `client_secret_*.json` file.
5. Import the JSON in Custódia (automatically detected in `~/Downloads` or via drag-and-drop in **Settings**) and authorize in your browser.

> **Full guide with screenshots for every step:** **[OAUTH_SETUP.md](OAUTH_SETUP.md)**  
> Facing `403: access_denied` or tray/AppImage issues? Check **[TROUBLESHOOTING.md](TROUBLESHOOTING.md)** and **[FAQ.md](FAQ.md)**.

## Security and Privacy

See the full architecture in **[SECURITY.md](SECURITY.md)**.

- **Local isolation and restricted permissions (`0700` / `0600`):**
  - `~/.config/custodia/settings.json`: general preferences and visual folder customizations.
  - `~/.config/custodia/contas/<email>/`: per-account directory containing `credentials.json`, `conta.json`, and `pastas-sincronizadas.json` with user-restricted access (`0600`).
  - `~/.local/share/custodia/tokens/`: OAuth2 tokens stored locally with `0600` permissions.
  - `~/.local/share/custodia/custodia.db`: local SQLite database with mappings, metadata, and activity logs.
- **How tokens are renewed:** the official Google SDK uses the locally stored `refresh_token` (`0600`) to obtain new temporary `access_token` values directly from Google's OAuth2 endpoint, without manual intervention or password storage.
- **The OAuth JSON does not contain your password:** `client_secret_*.json` only identifies the application ID you created in Google Cloud Console; your password is entered strictly on Google's official sign-in page in your browser.
- **What happens when revoking/disconnecting an account:** the local token is invalidated/removed, the `~/.config/custodia/contas/<email>/` session is closed, and you explicitly choose in the confirmation dialog whether to keep or also delete the configured folders linked to that email.
- **Local API surface:** the embedded REST server listens exclusively on `127.0.0.1:5080` (never on `0.0.0.0`) and **must not be exposed outside the device** (e.g., via reverse proxy or public tunnel).
- **How to completely remove all local data:**
  ```bash
  ./install-custodia.sh --uninstall
  rm -rf ~/.config/custodia ~/.local/share/custodia ~/.cache/custodia
  ```

## Privacy, Google OAuth Compliance, and Terms of Use

Official documents available in the repository: **[Privacy Policy](PRIVACY.md)** | **[Terms of Use](TERMS.md)** | **[Proprietary License](../../LICENSE)**.

- **Scope classification and BYO-OAuth model:** Custódia requests the `https://www.googleapis.com/auth/drive` scope (classified as a **sensitive scope** by [official Google documentation](https://developers.google.com/identity/protocols/oauth2/scopes)). In the current setup, each user creates and uses their own OAuth credentials in Google Cloud Console strictly for **personal/individual use** — this **does not** waive the formal [Google OAuth Verification](https://support.google.com/cloud/answer/9110914) process if a shared Client ID is ever publicly distributed by TundraLab.
- **Limited Use:** the use of data obtained from Google APIs follows the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy). Your Drive files, folders, metadata, hashes, and OAuth tokens stay exclusively on your machine and are never sent to TundraLab servers.
- **Optional lifecycle emails (`contato.tundralab@gmail.com`):** no Gmail scope is requested on your Google Cloud project. If you keep account alerts enabled in Custódia, only `recipientEmail`, `recipientName`, `eventType` (`connect` or `revoke`), `appName`, `appVersion`, `language`, and `timestampUtc` are sent to TundraLab's official delivery service strictly to notify account connection or revocation — an optional feature that can be declined on first launch or changed anytime in **About -> Review initial terms**.
- **Notice on download/install and first run:** both the terminal installer (`install-custodia.sh`) and the desktop app display the Privacy Policy and Terms of Use summary requiring explicit acceptance before first use.

## Local REST API (`127.0.0.1:5080`) and AI Agents

Complete guide with examples, Function Calling schemas, and Python client: **[Custodia-API.en.pdf](Custodia-API.en.pdf)** and **[API.md](API.md)**.

- `GET /api/v1/status`: active account, quota usage, transfers, and synced folders.
- `GET /api/v1/openapi.json`: OpenAPI 3.0.3 specification for AI agent auto-discovery.
- `POST /api/v1/sync/trigger`: triggers full sync or by `folderId` (`{"folderId": "...", "waitForCompletion": false}`).
- `GET /api/v1/files/search?q=report&includeRemote=true`: searches local SQLite and optionally remote Drive.
- `GET /api/v1/files/list?folderId=root`: lists folders and files in Drive.
- `GET /api/v1/files/trash?limit=50`: lists items in Drive Trash.
- `POST /api/v1/folders`: creates a folder in Drive (`{"name": "MyFolder", "parentFolderId": "root"}`).
- `POST /api/v1/files/upload`: uploads a local file to Drive (`{"localPath": "/path/file.pdf", "remoteFolderId": "root"}`).
- `POST /api/v1/files/download`: downloads a file from Drive (`{"remoteFileId": "...", "destinationPath": "/path/dest.pdf"}`).
- `POST /api/v1/files/{remoteFileId}/move`: moves to another folder and/or renames (`{"targetFolderId": "...", "newName": "new-name.txt"}`).
- `POST /api/v1/files/{remoteFileId}/trash`: moves an item to Drive Trash.
- `POST /api/v1/files/{remoteFileId}/restore`: restores an item from Trash.
- `DELETE /api/v1/files/{remoteFileId}?permanent=true`: deletes permanently (or moves to Trash if `permanent=true` is omitted).
- `GET /api/v1/transfers`: lists active and recent transfers.
- `GET /api/v1/logs?limit=50`: queries sync and action logs.

## License and Intellectual Property

**Proprietary Software (Source-Available).**  
**All Rights Reserved**  
Copyright © 2026 TundraLab and Gustavo Henrique Novaes Gomes (@GustaHerin).

- **Source Code Access (Portfolio, Study, and Audit):** Although this repository is public — technically allowing users to view, clone locally, or fork on GitHub strictly for viewing, reading, study, and technical evaluation — it is **not open source**. No permission or license is granted to copy, reproduce, modify, adapt, create derivative works, sublicense, distribute, publish, or use excerpts of the source code (in whole or in part) in other projects (commercial or otherwise) without prior express written authorization from the rights holders.
- **Use of Official Binaries (`.deb` / `.rpm` / `.AppImage` / `.run`):** End users receive a free, non-exclusive, non-transferable, revocable license to download, install, and run the official packages published under [Releases](https://github.com/TundraLab/Custodia/releases), strictly for personal or internal corporate use; redistribution (free or commercial), resale, offering as a service (SaaS), modification, or reverse engineering beyond limits expressly permitted by law is prohibited.
- **External Contributions, Trademarks, and Third Parties:** Any suggestions, bug reports, or Pull Requests grant the authors an irrevocable, royalty-free license to incorporate them into the Software; this license does not authorize use of the names **"TundraLab"**, **"Custódia"**, or visual identity, and third-party libraries remain governed by their respective original licenses.

Read the full binding text in [LICENSE](../../LICENSE).
