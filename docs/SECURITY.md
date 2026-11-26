# Segurança e Privacidade

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](en/SECURITY.md)

O Custódia foi projetado sob o princípio de **execução local direta** (*local-first*): seus arquivos, metadados e tokens OAuth2 permanecem exclusivamente na sua máquina e trafegam diretamente entre o seu Linux e a API oficial do Google Drive, sem servidores intermediários da TundraLab.

---

## 1. Modelo BYO-OAuth2 (Bring Your Own OAuth)

- **Sem servidor central de proxy:** você cria seu próprio cliente OAuth2 do tipo **App para computador** (*Desktop app*) no Google Cloud Console (veja [Configuração OAuth2](OAUTH_SETUP.md)).
- **Sem armazenamento de senha:** o arquivo `client_secret_*.json` identifica apenas o aplicativo OAuth; sua senha do Google é digitada exclusivamente na página oficial de login do Google no navegador.
- **Escopo solicitado:** `https://www.googleapis.com/auth/drive` (necessário para listar, enviar, baixar, mover, restaurar e sincronizar pastas escolhidas por você). Nenhum escopo do Gmail é solicitado.
- **Conformidade Google:** o tratamento de dados segue a [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy) (Uso Limitado).

---

## 2. Armazenamento Local e Permissões POSIX (`0700` / `0600`)

Todos os diretórios e arquivos sensíveis criados pelo Custódia recebem permissões restritas ao usuário atual:

| Caminho | Conteúdo | Permissão |
| :--- | :--- | :--- |
| `~/.config/custodia/settings.json` | Preferências gerais e personalizações visuais de pastas | Restrita ao usuário |
| `~/.config/custodia/contas/<email>/` | Diretório isolado por conta Google | `0700` |
| `~/.config/custodia/contas/<email>/credentials.json` | Credenciais do cliente OAuth2 Desktop | `0600` |
| `~/.config/custodia/contas/<email>/conta.json` | Metadados da conta conectada | `0600` |
| `~/.config/custodia/contas/<email>/pastas-sincronizadas.json` | Mapeamentos de pastas sincronizadas da conta | `0600` |
| `~/.local/share/custodia/tokens/` | Tokens OAuth2 (`refresh_token` / `access_token`) | `0600` |
| `~/.local/share/custodia/custodia.db` | Banco SQLite local (estado de sync, hashes MD5/SHA256 e logs) | Restrita ao usuário |

---

## 3. Ciclo de Vida dos Tokens e Revogação

- **Renovação automática:** o SDK oficial do Google utiliza o `refresh_token` salvo localmente (`0600`) para obter novos `access_token` temporários diretamente com o endpoint OAuth2 do Google.
- **Desconexão / Revogação:** ao remover uma conta, o token local é invalidado e a sessão em `~/.config/custodia/contas/<email>/` é encerrada. Por padrão, os mapeamentos de pastas são preservados para eventual reconexão, com opção explícita na UI para excluí-los.

---

## 4. Superfície da API REST Local (`127.0.0.1:5080`)

- A API embarcada escuta **exclusivamente em *loopback* (`127.0.0.1:5080`)**, nunca em `0.0.0.0`.
- Permite que scripts locais e agentes de IA interajam com o Custódia sem acessar nem manipular diretamente seus tokens OAuth2.
- **Recomendação:** não exponha a porta `5080` para fora da máquina (via proxy reverso, redirecionamento de porta ou túnel público).

---

## 5. E-mails Opcionais de Ciclo de Vida e Diagnóstico de Suporte

- **E-mails de ciclo de vida (`contato.tundralab@gmail.com`):** recurso **opcional** que pode ser recusado na primeira execução ou alterado em **Sobre → Rever termo inicial**. Quando habilitado, envia apenas `recipientEmail`, `recipientName`, `eventType` (`connect` ou `revoke`), `appName`, `appVersion`, `language` e `timestampUtc` para notificar conexão ou revogação da conta.
- **Diagnóstico de bugs:** ao abrir um relatório **Bug / Erro** na aba **Sobre**, os dados técnicos são sanitizados (sem tokens, credenciais ou conteúdo de arquivos) e podem ser inspecionados antes do envio pelo botão **Ver dados**.

---

## 6. Documentos Legais e Remoção Completa

- [Política de Privacidade](../PRIVACY.md)
- [Termos de Uso](../TERMS.md)
- [Licença](../LICENSE)

Para remover o aplicativo e apagar todos os dados locais de configuração, tokens e banco SQLite:
```bash
./install-custodia.sh --uninstall
rm -rf ~/.config/custodia ~/.local/share/custodia ~/.cache/custodia
```
