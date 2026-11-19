# Histórico de Versões (Changelog)

Todas as mudanças principais do TundraDrive seguem versionamento semântico (`MAJOR.MINOR.PATCH`).

## [2.7.2] - 2026-11-19

### Adicionado
- **Animação de 5 segundos ao buscar atualizações:** barra de progresso animada com fases de consulta (`Conectando ao GitHub Releases...`, `Consultando versões e assinaturas SHA-256...` e `Comparando com a versão instalada`) e contagem regressiva ao clicar em **Verificar atualizações** na aba **Sobre**.
- **Verificação automática mensal por padrão (configuração global):** novos campos `AutoCheckUpdates`, `UpdateCheckIntervalDays` (`30` dias por padrão) e `LastUpdateCheckUtc` em [AppSettings.cs](src/TundraDrive.Core/Models/AppSettings.cs) com agendamento contínuo em segundo plano (`UpdateSchedulePolicy`) e seletor de frequência nas **Configurações** (`1 vez por mês`, `A cada 15 dias`, `1 vez por semana`, `Sempre ao iniciar`).

## [2.7.1] - 2026-11-19

### Melhorado
- Exibição das notas da release disponíveis diretamente no card de **Atualizações** da aba **Sobre** antes de baixar/instalar.
- Versão instalada exibida também no tooltip do ícone da bandeja do sistema (`TundraDrive v2.7.1`).
- Instalador de terminal [install-tundradrive.sh](packaging/linux/install-tundradrive.sh) agora baixa pacotes de instalação/atualização em pasta temporária `/tmp/tundra-stage.*` e remove automaticamente após concluir, mantendo a pasta atual limpa (sem alterar o comportamento de `--download-*`).

## [2.7.0] - 2026-11-18

### Adicionado
- Proteção estrita de **instância única** (`SingleInstanceGuard` com trava exclusiva e socket Unix local) que impede abrir múltiplas instâncias e restaura/foca automaticamente a janela já em execução (inclusive quando oculta na bandeja).
- Suporte completo a **atualizações futuras** dentro do aplicativo: checagem automática e manual via GitHub Releases na aba **Sobre**, banner discreto e notificação nativa quando há versão nova, barra de progresso, validação `SHA-256` e aplicação automática para `.AppImage` e `.rpm` (`pkexec dnf/zypper/rpm`).
- Opção **Verificar atualizações automaticamente ao iniciar** (`AutoCheckUpdates`) nas **Configurações**.
- Instalador de terminal [install-tundradrive.sh](packaging/linux/install-tundradrive.sh) com resolução dinâmica da release mais recente, comando `--update`, encerramento seguro de instância em execução e upgrade automático via `dnf upgrade` quando a versão muda, além do ativo perene `TundraDrive-Installer-x86_64.sh`.

### Alterado
- Versão exibida na aba **Sobre** e no documento `OpenAPI` (`/openapi.json`) obtida dinamicamente do assembly (`AppVersionInfo`).
- Removidas do [README.md](README.md) as referências ao formato `.deb` não distribuído.

## [2.6.0] - 2026-11-18

### Adicionado
- Seleção múltipla de arquivos em **Meu Driver** (modo Lista e Grade) com caixas de seleção, `Ctrl + Clique`, `Shift + Clique`, `Ctrl + A`, ações em lote (download, mover para Lixeira, restaurar, excluir permanentemente) e seleção múltipla no upload.
- Animações e cores distintas para Upload (azul), Download (verde) e Exclusão (vermelho) na barra lateral e na Fila de Transferências, com tempo mínimo de exibição para operações rápidas e painel expansível de detalhes ao clicar em cada transferência.
- Envio de notificações nativas diretamente via D-Bus (`org.freedesktop.Notifications`) com botão `Abrir pasta` integrado ao gerenciador de arquivos via `org.freedesktop.FileManager1`.
- Filtro nas notificações de conclusão de sincronização para evitar avisos repetitivos quando não houver alterações.
- Compatibilidade aprimorada com **KDE Plasma 5/6** (portal XDG, atualização de cache `kbuildsycoca`, abertura no Dolphin e codificação de `.trashinfo` no padrão FreeDesktop) além de GNOME e COSMIC (Pop!_OS).
- Scripts [build-appimage.sh](packaging/linux/build-appimage.sh), [build-rpm.sh](packaging/linux/build-rpm.sh) e [install-tundradrive.sh](packaging/linux/install-tundradrive.sh) (instalador interativo proprietário no terminal com detecção de sistema, verificação `SHA-256` e suporte direto a `.rpm` e `.AppImage` `x86_64`).
- Suíte de testes automatizados com xUnit em `tests/TundraDrive.Tests` cobrindo localização, logs, Lixeira, segurança de caminhos e ViewModels.
- Fluxo isolado para adicionar novas contas Google (`+ Conta`): solicitação prévia do `credentials.json` específico da conta antes de abrir o login OAuth no navegador.
- Prévia visual das telas (`docs/images/preview-*.png`) e passo a passo ilustrado do Google Cloud OAuth no `README.md`.

### Segurança
- Proteção contra `Path Traversal` na extração/download de arquivos do Google Drive, na resolução de subpastas sincronizadas e na sanitização de nomes enviados (`Path.GetFileName` e validação estrita dentro do diretório raiz).
- Proteção contra CSRF e DNS rebinding na API local em loopback (`127.0.0.1:5080`) validando cabeçalhos `Host`, `Origin` e `Referer`.
- Bloqueio explícito de credenciais OAuth do tipo `web` na importação, exigindo cliente OAuth2 para computador (`installed`).

### Corrigido
- Restauração da janela no clique esquerdo do ícone na bandeja quando minimizada ou em segundo plano no GNOME / Pop!_OS e KDE Plasma.
- Suporte à variável `APPIMAGE` no autostart e no atalho `.desktop` gerado pelo aplicativo.
- Remoção dos subtítulos estáticos dos cabeçalhos de página e limpeza de chaves de tradução sem uso.

## [2.5.0] - 2026-11-17

### Adicionado
- Servidor ASP.NET Core Minimal API em loopback (`127.0.0.1:5080`) com especificação OpenAPI 3.0.3 para automação e integração local.
- Gerenciador de arquivos com miniaturas, cache em disco e pré-visualização integrada de imagens, áudio, vídeo, PDF e documentos Office.
- Limpador automático e manual de memória RAM (`MemoryCleanerService`) para liberar caches de mídia fora de tela e compactar o heap periodicamente.
- Base de gerenciamento de múltiplas contas Google conectadas com troca rápida na barra lateral.

### Melhorado
- Otimização na listagem incremental de pastas remotas e atualização imediata do espaço utilizado após exclusões ou downloads.

## [2.4.0] - 2026-11-05

### Adicionado
- Tela dedicada **Fila de Transferências** com barras de progresso em tempo real, cancelamento de tarefas em andamento e limpeza de itens concluídos.
- Indicador de armazenamento na nuvem discriminando uso de Drive, Gmail / Fotos e Lixeira, com atalho para gerenciamento de espaço.
- Troca dinâmica de idioma em tempo real (Português Brasil e Inglês) sem reiniciar o aplicativo.
- Ajuste de proporção da interface (Zoom entre 100% e 300%) via configurações ou atalhos `Ctrl +`, `Ctrl -` e `Ctrl 0`.

## [2.2.0] - 2026-10-18

### Adicionado
- Navegador Remoto do Google Drive para explorar pastas na nuvem antes de mapear ou baixar arquivos sob demanda.
- Suporte completo à **Lixeira do Google Drive** no Hub de Arquivos, permitindo visualizar itens apagados, restaurar ou excluir definitivamente.
- Busca rápida por nome ou caminho e alternância entre visualização em Grade e Lista.

### Melhorado
- Tratamento amigável de arquivos Google Docs/Sheets/Slides durante exportação e download local.

## [2.0.0] - 2026-10-02

### Alterado
- Reestruturação completa da solução em arquitetura modular de três projetos (`TundraDrive.Core`, `TundraDrive.Api` e `TundraDrive.Desktop`).
- Adoção do **Avalonia UI 11** com tema escuro customizado (`TundraTheme.axaml`) para interface desktop nativa em X11 e Wayland.

### Adicionado
- Ícone persistente na bandeja do sistema (System Tray) com opção de manter o aplicativo ativo em segundo plano ao fechar a janela.
- Inicialização automática com o sistema via padrão XDG Autostart (`~/.config/autostart/tundradrive.desktop`).
- Tela **Sobre** com informações de versão, organização e link para o repositório oficial.

## [1.5.0] - 2026-09-12

### Adicionado
- Suporte aos dois modos de operação por pasta mapeada: **Bidirecional** (`PC <-> Drive`) e **Somente envio / Backup** (`PC -> Drive`).
- Motor de regras de exclusão (`SyncIgnoreMatcher`) para ignorar arquivos temporários, caches e padrões personalizados durante a varredura.
- Comparação por hash MD5 (`FileHashUtility`) e resolução segura de conflitos antes de sobrescrever arquivos locais ou remotos.

### Corrigido
- Prevenção contra loops de reenvio imediato quando timestamps locais são atualizados após download.

## [1.2.0] - 2026-08-22

### Adicionado
- Monitoramento contínuo de alterações locais via `FileSystemWatcher` (`LocalFolderWatcherService`) com debounce para edições em lote.
- Persistência de estado de sincronização, contas e pastas mapeadas em banco SQLite local (`SqliteSyncStateRepository`).
- Tela **Logs de Atividade** com filtros por severidade (`INFO`, `AVISO`, `ERRO`, `SUCESSO`) e pesquisa textual para auditoria de sincronizações.

## [1.0.0] - 2026-08-01

### Adicionado
- Lançamento inicial do TundraDrive em C# / `.NET` para Linux.
- Autenticação OAuth2 desktop com Google Drive usando credenciais próprias do usuário (`credentials.json`).
- Armazenamento seguro de tokens e segredos locais (`SecureFileDataStore`) com aplicação automática de permissões POSIX restritas (`0700` para diretórios e `0600` para arquivos).
- Motor inicial de mapeamento entre diretórios locais do Linux e pastas do Google Drive.
