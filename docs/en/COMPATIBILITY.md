# Linux Compatibility Matrix

> 🌐 **Language / Idioma:** [🇧🇷 Português](../COMPATIBILITY.md) | **🇺🇸 English (current)**

Custódia is built on **.NET 10.0** + **Avalonia UI 11.3.8** for Linux `x86_64`, focusing on broad compatibility across desktop environments and distributions. This matrix explicitly distinguishes what has been **actively tested** from what is **supported by architecture**.

---

## Desktop Environments

| Environment | Status | System Tray | D-Bus Notifications | Trash / File Manager | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **GNOME** (Fedora Workstation / Pop!_OS) | **Tested** | Via `AppIndicator` extension | Native | FreeDesktop Trash + Files integration | On Fedora Workstation, install `gnome-shell-extension-appindicator` and log out/in the first time. |
| **KDE Plasma 5 & 6** (Fedora KDE) | **Tested** | Native (`StatusNotifierItem`) | Native | FreeDesktop Trash + Dolphin integration | Dynamic icon and context menu work without extra extensions. |
| **COSMIC** (Pop!_OS) | **Tested** | Native | Native | FreeDesktop Trash | Tested on Pop!_OS including modest hardware. |

---

## Distributions and Package Formats (`x86_64`)

| Distribution | Status | Recommended Format | System Dependencies |
| :--- | :--- | :--- | :--- |
| **Fedora Workstation / KDE** | **Tested** | Native `.rpm` (or `.AppImage`) | No external runtime required; optional packages only for multimedia preview. |
| **Pop!_OS** (GNOME / COSMIC) | **Tested** | Native `.deb` (or `.AppImage`) | Native `.deb` package with full desktop integration; or portable `.AppImage` (`--appimage-extract-and-run` if FUSE2 is missing). |
| **Ubuntu / Debian / Mint** | **Supported** | Native `.deb` (or `.AppImage`) | Installed via Official Installer or `sudo apt install`; portable `.AppImage` also supported. |
| **RHEL / Rocky / Alma / openSUSE** | **Supported** | Native `.rpm` (or `.AppImage`) | Standard `x86_64` `.rpm` package with `SHA-256` verification. |
| **Arch / Manjaro / Other distros** | **Supported** | `.AppImage` (`x86_64`) | Compatible with `libfuse2` (`fuse2`) or direct execution with `--appimage-extract-and-run`. |

---

## Display Servers and Features

| Feature | Wayland | X11 | Notes |
| :--- | :--- | :--- | :--- |
| **Main UI (Avalonia UI 11.3.8)** | Tested | Supported | Configurable zoom from `100%` to `300%` (`Ctrl++`, `Ctrl+-`, `Ctrl+0`). |
| **Drag & Drop** | Supported | Supported | Supported for uploading in *My Files* and importing `client_secret_*.json` in *Settings*. |
| **Single instance** | Tested | Tested | Prevents multiple concurrent instances from writing to the same SQLite database. |
| **Local REST API (`127.0.0.1:5080`)** | Independent | Independent | Runs on local *loopback* alongside the desktop application. |

---

## Optional Dependencies (Multimedia Preview)

The application works normally without these packages; they only enable advanced thumbnail generation and preview conversion:

| Package | Purpose |
| :--- | :--- |
| `poppler-utils` | PDF rendering and thumbnails |
| `ghostscript` | Complementary PDF/PostScript support |
| `ffmpeg` | Video/audio thumbnails, duration, and preview |
| `libreoffice` | Office document preview (`DOCX`, `XLSX`, `PPTX`, `ODT`, `ODS`, `ODP`) |
| `ImageMagick` | Auxiliary image processing |
