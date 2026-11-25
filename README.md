# Custódia v3.0.3

![Custódia by TundraLab - Meus arquivos. Sempre seguros.](docs/images/custodia-promo.jpg)

[![GitHub](https://img.shields.io/badge/GitHub-TundraLab%2FCustodia-181717?logo=github)](https://github.com/TundraLab/Custodia)
[![Autor Único](https://img.shields.io/badge/Autor%20e%20Dev%20Único-%40GustaHerin-181717?logo=github)](https://github.com/GustaHerin)
[![Release](https://img.shields.io/badge/Release-v3.0.3-2563EB?logo=github)](https://github.com/TundraLab/Custodia/releases)
[![Testes xUnit](https://img.shields.io/badge/Testes%20xUnit-64%20aprovados-16A34A)](tests/Custodia.Tests)
[![Fedora](https://img.shields.io/badge/Fedora-Workstation%20%2F%20KDE-51A2DA?logo=fedora&logoColor=white)](#sistemas-e-formatos)
[![Pop!_OS](https://img.shields.io/badge/Pop!__OS-GNOME%20%2F%20COSMIC-48B9C7?logo=popos&logoColor=white)](#sistemas-e-formatos)
[![KDE Plasma](https://img.shields.io/badge/KDE%20Plasma-5%20%2F%206-1D99F3?logo=kdeplasma&logoColor=white)](#sistemas-e-formatos)
[![AppImage](https://img.shields.io/badge/AppImage-x86__64-2C7FB8?logo=linux&logoColor=white)](#instalação)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia%20UI-11.3.8-8B44AC)](https://avaloniaui.net/)
[![Licença](https://img.shields.io/badge/License-All%20Rights%20Reserved-red)](LICENSE)
[![Privacidade](https://img.shields.io/badge/Política%20de-Privacidade-0F766E)](PRIVACY.md)
[![Termos de Uso](https://img.shields.io/badge/Termos%20de-Uso-1D4ED8)](TERMS.md)

> **Em desenvolvimento desde:** 2026  
> **Status:** estável, com releases oficiais ([GitHub Releases](https://github.com/TundraLab/Custodia/releases))  
> **Plataforma:** Linux `x86_64`  
> **Objetivo:** sincronização segura e intuitiva com o Google Drive

**Meus arquivos. Sempre seguros.** Cliente desktop nativo para Linux focado em sincronização de pastas, backups locais, gerenciamento multimídia e automação local com privacidade direto no Google Drive.

## Qualidade

- **Build e distribuição:** compilação limpa em `.NET 10.0` para Linux `x86_64` com empacotamento nativo `.rpm` e `.AppImage` verificado por SHA-256
- **Suíte de testes:** **56 testes unitários xUnit aprovados (`0` falhas)** cobrindo motor de sincronização, fila de transferências, persistência SQLite, isolamento de contas OAuth2, atualizador, sanitização de suporte, aceite de termos/privacidade e e-mails de ciclo de vida
- **Plataforma principal:** Linux `x86_64` (Fedora Workstation/KDE, Pop!_OS GNOME/COSMIC e KDE Plasma 5/6)
- **Documentação técnica:** OpenAPI `3.0.3` em tempo real (`GET /api/v1/openapi.json`), guia [docs/API.md](docs/API.md), [Custodia-API.pdf](Custodia-API.pdf) e histórico completo em [CHANGELOG.md](CHANGELOG.md)

## Prévia visual

![Prévia das telas do Custódia](docs/images/preview-showcase.png)

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

- Testado em **Fedora** e **Pop!_OS** (incluindo hardware modesto), com suporte aos ambientes GNOME, COSMIC e KDE Plasma 5/6.
- **Formatos distribuídos:** pacote nativo **`.rpm`** (Fedora / RHEL / openSUSE) e **`.AppImage` (`x86_64`)** independente com o runtime `.NET 10` embutido (Pop!_OS, Ubuntu, Debian e demais distribuições Linux, sem instalar dependências do `.NET`), ambos verificados por **SHA-256** (`SHA256SUMS.txt`).

## O que o aplicativo faz

- **Meus Arquivos com menu completo de botão direito:** navegação por **Meu Drive**, **Pastas Locais** e **Lixeira** em modo Lista ou Grade com miniaturas, seleção múltipla (`Ctrl + Clique`, `Shift + Clique`, `Ctrl + A`), upload por arrastar e soltar ou seleção múltipla e menu de contexto tanto sobre arquivos/pastas quanto em áreas vazias (**Nova Pasta**, **Enviar Arquivo**, **Personalizar pasta**, **Atualizar**, **Voltar**, **Selecionar Tudo** e **Mover para Lixeira** destacado em vermelho).
- **Personalização de pastas (cor, símbolo e apelido):** personalize diretamente com o botão direito em **Meus Arquivos** ou na aba **Pastas de Sincronização**, com cores e ícones preservados entre sessões.
- **Pré-visualização multimídia integrada:** imagens, vídeos (miniatura, duração e player), áudios (forma de onda e player interno), PDFs com navegação por páginas, documentos Office (`DOCX`, `XLSX`, `PPTX`, `ODT`, `ODS`, `ODP`), arquivos Google Workspace e texto/código.
- **Fila e ícone dinâmico na bandeja sem travamento:** fila sem teto de 200 itens, histórico expandido, banners animados em falhas de upload/download e ícone pulsante na barra superior que cresce e troca de cor/símbolo conforme o estado (`↑` upload azul, `↓` download verde, `✕` exclusão vermelho, `↻` sincronização e `!` erro), sincronizado com o menu nativo sem congelar status antigos.
- **Sincronização flexível por pasta:** modo **Bidirecional (`PC <-> Drive`)** ou **Backup Unidirecional (`PC -> Drive`)**, mapeamento múltiplo preservando nomes e subpastas vazias, validação por hash MD5/SHA256, política de conflitos, padrões ignorados e monitoramento contínuo de alterações locais em tempo real.
- **Lixeira segura:** exclusões em pastas sincronizadas enviam os itens para a Lixeira do Google Drive (ou para a Lixeira FreeDesktop do sistema compatível com GNOME Files e KDE Dolphin); exclusões permanentes ocorrem apenas manualmente na Lixeira.
- **Gestão avançada de múltiplas contas:** pastas dedicadas e legíveis por e-mail em `~/.config/custodia/contas/<email>/` (`credentials.json`, `conta.json` e `pastas-sincronizadas.json`), detecção automática de `client_secret_*.json` em `~/Downloads`, importação por arrastar e soltar em Configurações, inspeção de `Project ID` / `Client ID`, alternância rápida, reconexão e preservação automática de pastas ao remover/readicionar uma conta (com opção explícita para excluir a configuração).
- **Avisos de boas-vindas e revogação TundraLab:** envio opcional automático a partir de **`contato.tundralab@gmail.com`** via serviço dedicado TundraLab, sem precisar ativar a Gmail API nos projetos OAuth dos usuários.
- **Suporte e atualizações na aba Sobre:** verificação agendada de releases com proteção offline, formulário unificado com seletor **Bug / Erro** (`&labels=bug`) vs **Sugestão** (`&labels=enhancement`), diagnóstico técnico opcional e sanitizado apenas em relatórios de bug (prévia via **Ver dados** e limite seguro de URL preservando o log completo em **Copiar texto**).
- **Idioma, zoom, RAM e som:** PT-BR e EN-US, zoom de 100% a 300% (`Ctrl++`, `Ctrl+-`, `Ctrl+0`), limpador periódico de RAM e 6 variações de toques sintetizados nas notificações D-Bus (ou modo silencioso).

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
- **AppImage e RPM (`x86_64`):** `.rpm` oferece integração nativa no Fedora e derivados, enquanto o `.AppImage` garante execução universal em Ubuntu/Pop!_OS e outras distros sem exigir SDK ou runtime externo.
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

### 1. Instalador Interativo no Terminal (`Custódia by TundraLab` - Recomendado)

Detecta sua distribuição Linux, baixa apenas o formato escolhido (**`.rpm`** ou **`.AppImage`**), valida **SHA-256**, instala ícones/atalhos e configura inicialização automática:

```bash
curl -fsSL https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-Installer-x86_64.sh -o install-custodia.sh
chmod +x install-custodia.sh
./install-custodia.sh
```

### 2. Baixar pacote direto pelo terminal (opcional)

- **AppImage (`x86_64`, roda direto sem instalar):**
  ```bash
  curl -fLO https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-3.0.3-x86_64.AppImage
  chmod +x Custodia-3.0.3-x86_64.AppImage
  ./Custodia-3.0.3-x86_64.AppImage
  ```
- **Fedora / RHEL / openSUSE (`.rpm`):**
  ```bash
  curl -fLO https://github.com/TundraLab/Custodia/releases/latest/download/Custodia-3.0.3-1.x86_64.rpm
  sudo dnf install -y ./Custodia-3.0.3-1.x86_64.rpm
  ```

<details>
<summary>Opções avançadas (flags do instalador, extras de mídia e ícone no GNOME)</summary>

- **Atalhos do `install-custodia.sh`:**
  - `./install-custodia.sh --update`: detecta sua instalação atual (`.rpm` ou `.AppImage`) e atualiza para a release mais recente.
  - `./install-custodia.sh --rpm`: baixa, valida `SHA-256` e instala o `.rpm`.
  - `./install-custodia.sh --appimage`: baixa, valida `SHA-256` e instala o `.AppImage` integrado ao usuário.
  - `./install-custodia.sh --download-only`: baixa `.rpm`, `.AppImage` e `SHA256SUMS.txt` na pasta atual.
  - `./install-custodia.sh --uninstall`: remove a instalação local ou `.rpm` mantendo suas configurações.
- **AppImage sem FUSE:** `./Custodia-3.0.3-x86_64.AppImage --appimage-extract-and-run`
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

Para conectar sua conta diretamente ao Google sem servidores intermediários, crie suas credenciais OAuth2 Desktop no Google Cloud Console.

### Passo 1: Ativar a Google Drive API
1. Abra o [Google Cloud Console (Google Drive API)](https://console.cloud.google.com/apis/library/drive.googleapis.com).
2. Crie ou selecione um projeto no topo da página.
3. Clique em **Ativar** (*Enable*). Depois de ativa, a tela mostra **API ativada** e o botão **Gerenciar**. *(Não é necessário ativar a Gmail API no projeto.)*

![Google Drive API ativada](docs/images/01-google-drive-api.png)

### Passo 2: Abrir o menu de Credenciais OAuth
1. No menu lateral esquerdo, acesse **APIs e serviços > Credenciais** (ou abra [Credenciais](https://console.cloud.google.com/apis/credentials)).
2. Clique em **+ Criar credenciais** no topo e escolha **ID do cliente OAuth**:

![Menu Criar credenciais](docs/images/02-criar-credenciais-menu.png)

Se escolher **Ajude-me a escolher**, marque **Google Drive API** e **Dados do usuário** (*User data*):

![Tipo de credencial Dados do usuário](docs/images/03-tipo-credencial-dados-usuario.png)

3. Se aparecer o aviso *"Para criar um ID do cliente OAuth, primeiro é preciso configurar sua tela de permissão"*, clique em **Configurar tela de consentimento**:

![Configurar tela de consentimento](docs/images/02b-configurar-tela-consentimento.png)

4. Na tela seguinte (**Branding**), clique em **Vamos começar** para abrir a configuração:

![Tela inicial do Google Auth Platform Vamos começar](docs/images/02c-branding-vamos-comecar.png)

### Passo 3: Configurar a Tela de permissão OAuth (primeira vez do projeto)
Se for a primeira credencial do projeto, o Google pede a configuração da tela de consentimento (**Google Auth Platform**):
1. Em **Informações do app**, coloque o nome (por exemplo, `Custódia`) e seu e-mail de suporte. Clique em **Avançar**.
2. Em **Público**, escolha **Externo** (*External*) e clique em **Avançar**.
3. Em **Dados de contato**, informe seu e-mail novamente e clique em **Avançar**.
4. Aceite os termos e clique em **Criar**.

![Configuração da tela de permissão OAuth](docs/images/04-tela-permissao-oauth.png)

### Passo 4: Adicionar seu e-mail em Usuários de teste (obrigatório)
Enquanto o aplicativo estiver em modo **Testando** (*Testing*), o Google bloqueia logins (`Erro 403: access_denied`) de qualquer conta fora da lista de testadores.
1. No menu esquerdo do **Google Auth Platform**, clique em **Público-alvo** ([abrir página](https://console.cloud.google.com/auth/audience)).
2. Desça até **Usuários de teste** e clique em **+ Add users**:

![Público-alvo e botão Add users](docs/images/05-publico-alvo-add-users.png)

3. Informe o endereço Google que você vai usar no Custódia e clique em **Salvar**:

![Adicionar usuários de teste](docs/images/06-adicionar-usuarios-modal.png)

### Passo 5: Selecionar "App para computador" e baixar o JSON
1. No menu esquerdo, clique em **Clientes** e depois em **+ Criar cliente**:

![Menu Clientes e botão Criar cliente](docs/images/07a-clientes-criar-cliente.png)

2. Em **Tipo de aplicativo**, selecione **App para computador** (*Desktop app*):

![Criar ID do cliente do OAuth](docs/images/07-criar-cliente-oauth.png)

3. Coloque `Custódia` no campo **Nome** e clique em **Criar**.
4. Na janela **Cliente OAuth criado**, clique em **Baixar o JSON** (`client_secret_...json`):

![Janela Cliente OAuth criado com botão Baixar o JSON](docs/images/07b-cliente-oauth-criado-json.png)

### Passo 6: Importar no Custódia e conectar
1. Abra o Custódia (em **Configurações** o app já detecta automaticamente arquivos em `~/Downloads` e também aceita arrastar e soltar o `.json`, ou use o cartão no rodapé da barra lateral).
2. Importe o JSON baixado e clique em **Conectar**.
3. No navegador oficial do Google que abrir:
   - Escolha a conta cadastrada no **Passo 4**:

![Escolha uma conta Google para prosseguir para o Custódia](docs/images/09-google-escolher-conta.png)

   - Na tela *"O Google não verificou este app"*, clique em **Continuar**:

![Aviso O Google não verificou este app, clique em Continuar](docs/images/10-google-nao-verificou-app.png)

   - Confirme o acesso ao Google Drive clicando em **Continuar**:

![Confirmar permissões do Custódia no Google Drive](docs/images/11-google-confirmar-permissoes.png)

4. Depois de autorizar, use **Meus Arquivos** para navegar, criar pastas, enviar arquivos e personalizar pastas com o botão direito, e **Pastas de Sincronização** para mapear diretórios do PC ao Google Drive.

## Segurança e Privacidade

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
- **Uso dos Binários Oficiais (`.AppImage` / `.rpm`):** Usuários finais recebem licença gratuita, não exclusiva, intransferível e revogável para baixar, instalar e executar os pacotes oficiais publicados em [Releases](https://github.com/TundraLab/Custodia/releases), exclusivamente para uso pessoal ou corporativo interno, sendo vedada a redistribuição (gratuita ou comercial), revenda, oferta como serviço (SaaS), modificação ou engenharia reversa fora dos limites expressos em lei.
- **Contribuições Externas, Marcas e Terceiros:** Eventuais sugestões, relatórios de erro ou Pull Requests concedem aos autores licença irrevogável e livre de royalties para incorporação ao Software; esta licença não autoriza o uso dos nomes **"TundraLab"**, **"Custódia"** ou da identidade visual, e bibliotecas de terceiros seguem suas respectivas licenças originais.

Consulte o texto integral e vinculante em [LICENSE](./LICENSE).
