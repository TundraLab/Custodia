# Histórico de Versões (Changelog)

Todas as mudanças principais do TundraDrive seguem versionamento semântico (`MAJOR.MINOR.PATCH`).

## [2.6.0] - 2026-11-18

### Adicionado
- Seleção múltipla de arquivos em **Meu Driver** (modo Lista e Grade) com caixas de seleção, `Ctrl + Clique`, `Shift + Clique`, `Ctrl + A`, ações em lote (download, mover para Lixeira, restaurar, excluir permanentemente) e seleção múltipla no upload.
- Animações e cores distintas para Upload (azul), Download (verde) e Exclusão (vermelho) na barra lateral e na Fila de Transferências, com tempo mínimo de exibição para operações rápidas e painel expansível de detalhes ao clicar em cada transferência.
- Envio de notificações nativas diretamente via D-Bus (`org.freedesktop.Notifications`) com botão `Abrir pasta` integrado ao gerenciador de arquivos via `org.freedesktop.FileManager1`.
- Filtro nas notificações de conclusão de sincronização para evitar avisos repetitivos quando não houver alterações.
- Compatibilidade aprimorada com **KDE Plasma 5/6** (portal XDG, atualização de cache `kbuildsycoca`, abertura no Dolphin e codificação de `.trashinfo` no padrão FreeDesktop) além de GNOME e COSMIC (Pop!_OS).
- Script [build-appimage.sh](packaging/linux/build-appimage.sh) para gerar pacote AppImage `x86_64` independente com runtime `.NET 10` embutido.
- Suíte de testes automatizados em `tests/TundraDrive.Tests`.

### Corrigido
- Restauração da janela no clique esquerdo do ícone na bandeja quando minimizada ou em segundo plano no GNOME / Pop!_OS.
- Suporte à variável `APPIMAGE` no autostart e no atalho `.desktop` gerado pelo aplicativo.
- Remoção dos subtítulos estáticos dos cabeçalhos de página e limpeza de chaves sem uso.

## [2.5.0] - 2026-11-17

### Adicionado
- Sincronização bidirecional (`PC <-> Drive`) e backup unidirecional (`PC -> Drive`) com resolução de conflitos, exclusão segura para a Lixeira do Google Drive e monitoramento via `FileSystemWatcher`.
- Servidor ASP.NET Core Minimal API em loopback (`127.0.0.1:5080`) com OpenAPI 3.0.3 para integração local.
- Gerenciador de arquivos com miniaturas, pré-visualização de imagem, vídeo, áudio, PDF e documentos Office, além de limpador de RAM e suporte a múltiplas contas Google.
