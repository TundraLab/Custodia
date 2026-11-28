# Matriz de Compatibilidade Linux

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](en/COMPATIBILITY.md)

O Custódia é construído em **.NET 10.0** + **Avalonia UI 11.3.8** para Linux `x86_64`, com foco em compatibilidade ampla entre ambientes gráficos e distribuições. Esta matriz diferencia explicitamente o que foi **efetivamente testado** do que possui **suporte declarado pela arquitetura**.

---

## Ambientes de Desktop

| Ambiente | Status | Bandeja do Sistema | Notificações D-Bus | Lixeira / Gerenciador de Arquivos | Observações |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **GNOME** (Fedora Workstation / Pop!_OS) | **Testado** | Via extensão `AppIndicator` | Nativo | FreeDesktop Trash + integração Files | No Fedora Workstation, instale `gnome-shell-extension-appindicator` e faça logout/login na primeira vez. |
| **KDE Plasma 5 e 6** (Fedora KDE) | **Testado** | Nativo (`StatusNotifierItem`) | Nativo | FreeDesktop Trash + integração Dolphin | Ícone dinâmico e menu de contexto funcionam sem extensões extras. |
| **COSMIC** (Pop!_OS) | **Testado** | Nativo | Nativo | FreeDesktop Trash | Testado em Pop!_OS incluindo hardware modesto. |

---

## Distribuições e Formatos de Pacote (`x86_64`)

| Distribuição | Status | Formato Recomendado | Dependências do Sistema |
| :--- | :--- | :--- | :--- |
| **Fedora Workstation / KDE** | **Testado** | `.rpm` nativo (ou `.AppImage`) | Nenhum runtime externo exigido; opcionais apenas para pré-visualização multimídia. |
| **Pop!_OS** (GNOME / COSMIC) | **Testado** | `.deb` nativo (ou `.AppImage`) | Pacote `.deb` com atalhos nativos no sistema; ou `.AppImage` portátil (`--appimage-extract-and-run` sem FUSE2). |
| **Ubuntu / Debian / Mint** | **Suportado** | `.deb` nativo (ou `.AppImage`) | Pacote `.deb` instalado pelo Instalador Oficial ou `sudo apt install`; alternativa portátil em `.AppImage`. |
| **RHEL / Rocky / Alma / openSUSE** | **Suportado** | `.rpm` nativo (ou `.AppImage`) | Pacote `.rpm` padrão `x86_64` com verificação `SHA-256`. |
| **Arch / Manjaro / Outras distros** | **Suportado** | `.AppImage` (`x86_64`) | Compatível com `libfuse2` (`fuse2`) ou execução direta com `--appimage-extract-and-run`. |

---

## Servidores Gráficos e Recursos

| Recurso | Wayland | X11 | Notas |
| :--- | :--- | :--- | :--- |
| **Interface principal (Avalonia UI 11.3.8)** | Testado | Suportado | Zoom configurável de `100%` a `300%` (`Ctrl++`, `Ctrl+-`, `Ctrl+0`). |
| **Arrastar e soltar (Drag & Drop)** | Suportado | Suportado | Suportado para upload em *Meus Arquivos* e importação de `client_secret_*.json` em *Configurações*. |
| **Instância única** | Testado | Testado | Evita múltiplas instâncias concorrentes escrevendo no mesmo banco SQLite. |
| **API REST Local (`127.0.0.1:5080`)** | Independente | Independente | Roda em *loopback* local junto com a aplicação desktop. |

---

## Dependências Opcionais (Pré-visualização Multimídia)

O aplicativo funciona normalmente sem esses pacotes; eles habilitam apenas geração avançada de miniaturas e conversão para pré-visualização:

| Pacote | Finalidade |
| :--- | :--- |
| `poppler-utils` | Renderização e miniaturas de PDFs |
| `ghostscript` | Suporte complementar a PDFs/PostScript |
| `ffmpeg` | Miniaturas, duração e pré-visualização de vídeo/áudio |
| `libreoffice` | Pré-visualização de documentos Office (`DOCX`, `XLSX`, `PPTX`, `ODT`, `ODS`, `ODP`) |
| `ImageMagick` | Processamento auxiliar de imagens |
