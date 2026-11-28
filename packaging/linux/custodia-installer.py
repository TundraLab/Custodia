#!/usr/bin/env python3
"""
Lightweight native graphical installer for Custodia by TundraLab.
Lets users choose a GitHub release version and package format (.deb, .rpm, or
.AppImage) with the distro's recommended option highlighted, verifies SHA-256,
installs packages cleanly, and auto-deletes itself upon completion.
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
import urllib.request
import webbrowser
from pathlib import Path
from typing import Callable

REPO = "TundraLab/Custodia"
RELEASES_API = f"https://api.github.com/repos/{REPO}/releases?per_page=12"
RELEASES_URL = f"https://github.com/{REPO}/releases"
ISSUES_URL = f"https://github.com/{REPO}/issues/new"
TROUBLESHOOTING_URL = f"https://github.com/{REPO}/blob/main/docs/TROUBLESHOOTING.md"
DISCUSSIONS_URL = f"https://github.com/{REPO}/discussions"
GUIDE_URL = "https://custodia.tundralab.com.br/#instalacao"
FALLBACK_VERSION = "4.0.0"
USER_AGENT = "Custodia-Installer/4.0"

FORMATS = [
    ("deb", ".deb", "Ubuntu, Debian, Pop!_OS e Linux Mint"),
    ("rpm", ".rpm", "Fedora, RHEL e openSUSE"),
    ("appimage", ".AppImage", "Portátil para qualquer distribuição Linux"),
]

STEPS = [
    "Verificando o ambiente",
    "Baixando pacote do Custódia",
    "Verificando integridade (SHA-256)",
    "Instalando no sistema",
    "Finalizando e removendo instalador",
]

# Crisp inline SVG icons matching the dark desktop installer design.
SVG_DOWNLOAD = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="22" height="22" viewBox="0 0 24 24" fill="none">'
    '<path d="M12 4v11m0 0-4-4m4 4 4-4M4.5 16.5v2a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-2" '
    'stroke="#60A5FA" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHECK_BLUE = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none">'
    '<path d="M5 13l4 4L19 7" stroke="#60A5FA" stroke-width="2.6" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHECK_WHITE = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="11" height="11" viewBox="0 0 24 24" fill="none">'
    '<path d="M5 13l4 4L19 7" stroke="#FFFFFF" stroke-width="3" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHECK_GREEN = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="22" height="22" viewBox="0 0 24 24" fill="none">'
    '<path d="M5 13l4 4L19 7" stroke="#6DD58C" stroke-width="2.4" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_BELL = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24" fill="none">'
    '<path d="M15 18H5.5a1 1 0 0 1-.8-1.6l1.3-1.7V10a6 6 0 1 1 12 0v4.7l1.3 1.7a1 1 0 0 1-.8 1.6H15Zm-5 0a2 2 0 0 0 4 0" '
    'stroke="#9296A4" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CLOSE = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none">'
    '<path d="M6 6l12 12M18 6L6 18" stroke="#9296A4" stroke-width="2.2" stroke-linecap="round"/>'
    '</svg>'
)

SVG_CHEVRON_DOWN = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none">'
    '<path d="M6 9l6 6 6-6" stroke="#9296A4" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHEVRON_UP = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none">'
    '<path d="M18 15l-6-6-6 6" stroke="#9296A4" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_BRAND_FALLBACK = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 256 256">'
    '<rect width="256" height="256" rx="56" fill="#182942"/>'
    '<path fill="#60A5FA" d="M128 38 L200 162 L176 144 L128 76 L80 144 L56 162 Z"/>'
    '<path fill="#93C5FD" d="M128 118 L216 212 L184 198 L128 148 L72 198 L40 212 Z"/>'
    '</svg>'
)


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


def save_install_log(log_text: str) -> Path:
    downloads_dir = Path.home() / "Downloads"
    target_dir = downloads_dir if downloads_dir.is_dir() else Path.home()
    out_path = target_dir / "custodia-instalacao-log.txt"
    header = (
        f"Custodia Installer Log\n"
        f"System: {detect_distro_label()}\n"
        f"Recommended format: .{detect_default_format()}\n"
        f"{'-' * 48}\n"
    )
    body = log_text.strip() or "(nenhuma linha registrada até o momento)"
    out_path.write_text(header + body + "\n", encoding="utf-8")
    return out_path


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
                    raise RuntimeError("Instalação cancelada.")
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
    log(f"SHA-256 conferido ({actual[:12]}...).")


def run_privileged(
    cmd: list[str],
    log: Callable[[str], None],
    on_status: Callable[[str], None] | None = None,
) -> None:
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
            if on_status is not None:
                on_status(clean[:76])
    rc = proc.wait()
    if rc != 0:
        if rc in (126, 127):
            raise RuntimeError("Autenticação administrativa cancelada.")
        detail = tail_lines[-1] if tail_lines else f"código {rc}"
        raise RuntimeError(f"Falha ao instalar o pacote ({detail}).")


def install_deb(
    pkg_path: Path,
    log: Callable[[str], None],
    on_status: Callable[[str], None] | None = None,
) -> None:
    if shutil.which("apt-get"):
        run_privileged(["apt-get", "install", "-y", "--reinstall", "--allow-downgrades", str(pkg_path)], log, on_status)
    elif shutil.which("dpkg"):
        run_privileged(["dpkg", "-i", str(pkg_path)], log, on_status)
    else:
        raise RuntimeError("Nem apt-get nem dpkg estão disponíveis neste sistema.")


def _rpm_query_version(args: list[str]) -> str:
    try:
        res = subprocess.run(
            ["rpm"] + args,
            capture_output=True,
            text=True,
            check=False,
        )
        if res.returncode == 0:
            return res.stdout.strip().splitlines()[0].strip()
    except Exception:
        pass
    return ""


def _parse_evr(ver_str: str) -> tuple[int, ...]:
    nums: list[int] = []
    cur = ""
    for ch in ver_str:
        if ch.isdigit():
            cur += ch
        elif cur:
            nums.append(int(cur))
            cur = ""
    if cur:
        nums.append(int(cur))
    return tuple(nums)


def _compare_rpm_versions(target: str, installed: str) -> int:
    if shutil.which("rpmdev-vercmp"):
        res = subprocess.run(["rpmdev-vercmp", target, installed], capture_output=True, check=False)
        if res.returncode == 0:
            return 0
        if res.returncode == 11:
            return 1
        if res.returncode == 12:
            return -1
    t_tuple = _parse_evr(target)
    i_tuple = _parse_evr(installed)
    return (t_tuple > i_tuple) - (t_tuple < i_tuple)


def install_rpm(
    pkg_path: Path,
    log: Callable[[str], None],
    on_status: Callable[[str], None] | None = None,
) -> None:
    if shutil.which("dnf"):
        installed = _rpm_query_version(["-q", "--qf", "%{VERSION}-%{RELEASE}\n", "custodia"])
        target = _rpm_query_version(["-qp", "--qf", "%{VERSION}-%{RELEASE}\n", str(pkg_path)])
        if installed and target:
            cmp = _compare_rpm_versions(target, installed)
            if cmp == 0:
                run_privileged(["dnf", "reinstall", "-y", "--nogpgcheck", str(pkg_path)], log, on_status)
                return
            if cmp < 0:
                try:
                    run_privileged(["dnf", "install", "-y", "--nogpgcheck", "--allow-downgrade", str(pkg_path)], log, on_status)
                except RuntimeError:
                    run_privileged(["dnf", "downgrade", "-y", "--nogpgcheck", str(pkg_path)], log, on_status)
                return
        run_privileged(["dnf", "install", "-y", "--nogpgcheck", str(pkg_path)], log, on_status)
    elif shutil.which("zypper"):
        run_privileged(["zypper", "--non-interactive", "--no-gpg-checks", "install", "--force", str(pkg_path)], log, on_status)
    elif shutil.which("rpm"):
        run_privileged(["rpm", "-Uvh", "--replacepkgs", "--oldpackage", "--nosignature", str(pkg_path)], log, on_status)
    else:
        raise RuntimeError("Nem dnf, zypper nem rpm estão disponíveis neste sistema.")


def install_appimage(
    pkg_path: Path,
    log: Callable[[str], None],
    on_status: Callable[[str], None] | None = None,
) -> None:
    home = Path.home()
    install_dir = home / ".local" / "opt" / "custodia"
    bin_dir = home / ".local" / "bin"
    apps_dir = home / ".local" / "share" / "applications"
    autostart_dir = home / ".config" / "autostart"

    for d in (install_dir, bin_dir, apps_dir, autostart_dir):
        d.mkdir(parents=True, exist_ok=True)

    target_app = install_dir / "Custodia.AppImage"
    if on_status is not None:
        on_status("Copiando Custodia.AppImage")
    shutil.copy2(pkg_path, target_app)
    target_app.chmod(target_app.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    wrapper = bin_dir / "custodia"
    wrapper.write_text(
        f'#!/usr/bin/env bash\nexec "{target_app}" "$@"\n',
        encoding="utf-8",
    )
    wrapper.chmod(wrapper.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    if on_status is not None:
        on_status("Registrando atalhos no menu")
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
        # Preserve the source script when running inside the repository during development.
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


def _resolve_app_icon_path() -> Path | None:
    env_icon = os.environ.get("CUSTODIA_INSTALLER_ICON")
    candidates: list[Path] = []
    if env_icon:
        candidates.append(Path(env_icon))
    here = Path(__file__).resolve()
    candidates.extend([
        here.parent.parent / "custodia-installer.png",
        here.parent.parent.parent / "custodia-installer.png",
        here.parents[2] / "src" / "Custodia.Desktop" / "Assets" / "custodia-icon.png"
        if len(here.parents) > 2 else here,
    ])
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    return None


def _svg_to_pixbuf(svg_markup: str, width: int, height: int):
    from gi.repository import GdkPixbuf  # type: ignore

    loader = GdkPixbuf.PixbufLoader.new_with_type("svg")
    loader.set_size(width, height)
    loader.write(svg_markup.encode("utf-8"))
    loader.close()
    return loader.get_pixbuf()


SHARED_CSS = """
window, .installer-root {
    background-color: #111318;
    background-image: none;
    color: #F2F2F2;
    font-family: Inter, "Cantarell", "Noto Sans", system-ui, sans-serif;
}

headerbar.installer-topbar {
    background-color: #111318;
    background-image: none;
    border: none;
    border-bottom: 1px solid #222630;
    box-shadow: none;
    padding: 0 16px;
    min-height: 46px;
}

.topbar-brand {
    font-weight: 700;
    font-size: 13.5px;
    color: #F2F2F2;
}

.topbar-version {
    font-family: "JetBrains Mono", monospace;
    font-size: 12px;
    color: #929292;
    margin-left: 6px;
}

button.topbar-link {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    color: #929292;
    font-size: 12.5px;
    padding: 4px 8px;
    margin-left: 6px;
}

button.topbar-link:hover {
    color: #F2F2F2;
}

button.topbar-icon-btn {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    border-radius: 999px;
    min-width: 28px;
    min-height: 28px;
    padding: 4px;
}

button.topbar-icon-btn:hover {
    background-color: #1F232C;
}

button.topbar-close-btn {
    background-color: #1A1D24;
    background-image: none;
    border: 1px solid #292929;
    box-shadow: none;
    border-radius: 999px;
    min-width: 28px;
    min-height: 28px;
    padding: 4px;
}

button.topbar-close-btn:hover {
    background-color: #252A35;
}

.hero-icon-blue {
    background-color: #172742;
    border: 1px solid #243C63;
    border-radius: 13px;
    min-width: 48px;
    min-height: 48px;
    padding: 12px;
}

.hero-icon-green {
    background-color: #142C21;
    border: 1px solid #214B37;
    border-radius: 13px;
    min-width: 48px;
    min-height: 48px;
    padding: 12px;
}

.hero-title {
    font-size: 23px;
    font-weight: 700;
    color: #F2F2F2;
}

.hero-subtitle {
    font-size: 13.5px;
    color: #929292;
}

.source-mono {
    font-family: "JetBrains Mono", monospace;
    font-size: 12px;
    color: #7D8390;
    margin-top: 4px;
    margin-bottom: 2px;
}

.installer-card {
    background-color: #191919;
    border: 1px solid #292929;
    border-radius: 16px;
}

.card-section {
    padding: 18px 20px;
}

.card-section-tight {
    padding: 12px 16px 16px 16px;
}

.card-footer-row {
    padding: 12px 20px;
}

.h-divider {
    background-color: #262626;
    min-height: 1px;
}

.support-divider {
    background-color: #222630;
    min-height: 1px;
    margin-top: 4px;
}

.field-label {
    font-size: 12.5px;
    font-weight: 600;
    color: #929292;
}

button.fmt-row {
    background-color: #1E2026;
    background-image: none;
    border: 1px solid #292929;
    border-radius: 12px;
    padding: 12px 14px;
    box-shadow: none;
}

button.fmt-row:hover {
    background-color: #23262E;
    border-color: #363A45;
}

button.fmt-row.selected {
    background-color: #18263D;
    border-color: #3478F6;
}

.fmt-title {
    font-family: "JetBrains Mono", monospace;
    font-size: 13.5px;
    font-weight: 700;
    color: #F2F2F2;
}

.fmt-sub {
    font-size: 12px;
    color: #929292;
}

.badge-rec {
    background-color: #182D4F;
    color: #7DB2FF;
    border-radius: 999px;
    padding: 2px 9px;
    font-size: 11px;
    font-weight: 600;
}

.fmt-dot {
    min-width: 18px;
    min-height: 18px;
    border-radius: 999px;
    border: 1.5px solid #4A5160;
    background-color: transparent;
}

.fmt-dot.selected {
    background-color: #3478F6;
    border-color: #3478F6;
}

.distro-hint {
    font-size: 12px;
    color: #929292;
}

button.btn-primary {
    background-color: #3478F6;
    background-image: none;
    color: #FFFFFF;
    border: none;
    border-radius: 11px;
    padding: 9px 18px;
    font-weight: 600;
    font-size: 13px;
    box-shadow: none;
}

button.btn-primary:hover {
    background-color: #2563EB;
}

button.btn-secondary {
    background-color: #23262E;
    background-image: none;
    color: #F2F2F2;
    border: 1px solid #333742;
    border-radius: 11px;
    padding: 8px 16px;
    font-weight: 600;
    font-size: 13px;
    box-shadow: none;
}

button.btn-secondary:hover {
    background-color: #2C303A;
}

button.btn-danger {
    background-color: #B3261E;
    background-image: none;
    color: #FFFFFF;
    border: none;
    border-radius: 11px;
    padding: 8px 16px;
    font-weight: 600;
    font-size: 13px;
    box-shadow: none;
}

button.btn-danger:hover {
    background-color: #991F18;
}

button.btn-ghost {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    color: #929292;
    font-size: 13px;
    font-weight: 500;
    padding: 4px 6px;
}

button.btn-ghost:hover {
    color: #F2F2F2;
}

button.btn-cancel {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    color: #F2F2F2;
    font-size: 13px;
    font-weight: 600;
    padding: 4px 8px;
}

button.btn-cancel:hover {
    color: #7DB2FF;
}

progressbar.slim-progress trough {
    background-color: #292929;
    background-image: none;
    border: none;
    border-radius: 999px;
    min-height: 8px;
}

progressbar.slim-progress progress {
    background-color: #3478F6;
    background-image: none;
    border: none;
    border-radius: 999px;
    min-height: 8px;
}

.stats-mono {
    font-family: "JetBrains Mono", monospace;
    font-size: 12px;
    color: #929292;
}

.stats-pct {
    font-family: "JetBrains Mono", monospace;
    font-size: 12px;
    font-weight: 700;
    color: #F2F2F2;
}

.step-row {
    padding: 9px 12px;
    border-radius: 12px;
}

.step-row.active {
    background-color: #23252C;
    border: 1px solid #2F323B;
    padding: 11px 13px;
}

.step-circle-done {
    background-color: #182942;
    border-radius: 999px;
    min-width: 22px;
    min-height: 22px;
    padding: 5px;
}

.step-dot-pending {
    background-color: #4B505B;
    border-radius: 999px;
    min-width: 6px;
    min-height: 6px;
}

spinner.step-spinner {
    color: #3478F6;
}

.step-title-done {
    font-size: 13.5px;
    color: #929292;
}

.step-title-active {
    font-size: 13.5px;
    font-weight: 600;
    color: #F2F2F2;
}

.step-title-pending {
    font-size: 13.5px;
    color: #6B707B;
}

.step-sub {
    font-family: "JetBrains Mono", monospace;
    font-size: 11.5px;
    color: #929292;
}

.log-box {
    background-color: #111318;
    border: 1px solid #292929;
    border-radius: 10px;
    padding: 8px 10px;
    font-family: "JetBrains Mono", monospace;
    font-size: 11px;
    color: #9AA4B2;
}

.error-box {
    background-color: #31171B;
    border: 1px solid #5B242B;
    border-radius: 12px;
    padding: 12px 14px;
    color: #FFB4AB;
    font-size: 12.5px;
}

button.footer-link {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    color: #929292;
    font-size: 12px;
    padding: 2px 4px;
}

button.footer-link:hover {
    color: #F2F2F2;
}

.dialog-card {
    background-color: #191919;
    color: #F2F2F2;
    padding: 20px;
}

.dialog-title {
    font-size: 16px;
    font-weight: 700;
    color: #F2F2F2;
}

.dialog-body {
    font-size: 13px;
    color: #929292;
}

popover.info-popover > contents {
    background-color: #191919;
    background-image: none;
    border: 1px solid #292929;
    border-radius: 12px;
    color: #F2F2F2;
    padding: 12px 14px;
}
"""

GTK4_EXTRA_CSS = """
dropdown > button {
    background-color: #1E2026;
    background-image: none;
    border: 1px solid #292929;
    border-radius: 11px;
    color: #F2F2F2;
    padding: 7px 12px;
}
"""

GTK3_EXTRA_CSS = """
combobox button {
    background-color: #1E2026;
    background-image: none;
    border: 1px solid #292929;
    border-radius: 11px;
    color: #F2F2F2;
    padding: 6px 12px;
}
"""


def _load_css(provider, css_text: str) -> None:
    if hasattr(provider, "load_from_string"):
        provider.load_from_string(css_text)
    else:
        provider.load_from_data(css_text.encode("utf-8"))


def _svg_gtk4_image(svg_markup: str, size: int, centered: bool = False):
    from gi.repository import Gdk, GLib, Gtk  # type: ignore

    widget = None
    try:
        tex = Gdk.Texture.new_from_bytes(GLib.Bytes.new(svg_markup.encode("utf-8")))
        widget = Gtk.Image.new_from_paintable(tex)
        widget.set_pixel_size(size)
    except Exception:
        try:
            pb = _svg_to_pixbuf(svg_markup, size, size)
            _, png_bytes = pb.save_to_bufferv("png", [], [])
            tex = Gdk.Texture.new_from_bytes(GLib.Bytes.new(png_bytes))
            widget = Gtk.Image.new_from_paintable(tex)
            widget.set_pixel_size(size)
        except Exception:
            widget = Gtk.Label(label="•")

    if centered:
        widget.set_halign(Gtk.Align.CENTER)
        widget.set_valign(Gtk.Align.CENTER)
        widget.set_hexpand(True)
        widget.set_vexpand(True)
    return widget


def _app_icon_gtk4_image(size: int):
    from gi.repository import Gdk, GdkPixbuf, GLib, Gtk  # type: ignore

    icon_path = _resolve_app_icon_path()
    if icon_path is not None:
        try:
            pb = GdkPixbuf.Pixbuf.new_from_file_at_scale(str(icon_path), size, size, True)
            _, png_bytes = pb.save_to_bufferv("png", [], [])
            tex = Gdk.Texture.new_from_bytes(GLib.Bytes.new(png_bytes))
            img = Gtk.Image.new_from_paintable(tex)
            img.set_pixel_size(size)
            return img
        except Exception:
            pass
    return _svg_gtk4_image(SVG_BRAND_FALLBACK, size)


def run_gtk4() -> bool:
    try:
        import gi
        gi.require_version("Gdk", "4.0")
        gi.require_version("Gtk", "4.0")
        gi.require_version("GdkPixbuf", "2.0")
        from gi.repository import Gdk, GLib, Gtk, Pango  # type: ignore
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
            self.all_log_lines: list[str] = []

            self.fmt_buttons: dict[str, Gtk.Button] = {}
            self.fmt_dots: dict[str, Gtk.Box] = {}
            self.fmt_checks: dict[str, Gtk.Widget] = {}
            self.step_widgets: list[dict] = []
            self.save_log_buttons: list[Gtk.Button] = []

        def do_activate(self) -> None:
            settings = Gtk.Settings.get_default()
            if settings is not None:
                if hasattr(Gtk, "InterfaceColorScheme") and settings.find_property("gtk-interface-color-scheme"):
                    settings.set_property("gtk-interface-color-scheme", Gtk.InterfaceColorScheme.DARK)
                else:
                    settings.set_property("gtk-application-prefer-dark-theme", True)

            provider = Gtk.CssProvider()
            _load_css(provider, SHARED_CSS + GTK4_EXTRA_CSS)
            Gtk.StyleContext.add_provider_for_display(
                Gdk.Display.get_default(),
                provider,
                Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION,
            )

            self.win = Gtk.ApplicationWindow(application=self)
            self.win.set_title("Custódia")
            self.win.set_default_size(860, 620)
            self.win.set_resizable(True)
            self.win.add_css_class("installer-root")
            self.win.connect("close-request", self._on_window_close_request)

            # Custom 46px dark top bar matching the reference layout.
            header = Gtk.HeaderBar()
            header.add_css_class("installer-topbar")
            header.set_show_title_buttons(False)
            header.set_title_widget(Gtk.Box())

            brand_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            brand_box.set_valign(Gtk.Align.CENTER)
            brand_box.append(_app_icon_gtk4_image(20))

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

            right_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            right_box.set_valign(Gtk.Align.CENTER)

            bell_btn = Gtk.MenuButton()
            bell_btn.add_css_class("topbar-icon-btn")
            bell_btn.set_child(_svg_gtk4_image(SVG_BELL, 15))
            bell_pop = Gtk.Popover()
            bell_pop.add_css_class("info-popover")
            pop_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            self.bell_title_lbl = Gtk.Label(label="Instalador oficial Custódia", xalign=0)
            self.bell_title_lbl.add_css_class("field-label")
            self.bell_body_lbl = Gtk.Label(
                label=f"Sistema: {self.distro_label}\nFormato indicado: .{self.recommended_fmt}\nVerificação SHA-256 ativa",
                xalign=0,
            )
            self.bell_body_lbl.add_css_class("fmt-sub")
            pop_box.append(self.bell_title_lbl)
            pop_box.append(self.bell_body_lbl)
            bell_pop.set_child(pop_box)
            bell_btn.set_popover(bell_pop)

            close_btn = Gtk.Button()
            close_btn.add_css_class("topbar-close-btn")
            close_btn.set_child(_svg_gtk4_image(SVG_CLOSE, 12))
            close_btn.connect("clicked", lambda *_: self._request_quit())

            right_box.append(bell_btn)
            right_box.append(close_btn)
            header.pack_end(right_box)
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

        def _on_window_close_request(self, *_args) -> bool:
            if self.installing:
                self._confirm_cancel(quit_after=True)
                return True
            return False

        def _request_quit(self) -> None:
            if self.installing:
                self._confirm_cancel(quit_after=True)
            else:
                self.quit()

        def _confirm_cancel(self, quit_after: bool = False) -> None:
            dlg = Gtk.Window(transient_for=self.win, modal=True, title="Cancelar instalação")
            dlg.set_resizable(False)
            dlg.set_default_size(380, -1)
            dlg.add_css_class("installer-root")

            box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
            box.add_css_class("dialog-card")

            t_lbl = Gtk.Label(label="Cancelar instalação em andamento?", xalign=0)
            t_lbl.add_css_class("dialog-title")
            b_lbl = Gtk.Label(
                label="O processo atual será interrompido e os arquivos temporários serão removidos.",
                xalign=0,
            )
            b_lbl.set_wrap(True)
            b_lbl.add_css_class("dialog-body")

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            btn_row.set_halign(Gtk.Align.END)
            btn_row.set_margin_top(6)

            keep_btn = Gtk.Button(label="Continuar")
            keep_btn.add_css_class("btn-secondary")
            keep_btn.connect("clicked", lambda *_: dlg.close())

            confirm_btn = Gtk.Button(label="Cancelar instalação")
            confirm_btn.add_css_class("btn-danger")

            def _do_abort(*_a) -> None:
                self.cancel_event.set()
                self.cancel_btn.set_sensitive(False)
                dlg.close()
                if quit_after:
                    self.installing = False
                    self.quit()

            confirm_btn.connect("clicked", _do_abort)
            btn_row.append(keep_btn)
            btn_row.append(confirm_btn)

            box.append(t_lbl)
            box.append(b_lbl)
            box.append(btn_row)
            dlg.set_child(box)
            dlg.present()

        def _centered_shell(self) -> tuple[Gtk.ScrolledWindow, Gtk.Box]:
            scroll = Gtk.ScrolledWindow()
            scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
            scroll.set_hexpand(True)
            scroll.set_vexpand(True)

            outer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
            outer.set_halign(Gtk.Align.CENTER)
            outer.set_valign(Gtk.Align.CENTER)
            outer.set_hexpand(True)
            outer.set_vexpand(True)
            outer.set_margin_top(26)
            outer.set_margin_bottom(26)
            outer.set_margin_start(28)
            outer.set_margin_end(28)

            col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            col.set_size_request(580, -1)
            outer.append(col)
            scroll.set_child(outer)
            return scroll, col

        def _make_divider(self, css_class: str = "h-divider") -> Gtk.Box:
            div = Gtk.Box()
            div.add_css_class(css_class)
            div.set_hexpand(True)
            return div

        def _on_save_log_clicked(self, *_args) -> None:
            try:
                saved = save_install_log("\n".join(self.all_log_lines))
                self._append_log(f"Registro salvo em: {saved}")
                for btn in self.save_log_buttons:
                    btn.set_label("Registro salvo em Downloads")

                def _restore() -> bool:
                    for b in self.save_log_buttons:
                        b.set_label("Baixar registro da instalação")
                    return False

                GLib.timeout_add(3000, _restore)
            except Exception as exc:
                self._append_log(f"Aviso ao salvar registro: {exc}")

        def _build_support_links(self) -> Gtk.Box:
            wrap = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            wrap.append(self._make_divider("support-divider"))

            row1 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            row1.set_margin_top(6)

            save_btn = Gtk.Button(label="Baixar registro da instalação")
            save_btn.add_css_class("footer-link")
            save_btn.connect("clicked", self._on_save_log_clicked)
            self.save_log_buttons.append(save_btn)
            row1.append(save_btn)

            for label, url in (
                ("Guia de instalação", GUIDE_URL),
                ("Solução de problemas", TROUBLESHOOTING_URL),
                ("Relatar um problema", ISSUES_URL),
            ):
                btn = Gtk.Button(label=label)
                btn.add_css_class("footer-link")
                btn.connect("clicked", lambda _b, u=url: webbrowser.open(u))
                row1.append(btn)

            row2 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            comm_btn = Gtk.Button(label="Obter ajuda na comunidade")
            comm_btn.add_css_class("footer-link")
            comm_btn.connect("clicked", lambda *_: webbrowser.open(DISCUSSIONS_URL))
            row2.append(comm_btn)

            wrap.append(row1)
            wrap.append(row2)
            return wrap

        def _build_select_page(self) -> Gtk.Widget:
            shell, col = self._centered_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.add_css_class("hero-icon-blue")
            icon_tile.set_valign(Gtk.Align.START)
            icon_tile.set_halign(Gtk.Align.CENTER)
            icon_tile.append(_svg_gtk4_image(SVG_DOWNLOAD, 22, centered=True))

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            title = Gtk.Label(label="Instalar o Custódia", xalign=0)
            title.add_css_class("hero-title")
            sub = Gtk.Label(
                label="Escolha a versão e o formato para o seu Linux. Ao concluir, o instalador se apaga sozinho.",
                xalign=0,
            )
            sub.set_wrap(True)
            sub.set_max_width_chars(56)
            sub.add_css_class("hero-subtitle")
            hero_text.append(title)
            hero_text.append(sub)
            hero.append(icon_tile)
            hero.append(hero_text)
            col.append(hero)

            source_lbl = Gtk.Label(label="De github.com/TundraLab/Custodia", xalign=0)
            source_lbl.add_css_class("source-mono")
            col.append(source_lbl)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.add_css_class("installer-card")

            top_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            top_block.add_css_class("card-section")

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
            top_block.append(ver_row)

            fmt_lbl = Gtk.Label(label="Formato de instalação", xalign=0)
            fmt_lbl.add_css_class("field-label")
            top_block.append(fmt_lbl)

            fmt_list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            for key, name, desc in FORMATS:
                btn = Gtk.Button()
                btn.add_css_class("fmt-row")
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                row.set_valign(Gtk.Align.CENTER)

                dot = Gtk.Box()
                dot.add_css_class("fmt-dot")
                dot.set_valign(Gtk.Align.CENTER)
                dot.set_halign(Gtk.Align.CENTER)
                chk = _svg_gtk4_image(SVG_CHECK_WHITE, 11)
                chk.set_halign(Gtk.Align.CENTER)
                chk.set_valign(Gtk.Align.CENTER)
                chk.set_hexpand(True)
                chk.set_vexpand(True)
                dot.append(chk)

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
                self.fmt_checks[key] = chk

            top_block.append(fmt_list)
            card.append(top_block)
            card.append(self._make_divider())

            action_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            action_row.add_css_class("card-footer-row")
            distro_lbl = Gtk.Label(label=f"Sistema: {self.distro_label}", xalign=0)
            distro_lbl.add_css_class("distro-hint")
            distro_lbl.set_hexpand(True)
            distro_lbl.set_ellipsize(Pango.EllipsizeMode.END)

            self.install_btn = Gtk.Button(label="Instalar agora")
            self.install_btn.add_css_class("btn-primary")
            self.install_btn.connect("clicked", self._on_start_install)

            action_row.append(distro_lbl)
            action_row.append(self.install_btn)
            card.append(action_row)

            col.append(card)
            col.append(self._build_support_links())
            self._select_format(self.selected_fmt)
            return shell

        def _build_install_page(self) -> Gtk.Widget:
            shell, col = self._centered_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.add_css_class("hero-icon-blue")
            icon_tile.set_valign(Gtk.Align.START)
            icon_tile.set_halign(Gtk.Align.CENTER)
            icon_tile.append(_svg_gtk4_image(SVG_DOWNLOAD, 22, centered=True))

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            self.inst_title = Gtk.Label(label="Instalando...", xalign=0)
            self.inst_title.add_css_class("hero-title")
            self.inst_subtitle = Gtk.Label(
                label="Aguarde alguns instantes enquanto baixamos e preparamos o Custódia no seu sistema.",
                xalign=0,
            )
            self.inst_subtitle.set_wrap(True)
            self.inst_subtitle.set_max_width_chars(56)
            self.inst_subtitle.add_css_class("hero-subtitle")
            hero_text.append(self.inst_title)
            hero_text.append(self.inst_subtitle)
            hero.append(icon_tile)
            hero.append(hero_text)
            col.append(hero)

            source_lbl = Gtk.Label(label="De github.com/TundraLab/Custodia", xalign=0)
            source_lbl.add_css_class("source-mono")
            col.append(source_lbl)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.add_css_class("installer-card")

            prog_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            prog_block.add_css_class("card-section")

            self.progress_bar = Gtk.ProgressBar()
            self.progress_bar.add_css_class("slim-progress")
            self.progress_bar.set_fraction(0.0)
            prog_block.append(self.progress_bar)

            stats_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
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
            prog_block.append(stats_row)
            card.append(prog_block)

            steps_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            steps_block.add_css_class("card-section-tight")
            self.step_widgets = []
            for label_text in STEPS:
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                row.add_css_class("step-row")

                icon_stack = Gtk.Stack()
                icon_stack.set_valign(Gtk.Align.CENTER)
                icon_stack.set_size_request(22, 22)

                pending_wrap = Gtk.Box()
                pending_wrap.set_halign(Gtk.Align.CENTER)
                pending_wrap.set_valign(Gtk.Align.CENTER)
                dot_p = Gtk.Box()
                dot_p.add_css_class("step-dot-pending")
                pending_wrap.append(dot_p)

                spinner = Gtk.Spinner()
                spinner.add_css_class("step-spinner")
                spinner.set_size_request(18, 18)

                done_wrap = Gtk.Box()
                done_wrap.add_css_class("step-circle-done")
                done_wrap.set_halign(Gtk.Align.CENTER)
                done_wrap.set_valign(Gtk.Align.CENTER)
                done_wrap.append(_svg_gtk4_image(SVG_CHECK_BLUE, 12, centered=True))

                icon_stack.add_named(pending_wrap, "pending")
                icon_stack.add_named(spinner, "active")
                icon_stack.add_named(done_wrap, "done")
                icon_stack.set_visible_child_name("pending")

                text_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                text_box.set_hexpand(True)
                t_lbl = Gtk.Label(label=label_text, xalign=0)
                t_lbl.add_css_class("step-title-pending")
                sub_lbl = Gtk.Label(label="", xalign=0)
                sub_lbl.add_css_class("step-sub")
                sub_lbl.set_ellipsize(Pango.EllipsizeMode.END)
                sub_lbl.set_visible(False)
                text_box.append(t_lbl)
                text_box.append(sub_lbl)

                row.append(icon_stack)
                row.append(text_box)
                steps_block.append(row)
                self.step_widgets.append({
                    "row": row,
                    "icon_stack": icon_stack,
                    "spinner": spinner,
                    "title": t_lbl,
                    "sub": sub_lbl,
                })
            card.append(steps_block)

            # Error banner shown when download or install fails.
            self.error_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            self.error_box.add_css_class("error-box")
            self.error_box.set_margin_start(16)
            self.error_box.set_margin_end(16)
            self.error_box.set_margin_bottom(14)
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

            # Collapsible log viewer.
            self.log_revealer = Gtk.Revealer()
            self.log_revealer.set_transition_type(Gtk.RevealerTransitionType.SLIDE_DOWN)
            self.log_revealer.set_transition_duration(180)
            self.log_revealer.set_reveal_child(False)
            log_wrap = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
            log_wrap.set_margin_start(16)
            log_wrap.set_margin_end(16)
            log_wrap.set_margin_bottom(14)
            log_scroll = Gtk.ScrolledWindow()
            log_scroll.set_min_content_height(108)
            log_scroll.set_max_content_height(145)
            self.log_view = Gtk.TextView()
            self.log_view.set_editable(False)
            self.log_view.set_cursor_visible(False)
            self.log_view.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
            self.log_view.add_css_class("log-box")
            self.log_buffer = self.log_view.get_buffer()
            log_scroll.set_child(self.log_view)
            log_wrap.append(log_scroll)
            self.log_revealer.set_child(log_wrap)
            card.append(self.log_revealer)

            card.append(self._make_divider())

            footer = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            footer.add_css_class("card-footer-row")

            self.details_btn = Gtk.Button()
            self.details_btn.add_css_class("btn-ghost")
            det_inner = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
            det_inner.set_valign(Gtk.Align.CENTER)
            self.chevron_stack = Gtk.Stack()
            self.chevron_stack.add_named(_svg_gtk4_image(SVG_CHEVRON_DOWN, 13), "down")
            self.chevron_stack.add_named(_svg_gtk4_image(SVG_CHEVRON_UP, 13), "up")
            self.chevron_stack.set_visible_child_name("down")
            det_inner.append(self.chevron_stack)
            det_inner.append(Gtk.Label(label="Detalhes"))
            self.details_btn.set_child(det_inner)
            self.details_btn.connect("clicked", self._toggle_details)

            spacer = Gtk.Box()
            spacer.set_hexpand(True)

            self.elapsed_lbl = Gtk.Label(label="0 s decorridos")
            self.elapsed_lbl.add_css_class("stats-mono")

            self.cancel_btn = Gtk.Button(label="Cancelar")
            self.cancel_btn.add_css_class("btn-cancel")
            self.cancel_btn.connect("clicked", self._on_cancel)

            footer.append(self.details_btn)
            footer.append(spacer)
            footer.append(self.elapsed_lbl)
            footer.append(self.cancel_btn)
            card.append(footer)

            col.append(card)
            col.append(self._build_support_links())
            return shell

        def _build_done_page(self) -> Gtk.Widget:
            shell, col = self._centered_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.add_css_class("hero-icon-green")
            icon_tile.set_valign(Gtk.Align.START)
            icon_tile.set_halign(Gtk.Align.CENTER)
            icon_tile.append(_svg_gtk4_image(SVG_CHECK_GREEN, 22, centered=True))

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            title = Gtk.Label(label="Custódia instalado!", xalign=0)
            title.add_css_class("hero-title")
            self.done_sub = Gtk.Label(
                label="Tudo pronto para usar. O arquivo do instalador foi removido automaticamente.",
                xalign=0,
            )
            self.done_sub.set_wrap(True)
            self.done_sub.set_max_width_chars(56)
            self.done_sub.add_css_class("hero-subtitle")
            hero_text.append(title)
            hero_text.append(self.done_sub)
            hero.append(icon_tile)
            hero.append(hero_text)
            col.append(hero)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.add_css_class("installer-card")

            summary_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            summary_box.add_css_class("card-section")
            self.done_summary = Gtk.Label(label="", xalign=0)
            self.done_summary.set_wrap(True)
            self.done_summary.add_css_class("fmt-sub")
            summary_box.append(self.done_summary)
            card.append(summary_box)
            card.append(self._make_divider())

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            btn_row.add_css_class("card-footer-row")
            btn_row.set_halign(Gtk.Align.END)

            close_btn = Gtk.Button(label="Fechar")
            close_btn.add_css_class("btn-secondary")
            close_btn.connect("clicked", lambda *_: self.quit())
            open_btn = Gtk.Button(label="Abrir Custódia")
            open_btn.add_css_class("btn-primary")
            open_btn.connect("clicked", self._on_open_and_close)
            btn_row.append(close_btn)
            btn_row.append(open_btn)
            card.append(btn_row)

            col.append(card)
            col.append(self._build_support_links())
            return shell

        def _toggle_details(self, *_args) -> None:
            shown = not self.log_revealer.get_reveal_child()
            self.log_revealer.set_reveal_child(shown)
            self.chevron_stack.set_visible_child_name("up" if shown else "down")

        def _select_format(self, fmt_key: str) -> None:
            self.selected_fmt = fmt_key
            for k, btn in self.fmt_buttons.items():
                dot = self.fmt_dots[k]
                chk = self.fmt_checks[k]
                if k == fmt_key:
                    btn.add_css_class("selected")
                    dot.add_css_class("selected")
                    chk.set_visible(True)
                else:
                    btn.remove_css_class("selected")
                    dot.remove_css_class("selected")
                    chk.set_visible(False)

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
                btn.set_tooltip_text("" if available else f"Formato indisponível na versão {rel['tag']}")
            if self.selected_fmt not in assets:
                for k in (self.recommended_fmt, "deb", "rpm", "appimage"):
                    if k in assets:
                        self._select_format(k)
                        break

        def _append_log(self, msg: str) -> None:
            self.all_log_lines.append(msg)

            def _ui() -> bool:
                end = self.log_buffer.get_end_iter()
                self.log_buffer.insert(end, msg + "\n")
                mark = self.log_buffer.create_mark(None, self.log_buffer.get_end_iter(), False)
                self.log_view.scroll_to_mark(mark, 0.0, False, 0.0, 0.0)
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

        def _set_step_sub(self, idx: int, sub_text: str) -> None:
            def _ui() -> bool:
                if 0 <= idx < len(self.step_widgets):
                    sub = self.step_widgets[idx]["sub"]
                    if sub_text:
                        sub.set_label(sub_text)
                        sub.set_visible(True)
                return False

            GLib.idle_add(_ui)

        def _set_progress(self, fraction: float, bytes_text: str = "", speed_text: str | None = None) -> None:
            def _ui() -> bool:
                clamped = max(0.0, min(1.0, fraction))
                self.progress_bar.set_fraction(clamped)
                self.stats_pct.set_label(f"{int(round(clamped * 100))}%")
                if bytes_text:
                    self.stats_bytes.set_label(f"·  {bytes_text}")
                if speed_text is not None:
                    self.stats_speed.set_label(speed_text)
                return False

            GLib.idle_add(_ui)

        def _stop_timer(self) -> None:
            if self.timer_source_id is not None:
                GLib.source_remove(self.timer_source_id)
                self.timer_source_id = None

        def _tick_elapsed(self) -> bool:
            if not self.installing:
                self.timer_source_id = None
                return False
            secs = int(time.monotonic() - self.install_started_at)
            self.elapsed_lbl.set_label(f"{secs} s decorridos")
            return True

        def _on_cancel(self, *_args) -> None:
            if self.installing:
                self._confirm_cancel(quit_after=False)
            else:
                self.stack.set_visible_child_name("select")

        def _on_start_install(self, *_args) -> None:
            rel = self._selected_release()
            fmt = self.selected_fmt
            asset = (rel.get("assets") or {}).get(fmt)
            if not asset:
                return

            self._stop_timer()
            self.cancel_event.clear()
            self.installing = True
            self.install_started_at = time.monotonic()
            self.all_log_lines.clear()
            self.error_box.set_visible(False)
            self.cancel_btn.set_sensitive(True)
            self.log_buffer.set_text("")
            self.stats_pct.set_label("0%")
            self.stats_bytes.set_label("")
            self.stats_speed.set_label("")
            self.progress_bar.set_fraction(0.0)
            self.elapsed_lbl.set_label("0 s decorridos")
            fmt_disp = ".AppImage" if fmt == "appimage" else f".{fmt}"
            self.step_widgets[1]["title"].set_label(f"Baixando Custódia {rel['tag']} ({fmt_disp})")

            self.stack.set_visible_child_name("install")
            self.timer_source_id = GLib.timeout_add(1000, self._tick_elapsed)

            threading.Thread(
                target=self._install_worker,
                args=(rel, fmt, asset),
                daemon=True,
            ).start()

        def _install_worker(self, rel: dict, fmt: str, asset: dict) -> None:
            tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-install-"))
            os.chmod(tmp_dir, 0o755)
            try:
                self._set_step(0, self.distro_label)
                self._set_progress(0.04)
                self._append_log(f"Sistema detectado: {self.distro_label}")
                stop_running_custodia()

                pkg_path = tmp_dir / asset["name"]
                self._set_step(1, asset["name"])
                self._append_log(f"Baixando {asset['url']}")

                def on_dl(done: int, total: int, speed: float, eta: float) -> None:
                    frac = 0.08 + 0.64 * (done / total) if total > 0 else 0.35
                    b_txt = f"{format_bytes(done)} / {format_bytes(total)}" if total > 0 else format_bytes(done)
                    spd = f"{speed / (1024 * 1024):.1f} MB/s".replace(".", ",")
                    eta_s = format_eta(eta)
                    s_txt = f"{spd} · {eta_s}" if eta_s else spd
                    self._set_progress(frac, b_txt, s_txt)

                download_file(asset["url"], pkg_path, on_dl, self.cancel_event, int(asset.get("size") or 0))
                os.chmod(pkg_path, 0o644)

                self._set_step(2, "Conferindo SHA256SUMS.txt")
                self._set_progress(0.76, speed_text="Validando...")
                sums_asset = (rel.get("assets") or {}).get("sums")
                verify_sha256(pkg_path, sums_asset["url"] if sums_asset else None, asset["name"], self._append_log)

                self._set_step(3, "Aplicando pacote no sistema")
                self._set_progress(0.86, speed_text="Instalando...")
                on_sub = lambda text: self._set_step_sub(3, text)
                if fmt == "deb":
                    install_deb(pkg_path, self._append_log, on_sub)
                elif fmt == "rpm":
                    install_rpm(pkg_path, self._append_log, on_sub)
                else:
                    install_appimage(pkg_path, self._append_log, on_sub)

                self._set_step(4, "Removendo arquivo do instalador")
                self._set_progress(0.96, speed_text="Finalizando...")
                deleted = self_delete_installer(self._append_log)

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
            self._stop_timer()
            fmt_name = ".AppImage" if fmt == "appimage" else f".{fmt}"
            if deleted:
                self.done_sub.set_label("Tudo pronto para usar. O arquivo do instalador foi removido automaticamente.")
            else:
                self.done_sub.set_label("Tudo pronto. O Custódia já está disponível no menu de aplicativos.")
            self.done_summary.set_label(f"Versão instalada: Custódia {tag} ({fmt_name}) no {self.distro_label}.")
            self.stack.set_visible_child_name("done")
            return False

        def _show_error(self, message: str) -> bool:
            self._stop_timer()
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


def _svg_gtk3_image(svg_markup: str, size: int):
    from gi.repository import Gtk  # type: ignore

    try:
        pb = _svg_to_pixbuf(svg_markup, size, size)
        return Gtk.Image.new_from_pixbuf(pb)
    except Exception:
        return Gtk.Label(label="•")


def _app_icon_gtk3_image(size: int):
    from gi.repository import GdkPixbuf, Gtk  # type: ignore

    icon_path = _resolve_app_icon_path()
    if icon_path is not None:
        try:
            pb = GdkPixbuf.Pixbuf.new_from_file_at_scale(str(icon_path), size, size, True)
            return Gtk.Image.new_from_pixbuf(pb)
        except Exception:
            pass
    return _svg_gtk3_image(SVG_BRAND_FALLBACK, size)


def run_gtk3() -> bool:
    try:
        import gi
        gi.require_version("Gdk", "3.0")
        gi.require_version("Gtk", "3.0")
        gi.require_version("GdkPixbuf", "2.0")
        from gi.repository import Gdk, GLib, Gtk, Pango  # type: ignore
    except Exception:
        return False

    class CustodiaInstallerGtk3:
        def __init__(self) -> None:
            settings = Gtk.Settings.get_default()
            if settings is not None:
                if hasattr(Gtk, "InterfaceColorScheme") and settings.find_property("gtk-interface-color-scheme"):
                    settings.set_property("gtk-interface-color-scheme", Gtk.InterfaceColorScheme.DARK)
                else:
                    settings.set_property("gtk-application-prefer-dark-theme", True)

            provider = Gtk.CssProvider()
            provider.load_from_data((SHARED_CSS + GTK3_EXTRA_CSS).encode("utf-8"))
            screen = Gdk.Screen.get_default()
            if screen is not None:
                Gtk.StyleContext.add_provider_for_screen(
                    screen, provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION
                )

            self.releases: list[dict] = [build_fallback_release()]
            self.recommended_fmt = detect_default_format()
            self.selected_fmt = self.recommended_fmt
            self.distro_label = detect_distro_label()
            self.cancel_event = threading.Event()
            self.install_started_at = 0.0
            self.timer_source_id: int | None = None
            self.installing = False
            self.all_log_lines: list[str] = []

            self.fmt_buttons: dict[str, Gtk.Button] = {}
            self.fmt_dots: dict[str, Gtk.Box] = {}
            self.fmt_checks: dict[str, Gtk.Widget] = {}
            self.step_widgets: list[dict] = []
            self.save_log_buttons: list[Gtk.Button] = []

            self.win = Gtk.Window(title="Custódia")
            self.win.set_default_size(860, 620)
            self.win.get_style_context().add_class("installer-root")
            self.win.connect("delete-event", self._on_delete)

            header = Gtk.HeaderBar()
            header.get_style_context().add_class("installer-topbar")
            header.set_show_close_button(False)
            header.set_custom_title(Gtk.Box())

            brand_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            brand_box.pack_start(_app_icon_gtk3_image(20), False, False, 0)
            brand_lbl = Gtk.Label(label="Custódia")
            brand_lbl.get_style_context().add_class("topbar-brand")
            self.topbar_ver = Gtk.Label(label=f"v{FALLBACK_VERSION}")
            self.topbar_ver.get_style_context().add_class("topbar-version")
            whats_new = Gtk.Button(label="O que há de novo")
            whats_new.get_style_context().add_class("topbar-link")
            whats_new.connect("clicked", lambda *_: webbrowser.open(RELEASES_URL))
            brand_box.pack_start(brand_lbl, False, False, 0)
            brand_box.pack_start(self.topbar_ver, False, False, 0)
            brand_box.pack_start(whats_new, False, False, 0)
            header.pack_start(brand_box)

            right_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            bell_btn = Gtk.MenuButton()
            bell_btn.get_style_context().add_class("topbar-icon-btn")
            bell_btn.add(_svg_gtk3_image(SVG_BELL, 15))
            bell_pop = Gtk.Popover.new(bell_btn)
            bell_pop.get_style_context().add_class("info-popover")
            pop_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            t_pop = Gtk.Label(label="Instalador oficial Custódia", xalign=0)
            t_pop.get_style_context().add_class("field-label")
            b_pop = Gtk.Label(
                label=f"Sistema: {self.distro_label}\nFormato indicado: .{self.recommended_fmt}\nVerificação SHA-256 ativa",
                xalign=0,
            )
            b_pop.get_style_context().add_class("fmt-sub")
            pop_box.pack_start(t_pop, False, False, 0)
            pop_box.pack_start(b_pop, False, False, 0)
            pop_box.show_all()
            bell_pop.add(pop_box)
            bell_btn.set_popover(bell_pop)

            close_btn = Gtk.Button()
            close_btn.get_style_context().add_class("topbar-close-btn")
            close_btn.add(_svg_gtk3_image(SVG_CLOSE, 12))
            close_btn.connect("clicked", lambda *_: self._request_quit())

            right_box.pack_start(bell_btn, False, False, 0)
            right_box.pack_start(close_btn, False, False, 0)
            header.pack_end(right_box)
            self.win.set_titlebar(header)

            self.stack = Gtk.Stack()
            self.stack.set_transition_type(Gtk.StackTransitionType.CROSSFADE)
            self.stack.set_transition_duration(180)
            self.stack.add_named(self._build_select_page(), "select")
            self.stack.add_named(self._build_install_page(), "install")
            self.stack.add_named(self._build_done_page(), "done")
            self.stack.set_visible_child_name("select")

            self.win.add(self.stack)
            self.win.show_all()
            for k, chk in self.fmt_checks.items():
                chk.set_visible(k == self.selected_fmt)
            self.error_box.set_visible(False)
            for w in self.step_widgets:
                w["sub"].set_visible(False)

            threading.Thread(target=self._load_releases_bg, daemon=True).start()

        def _on_delete(self, *_args) -> bool:
            if self.installing:
                self._confirm_cancel(quit_after=True)
                return True
            Gtk.main_quit()
            return False

        def _request_quit(self) -> None:
            if self.installing:
                self._confirm_cancel(quit_after=True)
            else:
                Gtk.main_quit()

        def _confirm_cancel(self, quit_after: bool = False) -> None:
            dlg = Gtk.MessageDialog(
                transient_for=self.win,
                modal=True,
                message_type=Gtk.MessageType.QUESTION,
                buttons=Gtk.ButtonsType.NONE,
                text="Cancelar instalação em andamento?",
            )
            dlg.format_secondary_text("O processo atual será interrompido e os arquivos temporários serão removidos.")
            dlg.add_button("Continuar", Gtk.ResponseType.CANCEL)
            dlg.add_button("Cancelar instalação", Gtk.ResponseType.OK)
            resp = dlg.run()
            dlg.destroy()
            if resp == Gtk.ResponseType.OK:
                self.cancel_event.set()
                self.cancel_btn.set_sensitive(False)
                if quit_after:
                    self.installing = False
                    Gtk.main_quit()

        def _centered_shell(self):
            scroll = Gtk.ScrolledWindow()
            scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
            outer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
            outer.set_halign(Gtk.Align.CENTER)
            outer.set_valign(Gtk.Align.CENTER)
            outer.set_margin_top(24)
            outer.set_margin_bottom(24)
            outer.set_margin_start(28)
            outer.set_margin_end(28)
            col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            col.set_size_request(580, -1)
            outer.pack_start(col, False, False, 0)
            scroll.add(outer)
            return scroll, col

        def _make_divider(self, css_class: str = "h-divider") -> Gtk.Box:
            div = Gtk.Box()
            div.get_style_context().add_class(css_class)
            return div

        def _on_save_log_clicked(self, *_args) -> None:
            try:
                saved = save_install_log("\n".join(self.all_log_lines))
                self._append_log(f"Registro salvo em: {saved}")
                for btn in self.save_log_buttons:
                    btn.set_label("Registro salvo em Downloads")

                def _restore() -> bool:
                    for b in self.save_log_buttons:
                        b.set_label("Baixar registro da instalação")
                    return False

                GLib.timeout_add(3000, _restore)
            except Exception as exc:
                self._append_log(f"Aviso ao salvar registro: {exc}")

        def _build_support_links(self) -> Gtk.Box:
            wrap = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            wrap.pack_start(self._make_divider("support-divider"), False, False, 0)
            row1 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            save_btn = Gtk.Button(label="Baixar registro da instalação")
            save_btn.get_style_context().add_class("footer-link")
            save_btn.connect("clicked", self._on_save_log_clicked)
            self.save_log_buttons.append(save_btn)
            row1.pack_start(save_btn, False, False, 0)
            for label, url in (
                ("Guia de instalação", GUIDE_URL),
                ("Solução de problemas", TROUBLESHOOTING_URL),
                ("Relatar um problema", ISSUES_URL),
            ):
                btn = Gtk.Button(label=label)
                btn.get_style_context().add_class("footer-link")
                btn.connect("clicked", lambda _b, u=url: webbrowser.open(u))
                row1.pack_start(btn, False, False, 0)
            row2 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            comm_btn = Gtk.Button(label="Obter ajuda na comunidade")
            comm_btn.get_style_context().add_class("footer-link")
            comm_btn.connect("clicked", lambda *_: webbrowser.open(DISCUSSIONS_URL))
            row2.pack_start(comm_btn, False, False, 0)
            wrap.pack_start(row1, False, False, 0)
            wrap.pack_start(row2, False, False, 0)
            return wrap

        def _build_select_page(self):
            shell, col = self._centered_shell()
            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.get_style_context().add_class("hero-icon-blue")
            icon_tile.pack_start(_svg_gtk3_image(SVG_DOWNLOAD, 22), True, True, 0)
            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            title = Gtk.Label(label="Instalar o Custódia", xalign=0)
            title.get_style_context().add_class("hero-title")
            sub = Gtk.Label(
                label="Escolha a versão e o formato para o seu Linux. Ao concluir, o instalador se apaga sozinho.",
                xalign=0,
            )
            sub.set_line_wrap(True)
            sub.get_style_context().add_class("hero-subtitle")
            hero_text.pack_start(title, False, False, 0)
            hero_text.pack_start(sub, False, False, 0)
            hero.pack_start(icon_tile, False, False, 0)
            hero.pack_start(hero_text, True, True, 0)
            col.pack_start(hero, False, False, 0)

            source_lbl = Gtk.Label(label="De github.com/TundraLab/Custodia", xalign=0)
            source_lbl.get_style_context().add_class("source-mono")
            col.pack_start(source_lbl, False, False, 0)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.get_style_context().add_class("installer-card")
            top_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            top_block.get_style_context().add_class("card-section")

            ver_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            ver_lbl = Gtk.Label(label="Versão", xalign=0)
            ver_lbl.get_style_context().add_class("field-label")
            self.ver_combo = Gtk.ComboBoxText()
            self.ver_combo.append_text(f"v{FALLBACK_VERSION} (Mais recente)")
            self.ver_combo.set_active(0)
            self.ver_combo.connect("changed", self._on_release_changed)
            ver_row.pack_start(ver_lbl, True, True, 0)
            ver_row.pack_start(self.ver_combo, False, False, 0)
            top_block.pack_start(ver_row, False, False, 0)

            fmt_lbl = Gtk.Label(label="Formato de instalação", xalign=0)
            fmt_lbl.get_style_context().add_class("field-label")
            top_block.pack_start(fmt_lbl, False, False, 0)

            fmt_list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            for key, name, desc in FORMATS:
                btn = Gtk.Button()
                btn.get_style_context().add_class("fmt-row")
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                dot = Gtk.Box()
                dot.get_style_context().add_class("fmt-dot")
                chk = _svg_gtk3_image(SVG_CHECK_WHITE, 11)
                dot.pack_start(chk, True, True, 0)
                text_col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                name_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
                t_lbl = Gtk.Label(label=name, xalign=0)
                t_lbl.get_style_context().add_class("fmt-title")
                name_row.pack_start(t_lbl, False, False, 0)
                if key == self.recommended_fmt:
                    rec = Gtk.Label(label="Recomendado")
                    rec.get_style_context().add_class("badge-rec")
                    name_row.pack_start(rec, False, False, 0)
                d_lbl = Gtk.Label(label=desc, xalign=0)
                d_lbl.get_style_context().add_class("fmt-sub")
                text_col.pack_start(name_row, False, False, 0)
                text_col.pack_start(d_lbl, False, False, 0)
                row.pack_start(dot, False, False, 0)
                row.pack_start(text_col, True, True, 0)
                btn.add(row)
                btn.connect("clicked", lambda _b, k=key: self._select_format(k))
                fmt_list.pack_start(btn, False, False, 0)
                self.fmt_buttons[key] = btn
                self.fmt_dots[key] = dot
                self.fmt_checks[key] = chk

            top_block.pack_start(fmt_list, False, False, 0)
            card.pack_start(top_block, False, False, 0)
            card.pack_start(self._make_divider(), False, False, 0)

            action_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            action_row.get_style_context().add_class("card-footer-row")
            distro_lbl = Gtk.Label(label=f"Sistema: {self.distro_label}", xalign=0)
            distro_lbl.get_style_context().add_class("distro-hint")
            distro_lbl.set_ellipsize(Pango.EllipsizeMode.END)
            self.install_btn = Gtk.Button(label="Instalar agora")
            self.install_btn.get_style_context().add_class("btn-primary")
            self.install_btn.connect("clicked", self._on_start_install)
            action_row.pack_start(distro_lbl, True, True, 0)
            action_row.pack_start(self.install_btn, False, False, 0)
            card.pack_start(action_row, False, False, 0)

            col.pack_start(card, False, False, 0)
            col.pack_start(self._build_support_links(), False, False, 0)
            self._select_format(self.selected_fmt)
            return shell

        def _build_install_page(self):
            shell, col = self._centered_shell()
            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.get_style_context().add_class("hero-icon-blue")
            icon_tile.pack_start(_svg_gtk3_image(SVG_DOWNLOAD, 22), True, True, 0)
            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            title = Gtk.Label(label="Instalando...", xalign=0)
            title.get_style_context().add_class("hero-title")
            sub = Gtk.Label(
                label="Aguarde alguns instantes enquanto baixamos e preparamos o Custódia no seu sistema.",
                xalign=0,
            )
            sub.set_line_wrap(True)
            sub.get_style_context().add_class("hero-subtitle")
            hero_text.pack_start(title, False, False, 0)
            hero_text.pack_start(sub, False, False, 0)
            hero.pack_start(icon_tile, False, False, 0)
            hero.pack_start(hero_text, True, True, 0)
            col.pack_start(hero, False, False, 0)

            source_lbl = Gtk.Label(label="De github.com/TundraLab/Custodia", xalign=0)
            source_lbl.get_style_context().add_class("source-mono")
            col.pack_start(source_lbl, False, False, 0)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.get_style_context().add_class("installer-card")
            prog_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            prog_block.get_style_context().add_class("card-section")

            self.progress_bar = Gtk.ProgressBar()
            self.progress_bar.get_style_context().add_class("slim-progress")
            prog_block.pack_start(self.progress_bar, False, False, 0)

            stats_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
            self.stats_pct = Gtk.Label(label="0%", xalign=0)
            self.stats_pct.get_style_context().add_class("stats-pct")
            self.stats_bytes = Gtk.Label(label="", xalign=0)
            self.stats_bytes.get_style_context().add_class("stats-mono")
            self.stats_speed = Gtk.Label(label="", xalign=1)
            self.stats_speed.get_style_context().add_class("stats-mono")
            stats_row.pack_start(self.stats_pct, False, False, 0)
            stats_row.pack_start(self.stats_bytes, True, True, 0)
            stats_row.pack_start(self.stats_speed, False, False, 0)
            prog_block.pack_start(stats_row, False, False, 0)
            card.pack_start(prog_block, False, False, 0)

            steps_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            steps_block.get_style_context().add_class("card-section-tight")
            self.step_widgets = []
            for label_text in STEPS:
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                row.get_style_context().add_class("step-row")
                icon_stack = Gtk.Stack()
                icon_stack.set_size_request(22, 22)
                pending_wrap = Gtk.Box()
                pending_wrap.set_halign(Gtk.Align.CENTER)
                pending_wrap.set_valign(Gtk.Align.CENTER)
                dot_p = Gtk.Box()
                dot_p.get_style_context().add_class("step-dot-pending")
                pending_wrap.add(dot_p)
                spinner = Gtk.Spinner()
                spinner.get_style_context().add_class("step-spinner")
                done_wrap = Gtk.Box()
                done_wrap.get_style_context().add_class("step-circle-done")
                done_wrap.add(_svg_gtk3_image(SVG_CHECK_BLUE, 12))
                icon_stack.add_named(pending_wrap, "pending")
                icon_stack.add_named(spinner, "active")
                icon_stack.add_named(done_wrap, "done")
                icon_stack.set_visible_child_name("pending")

                text_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                t_lbl = Gtk.Label(label=label_text, xalign=0)
                t_lbl.get_style_context().add_class("step-title-pending")
                sub_lbl = Gtk.Label(label="", xalign=0)
                sub_lbl.get_style_context().add_class("step-sub")
                text_box.pack_start(t_lbl, False, False, 0)
                text_box.pack_start(sub_lbl, False, False, 0)

                row.pack_start(icon_stack, False, False, 0)
                row.pack_start(text_box, True, True, 0)
                steps_block.pack_start(row, False, False, 0)
                self.step_widgets.append({
                    "row": row,
                    "icon_stack": icon_stack,
                    "spinner": spinner,
                    "title": t_lbl,
                    "sub": sub_lbl,
                })
            card.pack_start(steps_block, False, False, 0)

            self.error_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            self.error_box.get_style_context().add_class("error-box")
            self.error_box.set_margin_start(16)
            self.error_box.set_margin_end(16)
            self.error_box.set_margin_bottom(14)
            self.error_lbl = Gtk.Label(label="", xalign=0)
            self.error_lbl.set_line_wrap(True)
            err_actions = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            back_btn = Gtk.Button(label="Voltar")
            back_btn.get_style_context().add_class("btn-secondary")
            back_btn.connect("clicked", lambda *_: self.stack.set_visible_child_name("select"))
            retry_btn = Gtk.Button(label="Tentar novamente")
            retry_btn.get_style_context().add_class("btn-primary")
            retry_btn.connect("clicked", self._on_start_install)
            err_actions.pack_start(back_btn, False, False, 0)
            err_actions.pack_start(retry_btn, False, False, 0)
            self.error_box.pack_start(self.error_lbl, False, False, 0)
            self.error_box.pack_start(err_actions, False, False, 0)
            card.pack_start(self.error_box, False, False, 0)

            self.log_revealer = Gtk.Revealer()
            self.log_revealer.set_transition_type(Gtk.RevealerTransitionType.SLIDE_DOWN)
            self.log_revealer.set_reveal_child(False)
            log_wrap = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
            log_wrap.set_margin_start(16)
            log_wrap.set_margin_end(16)
            log_wrap.set_margin_bottom(14)
            log_scroll = Gtk.ScrolledWindow()
            log_scroll.set_min_content_height(108)
            self.log_view = Gtk.TextView()
            self.log_view.set_editable(False)
            self.log_view.set_cursor_visible(False)
            self.log_view.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
            self.log_view.get_style_context().add_class("log-box")
            self.log_buffer = self.log_view.get_buffer()
            log_scroll.add(self.log_view)
            log_wrap.add(log_scroll)
            self.log_revealer.add(log_wrap)
            card.pack_start(self.log_revealer, False, False, 0)

            card.pack_start(self._make_divider(), False, False, 0)

            footer = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            footer.get_style_context().add_class("card-footer-row")
            self.details_btn = Gtk.Button()
            self.details_btn.get_style_context().add_class("btn-ghost")
            det_inner = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
            self.chevron_stack = Gtk.Stack()
            self.chevron_stack.add_named(_svg_gtk3_image(SVG_CHEVRON_DOWN, 13), "down")
            self.chevron_stack.add_named(_svg_gtk3_image(SVG_CHEVRON_UP, 13), "up")
            self.chevron_stack.set_visible_child_name("down")
            det_inner.pack_start(self.chevron_stack, False, False, 0)
            det_inner.pack_start(Gtk.Label(label="Detalhes"), False, False, 0)
            self.details_btn.add(det_inner)
            self.details_btn.connect("clicked", self._toggle_details)

            self.elapsed_lbl = Gtk.Label(label="0 s decorridos")
            self.elapsed_lbl.get_style_context().add_class("stats-mono")
            self.cancel_btn = Gtk.Button(label="Cancelar")
            self.cancel_btn.get_style_context().add_class("btn-cancel")
            self.cancel_btn.connect("clicked", self._on_cancel)

            footer.pack_start(self.details_btn, False, False, 0)
            footer.pack_end(self.cancel_btn, False, False, 0)
            footer.pack_end(self.elapsed_lbl, False, False, 0)
            card.pack_start(footer, False, False, 0)

            col.pack_start(card, False, False, 0)
            col.pack_start(self._build_support_links(), False, False, 0)
            return shell

        def _build_done_page(self):
            shell, col = self._centered_shell()
            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.get_style_context().add_class("hero-icon-green")
            icon_tile.pack_start(_svg_gtk3_image(SVG_CHECK_GREEN, 22), True, True, 0)
            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            title = Gtk.Label(label="Custódia instalado!", xalign=0)
            title.get_style_context().add_class("hero-title")
            self.done_sub = Gtk.Label(
                label="Tudo pronto para usar. O arquivo do instalador foi removido automaticamente.",
                xalign=0,
            )
            self.done_sub.set_line_wrap(True)
            self.done_sub.get_style_context().add_class("hero-subtitle")
            hero_text.pack_start(title, False, False, 0)
            hero_text.pack_start(self.done_sub, False, False, 0)
            hero.pack_start(icon_tile, False, False, 0)
            hero.pack_start(hero_text, True, True, 0)
            col.pack_start(hero, False, False, 0)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.get_style_context().add_class("installer-card")
            sbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            sbox.get_style_context().add_class("card-section")
            self.done_summary = Gtk.Label(label="", xalign=0)
            self.done_summary.set_line_wrap(True)
            self.done_summary.get_style_context().add_class("fmt-sub")
            sbox.pack_start(self.done_summary, False, False, 0)
            card.pack_start(sbox, False, False, 0)
            card.pack_start(self._make_divider(), False, False, 0)

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            btn_row.get_style_context().add_class("card-footer-row")
            open_btn = Gtk.Button(label="Abrir Custódia")
            open_btn.get_style_context().add_class("btn-primary")
            open_btn.connect("clicked", self._on_open_and_close)
            close_btn = Gtk.Button(label="Fechar")
            close_btn.get_style_context().add_class("btn-secondary")
            close_btn.connect("clicked", lambda *_: Gtk.main_quit())
            btn_row.pack_end(open_btn, False, False, 0)
            btn_row.pack_end(close_btn, False, False, 0)
            card.pack_start(btn_row, False, False, 0)

            col.pack_start(card, False, False, 0)
            col.pack_start(self._build_support_links(), False, False, 0)
            return shell

        def _toggle_details(self, *_args) -> None:
            shown = not self.log_revealer.get_reveal_child()
            self.log_revealer.set_reveal_child(shown)
            self.chevron_stack.set_visible_child_name("up" if shown else "down")

        def _select_format(self, fmt_key: str) -> None:
            self.selected_fmt = fmt_key
            for k, btn in self.fmt_buttons.items():
                dot_ctx = self.fmt_dots[k].get_style_context()
                btn_ctx = btn.get_style_context()
                chk = self.fmt_checks[k]
                if k == fmt_key:
                    btn_ctx.add_class("selected")
                    dot_ctx.add_class("selected")
                    chk.set_visible(True)
                else:
                    btn_ctx.remove_class("selected")
                    dot_ctx.remove_class("selected")
                    chk.set_visible(False)

        def _load_releases_bg(self) -> None:
            rels = fetch_releases()
            GLib.idle_add(self._apply_releases, rels)

        def _apply_releases(self, rels: list[dict]) -> bool:
            self.releases = rels
            self.ver_combo.remove_all()
            for idx, rel in enumerate(rels):
                suffix = " (Mais recente)" if idx == 0 else ""
                self.ver_combo.append_text(f"{rel['tag']}{suffix}")
            self.ver_combo.set_active(0)
            self._on_release_changed()
            return False

        def _selected_release(self) -> dict:
            idx = int(self.ver_combo.get_active())
            if 0 <= idx < len(self.releases):
                return self.releases[idx]
            return self.releases[0]

        def _on_release_changed(self, *_args) -> None:
            rel = self._selected_release()
            self.topbar_ver.set_text(rel["tag"])
            assets = rel.get("assets") or {}
            for k, btn in self.fmt_buttons.items():
                available = k in assets
                btn.set_sensitive(available)
                btn.set_tooltip_text("" if available else f"Formato indisponível na versão {rel['tag']}")
            if self.selected_fmt not in assets:
                for k in (self.recommended_fmt, "deb", "rpm", "appimage"):
                    if k in assets:
                        self._select_format(k)
                        break

        def _append_log(self, msg: str) -> None:
            self.all_log_lines.append(msg)

            def _ui() -> bool:
                end = self.log_buffer.get_end_iter()
                self.log_buffer.insert(end, msg + "\n")
                mark = self.log_buffer.create_mark(None, self.log_buffer.get_end_iter(), False)
                self.log_view.scroll_to_mark(mark, 0.0, False, 0.0, 0.0)
                return False

            GLib.idle_add(_ui)

        def _set_step(self, active_idx: int, sub_text: str = "") -> None:
            def _ui() -> bool:
                for i, w in enumerate(self.step_widgets):
                    row_ctx = w["row"].get_style_context()
                    t_ctx = w["title"].get_style_context()
                    icon_stack = w["icon_stack"]
                    spinner = w["spinner"]
                    sub = w["sub"]

                    row_ctx.remove_class("active")
                    t_ctx.remove_class("step-title-done")
                    t_ctx.remove_class("step-title-active")
                    t_ctx.remove_class("step-title-pending")

                    if i < active_idx:
                        icon_stack.set_visible_child_name("done")
                        spinner.stop()
                        t_ctx.add_class("step-title-done")
                        sub.set_visible(False)
                    elif i == active_idx:
                        row_ctx.add_class("active")
                        icon_stack.set_visible_child_name("active")
                        spinner.start()
                        t_ctx.add_class("step-title-active")
                        if sub_text:
                            sub.set_text(sub_text)
                            sub.set_visible(True)
                        else:
                            sub.set_visible(False)
                    else:
                        icon_stack.set_visible_child_name("pending")
                        spinner.stop()
                        t_ctx.add_class("step-title-pending")
                        sub.set_visible(False)
                return False

            GLib.idle_add(_ui)

        def _set_step_sub(self, idx: int, sub_text: str) -> None:
            def _ui() -> bool:
                if 0 <= idx < len(self.step_widgets) and sub_text:
                    sub = self.step_widgets[idx]["sub"]
                    sub.set_text(sub_text)
                    sub.set_visible(True)
                return False

            GLib.idle_add(_ui)

        def _set_progress(self, fraction: float, bytes_text: str = "", speed_text: str | None = None) -> None:
            def _ui() -> bool:
                clamped = max(0.0, min(1.0, fraction))
                self.progress_bar.set_fraction(clamped)
                self.stats_pct.set_text(f"{int(round(clamped * 100))}%")
                if bytes_text:
                    self.stats_bytes.set_text(f"·  {bytes_text}")
                if speed_text is not None:
                    self.stats_speed.set_text(speed_text)
                return False

            GLib.idle_add(_ui)

        def _stop_timer(self) -> None:
            if self.timer_source_id is not None:
                GLib.source_remove(self.timer_source_id)
                self.timer_source_id = None

        def _tick_elapsed(self) -> bool:
            if not self.installing:
                self.timer_source_id = None
                return False
            secs = int(time.monotonic() - self.install_started_at)
            self.elapsed_lbl.set_text(f"{secs} s decorridos")
            return True

        def _on_cancel(self, *_args) -> None:
            if self.installing:
                self._confirm_cancel(quit_after=False)
            else:
                self.stack.set_visible_child_name("select")

        def _on_start_install(self, *_args) -> None:
            rel = self._selected_release()
            fmt = self.selected_fmt
            asset = (rel.get("assets") or {}).get(fmt)
            if not asset:
                return
            self._stop_timer()
            self.cancel_event.clear()
            self.installing = True
            self.install_started_at = time.monotonic()
            self.all_log_lines.clear()
            self.error_box.set_visible(False)
            self.cancel_btn.set_sensitive(True)
            self.log_buffer.set_text("")
            self.stats_pct.set_text("0%")
            self.stats_bytes.set_text("")
            self.stats_speed.set_text("")
            self.progress_bar.set_fraction(0.0)
            self.elapsed_lbl.set_text("0 s decorridos")
            fmt_disp = ".AppImage" if fmt == "appimage" else f".{fmt}"
            self.step_widgets[1]["title"].set_text(f"Baixando Custódia {rel['tag']} ({fmt_disp})")
            self.stack.set_visible_child_name("install")
            self.timer_source_id = GLib.timeout_add(1000, self._tick_elapsed)

            threading.Thread(
                target=self._install_worker,
                args=(rel, fmt, asset),
                daemon=True,
            ).start()

        def _install_worker(self, rel: dict, fmt: str, asset: dict) -> None:
            tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-install-"))
            os.chmod(tmp_dir, 0o755)
            try:
                self._set_step(0, self.distro_label)
                self._set_progress(0.04)
                self._append_log(f"Sistema detectado: {self.distro_label}")
                stop_running_custodia()

                pkg_path = tmp_dir / asset["name"]
                self._set_step(1, asset["name"])
                self._append_log(f"Baixando {asset['url']}")

                def on_dl(done: int, total: int, speed: float, eta: float) -> None:
                    frac = 0.08 + 0.64 * (done / total) if total > 0 else 0.35
                    b_txt = f"{format_bytes(done)} / {format_bytes(total)}" if total > 0 else format_bytes(done)
                    spd = f"{speed / (1024 * 1024):.1f} MB/s".replace(".", ",")
                    eta_s = format_eta(eta)
                    s_txt = f"{spd} · {eta_s}" if eta_s else spd
                    self._set_progress(frac, b_txt, s_txt)

                download_file(asset["url"], pkg_path, on_dl, self.cancel_event, int(asset.get("size") or 0))
                os.chmod(pkg_path, 0o644)

                self._set_step(2, "Conferindo SHA256SUMS.txt")
                self._set_progress(0.76, speed_text="Validando...")
                sums_asset = (rel.get("assets") or {}).get("sums")
                verify_sha256(pkg_path, sums_asset["url"] if sums_asset else None, asset["name"], self._append_log)

                self._set_step(3, "Aplicando pacote no sistema")
                self._set_progress(0.86, speed_text="Instalando...")
                on_sub = lambda text: self._set_step_sub(3, text)
                if fmt == "deb":
                    install_deb(pkg_path, self._append_log, on_sub)
                elif fmt == "rpm":
                    install_rpm(pkg_path, self._append_log, on_sub)
                else:
                    install_appimage(pkg_path, self._append_log, on_sub)

                self._set_step(4, "Removendo arquivo do instalador")
                self._set_progress(0.96, speed_text="Finalizando...")
                deleted = self_delete_installer(self._append_log)

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
            self._stop_timer()
            fmt_name = ".AppImage" if fmt == "appimage" else f".{fmt}"
            if deleted:
                self.done_sub.set_text("Tudo pronto para usar. O arquivo do instalador foi removido automaticamente.")
            else:
                self.done_sub.set_text("Tudo pronto. O Custódia já está disponível no menu de aplicativos.")
            self.done_summary.set_text(f"Versão instalada: Custódia {tag} ({fmt_name}) no {self.distro_label}.")
            self.stack.set_visible_child_name("done")
            return False

        def _show_error(self, message: str) -> bool:
            self._stop_timer()
            self.error_lbl.set_text(message)
            self.error_box.set_visible(True)
            self.cancel_btn.set_sensitive(True)
            for w in self.step_widgets:
                w["spinner"].stop()
            return False

        def _on_open_and_close(self, *_args) -> None:
            launch_custodia()
            Gtk.main_quit()

    CustodiaInstallerGtk3()
    Gtk.main()
    return True


def _show_no_gtk_dialog() -> None:
    msg = (
        "Não foi possível abrir a interface gráfica (python3-gi / GTK 3 ou GTK 4 ausente).\n\n"
        "Você pode instalar python3-gi ou baixar diretamente o pacote .deb / .rpm em:\n"
        f"{RELEASES_URL}"
    )
    if shutil.which("zenity"):
        subprocess.run(["zenity", "--info", "--title=Custódia", f"--text={msg}"], check=False)
    elif shutil.which("kdialog"):
        subprocess.run(["kdialog", "--msgbox", msg, "--title", "Custódia"], check=False)


def run_cli_fallback() -> None:
    print("== Instalador do Custódia ==")
    rels = fetch_releases()
    rec_fmt = detect_default_format()
    print(f"Sistema detectado: {detect_distro_label()} (recomendado: .{rec_fmt})\n")

    for i, r in enumerate(rels[:5], 1):
        print(f"  {i}) {r['tag']}")
    choice = input("Versão [1]: ").strip()
    rel = rels[int(choice) - 1] if choice.isdigit() and 1 <= int(choice) <= len(rels[:5]) else rels[0]

    avail = [k for k, _, _ in FORMATS if k in rel["assets"]]
    default_fmt = rec_fmt if rec_fmt in avail else avail[0]
    fmt = input(f"Formato ({'/'.join(avail)}) [{default_fmt}]: ").strip().lower() or default_fmt
    if fmt not in rel["assets"]:
        fmt = default_fmt

    asset = rel["assets"][fmt]
    tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-install-"))
    os.chmod(tmp_dir, 0o755)
    try:
        pkg = tmp_dir / asset["name"]
        print(f"Baixando {asset['name']}...")
        download_file(asset["url"], pkg, lambda *_: None, threading.Event(), int(asset.get("size") or 0))
        os.chmod(pkg, 0o644)
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
        if run_gtk3():
            return 0
    if sys.stdin is not None and sys.stdin.isatty():
        run_cli_fallback()
        return 0
    _show_no_gtk_dialog()
    return 1


if __name__ == "__main__":
    sys.exit(main())
