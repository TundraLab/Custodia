# Política de Privacidade — Custódia

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](PRIVACY.en.md)

**Última atualização:** 24 de novembro de 2026  
**Responsável:** TundraLab (`contato.tundralab@gmail.com`)  
**Repositório oficial:** https://github.com/TundraLab/Custodia

Esta Política de Privacidade descreve como o aplicativo desktop **Custódia** trata dados locais, credenciais OAuth 2.0, integrações com APIs do Google e e-mails opcionais de ciclo de vida da conta.

---

## 1. Modelo de Operação Local (Zero Servidor para Arquivos)

O **Custódia** funciona diretamente na máquina do usuário:

- Todos os arquivos, diretórios sincronizados, índices de metadados, hashes, filas de transferência e logs de atividade permanecem exclusivamente no computador do usuário.
- As credenciais OAuth fornecidas pelo usuário (`credentials.json`) ficam armazenadas em `~/.config/custodia/contas/<email>/` e os tokens de acesso/atualização em `~/.local/share/custodia/tokens/` com permissão restrita (`0600`).
- O serviço local em segundo plano escuta apenas em `127.0.0.1:5080` (interface loopback local), não expondo portas de rede para acesso externo.
- A TundraLab **não** coleta, **não** armazena, **não** transmite e **não** tem acesso aos seus arquivos do Google Drive, nomes de pastas, conteúdo de documentos, histórico de sincronização ou tokens OAuth.

---

## 2. Acesso às APIs do Google e Política de Dados do Usuário (Limited Use)

Quando você conecta uma conta Google no Custódia:

- O aplicativo solicita o escopo `https://www.googleapis.com/auth/drive` (classificado como **escopo sensível** pela documentação oficial do Google OAuth 2.0) estritamente para listar, baixar, enviar, atualizar, organizar e sincronizar arquivos e pastas entre o Google Drive e as pastas configuradas localmente no Linux.
- O Custódia **não** solicita escopos do Gmail na sua conta Google nem na sua credencial OAuth.
- No modelo atual **BYO-OAuth (Traga suas Próprias Credenciais)**, cada usuário configura seu próprio Projeto e ID de Cliente OAuth no Google Cloud Console para uso pessoal/individual. Caso venha a existir um Client ID oficial compartilhado e distribuído publicamente pela TundraLab no futuro, ele será submetido previamente ao processo oficial de Verificação OAuth do Google.

### Declaração de Uso Limitado (Google API Services User Data Policy)

O uso e a transferência de informações recebidas das APIs do Google pelo Custódia para qualquer outro aplicativo seguirão integralmente a [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), incluindo os requisitos de **Uso Limitado (Limited Use)**:

1. Os dados obtidos via API do Google Drive são utilizados exclusivamente para fornecer e operar os recursos de sincronização e gerenciamento local visíveis ao usuário no Custódia.
2. Os dados do Google Drive nunca são vendidos, alugados, usados para publicidade direcionada, perfilamento comercial ou treinamento de modelos de IA/ML.
3. Nenhuma informação ou conteúdo do Google Drive do usuário é transferido para servidores da TundraLab ou terceiros (a comunicação ocorre apenas diretamente entre o Custódia no seu Linux e os endpoints oficiais `googleapis.com` / `accounts.google.com`).

---

## 3. E-mails Opcionais de Ciclo de Vida da Conta (`contato.tundralab@gmail.com`)

Para confirmações opcionais de conexão inicial e revogação/desconexão de conta, o Custódia inclui uma integração opcional com o serviço oficial de disparo da TundraLab (Google Apps Script sob a conta `contato.tundralab@gmail.com`):

- **O que pode ser enviado:** exclusivamente `recipientEmail` (endereço de e-mail da conta Google conectada/desconectada), `recipientName` (nome de exibição retornado no perfil OAuth), `eventType` (`connect` ou `revoke`), `appName`, `appVersion`, `language` e `timestampUtc`.
- **O que NUNCA é enviado:** arquivos do Drive, nomes de pastas, metadados de documentos, logs locais, credenciais `credentials.json`, tokens `access_token` / `refresh_token` ou chaves privadas.
- **Finalidade:** enviar exclusivamente um e-mail transacional informando o usuário quando uma conta é vinculada ou desconectada no Custódia.
- **Controle e opt-out:** você pode aceitar ou recusar esses e-mails logo na tela inicial de consentimento do Custódia e alterar essa preferência a qualquer momento em **Sobre -> Rever termo inicial** (ou via `GmailAccountAlertsEnabled` em `~/.config/custodia/settings.json`). Quando desativado, nenhuma requisição é enviada ao endpoint de e-mail.

---

## 4. Verificação de Atualizações e Links Externos

O Custódia pode consultar os metadados públicos de lançamentos do repositório oficial no GitHub (`api.github.com/repos/TundraLab/Custodia/releases/latest`) apenas para verificar se existe uma versão mais recente disponível para download. Nenhuma informação de conta Google ou arquivo local é enviada nessa verificação.

---

## 5. Direitos do Usuário e Contato

Como os dados de sincronização, credenciais e tokens permanecem no seu dispositivo:

- Você pode revogar o acesso do aplicativo diretamente pela sua Conta Google ([Permissões de Terceiros](https://myaccount.google.com/permissions)) ou desconectar/remover contas pela interface do Custódia, o que apaga o arquivo de token local correspondente.
- Você pode desativar alertas por e-mail de ciclo de vida a qualquer momento nas configurações do aplicativo.
- Para dúvidas sobre privacidade, conformidade ou exercício de direitos relacionados aos e-mails enviados por `contato.tundralab@gmail.com`, entre em contato pelo e-mail: **`contato.tundralab@gmail.com`**.

---

## 6. Documentação Oficial de Referência

- [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy)
- [OAuth 2.0 Scopes for Google APIs](https://developers.google.com/identity/protocols/oauth2/scopes)
- [Google Cloud OAuth Verification FAQ](https://support.google.com/cloud/answer/9110914)
