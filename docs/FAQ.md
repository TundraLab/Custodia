# Perguntas Frequentes (FAQ)

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](en/FAQ.md)

Respostas rápidas sobre funcionamento offline, resolução de conflitos, recuperação de arquivos, segurança de credenciais e remoção do Custódia.

---

## 1. O Custódia funciona sem internet?

**Parcialmente.**
- **Arquivos locais:** suas pastas mapeadas continuam disponíveis normalmente no computador para leitura e edição offline.
- **Fila e monitoramento:** alterações feitas enquanto o computador está sem internet ficam registradas localmente e são sincronizadas assim que a conexão retorna (o motor detecta mudanças de rede automaticamente).
- **Navegação remota (*Meu Drive*) e transferências:** exigem conexão ativa com a Google Drive API v3. O verificador de atualizações também possui proteção offline para não gerar falhas quando não há rede.

---

## 2. O que acontece quando o mesmo arquivo é modificado no PC e no Drive ao mesmo tempo?

O Custódia aplica a política configurada em **Configurações → Em caso de conflito** (`ConflictPolicy`). As opções disponíveis são:

| Política | Comportamento |
| :--- | :--- |
| **Mais recente vence** *(padrão)* | Compara a data de modificação local com a do Google Drive (`ModifiedTimeUtc`) e mantém a versão mais nova. |
| **Preferir local** | Envia o arquivo do computador e sobrescreve a versão do Drive. |
| **Preferir remoto** | Baixa a versão do Google Drive e sobrescreve o arquivo local. |
| **Manter cópia de conflito** | Preserva o arquivo local com sufixo de conflito no mesmo diretório, baixa a versão do Drive e registra um aviso (`ConflictRenamed`) nos logs. |

---

## 3. Como recuperar um arquivo ou pasta excluída?

O Custódia adota exclusão segura por padrão:
- **Exclusões sincronizadas no Drive:** itens removidos localmente são enviados para a **Lixeira do Google Drive**, não sendo apagados definitivamente de imediato.
- **Exclusões sincronizadas no PC:** no modo **Bidirecional (`PC <-> Drive`)**, itens removidos no Drive são enviados para a **Lixeira FreeDesktop do sistema** (compatível com GNOME Files e KDE Dolphin).
- **Restauração:** você pode restaurar itens diretamente pela aba **Lixeira** dentro do Custódia, pelo site do Google Drive ou pela lixeira do seu gerenciador de arquivos.
- **Exclusão permanente:** só ocorre quando você aciona manualmente a remoção definitiva na Lixeira (ou via `DELETE /api/v1/files/{id}?permanent=true` na API local).

---

## 4. Qual a diferença entre sincronização Bidirecional e Backup Unidirecional?

- **Bidirecional (`PC <-> Drive`):** mantém PC e Google Drive espelhados nos dois sentidos. Alterações e exclusões em um lado refletem no outro.
- **Backup Unidirecional (`PC -> Drive`):** envia novidades e edições do PC para o Drive, mas **nunca** baixa arquivos novos do Drive nem apaga arquivos do PC caso sejam removidos no Drive.

Veja a tabela comparativa completa no [README](../README.md#modos-de-sincronização).

---

## 5. Onde ficam armazenadas minhas credenciais e tokens OAuth2?

Todos os dados sensíveis ficam isolados localmente na sua pasta de usuário com permissões POSIX restritas (`0700` para diretórios e `0600` para arquivos):
- `~/.config/custodia/contas/<email>/credentials.json` — credenciais do seu cliente OAuth2 Desktop.
- `~/.config/custodia/contas/<email>/conta.json` e `pastas-sincronizadas.json` — metadados e mapeamentos da conta.
- `~/.local/share/custodia/tokens/` — `refresh_token` e tokens de sessão do Google.
- `~/.local/share/custodia/custodia.db` — banco SQLite local (estado de sincronização, hashes e logs).

Nenhum arquivo, token ou credencial é enviado para servidores da TundraLab. Para mais detalhes, consulte [Segurança e Privacidade](SECURITY.md).

---

## 6. Se eu remover uma conta no Custódia, perco as pastas configuradas?

**Não por padrão.** Ao remover uma conta, o token local é invalidado e a sessão é encerrada, mas o Custódia preserva a configuração de pastas vinculada àquele e-mail para facilitar caso você reconecte a mesma conta depois. Se quiser apagar também os mapeamentos de pastas, basta marcar a opção explícita na janela de confirmação.

---

## 7. Posso usar o Custódia com múltiplas contas Google?

**Sim.** Cada conta possui seu próprio diretório isolado em `~/.config/custodia/contas/<email>/` com credenciais, tokens e pastas sincronizadas independentes, permitindo alternância rápida pela interface.

---

## 8. Como desinstalar completamente o Custódia (incluindo dados locais)?

1. Execute o desinstalador oficial para remover o pacote `.rpm` ou a integração `.AppImage`:
   ```bash
   ./install-custodia.sh --uninstall
   ```
2. Caso queira apagar também todas as configurações, tokens, banco SQLite e cache local:
   ```bash
   rm -rf ~/.config/custodia ~/.local/share/custodia ~/.cache/custodia
   ```
   *(Seus arquivos pessoais nas pastas sincronizadas e no Google Drive não são apagados por esse comando.)*

---

## 9. Movi ou renomeei uma pasta e apareceram várias exclusões na fila — perdi meus arquivos?

**Não.** Durante a sincronização, **nada é excluído permanentemente** — todo item removido ou movido de lugar vai exclusivamente para a **Lixeira**.

- Como o motor compara arquivos pelo caminho relativo (`relPath`), mover ou renomear uma pasta com muitos arquivos entre diretórios sincronizados faz o Custódia enviar os caminhos antigos para a **Lixeira** (do Google Drive ou do sistema) e sincronizar os arquivos novamente no novo destino.
- Exclusões definitivas só acontecem se você abrir a aba **Lixeira** (ou usar o endpoint `DELETE` com `permanent=true`) e confirmar a remoção permanente manualmente.
