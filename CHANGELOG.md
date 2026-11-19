# Histórico de Versões (Changelog)

Todas as mudanças principais do TundraDrive seguem versionamento semântico (`MAJOR.MINOR.PATCH`).

## [2.7.3] - 2026-11-19

### Melhorado
- **Encerramento limpo ao desinstalar ou atualizar:** ao remover ou atualizar o TundraDrive pelo terminal (inclusive no formato AppImage rodando na bandeja), qualquer instância aberta é encerrada automaticamente e o ícone da bandeja desaparece na hora.
- **Desligamento gracioso por sinais do sistema:** o aplicativo agora responde corretamente a comandos de encerramento do sistema, fechando a bandeja, a sincronização e o servidor local com segurança.
- **Notas de versão mais legíveis na aba Sobre:** o painel de atualizações formata títulos e marcadores com clareza e sem símbolos de código na tela.

## [2.7.2] - 2026-11-19

### Adicionado
- **Animação de 5 segundos ao buscar atualizações:** ao clicar em **Verificar atualizações** na aba **Sobre**, o aplicativo exibe uma barra de progresso animada com etapas claras e contagem regressiva enquanto consulta as novidades.
- **Verificação automática mensal por padrão:** por padrão global, o TundraDrive passa a procurar novas versões automaticamente uma vez por mês (a cada 30 dias), com opção nas **Configurações** para escolher entre mensal, quinzenal, semanal ou a cada inicialização.

## [2.7.1] - 2026-11-19

### Melhorado
- **Resumo das novidades antes de atualizar:** as notas da nova versão passam a aparecer diretamente na aba **Sobre** antes de iniciar o download.
- **Identificação da versão na bandeja:** o número da versão instalada agora é exibido ao passar o cursor sobre o ícone da bandeja do sistema.
- **Pasta limpa após instalar ou atualizar pelo terminal:** os instaladores `.rpm` e `.AppImage` baixados durante uma instalação ou atualização são armazenados temporariamente e removidos ao final do processo, preservando a opção de apenas baixar os pacotes quando desejado.

## [2.7.0] - 2026-11-18

### Adicionado
- **Janela única garantida:** tentar abrir o TundraDrive novamente enquanto ele já está ativo (inclusive minimizado na bandeja) traz a janela existente para frente em vez de abrir uma segunda cópia.
- **Atualizador integrado no aplicativo:** checagem manual e automática de novas versões, aviso discreto no topo da janela, notificação no sistema, barra de progresso no download, conferência de autenticidade e instalação assistida para pacotes `.rpm` e `.AppImage`.
- **Opções extras no instalador de terminal:** detecção automática da versão mais recente disponível e novo comando de atualização direta para instalações existentes.

### Alterado
- Exibição automática da versão em uso em todo o aplicativo sem depender de texto fixo.
- Remoção de menções antigas a pacote `.deb` na documentação do repositório.

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
