# Troubleshooting

> 🌐 **Language / Idioma:** [🇧🇷 Português](../TROUBLESHOOTING.md) | **🇺🇸 English (current)**

Practical guide to diagnose and resolve common issues with installation, desktop integration, OAuth2 authentication, and multimedia previews.

---

## 1. AppImage won't open (FUSE error / `libfuse.so.2`)

On newer distributions (such as Ubuntu 24.04+, Pop!_OS, or Fedora without `fuse2`), the AppImage may fail when trying to mount the image.

**Solution 1 — Run by extracting temporarily (without FUSE):**
```bash
./Custodia-4.0.0-x86_64.AppImage --appimage-extract-and-run
```

**Solution 2 — Install FUSE2 support:**
- **Fedora:**
  ```bash
  sudo dnf install fuse fuse-libs
  ```
- **Ubuntu / Pop!_OS / Debian:**
  ```bash
  sudo apt install libfuse2
  ```

---

## 2. System tray icon does not appear on GNOME

GNOME Workstation does not display `StatusNotifierItem` / AppIndicator icons natively without an extension. *(On KDE Plasma and Pop!_OS, support is enabled by default.)*

**Solution (Fedora Workstation / GNOME):**
```bash
sudo dnf install gnome-shell-extension-appindicator
gsettings set org.gnome.shell enabled-extensions "['appindicatorsupport@rgcjonas.gmail.com']"
```
After enabling for the first time, **log out and log back in** to your GNOME session so the top bar loads the extension.

---

## 3. D-Bus notifications or sound are not working

- **Visual notifications:** verify that your desktop environment's notification daemon is running and **Do Not Disturb** mode is not turned on in GNOME, KDE Plasma, or COSMIC.
- **Notification ringtones:** Custódia includes 6 synthesized ringtone variations plus silent mode. Check in **Settings** that notification sound is not set to silent and that your default audio device (`PipeWire`/`PulseAudio`) is active.

---

## 4. `403: access_denied` error when connecting a Google account (OAuth2)

This error occurs when the Google Cloud Console project is in **Testing** mode and the email used to sign in has not been added to the test users list.

**Solution:**
1. Open [Audience in Google Auth Platform](https://console.cloud.google.com/auth/audience).
2. Scroll to **Test users** and click **+ Add users**.
3. Add the exact Gmail/Google address you are using in Custódia and save.
4. Try connecting again from the application.

See the illustrated step-by-step guide in [OAuth2 Setup](OAUTH_SETUP.md).

---

## 5. OAuth2 JSON rejected on import (`Web` vs `Desktop app`)

Custódia accepts only **Desktop app** credentials (`"installed"` field in the JSON). **Web application** (`"web"`) credentials are rejected for security.

**Solution:**
- In Google Cloud Console (**APIs & Services → Credentials → + Create credentials → OAuth client ID**), select **Desktop app**, download the new `client_secret_*.json`, and import it into Custódia.

---

## 6. PDF, video, audio, or Office previews do not load thumbnails

The core app runs without external dependencies, but full thumbnail generation and multimedia previews use standard system tools when available.

**Install the recommended optional packages:**
- **Fedora / RHEL:**
  ```bash
  sudo dnf install poppler-utils ghostscript ffmpeg libreoffice ImageMagick
  ```
- **Pop!_OS / Ubuntu / Debian:**
  ```bash
  sudo apt install poppler-utils ghostscript ffmpeg libreoffice imagemagick
  ```

---

## 7. `.rpm` package installation or update fails

- **Conflict or corrupted package:** validate the `SHA-256` hash against the `SHA256SUMS.txt` file from the same release or use the official installer:
  ```bash
  ./install-custodia.sh --update
  ```
- **Manual installation with `dnf`:**
  ```bash
  sudo dnf install -y ./Custodia-4.0.0-1.x86_64.rpm
  ```

---

## 8. Local REST API (`127.0.0.1:5080`) is not responding

1. Confirm Custódia is running (the embedded HTTP server starts alongside the application).
2. Test locally on *loopback*:
   ```bash
   curl -s http://127.0.0.1:5080/api/v1/status
   ```
3. Check whether another local process is already occupying port `5080`:
   ```bash
   ss -tlnp | grep 5080
   ```
4. Remember that the API listens exclusively on `127.0.0.1` (it does not respond on external network IPs or `0.0.0.0`).

---

## 9. Collecting technical diagnostics to report a bug

In Custódia's **About** tab:
1. Select **Bug / Error** in the support form.
2. Click **View data** to inspect the sanitized diagnostic bundle (no tokens, passwords, or file contents).
3. Use **Copy text** if the full log exceeds the browser's URL length limit when opening the GitHub issue.
