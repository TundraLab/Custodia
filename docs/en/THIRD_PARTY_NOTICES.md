# Third-Party Notices

**Custódia** is proprietary *source-available* software (All Rights Reserved), built on top of third-party open-source libraries and runtime components. Each component listed below remains governed strictly by its respective original license and copyright holders.

---

## 1. Libraries bundled in the desktop application (`Custodia.Desktop`, `Custodia.Core`, `Custodia.Api`)

| Component | Version | License | Purpose |
| :--- | :--- | :--- | :--- |
| **.NET Runtime & ASP.NET Core** (`Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`) | `10.0` | `MIT` | Cross-platform runtime and local loopback HTTP server (`127.0.0.1:5080`) |
| **Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent** | `11.3.8` | `MIT` | Desktop UI framework for Linux (Wayland and X11) |
| **Avalonia.Fonts.Inter** (**Inter** typeface) | `11.3.8` | `MIT` / `SIL OFL 1.1` | Embedded typography package for the interface |
| **CommunityToolkit.Mvvm** | `8.4.2` | `MIT` | MVVM source generators and commands |
| **Tmds.DBus.Protocol** | `0.21.3` | `MIT` | Native Linux D-Bus integration for notifications and system tray |
| **Google.Apis.Drive.v3** | `1.78.0.4327` | `Apache-2.0` | Official Google Drive API v3 SDK and OAuth 2.0 client |
| **Microsoft.Data.Sqlite** (with `SQLitePCLRaw` / SQLite) | `10.0.14` | `MIT` / `Apache-2.0` / `Public Domain` | Local relational database (`custodia.db`) |
| **Dapper** | `2.1.89` | `Apache-2.0` | Lightweight SQL mapping over SQLite |
| **Microsoft.Extensions.DependencyInjection / Logging** | `10.0.0` | `MIT` | Dependency injection container and structured logging |

---

## 2. Linux packaging and installer tooling

| Component | License | Notes |
| :--- | :--- | :--- |
| **AppImage type2-runtime** | `MIT` | AppImage container runtime embedded strictly inside distributed `.AppImage` artifacts |
| **PyGObject (`gi`), GTK 3 / GTK 4, and GLib** | `LGPL` | Host OS UI libraries dynamically loaded by the graphical installer |
| **nfpm** | `MIT` | Build-time packager used to produce `.deb` and `.rpm` artifacts |

---

## 3. Optional system utilities (not bundled)

To enable advanced multimedia previews on Linux, the app can invoke external system packages installed separately by the user via their distribution package manager (`apt` or `dnf`):

- `poppler-utils`, `ghostscript`, `ffmpeg`, `libreoffice`, and `ImageMagick`.

These packages are not redistributed inside Custódia binaries and remain governed by their distribution licenses.

---

## 4. Additional website libraries (`Custodia.Site`)

| Component | Version | License | Purpose |
| :--- | :--- | :--- | :--- |
| **Markdig** | `1.6.0` | `BSD-2-Clause` | Markdown rendering for the `/docs` portal |
| **LigerShark.WebOptimizer.Core** | `3.0.477` | `Apache-2.0` | CSS/JS bundling and optimization |

---

## 5. Attribution inquiries

If you notice any third-party attribution or license notice requiring correction, please write to `contato.tundralab@gmail.com`.
