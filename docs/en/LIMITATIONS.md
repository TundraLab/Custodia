# Known Limitations and Detailed Sync Behavior

> 🌐 **Language / Idioma:** [🇧🇷 Português](../LIMITATIONS.md) | **🇺🇸 English (current)**

This page documents specific behaviors, default exclusions, and technical boundaries of **Custódia (`v4.0.0`)** so you know exactly how your files are handled.

---

## 1. Native Google Docs, Sheets, and Slides

- **Regular binary files** (PDF, DOCX, XLSX, images, videos, archives) sync seamlessly between local disk and Google Drive.
- **Native Google Workspace documents** (`application/vnd.google-apps.*`, created online with no underlying binary blob) cannot be stored as editable raw binary copies; when fetched via the in-app browser or `POST /api/v1/files/download`, they are **automatically exported to portable formats (such as PDF)**.

---

## 2. Default ignore patterns (including One-Way Backup)

To avoid uploading transient browser/editor files and huge build trees, `AppSettings.IgnorePatterns` skips these patterns by default in both **Two-Way Sync** and **One-Way Backup**:

- Browser and OS temporary files: `*.tmp`, `*.swp`, `*.part`, `*.crdownload`, `.DS_Store`, `Thumbs.db`, `.goutputstream-*`
- Development and build folders: `.git/*`, `node_modules/*`, `bin/*`, `obj/*`

> **Heads up:** if you use **One-Way Backup** to back up Git repositories (`.git`) or folders named `bin/` / `obj/`, remove those entries under **Settings -> Sync -> Ignored files and folders** before syncing.

---

## 3. Symlinks, duplicate Drive names, and invalid filename characters

- **Symbolic links (symlinks):** not followed outside the synced folder tree to prevent infinite recursion loops and unintended system directory access.
- **Duplicate filenames in Google Drive:** Google Drive allows multiple items with the same name in a single folder (distinguished by ID), whereas Linux filesystems do not. Custódia keys tracked items by relative path (`relPath`) and defaults to **Keep conflict copy** (`RenameConflictCopy`) to prevent silent overwrites.
- **Invalid Linux characters:** remote names containing `/` or null bytes are sanitized via `SanitizeRemoteSegment` before writing to local disk.

---

## 4. Shared Drives and "Shared with me"

- Local folder synchronization targets folders inside **My Drive**.
- The **Shared with me** tab lets you browse and download shared items directly from the app without automatic organization-wide **Shared Drives** admin sync.

---

## 5. Google daily upload quota and 7-day Testing tokens

- Google enforces an account-level upload quota of **750 GB per day**. Initial uploads larger than 750 GB resume in later cycles after Google's quota resets.
- If your personal Google Cloud project stays in **Testing** mode, Google expires refresh tokens after **7 days** (see how to switch to *In production* for personal use in [OAuth2 Setup](OAUTH_SETUP.md)).

---

## 6. Cross-partition Linux Trash (`.Trash-UID`)

- When a cloud deletion syncs down in Two-Way mode, Custódia moves the local file to **FreeDesktop Trash**: inside `$HOME`, files move to `~/.local/share/Trash/`; on secondary mounted drives/partitions, Linux places them in `.Trash-<UID>` at the partition root when writable.

---

## 7. Supported architecture, installer runtime, and RAM cleaner

- **Target architecture:** Linux `x86_64` only (no `ARM64`, Windows, or macOS builds).
- **Graphical installer (`.AppImage` / `.run`):** relies on host system `GTK 3 / GTK 4` and `PyGObject` libraries. On minimal distros lacking Python GTK bindings, install the `.deb` or `.rpm` package directly.
- **Automatic RAM cleanup:** enabled by default (`AutoMemoryCleanupEnabled = true`, every 5 minutes) to keep resident tray memory low; configurable or toggleable under **Settings -> Application**.
