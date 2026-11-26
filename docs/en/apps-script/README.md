# TundraLab Email Service (`contato.tundralab@gmail.com`) via Google Apps Script

> 🌐 **Language / Idioma:** [🇧🇷 Português](../../apps-script/README.md) | **🇺🇸 English (current)**

This service allows the **Custódia** desktop application to send automatic **welcome on connect** and **confirmation on revoke/disconnect** emails directly from `contato.tundralab@gmail.com`, **without using the Gmail API in users' OAuth projects** and **without exposing passwords in the public source code**.

## How to enable in 1 minute

1. Open [script.google.com](https://script.google.com) signed in as **`contato.tundralab@gmail.com`**.
2. Click **New project** and paste the entire contents of `docs/apps-script/contato-tundralab-mailer.gs`.
3. Click **Deploy > New deployment > Type: Web app**:
   - **Execute as:** `Me (contato.tundralab@gmail.com)`
   - **Who has access:** `Anyone`
4. Authorize the script permission once and copy the generated `/exec` URL (example: `https://script.google.com/macros/s/.../exec`).
5. Provide this URL to Custódia using any of the options below:
   - Setting `DefaultWebhookUrl` in `src/Custodia.Core/Notifications/AppsScriptMailerClient.cs` before building packages; or
   - Setting `MailerWebhookUrl` in `~/.config/custodia/settings.json`; or
   - Exporting the environment variable `CUSTODIA_MAILER_WEBHOOK_URL`.
