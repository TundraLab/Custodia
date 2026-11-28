# Privacy Policy - Custódia

> 🌐 **Language / Idioma:** [🇧🇷 Português](../../PRIVACY.md) | **🇺🇸 English (current)**

**Last updated:** November 28, 2026  
**Controller / Rights Holder:** Gustavo Henrique Novaes Gomes (`@GustaHerin`), operating under the project and trade name **TundraLab** (`contato.tundralab@gmail.com`)  
**Official repository:** https://github.com/TundraLab/Custodia

This Privacy Policy describes how the **Custódia** desktop application handles local data, OAuth 2.0 credentials, Google API integrations, and optional account lifecycle emails.

---

## 1. Local Operation Model (Zero Server for Files)

**Custódia** runs directly on the user's machine:

- All files, synced directories, metadata indexes, hashes, transfer queues, and activity logs remain exclusively on the user's computer.
- User-provided OAuth credentials (`credentials.json`) are stored in `~/.config/custodia/contas/<email>/` and access/refresh tokens in `~/.local/share/custodia/tokens/`, with restricted POSIX permissions (`0700` on directories and `0600` on files inside).
- The local background service listens only on `127.0.0.1:5080` (local loopback interface), exposing no network ports for external access.
- TundraLab **does not** collect, **does not** store, **does not** transmit, and **has no** access to your Google Drive files, folder names, document contents, sync history, or OAuth tokens.

---

## 2. Google API Access and User Data Policy (Limited Use)

When you connect a Google account in Custódia:

- The application requests the `https://www.googleapis.com/auth/drive` scope (classified as a **restricted scope** by official Google Drive API documentation) strictly to list, download, upload, update, organize, and sync files and folders between Google Drive and locally configured Linux folders.
- Custódia **does not** request Gmail scopes on your Google account or on your OAuth credential.
- Under the current **BYO-OAuth (Bring Your Own Credentials)** model, each user configures their own Project and OAuth Client ID in Google Cloud Console for personal/individual use. If an official shared Client ID is ever publicly distributed by TundraLab in the future, it will first undergo Google's official OAuth Verification and applicable CASA security assessment.

### Limited Use Disclosure (Google API Services User Data Policy)

Custódia's use and transfer to any other app of information received from Google APIs will adhere strictly to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the **Limited Use** requirements:

1. Data obtained via the Google Drive API is used exclusively to provide and operate the local synchronization and management features visible to the user in Custódia.
2. Google Drive data is never sold, rented, or used for targeted advertising, commercial profiling, or AI/ML model training.
3. No Google Drive files, document contents, folder trees, or OAuth tokens are transferred to TundraLab servers or third parties (all file operations occur directly between Custódia on your Linux machine and official `googleapis.com` / `accounts.google.com` endpoints; only if you keep optional lifecycle alerts enabled under Section 3 are your email address and profile name sent to the transactional email dispatcher).

---

## 3. Optional Account Lifecycle Emails (`contato.tundralab@gmail.com`)

For optional initial connection confirmations and account revocation/disconnection notices, Custódia includes an optional integration with TundraLab's official delivery service (Google Apps Script under `contato.tundralab@gmail.com`):

- **Default state and consent:** `GmailAccountAlertsEnabled` starts enabled by default (`true`) subject to your choice on the initial welcome prompt before first sign-in.
- **What may be sent:** strictly `recipientEmail` (email address of the connected/disconnected Google account), `recipientName` (display name returned in the OAuth profile), `eventType` (`connect` or `revoke`), `appName`, `appVersion`, `language`, and `timestampUtc`.
- **What is NEVER sent:** Drive files, folder names, document metadata, local logs, `credentials.json`, `access_token` / `refresh_token` tokens, or private keys.
- **Purpose:** solely to send a transactional email informing the user when an account is linked or disconnected in Custódia.
- **Control and opt-out:** you can accept or decline these emails right on Custódia's initial consent screen and change that preference at any time in **About -> Review initial terms** (or set `GmailAccountAlertsEnabled: false` in `~/.config/custodia/settings.json`). When disabled, no request is sent to the email endpoint.

---

## 4. LGPD Compliance (Brazilian Law No. 13,709/2018)

- **Data Controller (natural person):** Gustavo Henrique Novaes Gomes (`@GustaHerin`), operating under the project and trade name TundraLab (`contato.tundralab@gmail.com`).
- **Legal bases:** execution of local app features requested by the user and explicit consent for optional lifecycle confirmation emails (LGPD Art. 7, I and V).
- **Retention & storage:** all sync state stays strictly on your Linux machine until you disconnect the account or uninstall and delete `~/.config/custodia` and `~/.local/share/custodia`.
- **International processing:** optional Google Apps Script lifecycle emails run on Google LLC cloud infrastructure outside Brazil only when email alerts remain enabled.
- **Data subject rights (Art. 18) & DPO contact:** you may exercise access, correction, consent withdrawal, or erasure directly inside the app/Google Account or via `contato.tundralab@gmail.com`. Full statutory wording is available under the **Legal** sub-mode on `/docs/privacy`.

---

## 4. Update Checks and External Links

Custódia may query public release metadata from the official GitHub repository (`api.github.com/repos/TundraLab/Custodia/releases/latest`) solely to check whether a newer version is available for download. No Google account information or local file is sent during this check.

---

## 5. User Rights and Contact

Because sync data, credentials, and tokens remain on your device:

- You can revoke the app's access directly from your Google Account ([Third-party access](https://myaccount.google.com/permissions)) or disconnect/remove accounts in Custódia's interface, which deletes the corresponding local token file.
- You can disable lifecycle email alerts at any time in the application settings.
- For questions regarding privacy, compliance, or exercising rights related to emails sent by `contato.tundralab@gmail.com`, contact: **`contato.tundralab@gmail.com`**.

---

## 6. Official Reference Documentation

- [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy)
- [OAuth 2.0 Scopes for Google APIs](https://developers.google.com/identity/protocols/oauth2/scopes)
- [Google Cloud OAuth Verification FAQ](https://support.google.com/cloud/answer/9110914)
