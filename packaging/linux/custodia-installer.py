#!/usr/bin/env python3
"""
Custodia Linux Installer by TundraLab
Lightweight graphical installer allowing release and package format (.deb, .rpm, .AppImage) selection.
Auto-deletes itself after a successful installation.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path

REPO_SLUG = "TundraLab/Custodia"
RELEASES_API = f"https://api.github.com/repos/{REPO_SLUG}/releases?per_page=12"
FALLBACK_VERSION = "4.0.0"
USER_AGENT = "Custodia-Installer/4.0.0"


def detect_default_format() -> str:
    if (
        os.path.exists("/etc/fedora-release")
        or os.path.exists("/etc/redhat-release")
        or shutil.which("dnf")
        or shutil.which("zypper")
    ):
        return "rpm"
    if (
        os.path.exists("/etc/debian_version")
        or shutil.which("apt-get")
        or shutil.which("dpkg")
    ):
        return "deb"
    return "appimage"


def fetch_releases() -> list[dict]:
    req = urllib.request.Request(
        RELEASES_API,
        headers={
            "User-Agent": USER_AGENT,
            "Accept": "application/vnd.github+json",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.loads(resp.read().decode("utf-8"))
        releases = []
        for rel in data:
            if rel.get("draft"):
                continue
            tag = (rel.get("tag_name") or "").strip()
            if not tag:
                continue
            assets = {}
            for a in rel.get("assets", []):
                name = a.get("name") or ""
                url = a.get("browser_download_url") or ""
                size = int(a.get("size") or 0)
                if not name or not url:
                    continue
                lower = name.lower()
                if lower.endswith(".deb"):
                    assets["deb"] = (name, url, size)
                elif lower.endswith(".rpm"):
                    assets["rpm"] = (name, url, size)
                elif lower.endswith(".appimage") and "installer" not in lower:
                    assets["appimage"] = (name, url, size)
                elif lower == "sha256sums.txt":
                    assets["sums"] = (name, url, size)
            if any(k in assets for k in ("deb", "rpm", "appimage")):
                releases.append({"tag": tag, "name": rel.get("name") or tag, "assets": assets})
        if releases:
            return releases
    except Exception:
        pass

    tag = f"v{FALLBACK_VERSION}"
    base = f"https://github.com/{REPO_SLUG}/releases/download/{tag}"
    return [
        {
            "tag": tag,
            "name": f"Custódia {tag}",
            "assets": {
                "deb": (f"Custodia-{FALLBACK_VERSION}_amd64.deb", f"{base}/Custodia-{FALLBACK_VERSION}_amd64.deb", 0),
                "rpm": (f"Custodia-{FALLBACK_VERSION}-1.x86_64.rpm", f"{base}/Custodia-{FALLBACK_VERSION}-1.x86_64.rpm", 0),
                "appimage": (f"Custodia-{FALLBACK_VERSION}-x86_64.AppImage", f"{base}/Custodia-{FALLBACK_VERSION}-x86_64.AppImage", 0),
                "sums": ("SHA256SUMS.txt", f"{base}/SHA256SUMS.txt", 0),
            },
        }
    ]


def download_file(url: str, dest: Path, on_progress, cancel_event: threading.Event) -> None:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=60) as resp:
        total = int(resp.headers.get("Content-Length") or 0)
        downloaded = 0
        start = time.monotonic()
        last_tick = 0.0
        with open(dest, "wb") as fh:
            while True:
                if cancel_event.is_set():
                    raise InterruptedError("Instalação cancelada.")
                chunk = resp.read(65536)
                if not chunk:
                    break
                fh.write(chunk)
                downloaded += len(chunk)
                now = time.monotonic()
                if now - last_tick >= 0.12 or (total > 0 and downloaded >= total):
                    last_tick = now
                    elapsed = max(0.05, now - start)
                    mbps = (downloaded / (1024 * 1024)) / elapsed
                    pct = (downloaded / total) if total > 0 else 0.0
                    on_progress(downloaded, total, min(1.0, pct), mbps)


def verify_sha256(file_path: Path, asset_name: str, sums_url: str | None) -> bool:
    if not sums_url:
        return False
    req = urllib.request.Request(sums_url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            text = resp.read().decode("utf-8", errors="ignore")
    except Exception:
        return False

    expected = None
    for line in text.splitlines():
        parts = line.strip().split()
        if len(parts) >= 2 and Path(parts[-1].lstrip("*")).name == asset_name:
            expected = parts[0].lower()
            break
    if not expected:
        return False

    h = hashlib.sha256()
    with open(file_path, "rb") as fh:
        for block in iter(lambda: fh.read(131072), b""):
            h.update(block)
    actual = h.hexdigest().lower()
    if actual != expected:
        raise RuntimeError(f"Falha na verificação SHA-256 de {asset_name}.")
    return True


def write_desktop_files(exec_path: str, is_appimage: bool) -> None:
    home = Path.home()
    apps_dir = home / ".local" / "share" / "applications"
    autostart_dir = home / ".config" / "autostart"
    apps_dir.mkdir(parents=True, exist_ok=True)
    autostart_dir.mkdir(parents=True, exist_ok=True)

    if is_appimage:
        local_bin = home / ".local" / "bin"
        local_bin.mkdir(parents=True, exist_ok=True)
        wrapper = local_bin / "custodia"
        wrapper.write_text(f'#!/usr/bin/env bash\nexec "{exec_path}" "$@"\n', encoding="utf-8")
        wrapper.chmod(0o755)
        menu_entry = apps_dir / "custodia.desktop"
        menu_entry.write_text(
            "[Desktop Entry]\n"
            "Type=Application\n"
            "Version=1.0\n"
            "Name=Custódia\n"
            "GenericName=Google Drive Client\n"
            "Comment=Custódia by TundraLab - Seus arquivos. Sempre seguros.\n"
            "Icon=custodia\n"
            f'Exec="{exec_path}"\n'
            "Terminal=false\n"
            "Categories=Network;Utility;\n"
            "StartupWMClass=Custodia\n",
            encoding="utf-8",
        )
    else:
        user_entry = apps_dir / "custodia.desktop"
        if user_entry.exists():
            try:
                user_entry.unlink()
            except OSError:
                pass

    autostart_entry = autostart_dir / "custodia.desktop"
    autostart_entry.write_text(
        "[Desktop Entry]\n"
        "Type=Application\n"
        "Version=1.0\n"
        "Name=Custódia\n"
        "Comment=Custódia by TundraLab - Seus arquivos. Sempre seguros.\n"
        "Icon=custodia\n"
        f'Exec="{exec_path}"\n'
        "Terminal=false\n"
        "X-GNOME-Autostart-enabled=true\n",
        encoding="utf-8",
    )

    if shutil.which("update-desktop-database"):
        subprocess.run(["update-desktop-database", "-q", str(apps_dir)], check=False)


def install_package(pkg_path: Path, fmt: str, log_cb) -> str:
    subprocess.run(["pkill", "-x", "Custodia"], check=False)
    time.sleep(0.3)

    if fmt == "appimage":
        target_dir = Path.home() / ".local" / "opt" / "custodia"
        target_dir.mkdir(parents=True, exist_ok=True)
        target_bin = target_dir / "Custodia.AppImage"
        tmp_bin = target_dir / "Custodia.AppImage.tmp"
        shutil.copy2(pkg_path, tmp_bin)
        tmp_bin.chmod(0o755)
        tmp_bin.replace(target_bin)
        write_desktop_files(str(target_bin), is_appimage=True)
        log_cb(f"AppImage instalado em {target_bin}")
        return str(target_bin)

    quoted = str(pkg_path).replace("'", "'\\''")
    if fmt == "deb":
        if shutil.which("apt-get"):
            cmd = f"pkexec apt-get install -y '{quoted}'"
        else:
            cmd = f"pkexec dpkg -i '{quoted}'"
    else:
        if shutil.which("dnf"):
            cmd = f"pkexec dnf install -y '{quoted}'"
        elif shutil.which("zypper"):
            cmd = f"pkexec zypper --non-interactive install -y --allow-unsigned-rpm '{quoted}'"
        else:
            cmd = f"pkexec rpm -Uvh --replacepkgs --nosignature '{quoted}'"

    log_cb(f"Executando instalação do pacote .{fmt}...")
    proc = subprocess.Popen(
        ["bash", "-c", cmd],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    assert proc.stdout is not None
    for line in proc.stdout:
        stripped = line.strip()
        if stripped:
            log_cb(stripped)
    rc = proc.wait()
    if rc != 0:
        raise RuntimeError(f"A instalação do pacote .{fmt} retornou código {rc}.")

    write_desktop_files("/usr/bin/custodia", is_appimage=False)
    return "/usr/bin/custodia"


def self_delete_installer(log_cb) -> None:
    try:
        self_path = Path(os.path.realpath(sys.argv[0]))
        if not self_path.is_file():
            return
        sp = str(self_path)
        if sp.startswith("/opt/") or sp.startswith("/usr/"):
            return
        if self_path.name.endswith(".py") and "packaging/linux" in sp:
            return
        self_path.unlink()
        log_cb(f"Instalador removido automaticamente: {self_path.name}")
    except Exception as ex:
        log_cb(f"Aviso ao remover instalador: {ex}")


def launch_custodia(bin_path: str) -> None:
    try:
        if shutil.which("systemd-run"):
            subprocess.Popen(
                ["systemd-run", "--user", "--collect", bin_path],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True,
            )
        else:
            subprocess.Popen(
                [bin_path],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True,
            )
    except Exception:
        pass


GTK_CSS = b"""
window, .custodia-root {
  background-color: #111318;
  color: #E2E2E9;
  font-family: Inter, Cantarell, sans-serif;
}
.title-label {
  font-size: 20px;
  font-weight: 700;
  color: #E2E2E9;
}
.subtitle-label {
  font-size: 13px;
  color: #9296A4;
}
.section-label {
  font-size: 12px;
  font-weight: 600;
  color: #C4C6D0;
}
.m3-card {
  background-color: #1D2024;
  border-radius: 16px;
  padding: 16px;
}
.status-label {
  font-size: 13px;
  color: #C4C6D0;
}
.status-ok {
  color: #6DD58C;
  font-weight: 600;
}
.status-err {
  color: #FFB4AB;
  font-weight: 600;
}
button.m3-primary {
  background-image: none;
  background-color: #A8C7FA;
  color: #062E6F;
  border-radius: 20px;
  border: none;
  padding: 9px 20px;
  font-weight: 700;
}
button.m3-primary:hover {
  background-color: #BDD6FF;
}
button.m3-secondary {
  background-image: none;
  background-color: #282A2F;
  color: #E2E2E9;
  border-radius: 20px;
  border: 1px solid #44474F;
  padding: 8px 16px;
  font-weight: 600;
}
progressbar trough {
  min-height: 7px;
  border-radius: 4px;
  background-color: #33363D;
}
progressbar progress {
  min-height: 7px;
  border-radius: 4px;
  background-color: #A8C7FA;
}
textview, textview text {
  background-color: #111318;
  color: #C4C6D0;
  font-family: monospace;
  font-size: 11px;
}
"""


def run_gtk4() -> bool:
    try:
        import gi
        gi.require_version("Gtk", "4.0")
        from gi.repository import Gdk, GLib, Gtk
    except Exception:
        return False

    class CustodiaInstallerApp(Gtk.Application):
        def __init__(self) -> None:
            super().__init__(application_id="com.tundralab.custodia.installer")
            self.releases: list[dict] = []
            self.selected_fmt = detect_default_format()
            self.cancel_event = threading.Event()
            self.installed_bin: str | None = None
            self.is_running = False

        def do_activate(self) -> None:
            provider = Gtk.CssProvider()
            provider.load_from_data(GTK_CSS)
            Gtk.StyleContext.add_provider_for_display(
                Gdk.Display.get_default(),
                provider,
                Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION,
            )

            win = Gtk.ApplicationWindow(application=self)
            win.set_title("Custódia — Instalador")
            win.set_default_size(520, 470)
            win.set_resizable(False)
            self.win = win

            root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            root.set_margin_top(22)
            root.set_margin_bottom(20)
            root.set_margin_start(22)
            root.set_margin_end(22)
            root.add_css_class("custodia-root")
            win.set_child(root)

            title = Gtk.Label(label="Instalador do Custódia", xalign=0)
            title.add_css_class("title-label")
            subtitle = Gtk.Label(
                label="Escolha a versão e o formato para instalar no seu Linux.",
                xalign=0,
            )
            subtitle.add_css_class("subtitle-label")
            root.append(title)
            root.append(subtitle)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
            card.add_css_class("m3-card")
            root.append(card)

            ver_lbl = Gtk.Label(label="Versão (Release no GitHub)", xalign=0)
            ver_lbl.add_css_class("section-label")
            card.append(ver_lbl)

            self.ver_dropdown = Gtk.DropDown.new_from_strings(["Carregando versões..."])
            self.ver_dropdown.set_sensitive(False)
            card.append(self.ver_dropdown)

            fmt_lbl = Gtk.Label(label="Formato de instalação", xalign=0)
            fmt_lbl.add_css_class("section-label")
            card.append(fmt_lbl)

            fmt_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            self.rb_deb = Gtk.CheckButton(label="Pacote nativo .deb (Ubuntu, Debian, Pop!_OS, Mint)")
            self.rb_rpm = Gtk.CheckButton(label="Pacote nativo .rpm (Fedora, RHEL, openSUSE)")
            self.rb_rpm.set_group(self.rb_deb)
            self.rb_app = Gtk.CheckButton(label="Portátil .AppImage (Qualquer Linux, sem senha root)")
            self.rb_app.set_group(self.rb_deb)

            if self.selected_fmt == "rpm":
                self.rb_rpm.set_active(True)
            elif self.selected_fmt == "deb":
                self.rb_deb.set_active(True)
            else:
                self.rb_app.set_active(True)

            fmt_box.append(self.rb_deb)
            fmt_box.append(self.rb_rpm)
            fmt_box.append(self.rb_app)
            card.append(fmt_box)

            self.progress = Gtk.ProgressBar()
            self.progress.set_fraction(0.0)
            self.progress.set_visible(False)
            card.append(self.progress)

            self.status_lbl = Gtk.Label(label="Pronto para instalar.", xalign=0)
            self.status_lbl.set_wrap(True)
            self.status_lbl.add_css_class("status-label")
            card.append(self.status_lbl)

            expander = Gtk.Expander(label="Detalhes")
            scroll = Gtk.ScrolledWindow()
            scroll.set_min_content_height(100)
            self.log_buf = Gtk.TextBuffer()
            tv = Gtk.TextView(buffer=self.log_buf)
            tv.set_editable(False)
            tv.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
            scroll.set_child(tv)
            expander.set_child(scroll)
            root.append(expander)

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            btn_row.set_halign(Gtk.Align.END)
            root.append(btn_row)

            self.cancel_btn = Gtk.Button(label="Fechar")
            self.cancel_btn.add_css_class("m3-secondary")
            self.cancel_btn.connect("clicked", self.on_cancel_clicked)
            btn_row.append(self.cancel_btn)

            self.action_btn = Gtk.Button(label="Instalar")
            self.action_btn.add_css_class("m3-primary")
            self.action_btn.set_sensitive(False)
            self.action_btn.connect("clicked", self.on_action_clicked)
            btn_row.append(self.action_btn)

            win.present()
            threading.Thread(target=self.load_releases_bg, daemon=True).start()

        def append_log(self, msg: str) -> None:
            def _do() -> bool:
                end_iter = self.log_buf.get_end_iter()
                self.log_buf.insert(end_iter, f"{msg}\n")
                return False
            GLib.idle_add(_do)

        def set_status(self, msg: str, css_class: str | None = None) -> None:
            def _do() -> bool:
                self.status_lbl.set_text(msg)
                self.status_lbl.remove_css_class("status-ok")
                self.status_lbl.remove_css_class("status-err")
                if css_class:
                    self.status_lbl.add_css_class(css_class)
                return False
            GLib.idle_add(_do)

        def load_releases_bg(self) -> None:
            rels = fetch_releases()
            self.releases = rels
            labels = [
                f"{r['tag']} (Mais recente)" if idx == 0 else r["tag"]
                for idx, r in enumerate(rels)
            ]

            def _apply() -> bool:
                model = Gtk.StringList.new(labels)
                self.ver_dropdown.set_model(model)
                self.ver_dropdown.set_selected(0)
                self.ver_dropdown.set_sensitive(True)
                self.action_btn.set_sensitive(True)
                self.append_log(f"{len(rels)} release(s) encontradas em {REPO_SLUG}.")
                return False

            GLib.idle_add(_apply)

        def get_selected_format(self) -> str:
            if self.rb_rpm.get_active():
                return "rpm"
            if self.rb_deb.get_active():
                return "deb"
            return "appimage"

        def on_cancel_clicked(self, _btn) -> None:
            if self.is_running:
                self.cancel_event.set()
                self.set_status("Cancelando...", None)
            else:
                self.win.close()

        def on_action_clicked(self, _btn) -> None:
            if self.installed_bin:
                launch_custodia(self.installed_bin)
                self.win.close()
                return
            if self.is_running or not self.releases:
                return

            idx = self.ver_dropdown.get_selected()
            rel = self.releases[min(idx, len(self.releases) - 1)]
            fmt = self.get_selected_format()

            self.is_running = True
            self.cancel_event.clear()
            self.action_btn.set_sensitive(False)
            self.ver_dropdown.set_sensitive(False)
            self.rb_deb.set_sensitive(False)
            self.rb_rpm.set_sensitive(False)
            self.rb_app.set_sensitive(False)
            self.cancel_btn.set_label("Cancelar")
            self.progress.set_visible(True)
            self.progress.set_fraction(0.05)

            threading.Thread(
                target=self.run_install_bg,
                args=(rel, fmt),
                daemon=True,
            ).start()

        def run_install_bg(self, rel: dict, fmt: str) -> None:
            tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-dl-"))
            try:
                assets = rel["assets"]
                if fmt not in assets:
                    raise RuntimeError(
                        f"A versão {rel['tag']} não possui pacote .{fmt}. Escolha outra versão ou formato."
                    )
                asset_name, url, _size = assets[fmt]
                sums_url = assets["sums"][1] if "sums" in assets else None
                dest = tmp_dir / asset_name

                self.set_status(f"Baixando {asset_name}...")
                self.append_log(f"Baixando {url}")

                def on_prog(dl: int, tot: int, frac: float, mbps: float) -> None:
                    dl_mb = dl / (1024 * 1024)
                    tot_mb = tot / (1024 * 1024) if tot > 0 else dl_mb
                    txt = f"Baixando {asset_name}... {int(frac * 100)}% ({dl_mb:.1f} / {tot_mb:.1f} MB • {mbps:.1f} MB/s)"
                    GLib.idle_add(self.progress.set_fraction, min(0.8, frac * 0.8))
                    GLib.idle_add(self.status_lbl.set_text, txt)

                download_file(url, dest, on_prog, self.cancel_event)

                self.set_status("Verificando integridade (SHA-256)...")
                GLib.idle_add(self.progress.set_fraction, 0.85)
                verified = verify_sha256(dest, asset_name, sums_url)
                self.append_log("SHA-256 validado com sucesso." if verified else "SHA-256 não disponível nesta release.")

                self.set_status(f"Instalando {asset_name}...")
                GLib.idle_add(self.progress.set_fraction, 0.92)
                bin_path = install_package(dest, fmt, self.append_log)
                self_delete_installer(self.append_log)

                self.installed_bin = bin_path
                GLib.idle_add(self.progress.set_fraction, 1.0)
                self.set_status(f"Custódia {rel['tag']} instalado com sucesso!", "status-ok")

                def _done() -> bool:
                    self.is_running = False
                    self.action_btn.set_label("Abrir Custódia")
                    self.action_btn.set_sensitive(True)
                    self.cancel_btn.set_label("Fechar")
                    return False

                GLib.idle_add(_done)
            except Exception as ex:
                self.append_log(f"ERRO: {ex}")
                self.set_status(f"Falha: {ex}", "status-err")

                def _fail() -> bool:
                    self.is_running = False
                    self.action_btn.set_sensitive(True)
                    self.ver_dropdown.set_sensitive(True)
                    self.rb_deb.set_sensitive(True)
                    self.rb_rpm.set_sensitive(True)
                    self.rb_app.set_sensitive(True)
                    self.cancel_btn.set_label("Fechar")
                    return False

                GLib.idle_add(_fail)
            finally:
                shutil.rmtree(tmp_dir, ignore_errors=True)

    app = CustodiaInstallerApp()
    app.run(None)
    return True


def run_gtk3() -> bool:
    try:
        import gi
        gi.require_version("Gtk", "3.0")
        from gi.repository import Gdk, GLib, Gtk
    except Exception:
        return False

    provider = Gtk.CssProvider()
    provider.load_from_data(GTK_CSS)
    Gtk.StyleContext.add_provider_for_screen(
        Gdk.Screen.get_default(),
        provider,
        Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION,
    )

    win = Gtk.Window(title="Custódia — Instalador")
    win.set_default_size(520, 450)
    win.set_resizable(False)
    win.connect("destroy", Gtk.main_quit)

    state = {"releases": [], "running": False, "installed_bin": None, "cancel": threading.Event()}

    root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
    root.set_border_width(20)
    win.add(root)

    title = Gtk.Label(label="Instalador do Custódia", xalign=0)
    title.get_style_context().add_class("title-label")
    sub = Gtk.Label(label="Escolha a versão e o formato para instalar no seu Linux.", xalign=0)
    sub.get_style_context().add_class("subtitle-label")
    root.pack_start(title, False, False, 0)
    root.pack_start(sub, False, False, 0)

    card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
    card.get_style_context().add_class("m3-card")
    root.pack_start(card, True, True, 0)

    combo = Gtk.ComboBoxText()
    combo.append_text("Carregando versões...")
    combo.set_active(0)
    combo.set_sensitive(False)
    card.pack_start(Gtk.Label(label="Versão (Release no GitHub)", xalign=0), False, False, 0)
    card.pack_start(combo, False, False, 0)

    default_fmt = detect_default_format()
    rb_deb = Gtk.RadioButton.new_with_label(None, "Pacote nativo .deb (Ubuntu, Debian, Pop!_OS, Mint)")
    rb_rpm = Gtk.RadioButton.new_with_label_from_widget(rb_deb, "Pacote nativo .rpm (Fedora, RHEL, openSUSE)")
    rb_app = Gtk.RadioButton.new_with_label_from_widget(rb_deb, "Portátil .AppImage (Qualquer Linux, sem senha root)")
    if default_fmt == "rpm":
        rb_rpm.set_active(True)
    elif default_fmt == "deb":
        rb_deb.set_active(True)
    else:
        rb_app.set_active(True)

    card.pack_start(Gtk.Label(label="Formato de instalação", xalign=0), False, False, 0)
    card.pack_start(rb_deb, False, False, 0)
    card.pack_start(rb_rpm, False, False, 0)
    card.pack_start(rb_app, False, False, 0)

    progress = Gtk.ProgressBar()
    progress.set_no_show_all(True)
    card.pack_start(progress, False, False, 0)

    status_lbl = Gtk.Label(label="Pronto para instalar.", xalign=0)
    status_lbl.set_line_wrap(True)
    card.pack_start(status_lbl, False, False, 0)

    btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
    btn_row.set_halign(Gtk.Align.END)
    root.pack_start(btn_row, False, False, 0)

    cancel_btn = Gtk.Button(label="Fechar")
    action_btn = Gtk.Button(label="Instalar")
    action_btn.get_style_context().add_class("m3-primary")
    action_btn.set_sensitive(False)
    btn_row.pack_start(cancel_btn, False, False, 0)
    btn_row.pack_start(action_btn, False, False, 0)

    def load_rels():
        rels = fetch_releases()
        state["releases"] = rels

        def _apply():
            combo.remove_all()
            for idx, r in enumerate(rels):
                combo.append_text(f"{r['tag']} (Mais recente)" if idx == 0 else r["tag"])
            combo.set_active(0)
            combo.set_sensitive(True)
            action_btn.set_sensitive(True)
            return False

        GLib.idle_add(_apply)

    def on_cancel(_b):
        if state["running"]:
            state["cancel"].set()
        else:
            win.close()

    def on_action(_b):
        if state["installed_bin"]:
            launch_custodia(state["installed_bin"])
            win.close()
            return
        if state["running"] or not state["releases"]:
            return
        idx = max(0, combo.get_active())
        rel = state["releases"][min(idx, len(state["releases"]) - 1)]
        fmt = "rpm" if rb_rpm.get_active() else ("deb" if rb_deb.get_active() else "appimage")
        state["running"] = True
        state["cancel"].clear()
        action_btn.set_sensitive(False)
        progress.show()
        cancel_btn.set_label("Cancelar")

        def worker():
            tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-dl-"))
            try:
                assets = rel["assets"]
                if fmt not in assets:
                    raise RuntimeError(f"A versão {rel['tag']} não possui pacote .{fmt}.")
                asset_name, url, _ = assets[fmt]
                sums_url = assets["sums"][1] if "sums" in assets else None
                dest = tmp_dir / asset_name
                download_file(
                    url,
                    dest,
                    lambda dl, tot, frac, mbps: (
                        GLib.idle_add(progress.set_fraction, min(0.8, frac * 0.8)),
                        GLib.idle_add(status_lbl.set_text, f"Baixando {asset_name}... {int(frac * 100)}% ({mbps:.1f} MB/s)"),
                    ),
                    state["cancel"],
                )
                GLib.idle_add(progress.set_fraction, 0.85)
                GLib.idle_add(status_lbl.set_text, "Verificando SHA-256...")
                verify_sha256(dest, asset_name, sums_url)
                GLib.idle_add(progress.set_fraction, 0.92)
                GLib.idle_add(status_lbl.set_text, f"Instalando {asset_name}...")
                bin_path = install_package(dest, fmt, lambda _m: None)
                self_delete_installer(lambda _m: None)
                state["installed_bin"] = bin_path

                def _done():
                    state["running"] = False
                    progress.set_fraction(1.0)
                    status_lbl.set_text(f"Custódia {rel['tag']} instalado com sucesso!")
                    action_btn.set_label("Abrir Custódia")
                    action_btn.set_sensitive(True)
                    cancel_btn.set_label("Fechar")
                    return False

                GLib.idle_add(_done)
            except Exception as ex:
                def _fail():
                    state["running"] = False
                    status_lbl.set_text(f"Falha: {ex}")
                    action_btn.set_sensitive(True)
                    cancel_btn.set_label("Fechar")
                    return False

                GLib.idle_add(_fail)
            finally:
                shutil.rmtree(tmp_dir, ignore_errors=True)

        threading.Thread(target=worker, daemon=True).start()

    cancel_btn.connect("clicked", on_cancel)
    action_btn.connect("clicked", on_action)
    win.show_all()
    threading.Thread(target=load_rels, daemon=True).start()
    Gtk.main()
    return True


def run_cli_fallback() -> None:
    print("Custódia — Instalador Linux")
    rels = fetch_releases()
    for i, r in enumerate(rels[:5], 1):
        print(f"  {i}) {r['tag']}")
    choice = input("Versão [1]: ").strip() or "1"
    rel = rels[max(0, min(len(rels) - 1, int(choice) - 1))] if choice.isdigit() else rels[0]
    default_fmt = detect_default_format()
    fmt = input(f"Formato (deb/rpm/appimage) [{default_fmt}]: ").strip().lower() or default_fmt
    if fmt not in rel["assets"]:
        print(f"Formato .{fmt} indisponível em {rel['tag']}.")
        sys.exit(1)
    tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-dl-"))
    try:
        asset_name, url, _ = rel["assets"][fmt]
        sums_url = rel["assets"]["sums"][1] if "sums" in rel["assets"] else None
        dest = tmp_dir / asset_name
        print(f"Baixando {asset_name}...")
        download_file(url, dest, lambda *_: None, threading.Event())
        verify_sha256(dest, asset_name, sums_url)
        install_package(dest, fmt, print)
        self_delete_installer(print)
        print(f"Custódia {rel['tag']} instalado com sucesso!")
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)


def main() -> None:
    if "--cli" not in sys.argv and (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
        if run_gtk4():
            return
        if run_gtk3():
            return
    run_cli_fallback()


if __name__ == "__main__":
    main()
