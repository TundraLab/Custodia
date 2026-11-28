# Avisos de Componentes de Terceiros (Third-Party Notices)

O **Custódia** é um software proprietário (*source-available*, Todos os Direitos Reservados), mas utiliza bibliotecas e componentes de código aberto em seu funcionamento. Todos os componentes listados abaixo permanecem integralmente regidos por suas respectivas licenças originais e direitos de seus autores.

---

## 1. Bibliotecas embutidas no aplicativo desktop (`Custodia.Desktop`, `Custodia.Core`, `Custodia.Api`)

| Componente | Versão | Licença | Finalidade |
| :--- | :--- | :--- | :--- |
| **.NET Runtime e ASP.NET Core** (`Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`) | `10.0` | `MIT` | Plataforma de execução multiplataforma e servidor HTTP loopback (`127.0.0.1:5080`) |
| **Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent** | `11.3.8` | `MIT` | Framework de interface gráfica desktop para Linux (Wayland e X11) |
| **Avalonia.Fonts.Inter** (Fonte **Inter**) | `11.3.8` | `MIT` / `SIL OFL 1.1` | Empacotamento e família tipográfica padrão da interface |
| **CommunityToolkit.Mvvm** | `8.4.2` | `MIT` | Infraestrutura MVVM (`ObservableProperty`, `RelayCommand`) |
| **Tmds.DBus.Protocol** | `0.21.3` | `MIT` | Comunicação D-Bus nativa para notificações e bandeja do sistema no Linux |
| **Google.Apis.Drive.v3** | `1.78.0.4327` | `Apache-2.0` | Cliente oficial da Google Drive API v3 e fluxo OAuth 2.0 |
| **Microsoft.Data.Sqlite** (com `SQLitePCLRaw` / SQLite) | `10.0.14` | `MIT` / `Apache-2.0` / `Public Domain` | Banco de dados relacional local (`custodia.db`) |
| **Dapper** | `2.1.89` | `Apache-2.0` | Micro-ORM de alto desempenho sobre SQLite |
| **Microsoft.Extensions.DependencyInjection / Logging** | `10.0.0` | `MIT` | Injeção de dependências e registro estruturado de logs |

---

## 2. Ferramentas usadas no empacotamento e instalador Linux

| Componente | Licença | Observação |
| :--- | :--- | :--- |
| **AppImage type2-runtime** | `MIT` | Runtime de contêiner AppImage embutido apenas nos artefatos `.AppImage` distribuídos |
| **PyGObject (`gi`), GTK 3 / GTK 4 e GLib** | `LGPL` | Bibliotecas gráficas do sistema operacional carregadas dinamicamente pelo instalador |
| **nfpm** | `MIT` | Ferramenta utilizada apenas em tempo de compilação para gerar pacotes `.deb` e `.rpm` |

---

## 3. Dependências opcionais do sistema (não distribuídas no pacote)

Para habilitar miniaturas e pré-visualizações multimídia avançadas no Linux, o aplicativo pode invocar ferramentas instaladas separadamente pelo próprio usuário por meio do gerenciador de pacotes da distribuição (`apt` ou `dnf`):

- `poppler-utils`, `ghostscript`, `ffmpeg`, `libreoffice` e `ImageMagick`.

Esses pacotes não acompanham o binário do Custódia e permanecem sob suas licenças originais fornecidas pela respectiva distribuição Linux.

---

## 4. Bibliotecas adicionais do site oficial (`Custodia.Site`)

| Componente | Versão | Licença | Finalidade |
| :--- | :--- | :--- | :--- |
| **Markdig** | `1.6.0` | `BSD-2-Clause` | Renderização de documentação Markdown no portal `/docs` |
| **LigerShark.WebOptimizer.Core** | `3.0.477` | `Apache-2.0` | Otimização e entrega de CSS/JS estáticos do site |

---

## 5. Contato sobre atribuições e conformidade

Caso identifique qualquer aviso de atribuição que precise de atualização ou complementação, entre em contato pelo e-mail oficial: `contato.tundralab@gmail.com`.
