# Termos de Uso: Custódia

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](docs/en/TERMS.md)

**Última atualização:** 28 de novembro de 2026  
**Titular / Desenvolvedor:** Gustavo Henrique Novaes Gomes (`@GustaHerin`), sob o nome comercial e projeto **TundraLab** (`contato.tundralab@gmail.com`)  
**Repositório oficial:** https://github.com/TundraLab/Custodia

Ao baixar, instalar, compilar ou utilizar o **Custódia**, você declara que leu e concorda com estes Termos de Uso, com a [Política de Privacidade](PRIVACY.md) e com o [Contrato de Licença Proprietária](LICENSE).

---

## 1. Objeto e Licenciamento

O **Custódia** é um software cliente desktop para sincronização e gerenciamento local do Google Drive em sistemas Linux, de titularidade intelectual exclusiva de **Gustavo Henrique Novaes Gomes**, atuando sob o nome comercial e projeto **TundraLab** (`Copyright (c) 2026 Gustavo Henrique Novaes Gomes (TundraLab). All Rights Reserved.`).

- Os termos específicos de acesso ao código-fonte, proibição de redistribuição comercial sem autorização, uso de marcas e propriedade intelectual são regidos pelo arquivo [LICENSE](LICENSE).
- Nenhuma disposição destes Termos concede direitos sobre marcas, logotipos ou propriedade intelectual do Titular (Gustavo Henrique Novaes Gomes / TundraLab) ou do Google LLC.

---

## 2. Integração com Serviços do Google e Credenciais do Usuário

Para utilizar a sincronização com o Google Drive, você deve autenticar sua própria Conta Google:

1. **Modelo BYO-OAuth (Uso Pessoal/Individual):** na configuração padrão descrita no repositório, cada usuário cria e utiliza suas próprias credenciais OAuth 2.0 no Google Cloud Console (escopo sensível `https://www.googleapis.com/auth/drive`). Você é o único responsável pela guarda, configuração e conformidade do seu projeto no Google Cloud Console, bem como pelos limites de cota da sua conta.
2. **Proibição de Distribuição de Credenciais Alheias:** você não deve reutilizar credenciais OAuth de terceiros sem autorização nem redistribuir versões modificadas do aplicativo com Client IDs não verificados para terceiros como se fossem oficiais.
3. **Conformidade com o Google:** ao conectar o aplicativo ao Google Drive, você também permanece sujeito aos Termos de Serviço do Google e à [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy).

---

## 3. Responsabilidade pelos Dados e Sincronização

O Custódia executa operações locais e remotas de envio, atualização, exclusão e espelhamento conforme as regras de sincronização configuradas por você:

- **Cópias de segurança (Backups):** antes de habilitar sincronização bidirecional ou operações em lote sobre diretórios críticos, recomenda-se manter backups independentes de seus arquivos importantes.
- **Segurança local:** a proteção física e lógica da máquina Linux (usuário do sistema, criptografia de disco, permissões de pasta) é de responsabilidade exclusiva do usuário.

---

## 4. Comunicações Opcionais de Conta

Caso você habilite os alertas de ciclo de vida de conta no aplicativo, a TundraLab enviará apenas e-mails transacionais de confirmação de conexão ou revogação por meio do endereço `contato.tundralab@gmail.com`, utilizando exclusivamente seu endereço de e-mail e nome de perfil conforme detalhado na [Política de Privacidade](PRIVACY.md). Esse recurso é opcional e pode ser desativado a qualquer instante nas configurações.

---

## 5. Isenção de Garantias e Limitação de Responsabilidade

Na extensão máxima permitida pela legislação aplicável:

- O software é fornecido **"NO ESTADO EM QUE SE ENCONTRA" ("AS IS")**, sem garantias expressas ou implícitas de disponibilidade contínua, adequação a um propósito específico, ausência de interrupções por mudanças nas APIs do Google ou recuperação automática de dados apagados externamente.
- A TundraLab não se responsabiliza por danos diretos, indiretos, incidentais, perda de arquivos, indisponibilidade do Google Drive ou suspensão de projeto Google Cloud decorrente de uso indevido pelo usuário.
- O Custódia é um software independente desenvolvido pela TundraLab e **não possui afiliação, patrocínio ou endosso do Google LLC**. "Google Drive" e "Google Cloud" são marcas registradas de titularidade do Google LLC.

---

## 6. Alterações e Contato

Estes Termos podem ser atualizados para refletir mudanças técnicas, legais ou nas políticas das APIs do Google, exibindo a nova versão no aplicativo quando aplicável.

Dúvidas jurídicas, solicitações comerciais ou questões sobre estes Termos podem ser encaminhadas para **`contato.tundralab@gmail.com`**.
