# Custódia v4.0.0

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](docs/en/README.md)

![Custódia by TundraLab - Meus arquivos. Sempre seguros.](docs/images/custodia-promo-pt.jpg)

[![Release](https://img.shields.io/badge/Release-v4.0.0-2563EB?style=flat-square&logo=github&labelColor=1D2024)](https://github.com/TundraLab/Custodia/releases)
[![Testes xUnit](https://img.shields.io/badge/Testes%20xUnit-94%20aprovados-16A34A?style=flat-square&labelColor=1D2024)](tests/Custodia.Tests)
[![Linux](https://img.shields.io/badge/Linux-x86__64%20(Instalador%20%7C%20.deb%20%7C%20.rpm%20%7C%20.AppImage)-334155?style=flat-square&logo=linux&logoColor=white&labelColor=1D2024)](#instalacao)
[![.NET 10](https://img.shields.io/badge/.NET%2010-Avalonia%2011.3.8-3B82F6?style=flat-square&logo=dotnet&logoColor=white&labelColor=1D2024)](https://dotnet.microsoft.com/)
[![Licença](https://img.shields.io/badge/Licen%C3%A7a-All%20Rights%20Reserved-475569?style=flat-square&labelColor=1D2024)](LICENSE)
[![Privacidade](https://img.shields.io/badge/Privacidade-PT-0F766E?style=flat-square&labelColor=1D2024)](PRIVACY.md)
[![Termos de Uso](https://img.shields.io/badge/Termos-PT-1D4ED8?style=flat-square&labelColor=1D2024)](TERMS.md)

> **Em desenvolvimento desde:** 2026  
> **Status:** estável, com releases oficiais ([GitHub Releases](https://github.com/TundraLab/Custodia/releases))  
> **Plataforma:** Linux `x86_64`  
> **Objetivo:** sincronização segura e intuitiva com o Google Drive

**Meus arquivos. Sempre seguros.** Cliente desktop nativo para Linux focado em sincronização de pastas, backups locais, gerenciamento multimídia e automação local com privacidade direto no Google Drive.

## Qualidade

- **Build e distribuição:** compilação limpa em `.NET 10.0` para Linux `x86_64` com pacotes nativos **`.deb`**, **`.rpm`**, **`.AppImage`** e instalador gráfico leve **`.run`** (~30 KB), todos verificados por SHA-256
- **Suíte de testes:** **94 testes unitários e de interface xUnit aprovados (`0` falhas)** cobrindo motor de sincronização, fila e retomada de transferências, persistência SQLite, isolamento de contas OAuth2, atualizador, sanitização de suporte, aceite de termos/privacidade e e-mails de ciclo de vida
- **Plataforma principal:** Linux `x86_64` (Fedora Workstation/KDE, Pop!_OS GNOME/COSMIC e KDE Plasma 5/6)
- **Documentação técnica:** OpenAPI `3.0.3` em tempo real (`GET /api/v1/openapi.json`), guia [docs/API.md](docs/API.md), [Custodia-API.pdf](Custodia-API.pdf) e histórico completo em [CHANGELOG.md](CHANGELOG.md)

## Documentação

> Disponível em **[🇧🇷 Português](README.md)** e **[🇺🇸 English](docs/en/README.md)**.

| Guia | Conteúdo |
| :--- | :--- |
| **[Configuração OAuth2](docs/OAUTH_SETUP.md)** | Passo a passo ilustrado para criar o `credentials.json` no Google Cloud Console e conectar sua conta |
| **[Compatibilidade Linux](docs/COMPATIBILITY.md)** | Matriz de recursos testados vs. suportados em GNOME, KDE Plasma 5/6, COSMIC, Wayland e X11 |
| **[Segurança e Privacidade](docs/SECURITY.md)** | Modelo BYO-OAuth2, permissões `0700`/`0600`, renovação de tokens e superfície da API local |
| **[Perguntas Frequentes (FAQ)](docs/FAQ.md)** | Modo offline, política de conflitos, restauração da Lixeira, múltiplas contas e remoção total |
| **[Solução de Problemas](docs/TROUBLESHOOTING.md)** | Diagnóstico para `.rpm`, `.AppImage`/FUSE, bandeja no GNOME, D-Bus, erro `403` e pré-visualização |
| **[API REST Local](docs/API.md)** | Endpoints em `127.0.0.1:5080`, OpenAPI `3.0.3`, exemplos em Python e integração com agentes de IA |

## Prévia visual

![Prévia das telas do Custódia](docs/images/preview-showcase.png)

- **[Assistir vídeo de divulgação](docs/videos/custodia-divulgacao.mp4)** (`MP4`, 21 MB)

<details>
<summary>Ver todas as 6 telas individuais</summary>

| Tela | Prévia |
| :--- | :--- |
| **Meus Arquivos** | ![Meus Arquivos](docs/images/preview-01-meu-driver.png) |
| **Pastas de Sincronização** | ![Pastas de Sincronização](docs/images/preview-02-pastas.png) |
| **Fila de Transferências** | ![Fila de Transferências](docs/images/preview-03-transferencias.png) |
| **Logs de Atividade** | ![Logs de Atividade](docs/images/preview-04-logs.png) |
| **Configurações** | ![Configurações](docs/images/preview-05-configuracoes.png) |
| **Sobre** | ![Sobre](docs/images/preview-06-sobre.png) |

</details>

## Sistemas e formatos

- Testado em **Fedora**, **Ubuntu**, **Pop!_OS**, **Linux Mint** e **Debian** (incluindo hardware modesto), com suporte aos ambientes **GNOME**, **COSMIC** e **KDE Plasma 5/6** — veja a matriz completa em **[docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)**.
- **Formatos distribuídos:** instalador gráfico leve **`.run`** (~30 KB, escolhe versão e formato e se apaga ao concluir), pacote nativo **`.deb`** (Ubuntu / Debian / Pop!_OS / Linux Mint), pacote nativo **`.rpm`** (Fedora / RHEL / openSUSE) e **`.AppImage` (`x86_64`)** portátil, todos verificados por **SHA-256** (`SHA256SUMS.txt`).

### Requisitos do sistema

| Recurso | Mínimo | Recomendado |
| :--- | :--- | :--- |
| **Sistema / Arquitetura** | Linux `x86_64` (Wayland ou X11) | Fedora, Ubuntu, Pop!_OS ou Linux Mint (GNOME/COSMIC/KDE) |
| **Processador** | Dual-core `x86_64` | Quad-core `x86_64` |
| **Memória RAM** | `2 GB` | `4 GB` ou mais |
| **Espaço em disco** | `200 MB` livres *(+ espaço dos seus arquivos)* | SSD com folga para cache e arquivos sincronizados |
| **Dependências extras** | Nenhuma no `.deb`/`.rpm`; `libfuse2` no `.AppImage` *(ou `--appimage-extract-and-run`)* | `poppler-utils`, `ghostscript`, `ffmpeg`, `libreoffice`, `ImageMagick` *(para pré-visualização completa)* |

## O que o aplicativo faz

- **Meus Arquivos redesenhado com compartilhamento e favoritos:** navegue por **Google Drive**, **Sincronizados**, **Favoritos**, **Compartilhados comigo** e **Lixeira** em Lista ou Grade com miniaturas rápidas fora da thread da UI, ícones proprietários da TundraLab, seleção múltipla, **Salvar como...** para escolher a pasta destino ao baixar, upload por arrastar e soltar e painel de **Compartilhamento e permissões** (link público de leitura/edição, revogação e convites por e-mail).
- **Personalização de pastas (cor, símbolo e apelido):** personalize diretamente com o botão direito em **Meus Arquivos** ou na aba **Pastas de Sincronização**, com cores e ícones preservados entre sessões.
- **Pré-visualização multimídia integrada:** imagens, vídeos (inclusive **Google Vids** via download direto), áudios (forma de onda e player interno), PDFs com navegação por páginas, documentos Office (`DOCX`, `XLSX`, `PPTX`, `ODT`, `ODS`, `ODP`), arquivos Google Workspace (Docs, Sheets, Slides, Drawings) e texto/código.
- **Fila de Transferências com filtros, retomada e limite de banda:** filtre por **Todos**, **Envios**, **Downloads** e **Falhas**, retome itens individuais ou todas as falhas com um clique, conte com retomada persistente de envios/downloads grandes após queda de internet ou fechamento do app e configure limite opcional de velocidade em **Configurações**.
- **Sincronização flexível por pasta:** modo **Bidirecional (`PC <-> Drive`)** ou **Backup Unidirecional (`PC -> Drive`)**, mapeamento múltiplo preservando nomes e subpastas vazias, validação por hash MD5/SHA256, política de conflitos, padrões ignorados e monitoramento contínuo em tempo real.
- **Lixeira segura:** exclusões em pastas sincronizadas enviam os itens para a Lixeira do Google Drive ou para a Lixeira do sistema (GNOME Files / KDE Dolphin); exclusões permanentes ocorrem apenas manualmente na Lixeira.
- **Gestão avançada de múltiplas contas:** pastas dedicadas por e-mail em `~/.config/custodia/contas/<email>/`, detecção automática de `client_secret_*.json` em `~/Downloads`, importação por arrastar e soltar em Configurações, alternância rápida e preservação automática das pastas mapeadas.
- **Configurações com salvamento automático, temas e cores:** alterações salvas instantaneamente, tema **Escuro**, **Claro** ou **Automático (Sistema)**, 6 cores de destaque, PT-BR / EN-US, zoom de 100% a 300%, limpador periódico de RAM e 6 toques sintetizados nas notificações.
- **Sobre, atualizador e GitHub:** verificação agendada de novas versões com instalador gráfico embutido, atalho visível para o GitHub e formulário unificado para relatar erros ou enviar sugestões.

## Arquitetura

```text
  Interface Desktop (Avalonia UI — MVVM, Bandeja e Notificações)
                          │
                          ▼
   Núcleo de Sincronização (Sync Engine, OAuth2, Hashes e Updater)
     ├──▶ Google Drive API v3 (escopo drive direto da máquina do usuário)
     ├──▶ SQLite local (~/.local/share/custodia/custodia.db)
     ├──▶ Serviço de E-mail TundraLab (contato.tundralab@gmail.com)
     └──▶ API REST Local (ASP.NET Core + OpenAPI 3.0.3 em 127.0.0.1:5080)
```

- **Camada Desktop:** interface em Avalonia UI 11.3.8 com padrão MVVM, instância única garantida, ícone dinâmico na bandeja via D-Bus/AppIndicator e pré-visualização multimídia.
- **Núcleo de Sincronização:** motor bidirecional e unidirecional, resolução de conflitos, validação de integridade MD5/SHA256, monitoramento local contínuo, cofre OAuth2 por conta e verificador de atualizações oficiais.
- **API REST Local:** servidor HTTP Minimal API embarcado em *loopback* local (`127.0.0.1`) expondo endpoints REST e schema OpenAPI `3.0.3` para integração segura com scripts locais e agentes de IA.
- **Fluxo de dados:** mudanças locais ou remotas são comparadas com o estado persistido no SQLite e sincronizadas diretamente com a Google Drive API v3, atualizando em tempo real a fila de transferências e os registros locais.

## Decisões técnicas

- **C# e `.NET 10.0`:** escolhido pela tipagem forte, excelente suporte a concorrência assíncrona controlada e publicação *self-contained* estável para Linux `x86_64`.
- **Avalonia UI (`11.3.8`):** escolhida para entregar uma UI fluida e consistente entre GNOME, COSMIC e KDE Plasma (X11 e Wayland) com baixo consumo de memória e arquitetura MVVM testável.
- **SQLite + Dapper:** escolhidos por oferecerem banco relacional local leve, transacional (ACID) e de baixa latência sem exigir serviço externo instalado no PC do usuário.
- **Google Drive API v3 direta:** escolhida para que arquivos e tokens trafeguem somente entre o computador do usuário e o Google Drive, sem servidores intermediários.
- **Pacotes `.deb`, `.rpm`, `.AppImage` e instalador leve `.run` (`x86_64`):** `.deb` e `.rpm` entregam integração nativa no Ubuntu/Debian/Mint/Pop!_OS e Fedora/openSUSE, o `.AppImage` cobre qualquer outra distro e o instalador `.run` (~30 KB) baixa e instala a versão escolhida sem exigir terminal.
- **API Local em `127.0.0.1:5080`:** restringe automações e agentes de IA à própria máquina, permitindo operar arquivos sem expor tokens OAuth nem abrir portas na rede externa.

## Modos de sincronização

Cada pasta mapeada pode operar em **Espelho Bidirecional (`PC <-> Drive`)** ou **Backup Unidirecional (`PC -> Drive`)**:

| Ação feita | Bidirecional (`PC <-> Drive`) | Backup Unidirecional (`PC -> Drive`) |
| :--- | :--- | :--- |
| Apagou no **PC** | Vai para Lixeira do Drive | Vai para Lixeira do Drive |
| Apagou no **Drive** | Vai para Lixeira do PC | Fica salvo no PC e não volta ao Drive |
| Adicionou no **Drive** | Baixa para o PC | Não baixa para o PC |
| Adicionou/editou no **PC** | Envia para o Drive | Envia para o Drive |

## Instalação

### 1. Instalador Gráfico Leve (`Custódia by TundraLab`)

Um instalador gráfico leve (~30 KB, sem AppImage nem FUSE) onde você escolhe a versão do lançamento e o formato de instalação (`.deb`, `.rpm` ou `.AppImage`):

1. Baixe o instalador oficial: **[Custodia-Installer-x86_64.run](https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-Installer-x86_64.run)**
2. Clique com o botão direito no arquivo → **Propriedades** → marque **Permitir execução** *(ou `chmod +x Custodia-Installer-x86_64.run`)* e abra o arquivo.
3. Escolha a versão e o formato (`.deb` para Ubuntu/Pop!_OS/Mint/Debian, `.rpm` para Fedora/RHEL/openSUSE ou `.AppImage` portátil). O instalador baixa o pacote escolhido, valida via **SHA-256**, instala em segundo plano, cria os atalhos e **se apaga automaticamente ao concluir**.

<details>
<summary>Outros métodos de instalação (pacotes diretos .deb / .rpm / .AppImage, terminal e opções avançadas)</summary>

#### Download direto de pacote

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
- **AppImage (`x86_64`, portátil):**
  ```bash
  curl -fLO https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-4.0.0-x86_64.AppImage
  chmod +x Custodia-4.0.0-x86_64.AppImage
  ./Custodia-4.0.0-x86_64.AppImage
  ```

#### Instalador via terminal (`install-custodia.sh`)

```bash
curl -fsSL https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-Installer-x86_64.sh -o install-custodia.sh
chmod +x install-custodia.sh
./install-custodia.sh
```

- **Atalhos do `install-custodia.sh`:**
  - `./install-custodia.sh --gui`: abre o instalador gráfico do Custódia.
  - `./install-custodia.sh --background`: instala ou atualiza silenciosamente em segundo plano.
  - `./install-custodia.sh --update`: detecta sua instalação atual (`.rpm` ou `.AppImage`) e atualiza para a release mais recente.
  - `./install-custodia.sh --rpm`: baixa, valida `SHA-256` e instala o `.rpm`.
  - `./install-custodia.sh --appimage`: baixa, valida `SHA-256` e instala o `.AppImage` integrado ao usuário.
  - `./install-custodia.sh --download-only`: baixa os pacotes e `SHA256SUMS.txt` na pasta atual.
  - `./install-custodia.sh --uninstall`: remove a instalação local ou `.rpm` mantendo suas configurações.
- **AppImage sem FUSE:** `./Custodia-4.0.0-x86_64.AppImage --appimage-extract-and-run`
- **Pacotes opcionais para pré-visualização completa (PDF, Office, áudio e vídeo):**
  - Fedora: `sudo dnf install poppler-utils ghostscript ffmpeg libreoffice ImageMagick`
  - Pop!_OS / Ubuntu: `sudo apt install poppler-utils ghostscript ffmpeg libreoffice imagemagick`
- **Ícone da nuvem na barra superior do GNOME Workstation** *(no KDE Plasma e Pop!_OS já funciona nativamente)*:
  ```bash
  sudo dnf install gnome-shell-extension-appindicator
  gsettings set org.gnome.shell enabled-extensions "['appindicatorsupport@rgcjonas.gmail.com']"
  ```
  *(Faça logout e login no GNOME após habilitar pela primeira vez.)*

</details>

## Como configurar o Google Drive (`credentials.json`)

Para conectar sua conta diretamente ao Google sem servidores intermediários, crie suas credenciais OAuth2 Desktop no Google Cloud Console:

1. Ative a **Google Drive API** no seu projeto do Google Cloud Console.
2. Configure a **Tela de permissão OAuth** (*Google Auth Platform* → **Externo**).
3. Adicione seu e-mail em **Público-alvo → Usuários de teste** *(obrigatório enquanto o app estiver em modo Testando)*.
4. Em **Clientes → + Criar cliente**, escolha **App para computador** (*Desktop app*) e baixe o arquivo `client_secret_*.json`.
5. Importe o JSON no Custódia (detectado automaticamente em `~/Downloads` ou via arrastar e soltar em **Configurações**) e autorize no navegador.

> **Guia completo com capturas de tela de cada etapa:** **[docs/OAUTH_SETUP.md](docs/OAUTH_SETUP.md)**  
> Enfrentando erro `403: access_denied` ou problemas na bandeja/AppImage? Consulte **[docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)** e **[docs/FAQ.md](docs/FAQ.md)**.

## Segurança e Privacidade

Consulte a arquitetura completa em **[docs/SECURITY.md](docs/SECURITY.md)**.

- **Isolamento local e permissões restritas (`0700` / `0600`):**
  - `~/.config/custodia/settings.json`: preferências gerais e personalizações visuais de pastas.
  - `~/.config/custodia/contas/<email>/`: diretório organizado por conta contendo `credentials.json`, `conta.json` e `pastas-sincronizadas.json` com acesso restrito ao usuário (`0600`).
  - `~/.local/share/custodia/tokens/`: tokens OAuth2 armazenados localmente com permissão `0600`.
  - `~/.local/share/custodia/custodia.db`: banco SQLite local com mapeamentos, metadados e logs de atividade.
- **Como os tokens são renovados:** o SDK oficial do Google utiliza o `refresh_token` salvo localmente (`0600`) para obter novos `access_token` temporários diretamente com o endpoint OAuth2 do Google, sem intervenção manual nem armazenamento de senha.
- **O JSON OAuth não contém sua senha:** o `client_secret_*.json` identifica apenas o ID de aplicativo criado por você no Google Cloud Console; sua senha é digitada apenas na página oficial de login do Google no navegador.
- **O que acontece ao revogar/desconectar uma conta:** o token local é invalidado/removido, o diretório `~/.config/custodia/contas/<email>/` tem a sessão encerrada e você escolhe explicitamente na janela de confirmação se deseja manter ou excluir também as pastas configuradas vinculadas àquele e-mail.
- **Superfície da API local:** o servidor REST embarcado escuta exclusivamente em `127.0.0.1:5080` (nunca em `0.0.0.0`) e **não deve ser exposto para fora do dispositivo** (ex.: via proxy reverso ou túnel público).
- **Como remover completamente todos os dados locais:**
  ```bash
  ./install-custodia.sh --uninstall
  rm -rf ~/.config/custodia ~/.local/share/custodia ~/.cache/custodia
  ```

## Privacidade, Conformidade Google OAuth e Termos de Uso

Documentos oficiais disponíveis no repositório: **[Política de Privacidade](PRIVACY.md)** | **[Termos de Uso](TERMS.md)** | **[Licença Proprietária](LICENSE)**.

- **Classificação do escopo e modelo BYO-OAuth:** o Custódia solicita o escopo `https://www.googleapis.com/auth/drive` (classificado como **escopo sensível** pela [documentação oficial do Google](https://developers.google.com/identity/protocols/oauth2/scopes)). Na configuração atual, cada usuário cria e utiliza suas próprias credenciais OAuth no Google Cloud Console estritamente para **uso pessoal/individual** — isso **não** dispensa o processo formal de [Verificação OAuth do Google](https://support.google.com/cloud/answer/9110914) caso um Client ID compartilhado venha a ser distribuído publicamente pela TundraLab.
- **Uso Limitado (Limited Use):** o uso de dados obtidos das APIs do Google segue a [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy). Seus arquivos do Drive, pastas, metadados, hashes e tokens OAuth permanecem exclusivamente na sua máquina e nunca são enviados a servidores da TundraLab.
- **E-mails opcionais de ciclo de vida (`contato.tundralab@gmail.com`):** nenhum escopo do Gmail é solicitado no seu projeto Google Cloud. Caso você mantenha os alertas de conta habilitados no Custódia, apenas `recipientEmail`, `recipientName`, `eventType` (`connect` ou `revoke`), `appName`, `appVersion`, `language` e `timestampUtc` são enviados ao serviço de disparo oficial da TundraLab exclusivamente para notificar conexão ou revogação da conta — recurso opcional que pode ser recusado logo na primeira execução ou alterado a qualquer momento em **Sobre -> Rever termo inicial**.
- **Aviso ao baixar/instalar e na primeira execução:** tanto o instalador de terminal (`install-custodia.sh`) quanto o aplicativo desktop exibem o resumo da Política de Privacidade e dos Termos de Uso com aceite explícito antes do primeiro uso.

## API REST Local (`127.0.0.1:5080`) e Agentes de IA

Guia completo com exemplos, schemas de Function Calling e cliente Python: **[Custodia-API.pdf](Custodia-API.pdf)** e **[docs/API.md](docs/API.md)**.

- `GET /api/v1/status`: conta ativa, uso de cota, transferências e pastas sincronizadas.
- `GET /api/v1/openapi.json`: especificação OpenAPI 3.0.3 para autodescoberta por agentes de IA.
- `POST /api/v1/sync/trigger`: dispara sincronização total ou por `folderId` (`{"folderId": "...", "waitForCompletion": false}`).
- `GET /api/v1/files/search?q=relatorio&includeRemote=true`: busca no SQLite local e opcionalmente no Drive remoto.
- `GET /api/v1/files/list?folderId=root`: lista pastas e arquivos no Drive.
- `GET /api/v1/files/trash?limit=50`: lista itens na Lixeira do Drive.
- `POST /api/v1/folders`: cria uma pasta no Drive (`{"name": "MinhaPasta", "parentFolderId": "root"}`).
- `POST /api/v1/files/upload`: envia um arquivo local para o Drive (`{"localPath": "/caminho/arquivo.pdf", "remoteFolderId": "root"}`).
- `POST /api/v1/files/download`: baixa um arquivo do Drive (`{"remoteFileId": "...", "destinationPath": "/caminho/destino.pdf"}`).
- `POST /api/v1/files/{remoteFileId}/move`: move de pasta e/ou renomeia (`{"targetFolderId": "...", "newName": "novo-nome.txt"}`).
- `POST /api/v1/files/{remoteFileId}/trash`: envia um item para a Lixeira do Drive.
- `POST /api/v1/files/{remoteFileId}/restore`: restaura um item da Lixeira.
- `DELETE /api/v1/files/{remoteFileId}?permanent=true`: apaga definitivamente (ou envia para a Lixeira se omitir `permanent=true`).
- `GET /api/v1/transfers`: lista transferências ativas e recentes.
- `GET /api/v1/logs?limit=50`: consulta os logs de sincronização e ações.

## Licença e Propriedade Intelectual

**Software Proprietário (Source-Available).**  
**Todos os Direitos Reservados (All Rights Reserved)**  
Copyright © 2026 TundraLab e Gustavo Henrique Novaes Gomes (@GustaHerin).

- **Acesso ao Código-Fonte (Portfólio, Estudo e Auditoria):** Embora este repositório seja público — permitindo tecnicamente que usuários visualizem, clonem localmente ou criem *fork* no GitHub estritamente para visualização, leitura, estudo e avaliação técnica —, **não é código aberto (*open source*)**. Nenhuma permissão ou licença é concedida para copiar, reproduzir, modificar, adaptar, criar obras derivadas, sublicenciar, distribuir, publicar ou utilizar trechos do código-fonte (no todo ou em parte) em outros projetos (comerciais ou não) sem autorização prévia e expressa por escrito dos titulares.
- **Uso dos Binários Oficiais (`.deb` / `.rpm` / `.AppImage`):** Usuários finais recebem licença gratuita, não exclusiva, intransferível e revogável para baixar, instalar e executar os pacotes oficiais publicados em [Releases](https://github.com/TundraLab/Custodia/releases), exclusivamente para uso pessoal ou corporativo interno, sendo vedada a redistribuição (gratuita ou comercial), revenda, oferta como serviço (SaaS), modificação ou engenharia reversa fora dos limites expressos em lei.
- **Contribuições Externas, Marcas e Terceiros:** Eventuais sugestões, relatórios de erro ou Pull Requests concedem aos autores licença irrevogável e livre de royalties para incorporação ao Software; esta licença não autoriza o uso dos nomes **"TundraLab"**, **"Custódia"** ou da identidade visual, e bibliotecas de terceiros seguem suas respectivas licenças originais.

Consulte o texto integral e vinculante em [LICENSE](./LICENSE).
