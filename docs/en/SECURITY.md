# Security and Privacy

> 🌐 **Language / Idioma:** [🇧🇷 Português](../SECURITY.md) | **🇺🇸 English (current)**

Custódia is designed around a **local-first** principle: your files, metadata, and OAuth2 tokens remain exclusively on your machine and travel directly between your Linux system and the official Google Drive API, with no TundraLab intermediary servers.

---

## 1. BYO-OAuth2 Model (Bring Your Own OAuth)

- **No central proxy server:** you create your own **Desktop app** OAuth2 client in Google Cloud Console (see [OAuth2 Setup](OAUTH_SETUP.md)).
- **No password storage:** the `client_secret_*.json` file only identifies the OAuth application; your Google password is entered exclusively on Google's official sign-in page in your browser.
- **Requested scope:** `https://www.googleapis.com/auth/drive` (required to list, upload, download, move, restore, and sync your chosen folders). No Gmail scope is requested.
- **Google compliance:** data handling adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy) (Limited Use).

---

## 2. Local Storage and POSIX Permissions (`0700` / `0600`)

All sensitive directories and files created by Custódia receive permissions restricted to the current user:

| Path | Contents | Permission |
| :--- | :--- | :--- |
| `~/.config/custodia/settings.json` | General preferences and visual folder customizations | User-restricted |
| `~/.config/custodia/contas/<email>/` | Isolated per-Google-account directory | `0700` |
| `~/.config/custodia/contas/<email>/credentials.json` | Desktop OAuth2 client credentials | `0600` |
| `~/.config/custodia/contas/<email>/conta.json` | Connected account metadata | `0600` |
| `~/.config/custodia/contas/<email>/pastas-sincronizadas.json` | Account synced folder mappings | `0600` |
| `~/.local/share/custodia/tokens/` | OAuth2 tokens (`refresh_token` / `access_token`) | `0600` |
| `~/.local/share/custodia/custodia.db` | Local SQLite database (sync state, MD5/SHA256 hashes, and logs) | User-restricted |

---

## 3. Token Lifecycle and Revocation

- **Automatic renewal:** the official Google SDK uses the locally saved `refresh_token` (`0600`) to obtain new temporary `access_token` values directly from Google's OAuth2 endpoint.
- **Disconnection / Revocation:** when removing an account, the local token is invalidated and the session in `~/.config/custodia/contas/<email>/` is closed. By default, folder mappings are preserved for future reconnection, with an explicit UI option to delete them.

---

## 4. Local REST API Surface (`127.0.0.1:5080`)

- The embedded API listens **exclusively on *loopback* (`127.0.0.1:5080`)**, never on `0.0.0.0`.
- Allows local scripts and AI agents to interact with Custódia without accessing or directly handling your OAuth2 tokens.
- **Recommendation:** do not expose port `5080` outside the machine (via reverse proxy, port forwarding, or public tunnel).

---

## 5. Optional Lifecycle Emails and Support Diagnostics

- **Lifecycle emails (`contato.tundralab@gmail.com`):** **optional** feature that can be declined on first launch or changed in **About → Review initial terms**. When enabled, it sends only `recipientEmail`, `recipientName`, `eventType` (`connect` or `revoke`), `appName`, `appVersion`, `language`, and `timestampUtc` to notify account connection or revocation.
- **Bug diagnostics:** when opening a **Bug / Error** report on the **About** tab, technical data is sanitized (no tokens, credentials, or file contents) and can be inspected before sending via the **View data** button.

---

## 6. Legal Documents and Complete Removal

- [Privacy Policy](../../PRIVACY.en.md)
- [Terms of Use](../../TERMS.en.md)
- [License](../../LICENSE)

To uninstall the application and erase all local configuration, tokens, and SQLite database data:
```bash
./install-custodia.sh --uninstall
rm -rf ~/.config/custodia ~/.local/share/custodia ~/.cache/custodia
```
