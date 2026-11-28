#!/usr/bin/env python3
"""
Lightweight native graphical installer for Custodia by TundraLab.
Lets the user pick a GitHub release version and package format (.deb, .rpm, or
.AppImage) with the recommended format highlighted per distro, verifies SHA-256,
installs cleanly, and auto-deletes itself when finished.
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
import webbrowser
from pathlib import Path
from typing import Callable

REPO = "TundraLab/Custodia"
RELEASES_API = f"https://api.github.com/repos/{REPO}/releases?per_page=12"
RELEASES_URL = f"https://github.com/{REPO}/releases"
ISSUES_URL = f"https://github.com/{REPO}/issues/new"
GUIDE_URL = "https://custodia.tundralab.com.br/#instalacao"
FALLBACK_VERSION = "4.0.0"
USER_AGENT = "Custodia-Installer/4.0"

FORMATS = [
    ("deb", ".deb", "Ubuntu, Debian, Pop!_OS e Linux Mint"),
    ("rpm", ".rpm", "Fedora, RHEL e openSUSE"),
    ("appimage", ".AppImage", "Portátil para qualquer Linux"),
]

STEPS = [
    "Verificando o ambiente",
    "Baixando pacote do Custódia",
    "Verificando integridade (SHA-256)",
    "Instalando no sistema",
    "Finalizando e removendo instalador",
]


def read_os_release() -> dict[str, str]:
    data: dict[str, str] = {}
    for candidate in ("/etc/os-release", "/usr/lib/os-release"):
        if os.path.isfile(candidate):
            try:
                with open(candidate, "r", encoding="utf-8", errors="ignore") as f:
                    for line in f:
                        line = line.strip()
                        if not line or line.startswith("#") or "=" not in line:
                            continue
                        k, v = line.split("=", 1)
                        data[k] = v.strip().strip('"').strip("'")
                break
            except OSError:
                pass
    return data


def detect_distro_label() -> str:
    info = read_os_release()
    return info.get("PRETTY_NAME") or info.get("NAME") or "Linux x86_64"


def detect_default_format() -> str:
    info = read_os_release()
    distro_ids = f"{info.get('ID', '')} {info.get('ID_LIKE', '')}".lower()
    if any(k in distro_ids for k in ("ubuntu", "debian", "pop", "linuxmint", "mint", "elementary", "zorin", "kali")):
        if shutil.which("apt-get") or shutil.which("dpkg"):
            return "deb"
    if any(k in distro_ids for k in ("fedora", "rhel", "centos", "rocky", "almalinux", "opensuse", "suse")):
        if shutil.which("dnf") or shutil.which("zypper") or shutil.which("rpm"):
            return "rpm"
    if shutil.which("apt-get") or shutil.which("dpkg"):
        return "deb"
    if shutil.which("dnf") or shutil.which("zypper") or shutil.which("rpm"):
        return "rpm"
    return "appimage"


def build_fallback_release() -> dict:
    v = FALLBACK_VERSION
    base = f"https://github.com/{REPO}/releases/download/v{v}"
    return {
        "tag": f"v{v}",
        "version": v,
        "name": f"v{v}",
        "assets": {
            "deb": {"name": f"Custodia-{v}_amd64.deb", "url": f"{base}/Custodia-{v}_amd64.deb", "size": 0},
            "rpm": {"name": f"Custodia-{v}-1.x86_64.rpm", "url": f"{base}/Custodia-{v}-1.x86_64.rpm", "size": 0},
            "appimage": {"name": f"Custodia-{v}-x86_64.AppImage", "url": f"{base}/Custodia-{v}-x86_64.AppImage", "size": 0},
            "sums": {"name": "SHA256SUMS.txt", "url": f"{base}/SHA256SUMS.txt", "size": 0},
        },
    }


def fetch_releases() -> list[dict]:
    req = urllib.request.Request(
        RELEASES_API,
        headers={"Accept": "application/vnd.github+json", "User-Agent": USER_AGENT},
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            raw = json.loads(resp.read().decode("utf-8", errors="replace"))
    except Exception:
        return [build_fallback_release()]

    parsed: list[dict] = []
    for rel in raw:
        if not isinstance(rel, dict) or rel.get("draft"):
            continue
        tag = str(rel.get("tag_name") or "").strip()
        if not tag:
            continue
        assets_map: dict[str, dict] = {}
        for asset in rel.get("assets") or []:
            name = str(asset.get("name") or "")
            url = str(asset.get("browser_download_url") or "")
            size = int(asset.get("size") or 0)
            if not name or not url:
                continue
            low = name.lower()
            if low.endswith(".deb"):
                assets_map["deb"] = {"name": name, "url": url, "size": size}
            elif low.endswith(".rpm"):
                assets_map["rpm"] = {"name": name, "url": url, "size": size}
            elif low.endswith(".appimage") and "installer" not in low:
                assets_map["appimage"] = {"name": name, "url": url, "size": size}
            elif name == "SHA256SUMS.txt":
                assets_map["sums"] = {"name": name, "url": url, "size": size}
        if any(k in assets_map for k in ("deb", "rpm", "appimage")):
            parsed.append({
                "tag": tag,
                "version": tag.lstrip("v"),
                "name": tag,
                "assets": assets_map,
            })

    return parsed if parsed else [build_fallback_release()]


def format_bytes(num_bytes: int) -> str:
    if num_bytes <= 0:
        return "0 MB"
    mb = num_bytes / (1024 * 1024)
    if mb >= 1:
        return f"{mb:.1f} MB".replace(".", ",")
    kb = num_bytes / 1024
    return f"{kb:.0f} KB"


def format_eta(seconds: float) -> str:
    if seconds <= 0 or seconds > 3600:
        return ""
    s = int(round(seconds))
    mins, secs = divmod(s, 60)
    return f"~{mins}:{secs:02d} restantes"


def download_file(
    url: str,
    dest: Path,
    on_progress: Callable[[int, int, float, float], None],
    cancel_event: threading.Event,
    expected_size: int = 0,
) -> None:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    start = time.monotonic()
    last_emit = 0.0
    downloaded = 0

    with urllib.request.urlopen(req, timeout=30) as resp:
        header_len = int(resp.headers.get("Content-Length") or 0)
        total = header_len if header_len > 0 else expected_size
        with open(dest, "wb") as out:
            while True:
                if cancel_event.is_set():
                    raise RuntimeError("Instalação cancelada pelo usuário.")
                chunk = resp.read(128 * 1024)
                if not chunk:
                    break
                out.write(chunk)
                downloaded += len(chunk)
                now = time.monotonic()
                if now - last_emit >= 0.12 or (total > 0 and downloaded >= total):
                    elapsed = max(0.05, now - start)
                    speed = downloaded / elapsed
                    eta = (total - downloaded) / speed if (total > downloaded and speed > 0) else 0.0
                    on_progress(downloaded, total, speed, eta)
                    last_emit = now

    elapsed = max(0.05, time.monotonic() - start)
    on_progress(downloaded, max(downloaded, expected_size), downloaded / elapsed, 0.0)


def verify_sha256(file_path: Path, sums_url: str | None, asset_name: str, log: Callable[[str], None]) -> None:
    if not sums_url:
        log("SHA256SUMS.txt não publicado para esta versão; prosseguindo.")
        return

    req = urllib.request.Request(sums_url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            content = resp.read().decode("utf-8", errors="replace")
    except Exception as exc:
        log(f"Aviso: não foi possível baixar SHA256SUMS.txt ({exc}).")
        return

    expected = ""
    for line in content.splitlines():
        parts = line.strip().split()
        if len(parts) >= 2 and parts[-1].lstrip("*") == asset_name:
            expected = parts[0].lower()
            break

    if not expected:
        log(f"Aviso: hash de {asset_name} não encontrado em SHA256SUMS.txt.")
        return

    h = hashlib.sha256()
    with open(file_path, "rb") as f:
        while True:
            buf = f.read(1024 * 1024)
            if not buf:
                break
            h.update(buf)
    actual = h.hexdigest().lower()
    if actual != expected:
        raise RuntimeError("Falha na verificação SHA-256. O arquivo baixado está corrompido.")
    log(f"Integridade SHA-256 verificada ({actual[:12]}...).")


def run_privileged(cmd: list[str], log: Callable[[str], None]) -> None:
    if os.geteuid() == 0:
        full_cmd = cmd
    elif shutil.which("pkexec"):
        full_cmd = ["pkexec"] + cmd
    else:
        raise RuntimeError("pkexec não encontrado. Instale o pacote manualmente pelo terminal.")

    log(f"Executando: {' '.join(full_cmd)}")
    proc = subprocess.Popen(
        full_cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        errors="replace",
    )
    assert proc.stdout is not None
    tail_lines: list[str] = []
    for line in proc.stdout:
        clean = line.strip()
        if clean:
            tail_lines.append(clean)
            if len(tail_lines) > 12:
                tail_lines.pop(0)
            log(clean)
    rc = proc.wait()
    if rc != 0:
        if rc in (126, 127):
            raise RuntimeError("Autenticação administrativa cancelada.")
        detail = tail_lines[-1] if tail_lines else f"código {rc}"
        raise RuntimeError(f"Falha ao instalar o pacote ({detail}).")


def install_deb(pkg_path: Path, log: Callable[[str], None]) -> None:
    if shutil.which("apt-get"):
        run_privileged(["apt-get", "install", "-y", str(pkg_path)], log)
    elif shutil.which("dpkg"):
        run_privileged(["dpkg", "-i", str(pkg_path)], log)
    else:
        raise RuntimeError("Nem apt-get nem dpkg estão disponíveis neste sistema.")


def install_rpm(pkg_path: Path, log: Callable[[str], None]) -> None:
    if shutil.which("dnf"):
        run_privileged(["dnf", "install", "-y", "--nogpgcheck", str(pkg_path)], log)
    elif shutil.which("zypper"):
        run_privileged(["zypper", "--non-interactive", "--no-gpg-checks", "install", str(pkg_path)], log)
    elif shutil.which("rpm"):
        run_privileged(["rpm", "-Uvh", "--replacepkgs", "--nosignature", str(pkg_path)], log)
    else:
        raise RuntimeError("Nem dnf, zypper nem rpm estão disponíveis neste sistema.")


def install_appimage(pkg_path: Path, log: Callable[[str], None]) -> None:
    home = Path.home()
    install_dir = home / ".local" / "opt" / "custodia"
    bin_dir = home / ".local" / "bin"
    apps_dir = home / ".local" / "share" / "applications"
    autostart_dir = home / ".config" / "autostart"

    for d in (install_dir, bin_dir, apps_dir, autostart_dir):
        d.mkdir(parents=True, exist_ok=True)

    target_app = install_dir / "Custodia.AppImage"
    shutil.copy2(pkg_path, target_app)
    target_app.chmod(target_app.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    wrapper = bin_dir / "custodia"
    wrapper.write_text(
        f'#!/usr/bin/env bash\nexec "{target_app}" "$@"\n',
        encoding="utf-8",
    )
    wrapper.chmod(wrapper.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    desktop_content = (
        "[Desktop Entry]\n"
        "Type=Application\n"
        "Name=Custódia\n"
        "Comment=Google Drive nativo para Linux\n"
        f"Exec={wrapper}\n"
        "Icon=custodia\n"
        "Terminal=false\n"
        "Categories=Network;FileTransfer;Utility;\n"
        "StartupWMClass=Custodia\n"
    )
    (apps_dir / "custodia.desktop").write_text(desktop_content, encoding="utf-8")
    (autostart_dir / "custodia.desktop").write_text(
        desktop_content + "X-GNOME-Autostart-enabled=true\n",
        encoding="utf-8",
    )
    if shutil.which("update-desktop-database"):
        subprocess.run(["update-desktop-database", str(apps_dir)], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    log(f"AppImage instalado em {target_app}")


def self_delete_installer(log: Callable[[str], None]) -> bool:
    try:
        appimage_env = os.environ.get("APPIMAGE")
        if appimage_env:
            target_path = Path(os.path.realpath(appimage_env))
        else:
            target_path = Path(os.path.realpath(__file__))
        if not target_path.exists():
            return False
        # Never delete the source file inside the repository during development.
        if not appimage_env and (
            target_path.name.endswith(".py")
            or (target_path.parent.name == "linux" and target_path.parent.parent.name == "packaging")
        ):
            log("Modo desenvolvimento: arquivo fonte preservado.")
            return False
        target_path.unlink(missing_ok=True)
        log(f"Instalador removido: {target_path.name}")
        return True
    except Exception as exc:
        log(f"Aviso: não foi possível remover o instalador ({exc}).")
        return False


def launch_custodia() -> None:
    candidates = [
        "/opt/custodia/Custodia",
        "/usr/bin/custodia",
        str(Path.home() / ".local" / "bin" / "custodia"),
        str(Path.home() / ".local" / "opt" / "custodia" / "Custodia.AppImage"),
    ]
    for path in candidates:
        if os.path.isfile(path) and os.access(path, os.X_OK):
            subprocess.Popen(
                [path],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True,
            )
            return
    if shutil.which("custodia"):
        subprocess.Popen(
            ["custodia"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=True,
        )


def stop_running_custodia() -> None:
    subprocess.run(["pkill", "-x", "Custodia"], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


CSS = """
window, .installer-root {
    background-color: #0E1117;
    color: #E6EDF3;
    font-family: Inter, "Cantarell", "Noto Sans", system-ui, sans-serif;
}

headerbar.installer-topbar {
    background-color: #0E1117;
    border-bottom: 1px solid #1C212B;
    box-shadow: none;
    padding: 4px 12px;
    min-height: 44px;
}

.topbar-brand {
    font-weight: 700;
    font-size: 13.5px;
    color: #E6EDF3;
}

.topbar-version {
    font-family: "JetBrains Mono", monospace;
    font-size: 12px;
    color: #7D8590;
    margin-left: 6px;
}

button.topbar-link {
    background: transparent;
    border: none;
    box-shadow: none;
    color: #8B949E;
    font-size: 12px;
    padding: 2px 8px;
    margin-left: 6px;
}

button.topbar-link:hover {
    color: #A8C7FA;
}

.hero-icon-blue {
    background-color: #16243A;
    border: 1px solid #243A5E;
    border-radius: 12px;
    min-width: 44px;
    min-height: 44px;
    color: #60A5FA;
    font-size: 20px;
    font-weight: 700;
}

.hero-icon-green {
    background-color: #132C20;
    border: 1px solid #1F4E38;
    border-radius: 12px;
    min-width: 44px;
    min-height: 44px;
    color: #6DD58C;
    font-size: 20px;
    font-weight: 700;
}

.hero-title {
    font-size: 22px;
    font-weight: 700;
    color: #F0F6FC;
}

.hero-subtitle {
    font-size: 13.5px;
    color: #8B949E;
}

.source-mono {
    font-family: "JetBrains Mono", monospace;
    font-size: 11.5px;
    color: #6E7681;
    margin-top: 2px;
}

.installer-card {
    background-color: #161920;
    border: 1px solid #242934;
    border-radius: 14px;
    padding: 18px 20px;
}

.field-label {
    font-size: 12.5px;
    font-weight: 600;
    color: #9AA4B2;
}

button.fmt-row {
    background-color: #1C2029;
    border: 1px solid #282D3A;
    border-radius: 11px;
    padding: 11px 14px;
    box-shadow: none;
}

button.fmt-row:hover {
    background-color: #212633;
    border-color: #363D4E;
}

button.fmt-row.selected {
    background-color: #18253A;
    border-color: #4D8BF5;
}

.fmt-title {
    font-family: "JetBrains Mono", monospace;
    font-size: 13.5px;
    font-weight: 700;
    color: #F0F6FC;
}

.fmt-sub {
    font-size: 12px;
    color: #8B949E;
}

.badge-rec {
    background-color: #1E3A5F;
    color: #93C5FD;
    border-radius: 999px;
    padding: 2px 9px;
    font-size: 11px;
    font-weight: 600;
}

.fmt-dot {
    min-width: 18px;
    min-height: 18px;
    border-radius: 999px;
    border: 1.5px solid #4B5563;
    color: transparent;
    font-size: 11px;
    font-weight: 700;
}

.fmt-dot.selected {
    background-color: #3B82F6;
    border-color: #3B82F6;
    color: #FFFFFF;
}

.distro-hint {
    font-size: 12px;
    color: #7D8590;
}

button.btn-primary {
    background-color: #3B82F6;
    color: #FFFFFF;
    border: none;
    border-radius: 10px;
    padding: 10px 20px;
    font-weight: 600;
    font-size: 13.5px;
    box-shadow: none;
}

button.btn-primary:hover {
    background-color: #2563EB;
}

button.btn-secondary {
    background-color: #212631;
    color: #E6EDF3;
    border: 1px solid #303644;
    border-radius: 10px;
    padding: 8px 16px;
    font-weight: 600;
    font-size: 13px;
    box-shadow: none;
}

button.btn-secondary:hover {
    background-color: #2A303E;
}

button.btn-ghost {
    background: transparent;
    border: none;
    box-shadow: none;
    color: #9AA4B2;
    font-size: 12.5px;
    padding: 4px 8px;
}

button.btn-ghost:hover {
    color: #F0F6FC;
}

progressbar.slim-progress trough {
    background-color: #232834;
    border-radius: 999px;
    min-height: 5px;
}

progressbar.slim-progress progress {
    background-color: #3B82F6;
    border-radius: 999px;
    min-height: 5px;
}

.stats-mono {
    font-family: "JetBrains Mono", monospace;
    font-size: 11.5px;
    color: #8B949E;
}

.stats-pct {
    font-family: "JetBrains Mono", monospace;
    font-size: 12px;
    font-weight: 700;
    color: #F0F6FC;
}

.step-row {
    padding: 8px 12px;
    border-radius: 10px;
}

.step-row.active {
    background-color: #1F2430;
    border: 1px solid #2A3040;
}

.step-icon-done {
    background-color: #1A2B46;
    color: #60A5FA;
    border-radius: 999px;
    min-width: 22px;
    min-height: 22px;
    font-size: 12px;
    font-weight: 700;
}

.step-icon-pending {
    color: #4B5563;
    min-width: 22px;
    min-height: 22px;
    font-size: 14px;
}

.step-title-done {
    font-size: 13px;
    color: #8B949E;
}

.step-title-active {
    font-size: 13.5px;
    font-weight: 600;
    color: #F0F6FC;
}

.step-title-pending {
    font-size: 13px;
    color: #5D6674;
}

.step-sub {
    font-family: "JetBrains Mono", monospace;
    font-size: 11.5px;
    color: #8B949E;
}

.log-box {
    background-color: #0D1016;
    border: 1px solid #222732;
    border-radius: 10px;
    padding: 8px 10px;
    font-family: "JetBrains Mono", monospace;
    font-size: 11px;
    color: #9AA4B2;
}

.error-box {
    background-color: #31171B;
    border: 1px solid #5B242B;
    border-radius: 10px;
    padding: 10px 14px;
    color: #FFB4AB;
    font-size: 12.5px;
}

button.footer-link {
    background: transparent;
    border: none;
    box-shadow: none;
    color: #6E7681;
    font-size: 12px;
    padding: 2px 6px;
}

button.footer-link:hover {
    color: #A8C7FA;
}

dropdown button {
    background-color: #1C2029;
    border: 1px solid #2C3240;
    border-radius: 10px;
    color: #F0F6FC;
    padding: 6px 12px;
}
"""


def run_gtk4() -> bool:
    try:
        import gi
        gi.require_version("Gdk", "4.0")
        gi.require_version("Gtk", "4.0")
        from gi.repository import Gdk, GLib, Gtk  # type: ignore
    except Exception:
        return False

    class CustodiaInstallerApp(Gtk.Application):
        def __init__(self) -> None:
            super().__init__(application_id="com.tundralab.CustodiaInstaller")
            self.releases: list[dict] = [build_fallback_release()]
            self.recommended_fmt = detect_default_format()
            self.selected_fmt = self.recommended_fmt
            self.distro_label = detect_distro_label()
            self.cancel_event = threading.Event()
            self.install_started_at = 0.0
            self.timer_source_id: int | None = None
            self.installing = False
            self.self_deleted = False

            self.fmt_buttons: dict[str, Gtk.Button] = {}
            self.fmt_dots: dict[str, Gtk.Label] = {}
            self.step_widgets: list[dict] = []

        def do_activate(self) -> None:
            provider = Gtk.CssProvider()
            provider.load_from_data(CSS.encode("utf-8"))
            Gtk.StyleContext.add_provider_for_display(
                Gdk.Display.get_default(),
                provider,
                Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION,
            )

            self.win = Gtk.ApplicationWindow(application=self)
            self.win.set_title("Custódia")
            self.win.set_default_size(680, 560)
            self.win.set_resizable(True)
            self.win.add_css_class("installer-root")

            # Clean dark top bar matching the reference screenshot
            header = Gtk.HeaderBar()
            header.add_css_class("installer-topbar")
            header.set_show_title_buttons(True)
            header.set_title_widget(Gtk.Box())

            brand_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=4)
            brand_box.set_valign(Gtk.Align.CENTER)
            brand_lbl = Gtk.Label(label="Custódia")
            brand_lbl.add_css_class("topbar-brand")
            self.topbar_ver = Gtk.Label(label=f"v{FALLBACK_VERSION}")
            self.topbar_ver.add_css_class("topbar-version")
            whats_new = Gtk.Button(label="O que há de novo")
            whats_new.add_css_class("topbar-link")
            whats_new.connect("clicked", lambda *_: webbrowser.open(RELEASES_URL))
            brand_box.append(brand_lbl)
            brand_box.append(self.topbar_ver)
            brand_box.append(whats_new)
            header.pack_start(brand_box)
            self.win.set_titlebar(header)

            self.stack = Gtk.Stack()
            self.stack.set_transition_type(Gtk.StackTransitionType.CROSSFADE)
            self.stack.set_transition_duration(180)
            self.stack.set_vexpand(True)
            self.stack.set_hexpand(True)

            self.stack.add_named(self._build_select_page(), "select")
            self.stack.add_named(self._build_install_page(), "install")
            self.stack.add_named(self._build_done_page(), "done")
            self.stack.set_visible_child_name("select")

            self.win.set_child(self.stack)
            self.win.present()

            threading.Thread(target=self._load_releases_bg, daemon=True).start()

        def _centered_shell(self) -> tuple[Gtk.Box, Gtk.Box]:
            outer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
            outer.set_halign(Gtk.Align.CENTER)
            outer.set_valign(Gtk.Align.CENTER)
            outer.set_hexpand(True)
            outer.set_vexpand(True)
            outer.set_margin_top(24)
            outer.set_margin_bottom(24)
            outer.set_margin_start(28)
            outer.set_margin_end(28)

            col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            col.set_size_request(520, -1)
            outer.append(col)
            return outer, col

        def _build_footer_links(self) -> Gtk.Box:
            row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            row.set_margin_top(4)
            for label, url in (
                ("Guia de instalação", GUIDE_URL),
                ("Relatar um problema", ISSUES_URL),
                ("GitHub", RELEASES_URL),
            ):
                btn = Gtk.Button(label=label)
                btn.add_css_class("footer-link")
                btn.connect("clicked", lambda _b, u=url: webbrowser.open(u))
                row.append(btn)
            return row

        def _build_select_page(self) -> Gtk.Widget:
            outer, col = self._centered_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
            icon_box = Gtk.Label(label="↓")
            icon_box.add_css_class("hero-icon-blue")
            icon_box.set_valign(Gtk.Align.START)

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=3)
            title = Gtk.Label(label="Instalar o Custódia", xalign=0)
            title.add_css_class("hero-title")
            sub = Gtk.Label(
                label="Escolha a versão e o formato para o seu Linux. Ao terminar, este instalador se apaga sozinho.",
                xalign=0,
            )
            sub.set_wrap(True)
            sub.add_css_class("hero-subtitle")
            hero_text.append(title)
            hero_text.append(sub)
            hero.append(icon_box)
            hero.append(hero_text)
            col.append(hero)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            card.add_css_class("installer-card")

            # Release version dropdown
            ver_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            ver_lbl = Gtk.Label(label="Versão", xalign=0)
            ver_lbl.add_css_class("field-label")
            ver_lbl.set_hexpand(True)
            self.ver_model = Gtk.StringList.new([f"v{FALLBACK_VERSION} (Mais recente)"])
            self.ver_dropdown = Gtk.DropDown(model=self.ver_model)
            self.ver_dropdown.set_selected(0)
            self.ver_dropdown.connect("notify::selected", self._on_release_changed)
            ver_row.append(ver_lbl)
            ver_row.append(self.ver_dropdown)
            card.append(ver_row)

            # Format selector with per-distro Recommended badge
            fmt_lbl = Gtk.Label(label="Formato de instalação", xalign=0)
            fmt_lbl.add_css_class("field-label")
            card.append(fmt_lbl)

            fmt_list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            for key, name, desc in FORMATS:
                btn = Gtk.Button()
                btn.add_css_class("fmt-row")
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                row.set_valign(Gtk.Align.CENTER)

                dot = Gtk.Label(label="✓")
                dot.add_css_class("fmt-dot")
                dot.set_valign(Gtk.Align.CENTER)

                text_col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                text_col.set_hexpand(True)
                name_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
                t_lbl = Gtk.Label(label=name, xalign=0)
                t_lbl.add_css_class("fmt-title")
                name_row.append(t_lbl)
                if key == self.recommended_fmt:
                    rec = Gtk.Label(label="Recomendado")
                    rec.add_css_class("badge-rec")
                    name_row.append(rec)
                d_lbl = Gtk.Label(label=desc, xalign=0)
                d_lbl.add_css_class("fmt-sub")
                text_col.append(name_row)
                text_col.append(d_lbl)

                row.append(dot)
                row.append(text_col)
                btn.set_child(row)
                btn.connect("clicked", lambda _b, k=key: self._select_format(k))
                fmt_list.append(btn)
                self.fmt_buttons[key] = btn
                self.fmt_dots[key] = dot

            card.append(fmt_list)

            # Bottom row: distro detection + Install CTA
            action_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            action_row.set_margin_top(4)
            distro_lbl = Gtk.Label(label=f"Sistema: {self.distro_label}", xalign=0)
            distro_lbl.add_css_class("distro-hint")
            distro_lbl.set_hexpand(True)
            distro_lbl.set_ellipsize(3)  # PANGO_ELLIPSIZE_END

            self.install_btn = Gtk.Button(label="Instalar agora")
            self.install_btn.add_css_class("btn-primary")
            self.install_btn.connect("clicked", self._on_start_install)

            action_row.append(distro_lbl)
            action_row.append(self.install_btn)
            card.append(action_row)

            col.append(card)
            col.append(self._build_footer_links())
            self._select_format(self.selected_fmt)
            return outer

        def _build_install_page(self) -> Gtk.Widget:
            outer, col = self._centered_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
            icon_box = Gtk.Label(label="↓")
            icon_box.add_css_class("hero-icon-blue")
            icon_box.set_valign(Gtk.Align.START)

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=3)
            self.inst_title = Gtk.Label(label="Instalando...", xalign=0)
            self.inst_title.add_css_class("hero-title")
            self.inst_subtitle = Gtk.Label(
                label="Baixando e preparando o Custódia no seu computador.",
                xalign=0,
            )
            self.inst_subtitle.set_wrap(True)
            self.inst_subtitle.add_css_class("hero-subtitle")
            hero_text.append(self.inst_title)
            hero_text.append(self.inst_subtitle)
            hero.append(icon_box)
            hero.append(hero_text)
            col.append(hero)

            source_lbl = Gtk.Label(label="De github.com/TundraLab/Custodia", xalign=0)
            source_lbl.add_css_class("source-mono")
            col.append(source_lbl)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
            card.add_css_class("installer-card")

            self.progress_bar = Gtk.ProgressBar()
            self.progress_bar.add_css_class("slim-progress")
            self.progress_bar.set_fraction(0.0)
            card.append(self.progress_bar)

            stats_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            self.stats_pct = Gtk.Label(label="0%", xalign=0)
            self.stats_pct.add_css_class("stats-pct")
            self.stats_bytes = Gtk.Label(label="", xalign=0)
            self.stats_bytes.add_css_class("stats-mono")
            self.stats_bytes.set_hexpand(True)
            self.stats_speed = Gtk.Label(label="", xalign=1)
            self.stats_speed.add_css_class("stats-mono")
            stats_row.append(self.stats_pct)
            stats_row.append(self.stats_bytes)
            stats_row.append(self.stats_speed)
            card.append(stats_row)

            # 5-step checklist
            steps_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            self.step_widgets = []
            for label_text in STEPS:
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
                row.add_css_class("step-row")

                icon_stack = Gtk.Stack()
                icon_stack.set_valign(Gtk.Align.CENTER)
                dot_lbl = Gtk.Label(label="•")
                dot_lbl.add_css_class("step-icon-pending")
                spinner = Gtk.Spinner()
                spinner.set_size_request(18, 18)
                done_lbl = Gtk.Label(label="✓")
                done_lbl.add_css_class("step-icon-done")
                icon_stack.add_named(dot_lbl, "pending")
                icon_stack.add_named(spinner, "active")
                icon_stack.add_named(done_lbl, "done")
                icon_stack.set_visible_child_name("pending")

                text_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                text_box.set_hexpand(True)
                t_lbl = Gtk.Label(label=label_text, xalign=0)
                t_lbl.add_css_class("step-title-pending")
                sub_lbl = Gtk.Label(label="", xalign=0)
                sub_lbl.add_css_class("step-sub")
                sub_lbl.set_visible(False)
                text_box.append(t_lbl)
                text_box.append(sub_lbl)

                row.append(icon_stack)
                row.append(text_box)
                steps_box.append(row)
                self.step_widgets.append({
                    "row": row,
                    "icon_stack": icon_stack,
                    "spinner": spinner,
                    "title": t_lbl,
                    "sub": sub_lbl,
                })
            card.append(steps_box)

            # Error banner (hidden unless error)
            self.error_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            self.error_box.add_css_class("error-box")
            self.error_box.set_visible(False)
            self.error_lbl = Gtk.Label(label="", xalign=0)
            self.error_lbl.set_wrap(True)
            err_actions = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            back_btn = Gtk.Button(label="Voltar")
            back_btn.add_css_class("btn-secondary")
            back_btn.connect("clicked", lambda *_: self.stack.set_visible_child_name("select"))
            retry_btn = Gtk.Button(label="Tentar novamente")
            retry_btn.add_css_class("btn-primary")
            retry_btn.connect("clicked", self._on_start_install)
            err_actions.append(back_btn)
            err_actions.append(retry_btn)
            self.error_box.append(self.error_lbl)
            self.error_box.append(err_actions)
            card.append(self.error_box)

            # Collapsible log details
            self.log_revealer = Gtk.Revealer()
            self.log_revealer.set_reveal_child(False)
            log_scroll = Gtk.ScrolledWindow()
            log_scroll.set_min_content_height(96)
            log_scroll.set_max_content_height(130)
            self.log_view = Gtk.TextView()
            self.log_view.set_editable(False)
            self.log_view.set_cursor_visible(False)
            self.log_view.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
            self.log_view.add_css_class("log-box")
            self.log_buffer = self.log_view.get_buffer()
            log_scroll.set_child(self.log_view)
            self.log_revealer.set_child(log_scroll)
            card.append(self.log_revealer)

            # Card footer: Details toggle | Elapsed + Cancel
            footer = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            footer.set_margin_top(4)
            self.details_btn = Gtk.Button(label="⌄ Detalhes")
            self.details_btn.add_css_class("btn-ghost")
            self.details_btn.connect("clicked", self._toggle_details)

            spacer = Gtk.Box()
            spacer.set_hexpand(True)

            self.elapsed_lbl = Gtk.Label(label="0 s decorridos")
            self.elapsed_lbl.add_css_class("stats-mono")

            self.cancel_btn = Gtk.Button(label="Cancelar")
            self.cancel_btn.add_css_class("btn-ghost")
            self.cancel_btn.connect("clicked", self._on_cancel)

            footer.append(self.details_btn)
            footer.append(spacer)
            footer.append(self.elapsed_lbl)
            footer.append(self.cancel_btn)
            card.append(footer)

            col.append(card)
            col.append(self._build_footer_links())
            return outer

        def _build_done_page(self) -> Gtk.Widget:
            outer, col = self._centered_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
            icon_box = Gtk.Label(label="✓")
            icon_box.add_css_class("hero-icon-green")
            icon_box.set_valign(Gtk.Align.START)

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=3)
            title = Gtk.Label(label="Custódia instalado!", xalign=0)
            title.add_css_class("hero-title")
            self.done_sub = Gtk.Label(
                label="Tudo pronto. O arquivo do instalador foi removido automaticamente.",
                xalign=0,
            )
            self.done_sub.set_wrap(True)
            self.done_sub.add_css_class("hero-subtitle")
            hero_text.append(title)
            hero_text.append(self.done_sub)
            hero.append(icon_box)
            hero.append(hero_text)
            col.append(hero)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            card.add_css_class("installer-card")
            self.done_summary = Gtk.Label(label="", xalign=0)
            self.done_summary.set_wrap(True)
            self.done_summary.add_css_class("fmt-sub")
            card.append(self.done_summary)

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            open_btn = Gtk.Button(label="Abrir Custódia")
            open_btn.add_css_class("btn-primary")
            open_btn.connect("clicked", self._on_open_and_close)
            close_btn = Gtk.Button(label="Fechar")
            close_btn.add_css_class("btn-secondary")
            close_btn.connect("clicked", lambda *_: self.quit())
            btn_row.append(open_btn)
            btn_row.append(close_btn)
            card.append(btn_row)

            col.append(card)
            return outer

        def _toggle_details(self, *_args) -> None:
            shown = not self.log_revealer.get_reveal_child()
            self.log_revealer.set_reveal_child(shown)
            self.details_btn.set_label("⌃ Detalhes" if shown else "⌄ Detalhes")

        def _select_format(self, fmt_key: str) -> None:
            self.selected_fmt = fmt_key
            for k, btn in self.fmt_buttons.items():
                dot = self.fmt_dots[k]
                if k == fmt_key:
                    btn.add_css_class("selected")
                    dot.add_css_class("selected")
                else:
                    btn.remove_css_class("selected")
                    dot.remove_css_class("selected")

        def _load_releases_bg(self) -> None:
            rels = fetch_releases()
            GLib.idle_add(self._apply_releases, rels)

        def _apply_releases(self, rels: list[dict]) -> bool:
            self.releases = rels
            labels = []
            for idx, rel in enumerate(rels):
                suffix = " (Mais recente)" if idx == 0 else ""
                labels.append(f"{rel['tag']}{suffix}")
            self.ver_model = Gtk.StringList.new(labels)
            self.ver_dropdown.set_model(self.ver_model)
            self.ver_dropdown.set_selected(0)
            self._on_release_changed()
            return False

        def _selected_release(self) -> dict:
            idx = int(self.ver_dropdown.get_selected())
            if 0 <= idx < len(self.releases):
                return self.releases[idx]
            return self.releases[0]

        def _on_release_changed(self, *_args) -> None:
            rel = self._selected_release()
            self.topbar_ver.set_label(rel["tag"])
            assets = rel.get("assets") or {}
            for k, btn in self.fmt_buttons.items():
                available = k in assets
                btn.set_sensitive(available)
            if self.selected_fmt not in assets:
                for k in (self.recommended_fmt, "deb", "rpm", "appimage"):
                    if k in assets:
                        self._select_format(k)
                        break

        def _append_log(self, msg: str) -> None:
            def _ui() -> bool:
                end = self.log_buffer.get_end_iter()
                self.log_buffer.insert(end, msg + "\n")
                return False
            GLib.idle_add(_ui)

        def _set_step(self, active_idx: int, sub_text: str = "") -> None:
            def _ui() -> bool:
                for i, w in enumerate(self.step_widgets):
                    row = w["row"]
                    icon_stack = w["icon_stack"]
                    spinner = w["spinner"]
                    title = w["title"]
                    sub = w["sub"]

                    row.remove_css_class("active")
                    title.remove_css_class("step-title-done")
                    title.remove_css_class("step-title-active")
                    title.remove_css_class("step-title-pending")

                    if i < active_idx:
                        icon_stack.set_visible_child_name("done")
                        spinner.stop()
                        title.add_css_class("step-title-done")
                        sub.set_visible(False)
                    elif i == active_idx:
                        row.add_css_class("active")
                        icon_stack.set_visible_child_name("active")
                        spinner.start()
                        title.add_css_class("step-title-active")
                        if sub_text:
                            sub.set_label(sub_text)
                            sub.set_visible(True)
                        else:
                            sub.set_visible(False)
                    else:
                        icon_stack.set_visible_child_name("pending")
                        spinner.stop()
                        title.add_css_class("step-title-pending")
                        sub.set_visible(False)
                return False
            GLib.idle_add(_ui)

        def _set_progress(self, fraction: float, bytes_text: str = "", speed_text: str = "") -> None:
            def _ui() -> bool:
                clamped = max(0.0, min(1.0, fraction))
                self.progress_bar.set_fraction(clamped)
                self.stats_pct.set_label(f"{int(round(clamped * 100))}%")
                if bytes_text:
                    self.stats_bytes.set_label(f"•  {bytes_text}")
                if speed_text is not None:
                    self.stats_speed.set_label(speed_text)
                return False
            GLib.idle_add(_ui)

        def _tick_elapsed(self) -> bool:
            if not self.installing:
                return False
            secs = int(time.monotonic() - self.install_started_at)
            self.elapsed_lbl.set_label(f"{secs} s decorridos")
            return True

        def _on_cancel(self, *_args) -> None:
            if self.installing:
                self.cancel_event.set()
                self.cancel_btn.set_sensitive(False)
            else:
                self.stack.set_visible_child_name("select")

        def _on_start_install(self, *_args) -> None:
            rel = self._selected_release()
            fmt = self.selected_fmt
            asset = (rel.get("assets") or {}).get(fmt)
            if not asset:
                return

            self.cancel_event.clear()
            self.installing = True
            self.install_started_at = time.monotonic()
            self.error_box.set_visible(False)
            self.cancel_btn.set_sensitive(True)
            self.log_buffer.set_text("")
            self.stats_pct.set_label("0%")
            self.stats_bytes.set_label("")
            self.stats_speed.set_label("")
            self.progress_bar.set_fraction(0.0)
            self.elapsed_lbl.set_label("0 s decorridos")
            self.step_widgets[1]["title"].set_label(f"Baixando Custódia {rel['tag']} (.{fmt if fmt != 'appimage' else 'AppImage'})")

            self.stack.set_visible_child_name("install")
            self.timer_source_id = GLib.timeout_add(1000, self._tick_elapsed)

            threading.Thread(
                target=self._install_worker,
                args=(rel, fmt, asset),
                daemon=True,
            ).start()

        def _install_worker(self, rel: dict, fmt: str, asset: dict) -> None:
            tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-install-"))
            try:
                # Step 0: environment check
                self._set_step(0, self.distro_label)
                self._set_progress(0.05)
                self._append_log(f"Sistema detectado: {self.distro_label}")
                stop_running_custodia()

                # Step 1: download
                pkg_path = tmp_dir / asset["name"]
                self._set_step(1, asset["name"])
                self._append_log(f"Baixando {asset['url']}")

                def on_dl(done: int, total: int, speed: float, eta: float) -> None:
                    frac = 0.08 + 0.62 * (done / total) if total > 0 else 0.35
                    b_txt = f"{format_bytes(done)} / {format_bytes(total)}" if total > 0 else format_bytes(done)
                    spd = f"{speed / (1024 * 1024):.1f} MB/s".replace(".", ",")
                    eta_s = format_eta(eta)
                    s_txt = f"{spd}  •  {eta_s}" if eta_s else spd
                    self._set_progress(frac, b_txt, s_txt)

                download_file(asset["url"], pkg_path, on_dl, self.cancel_event, int(asset.get("size") or 0))

                # Step 2: SHA-256
                self._set_step(2, "Conferindo SHA256SUMS.txt")
                self._set_progress(0.75, speed_text="Validando...")
                sums_asset = (rel.get("assets") or {}).get("sums")
                verify_sha256(pkg_path, sums_asset["url"] if sums_asset else None, asset["name"], self._append_log)

                # Step 3: install
                self._set_step(3, "Aplicando pacote no sistema")
                self._set_progress(0.86, speed_text="Instalando...")
                if fmt == "deb":
                    install_deb(pkg_path, self._append_log)
                elif fmt == "rpm":
                    install_rpm(pkg_path, self._append_log)
                else:
                    install_appimage(pkg_path, self._append_log)

                # Step 4: finalize + auto-delete
                self._set_step(4, "Removendo arquivo instalador")
                self._set_progress(0.96, speed_text="Finalizando...")
                deleted = self_delete_installer(self._append_log)
                self.self_deleted = deleted

                self._set_step(5)
                self._set_progress(1.0, speed_text="Concluído")
                self.installing = False
                GLib.idle_add(self._show_done, rel["tag"], fmt, deleted)
            except Exception as exc:
                self.installing = False
                self._append_log(f"ERRO: {exc}")
                GLib.idle_add(self._show_error, str(exc))
            finally:
                shutil.rmtree(tmp_dir, ignore_errors=True)

        def _show_done(self, tag: str, fmt: str, deleted: bool) -> bool:
            fmt_name = ".AppImage" if fmt == "appimage" else f".{fmt}"
            if deleted:
                self.done_sub.set_label("Tudo pronto. O arquivo do instalador foi removido automaticamente.")
            else:
                self.done_sub.set_label("Tudo pronto. O Custódia já está disponível no menu de aplicativos.")
            self.done_summary.set_label(f"Versão instalada: Custódia {tag} ({fmt_name}) no {self.distro_label}.")
            self.stack.set_visible_child_name("done")
            return False

        def _show_error(self, message: str) -> bool:
            self.error_lbl.set_label(message)
            self.error_box.set_visible(True)
            self.cancel_btn.set_sensitive(True)
            for w in self.step_widgets:
                w["spinner"].stop()
            return False

        def _on_open_and_close(self, *_args) -> None:
            launch_custodia()
            self.quit()

    app = CustodiaInstallerApp()
    app.run([])
    return True


def run_cli_fallback() -> None:
    print("== Instalador do Custódia ==")
    rels = fetch_releases()
    rec_fmt = detect_default_format()
    print(f"Sistema detectado: {detect_distro_label()} (recomendado: .{rec_fmt})\n")

    for i, r in enumerate(rels[:5], 1):
        print(f"  {i}) {r['tag']}")
    choice = input(f"Versão [1]: ").strip()
    rel = rels[int(choice) - 1] if choice.isdigit() and 1 <= int(choice) <= len(rels[:5]) else rels[0]

    avail = [k for k, _, _ in FORMATS if k in rel["assets"]]
    default_fmt = rec_fmt if rec_fmt in avail else avail[0]
    fmt = input(f"Formato ({'/'.join(avail)}) [{default_fmt}]: ").strip().lower() or default_fmt
    if fmt not in rel["assets"]:
        fmt = default_fmt

    asset = rel["assets"][fmt]
    tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-install-"))
    try:
        pkg = tmp_dir / asset["name"]
        print(f"Baixando {asset['name']}...")
        download_file(asset["url"], pkg, lambda *_: None, threading.Event(), int(asset.get("size") or 0))
        sums = rel["assets"].get("sums")
        verify_sha256(pkg, sums["url"] if sums else None, asset["name"], print)
        stop_running_custodia()
        if fmt == "deb":
            install_deb(pkg, print)
        elif fmt == "rpm":
            install_rpm(pkg, print)
        else:
            install_appimage(pkg, print)
        self_delete_installer(print)
        print("Custódia instalado com sucesso!")
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)


def main() -> int:
    if os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY"):
        if run_gtk4():
            return 0
    run_cli_fallback()
    return 0


if __name__ == "__main__":
    sys.exit(main())
