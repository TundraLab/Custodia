# Avisos de Componentes de Terceiros (Third-Party Notices)

**Versão de referência do Custódia:** `v4.0.0` (`.NET 10.0`)  
**Origem do inventário:** dependências diretas declaradas em `src/Custodia.Desktop/Custodia.Desktop.csproj`, `src/Custodia.Core/Custodia.Core.csproj`, `src/Custodia.Api/Custodia.Api.csproj` e `site/Custodia.Site.csproj` (árvore completa reproduzível com `dotnet list package --include-transitive`).

O **Custódia** é um software proprietário (*source-available*, Todos os Direitos Reservados), mas utiliza bibliotecas e componentes de código aberto em seu funcionamento. Todos os componentes listados abaixo permanecem integralmente regidos por suas respectivas licenças originais e direitos de seus autores.

---

## 1. Bibliotecas embutidas no aplicativo desktop (`Custodia.Desktop`, `Custodia.Core`, `Custodia.Api`)

| Componente | Versão | Licença e Texto Integral | Repositório Oficial | Finalidade |
| :--- | :--- | :--- | :--- | :--- |
| **.NET Runtime e ASP.NET Core** (`Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`) | `10.0` | [`MIT`](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | [dotnet/runtime](https://github.com/dotnet/runtime) | Plataforma de execução multiplataforma e servidor HTTP loopback (`127.0.0.1:5080`) |
| **Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent** | `11.3.8` | [`MIT`](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) | Framework de interface gráfica desktop para Linux (Wayland e X11) |
| **Avalonia.Fonts.Inter** (Fonte **Inter**) | `11.3.8` | [`MIT`](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) / [`SIL OFL 1.1`](https://github.com/rsms/inter/blob/master/LICENSE.txt) | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) / [rsms/inter](https://github.com/rsms/inter) | Empacotamento e família tipográfica padrão da interface |
| **CommunityToolkit.Mvvm** | `8.4.2` | [`MIT`](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) | [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) | Infraestrutura MVVM (`ObservableProperty`, `RelayCommand`) |
| **Tmds.DBus.Protocol** | `0.21.3` | [`MIT`](https://github.com/tmds/Tmds.DBus/blob/main/COPYING) | [tmds/Tmds.DBus](https://github.com/tmds/Tmds.DBus) | Comunicação D-Bus nativa para notificações e bandeja do sistema no Linux |
| **Google.Apis.Drive.v3** | `1.78.0.4327` | [`Apache-2.0`](https://github.com/googleapis/google-api-dotnet-client/blob/main/LICENSE) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) | Cliente oficial da Google Drive API v3 e fluxo OAuth 2.0 |
| **Microsoft.Data.Sqlite** (com `SQLitePCLRaw` / SQLite) | `10.0.14` | [`MIT`](https://github.com/dotnet/efcore/blob/main/LICENSE.txt) / [`Apache-2.0`](https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT) / [Public Domain](https://www.sqlite.org/copyright.html) | [dotnet/efcore](https://github.com/dotnet/efcore) / [sqlite.org](https://www.sqlite.org/) | Banco de dados relacional local (`custodia.db`) |
| **Dapper** | `2.1.89` | [`Apache-2.0`](https://github.com/DapperLib/Dapper/blob/main/License.txt) | [DapperLib/Dapper](https://github.com/DapperLib/Dapper) | Micro-ORM de alto desempenho sobre SQLite |
| **Microsoft.Extensions.DependencyInjection / Logging** | `10.0.0` | [`MIT`](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | [dotnet/runtime](https://github.com/dotnet/runtime) | Injeção de dependências e registro estruturado de logs |

---

## 2. Ferramentas usadas no empacotamento e instalador Linux

| Componente | Licença e Texto Integral | Repositório Oficial | Observação |
| :--- | :--- | :--- | :--- |
| **AppImage type2-runtime** | [`MIT`](https://github.com/AppImage/type2-runtime/blob/main/LICENSE) | [AppImage/type2-runtime](https://github.com/AppImage/type2-runtime) | Runtime de contêiner AppImage embutido apenas nos artefatos `.AppImage` distribuídos |
| **PyGObject (`gi`), GTK 3 / GTK 4 e GLib** | [`LGPL-2.1+`](https://gitlab.gnome.org/GNOME/pygobject/-/blob/main/COPYING) | [GNOME/pygobject](https://gitlab.gnome.org/GNOME/pygobject) / [GNOME/gtk](https://gitlab.gnome.org/GNOME/gtk) | Bibliotecas gráficas do sistema operacional carregadas dinamicamente pelo instalador |
| **nfpm** | [`MIT`](https://github.com/goreleaser/nfpm/blob/main/LICENSE.md) | [goreleaser/nfpm](https://github.com/goreleaser/nfpm) | Ferramenta utilizada apenas em tempo de compilação para gerar pacotes `.deb` e `.rpm` |

---

## 3. Dependências opcionais do sistema (não distribuídas no pacote)

Para habilitar miniaturas e pré-visualizações multimídia avançadas no Linux, o aplicativo pode invocar ferramentas instaladas separadamente pelo próprio usuário por meio do gerenciador de pacotes da distribuição (`apt` ou `dnf`):

- [`poppler-utils`](https://gitlab.freedesktop.org/poppler/poppler) (`GPL-2.0+`), [`ghostscript`](https://github.com/ArtifexSoftware/ghostpdl) (`AGPL-3.0`), [`ffmpeg`](https://git.ffmpeg.org/ffmpeg.git) (`LGPL-2.1+` / `GPL`), [`libreoffice`](https://git.libreoffice.org/core) (`MPL-2.0`) e [`ImageMagick`](https://github.com/ImageMagick/ImageMagick) (`Apache-2.0` / ImageMagick License).

Esses pacotes não acompanham o binário do Custódia e permanecem sob suas licenças originais fornecidas pela respectiva distribuição Linux.

---

## 4. Bibliotecas adicionais do site oficial (`Custodia.Site`)

| Componente | Versão | Licença e Texto Integral | Repositório Oficial | Finalidade |
| :--- | :--- | :--- | :--- | :--- |
| **Markdig** | `1.6.0` | [`BSD-2-Clause`](https://github.com/xoofx/markdig/blob/master/license.txt) | [xoofx/markdig](https://github.com/xoofx/markdig) | Renderização de documentação Markdown no portal `/docs` |
| **LigerShark.WebOptimizer.Core** | `3.0.477` | [`Apache-2.0`](https://github.com/ligershark/WebOptimizer/blob/master/LICENSE) | [ligershark/WebOptimizer](https://github.com/ligershark/WebOptimizer) | Otimização e entrega de CSS/JS estáticos do site |

---

## 5. Contato sobre atribuições e conformidade

Caso identifique qualquer aviso de atribuição que precise de atualização ou complementação, entre em contato pelo e-mail oficial: `contato.tundralab@gmail.com`.
