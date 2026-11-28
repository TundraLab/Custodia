# Third-Party Notices

**Custódia target release:** `v4.0.0` (`.NET 10.0`)  
**Manifest sources:** direct dependencies declared in `src/Custodia.Desktop/Custodia.Desktop.csproj`, `src/Custodia.Core/Custodia.Core.csproj`, `src/Custodia.Api/Custodia.Api.csproj`, and `site/Custodia.Site.csproj` (full transitive tree inspectable via `dotnet list package --include-transitive`).

**Custódia** is proprietary *source-available* software (All Rights Reserved), built on top of third-party open-source libraries and runtime components. Each component listed below remains governed strictly by its respective original license and copyright holders.

---

## 1. Libraries bundled in the desktop application (`Custodia.Desktop`, `Custodia.Core`, `Custodia.Api`)

| Component | Version | License & Full Text | Official Repository | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **.NET Runtime & ASP.NET Core** (`Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`) | `10.0` | [`MIT`](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | [dotnet/runtime](https://github.com/dotnet/runtime) | Cross-platform runtime and local loopback HTTP server (`127.0.0.1:5080`) |
| **Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent** | `11.3.8` | [`MIT`](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) | Desktop UI framework for Linux (Wayland and X11) |
| **Avalonia.Fonts.Inter** (**Inter** typeface) | `11.3.8` | [`MIT`](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) / [`SIL OFL 1.1`](https://github.com/rsms/inter/blob/master/LICENSE.txt) | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) / [rsms/inter](https://github.com/rsms/inter) | Embedded typography package for the interface |
| **CommunityToolkit.Mvvm** | `8.4.2` | [`MIT`](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) | [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) | MVVM source generators and commands |
| **Tmds.DBus.Protocol** | `0.21.3` | [`MIT`](https://github.com/tmds/Tmds.DBus/blob/main/COPYING) | [tmds/Tmds.DBus](https://github.com/tmds/Tmds.DBus) | Native Linux D-Bus integration for notifications and system tray |
| **Google.Apis.Drive.v3** | `1.78.0.4327` | [`Apache-2.0`](https://github.com/googleapis/google-api-dotnet-client/blob/main/LICENSE) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) | Official Google Drive API v3 SDK and OAuth 2.0 client |
| **Microsoft.Data.Sqlite** (with `SQLitePCLRaw` / SQLite) | `10.0.14` | [`MIT`](https://github.com/dotnet/efcore/blob/main/LICENSE.txt) / [`Apache-2.0`](https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT) / [Public Domain](https://www.sqlite.org/copyright.html) | [dotnet/efcore](https://github.com/dotnet/efcore) / [sqlite.org](https://www.sqlite.org/) | Local relational database (`custodia.db`) |
| **Dapper** | `2.1.89` | [`Apache-2.0`](https://github.com/DapperLib/Dapper/blob/main/License.txt) | [DapperLib/Dapper](https://github.com/DapperLib/Dapper) | Lightweight SQL mapping over SQLite |
| **Microsoft.Extensions.DependencyInjection / Logging** | `10.0.0` | [`MIT`](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | [dotnet/runtime](https://github.com/dotnet/runtime) | Dependency injection container and structured logging |

---

## 2. Linux packaging and installer tooling

| Component | License & Full Text | Official Repository | Notes |
| :--- | :--- | :--- | :--- |
| **AppImage type2-runtime** | [`MIT`](https://github.com/AppImage/type2-runtime/blob/main/LICENSE) | [AppImage/type2-runtime](https://github.com/AppImage/type2-runtime) | AppImage container runtime embedded strictly inside distributed `.AppImage` artifacts |
| **PyGObject (`gi`), GTK 3 / GTK 4, and GLib** | [`LGPL-2.1+`](https://gitlab.gnome.org/GNOME/pygobject/-/blob/main/COPYING) | [GNOME/pygobject](https://gitlab.gnome.org/GNOME/pygobject) / [GNOME/gtk](https://gitlab.gnome.org/GNOME/gtk) | Host OS UI libraries dynamically loaded by the graphical installer |
| **nfpm** | [`MIT`](https://github.com/goreleaser/nfpm/blob/main/LICENSE.md) | [goreleaser/nfpm](https://github.com/goreleaser/nfpm) | Build-time packager used to produce `.deb` and `.rpm` artifacts |

---

## 3. Optional system utilities (not bundled)

To enable advanced multimedia previews on Linux, the app can invoke external system packages installed separately by the user via their distribution package manager (`apt` or `dnf`):

- [`poppler-utils`](https://gitlab.freedesktop.org/poppler/poppler) (`GPL-2.0+`), [`ghostscript`](https://github.com/ArtifexSoftware/ghostpdl) (`AGPL-3.0`), [`ffmpeg`](https://git.ffmpeg.org/ffmpeg.git) (`LGPL-2.1+` / `GPL`), [`libreoffice`](https://git.libreoffice.org/core) (`MPL-2.0`), and [`ImageMagick`](https://github.com/ImageMagick/ImageMagick) (`Apache-2.0` / ImageMagick License).

These packages are not redistributed inside Custódia binaries and remain governed by their distribution licenses.

---

## 4. Additional website libraries (`Custodia.Site`)

| Component | Version | License & Full Text | Official Repository | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **Markdig** | `1.6.0` | [`BSD-2-Clause`](https://github.com/xoofx/markdig/blob/master/license.txt) | [xoofx/markdig](https://github.com/xoofx/markdig) | Markdown rendering for the `/docs` portal |
| **LigerShark.WebOptimizer.Core** | `3.0.477` | [`Apache-2.0`](https://github.com/ligershark/WebOptimizer/blob/master/LICENSE) | [ligershark/WebOptimizer](https://github.com/ligershark/WebOptimizer) | CSS/JS bundling and optimization |

---

## 5. Attribution inquiries

If you notice any third-party attribution or license notice requiring correction, please write to `contato.tundralab@gmail.com`.
