# Limitações Conhecidas e Comportamento Detalhado

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](en/LIMITATIONS.md)

Esta página reúne comportamentos específicos, exclusões automáticas e limites técnicos do **Custódia (`v4.0.0`)** para evitar surpresas antes de sincronizar seus arquivos.

---

## 1. Arquivos nativos do Google Docs, Sheets e Slides

- **Arquivos binários comuns** (PDF, DOCX, XLSX, imagens, vídeos, compactados) são sincronizados integralmente entre disco e nuvem.
- **Documentos nativos do Google Workspace** (`application/vnd.google-apps.*`, criados diretamente no navegador sem arquivo binário subjacente) não possuem conteúdo binário direto para edição offline idêntica; quando baixados pela interface de navegador ou via `POST /api/v1/files/download`, são **exportados automaticamente para formatos compatíveis (como PDF)** em vez de sobrescrever formatos proprietários.

---

## 2. Padrões ignorados por padrão (inclusive no Backup Unidirecional)

Para evitar envio infinito de arquivos temporários e pastas de build gigantes, `AppSettings.IgnorePatterns` ignora os seguintes padrões por padrão tanto no **Espelho Bidirecional** quanto no **Backup Unidirecional**:

- Temporários de navegador e sistema: `*.tmp`, `*.swp`, `*.part`, `*.crdownload`, `.DS_Store`, `Thumbs.db`, `.goutputstream-*`
- Diretórios de desenvolvimento e build: `.git/*`, `node_modules/*`, `bin/*`, `obj/*`

> **Atenção:** se você usa o **Backup Unidirecional** para guardar código-fonte com histórico Git (`.git`) ou pastas chamadas `bin/` / `obj/`, remova os padrões desejados em **Configurações -> Sincronização -> Arquivos e pastas ignorados** antes de iniciar.

---

## 3. Links simbólicos (*symlinks*), nomes duplicados e caracteres inválidos

- **Links simbólicos (*symlinks*):** não são seguidos recursivamente fora da pasta mapeada para evitar loops infinitos e vazamento acidental de diretórios do sistema.
- **Nomes duplicados no Google Drive:** o Google Drive permite dois arquivos com o mesmo nome dentro da mesma pasta (diferenciados por ID), enquanto sistemas Linux não permitem. O motor opera pela chave de caminho relativo (`relPath`), evitando sobrescrita silenciosa com a política padrão **Manter cópia de conflito**.
- **Caracteres inválidos no Linux:** nomes remotos contendo barras (`/`) ou bytes nulos são sanitizados com segurança antes de serem gravados no sistema de arquivos local.

---

## 4. Drives Compartilhados (*Shared Drives*) e "Compartilhados comigo"

- A sincronização de pastas locais foca em pastas do seu **Meu Drive** (*My Drive*).
- A guia **Compartilhados comigo** (*Shared with me*) permite listar e baixar itens compartilhados diretamente pela interface, sem espelhamento bidirecional automático de **Drives Compartilhados** corporativos.

---

## 5. Cota diária de envio do Google e limites de API

- O Google Drive aplica um limite operacional por conta de até **750 GB por dia** de uploads. Pastas muito maiores que esse limite serão retomadas nos ciclos seguintes assim que a cota diária do Google renovar.
- Se o seu projeto pessoal no Google Cloud permanecer em modo **Testando** (*Testing*), o Google expira o token de renovação a cada **7 dias** (veja como publicar para uso pessoal em [Configuração OAuth2](OAUTH_SETUP.md)).

---

## 6. Lixeira em outras partições Linux (`.Trash-UID`)

- Ao excluir um arquivo sincronizado no modo Bidirecional, o Custódia envia o arquivo local para a **Lixeira FreeDesktop** do diretório apropriado: na partição `$HOME` ele vai para `~/.local/share/Trash/`; em HDs externos ou outras partições montadas, o Linux utiliza `.Trash-<UID>` na raiz da respectiva partição quando gravável.

---

## 7. Plataforma suportada, instalador gráfico e gerenciador de RAM

- **Arquitetura suportada:** apenas Linux `x86_64` (sem pacotes para `ARM64`, Windows ou macOS).
- **Instalador gráfico (`.AppImage` / `.run`):** utiliza bibliotecas gráficas já presentes no sistema Linux (`GTK 3 / GTK 4` e `PyGObject`). Em distros minimalistas sem ambiente gráfico GTK/Python-GI completo, instale diretamente os pacotes `.deb` ou `.rpm` ou use `install-custodia.sh`.
- **Otimizador automático de RAM:** ativo por padrão (`AutoMemoryCleanupEnabled = true`, ciclo de 5 minutos) para manter o uso de memória enxuto enquanto o app fica residente na bandeja, podendo ser ajustado ou desativado em **Configurações -> Aplicativo**.
