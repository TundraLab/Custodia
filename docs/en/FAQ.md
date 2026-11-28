# Frequently Asked Questions (FAQ)

> 🌐 **Language / Idioma:** [🇧🇷 Português](../FAQ.md) | **🇺🇸 English (current)**

Quick answers about offline operation, conflict resolution, file recovery, credential security, and removing Custódia.

---

## 1. Does Custódia work without internet?

**Partially.**
- **Local files:** your mapped folders remain available normally on your computer for offline reading and editing.
- **Queue and monitoring:** changes made while the computer is offline are recorded locally and synced as soon as connectivity returns (the engine detects network changes automatically).
- **Remote browsing (*My Drive*) and transfers:** require an active connection to Google Drive API v3. The update checker also includes offline protection so it doesn't fail when there is no network.

---

## 2. What happens when the same file is modified on both PC and Drive at the same time?

Custódia applies the policy configured in **Settings → On conflict** (`ConflictPolicy`). Available options are:

| Policy | Behavior |
| :--- | :--- |
| **Newest wins** *(default)* | Compares the local modification timestamp with Google Drive (`ModifiedTimeUtc`) and keeps the newer version. |
| **Prefer local** | Uploads the computer's file and overwrites the Drive version. |
| **Prefer remote** | Downloads the Google Drive version and overwrites the local file. |
| **Keep conflict copy** | Preserves the local file with a conflict suffix in the same directory, downloads the Drive version, and logs a warning (`ConflictRenamed`). |

---

## 3. How do I recover a deleted file or folder?

Custódia uses safe deletion by default:
- **Deletions synced to Drive:** locally removed items are sent to **Google Drive Trash**, rather than being permanently deleted immediately.
- **Deletions synced to PC:** in **Two-Way (`PC <-> Drive`)** mode, items removed in Drive are sent to the **system FreeDesktop Trash** (compatible with GNOME Files and KDE Dolphin).
- **Restoration:** you can restore items directly from the **Trash** tab inside Custódia, from the Google Drive website, or from your file manager's trash.
- **Permanent deletion:** only occurs when you manually trigger permanent removal in Trash (or via `DELETE /api/v1/files/{id}?permanent=true` in the local API).

---

## 4. What is the difference between Two-Way sync and One-Way Backup?

- **Two-Way (`PC <-> Drive`):** keeps PC and Google Drive mirrored in both directions. Changes and deletions on one side reflect on the other (always via Trash).
- **One-Way Backup (`PC -> Drive`):** treats the PC as the source of truth. New or edited files on PC upload to Drive, and deleting a file on **PC** moves the remote item to **Google Drive Trash** (recoverable for up to 30 days). Conversely, it **never** downloads new files from Drive nor deletes local files on PC if they are removed on Drive.

See the full comparison table in the [README](README.md#sync-modes).

---

## 5. Where are my OAuth2 credentials and tokens stored?

All sensitive data is isolated locally in your user directory with restricted POSIX permissions (`0700` for directories and `0600` for files):
- `~/.config/custodia/contas/<email>/credentials.json` — your Desktop OAuth2 client credentials.
- `~/.config/custodia/contas/<email>/conta.json` and `pastas-sincronizadas.json` — account metadata and mappings.
- `~/.local/share/custodia/tokens/` — `refresh_token` and Google session tokens.
- `~/.local/share/custodia/custodia.db` — local SQLite database (sync state, hashes, and logs).

No files, tokens, or credentials are ever sent to TundraLab servers. For more details, see [Security and Privacy](SECURITY.md).

---

## 6. If I remove an account in Custódia, do I lose my configured folders?

**Not by default.** When removing an account, the local token is invalidated and the session is closed, but Custódia preserves the folder configuration linked to that email so you can easily reconnect the same account later. If you also want to delete the folder mappings, check the explicit option in the confirmation dialog.

---

## 7. Can I use Custódia with multiple Google accounts?

**Yes.** Each account has its own isolated directory in `~/.config/custodia/contas/<email>/` with independent credentials, tokens, and synced folders, allowing fast switching in the interface.

---

## 8. How do I completely uninstall Custódia (including local data)?

1. Run the official uninstaller to remove the `.rpm` package or `.AppImage` integration:
   ```bash
   ./install-custodia.sh --uninstall
   ```
2. If you also want to delete all settings, tokens, SQLite database, and local cache:
   ```bash
   rm -rf ~/.config/custodia ~/.local/share/custodia ~/.cache/custodia
   ```
   *(Your personal files inside synced folders and on Google Drive are not deleted by this command.)*

---

## 9. I moved or renamed a folder and several deletions appeared in the queue — did I lose my files?

**No.** During synchronization, **nothing is permanently deleted** — every removed or relocated item goes exclusively to **Trash**.

- Because the engine compares files by relative path (`relPath`), moving or renaming a folder with many files between synced directories causes Custódia to send the old paths to **Trash** (Google Drive or system Trash) and re-sync the files at the new destination.
- Permanent deletions only happen if you open the **Trash** tab (or call the `DELETE` endpoint with `permanent=true`) and manually confirm permanent removal.
