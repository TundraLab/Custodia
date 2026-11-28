#!/usr/bin/env python3
"""
Lightweight native graphical installer for Custodia by TundraLab.
Provides a full-window Material Design 3 interface with the site's hero
backdrop, live GitHub releases and changelog viewer, distro-recommended package
selection (.deb, .rpm, or .AppImage), upfront admin authentication, smooth
progress feedback, and automatic self-deletion upon completion.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
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
    ("deb", "Ubuntu, Debian e Mint", "Pacote nativo (.deb) integrado ao sistema"),
    ("rpm", "Fedora, RHEL e openSUSE", "Pacote nativo (.rpm) integrado ao sistema"),
    ("appimage", "Qualquer outro Linux", "Versão portátil em arquivo único"),
]

STEPS = [
    "Preparando a instalação",
    "Baixando o Custódia",
    "Conferindo segurança do arquivo",
    "Instalando no computador",
    "Finalizando e limpando",
]

# Full-bleed hero backdrop echoing the official website hero section.
SVG_HERO_BACKDROP = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800" viewBox="0 0 1200 800" preserveAspectRatio="xMidYMid slice">'
    '<defs>'
    '<radialGradient id="g1" cx="15%" cy="15%" r="60%">'
    '<stop offset="0%" stop-color="#A8C7FA" stop-opacity="0.16"/>'
    '<stop offset="100%" stop-color="#111318" stop-opacity="0"/>'
    '</radialGradient>'
    '<radialGradient id="g2" cx="88%" cy="35%" r="65%">'
    '<stop offset="0%" stop-color="#374955" stop-opacity="0.48"/>'
    '<stop offset="100%" stop-color="#111318" stop-opacity="0"/>'
    '</radialGradient>'
    '<linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">'
    '<stop offset="0%" stop-color="#171A1F"/>'
    '<stop offset="100%" stop-color="#111318"/>'
    '</linearGradient>'
    '</defs>'
    '<rect width="1200" height="800" fill="url(#bg)"/>'
    '<rect width="1200" height="800" fill="url(#g1)"/>'
    '<rect width="1200" height="800" fill="url(#g2)"/>'
    '<g transform="translate(760,-60) rotate(-8 260 260)">'
    '<path fill="#A8C7FA" fill-opacity="0.045" d="M260 56L420 132V286L260 464L100 286V132Z"/>'
    '<path fill="none" stroke="#A8C7FA" stroke-opacity="0.13" stroke-width="5" stroke-linejoin="round" d="M260 56L420 132V286L260 464L100 286V132Z"/>'
    '<path fill="none" stroke="#A8C7FA" stroke-opacity="0.11" stroke-width="5" stroke-linecap="round" stroke-linejoin="round" d="M234 247A43 43 0 1 1 286 247L304 342H216Z"/>'
    '</g>'
    '<g transform="translate(-70,410)">'
    '<path fill="#A8C7FA" fill-opacity="0.04" d="M220 55L55 220L220 385L257 348L128 220L257 92Z"/>'
    '<path fill="#A8C7FA" fill-opacity="0.065" d="M385 150L308 73L161 220L308 367L385 290L348 253L308 293L235 220L308 147L348 187Z"/>'
    '</g>'
    '</svg>'
)

SVG_DOWNLOAD = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="22" height="22" viewBox="0 0 24 24" fill="none">'
    '<path d="M12 4v11m0 0-4-4m4 4 4-4M4.5 16.5v2a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-2" '
    'stroke="#A8C7FA" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHECK_SUCCESS = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none">'
    '<path d="M5 13l4 4L19 7" stroke="#6DD58C" stroke-width="2.6" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHECK_ON_PRIMARY = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="11" height="11" viewBox="0 0 24 24" fill="none">'
    '<path d="M5 13l4 4L19 7" stroke="#062E6F" stroke-width="3" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHECK_GREEN_LG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none">'
    '<path d="M5 13l4 4L19 7" stroke="#6DD58C" stroke-width="2.4" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_WARNING = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none">'
    '<path d="M12 9v4m0 4h.01M10.3 3.9 2.7 17.1A1.9 1.9 0 0 0 4.4 20h15.2a1.9 1.9 0 0 0 1.7-2.9L13.7 3.9a1.9 1.9 0 0 0-3.4 0Z" '
    'stroke="#F5C26B" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_BELL = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24" fill="none">'
    '<path d="M15 18H5.5a1 1 0 0 1-.8-1.6l1.3-1.7V10a6 6 0 1 1 12 0v4.7l1.3 1.7a1 1 0 0 1-.8 1.6H15Zm-5 0a2 2 0 0 0 4 0" '
    'stroke="#C4C6D0" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CLOSE = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none">'
    '<path d="M6 6l12 12M18 6L6 18" stroke="#C4C6D0" stroke-width="2.2" stroke-linecap="round"/>'
    '</svg>'
)

SVG_CHEVRON_DOWN = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none">'
    '<path d="M6 9l6 6 6-6" stroke="#C4C6D0" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_CHEVRON_UP = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none">'
    '<path d="M18 15l-6-6-6 6" stroke="#C4C6D0" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>'
    '</svg>'
)

SVG_BRAND_FALLBACK = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 256 256">'
    '<rect width="256" height="256" rx="56" fill="#0B1933"/>'
    '<path fill="#A8C7FA" d="M128 38 L200 162 L176 144 L128 76 L80 144 L56 162 Z"/>'
    '<path fill="#7FCFFF" d="M128 118 L216 212 L184 198 L128 148 L72 198 L40 212 Z"/>'
    '</svg>'
)

# Privileged helper loop executed once via pkexec to avoid prompting twice.
PRIVILEGED_HELPER_CODE = r"""
import json, os, shutil, subprocess, sys

def emit(line):
    sys.stdout.write(line + "\n")
    sys.stdout.flush()

def run_cmd(cmd):
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
    for line in p.stdout:
        s = line.strip()
        if s:
            emit("OUT:" + s)
    return p.wait()

def ver_rpm(args):
    r = subprocess.run(["rpm"] + args, capture_output=True, text=True, check=False)
    return r.stdout.strip().splitlines()[0].strip() if r.returncode == 0 and r.stdout.strip() else ""

def parse_evr(s):
    out, cur = [], ""
    for ch in s:
        if ch.isdigit():
            cur += ch
        elif cur:
            out.append(int(cur))
            cur = ""
    if cur:
        out.append(int(cur))
    return tuple(out)

emit("READY")
for raw in sys.stdin:
    raw = raw.strip()
    if not raw:
        continue
    try:
        req = json.loads(raw)
        action = req.get("action")
        path = req.get("path", "")
        if action in ("install_deb", "install_rpm") and not os.path.isfile(path):
            emit("ERR:Arquivo do pacote não encontrado.")
            emit("DONE:1")
            continue
        if action == "install_deb":
            if shutil.which("apt-get"):
                rc = run_cmd(["apt-get", "install", "-y", "--reinstall", "--allow-downgrades", path])
            elif shutil.which("dpkg"):
                rc = run_cmd(["dpkg", "-i", path])
            else:
                emit("ERR:Gerenciador de pacotes não encontrado.")
                rc = 1
            emit(f"DONE:{rc}")
        elif action == "install_rpm":
            if shutil.which("dnf"):
                inst = ver_rpm(["-q", "--qf", "%{VERSION}-%{RELEASE}\\n", "custodia"])
                tgt = ver_rpm(["-qp", "--qf", "%{VERSION}-%{RELEASE}\\n", path])
                if inst and tgt and parse_evr(tgt) == parse_evr(inst):
                    rc = run_cmd(["dnf", "reinstall", "-y", "--nogpgcheck", path])
                elif inst and tgt and parse_evr(tgt) < parse_evr(inst):
                    rc = run_cmd(["dnf", "install", "-y", "--nogpgcheck", "--allow-downgrade", path])
                    if rc != 0:
                        rc = run_cmd(["dnf", "downgrade", "-y", "--nogpgcheck", path])
                else:
                    rc = run_cmd(["dnf", "install", "-y", "--nogpgcheck", path])
            elif shutil.which("zypper"):
                rc = run_cmd(["zypper", "--non-interactive", "--no-gpg-checks", "install", "--force", path])
            elif shutil.which("rpm"):
                rc = run_cmd(["rpm", "-Uvh", "--replacepkgs", "--oldpackage", "--nosignature", path])
            else:
                emit("ERR:Gerenciador de pacotes não encontrado.")
                rc = 1
            emit(f"DONE:{rc}")
        elif action == "uninstall_deb":
            if shutil.which("apt-get"):
                rc = run_cmd(["apt-get", "remove", "-y", "custodia"])
            elif shutil.which("dpkg"):
                rc = run_cmd(["dpkg", "-r", "custodia"])
            else:
                emit("ERR:Gerenciador de pacotes não encontrado.")
                rc = 1
            emit(f"DONE:{rc}")
        elif action == "uninstall_rpm":
            if shutil.which("dnf"):
                rc = run_cmd(["dnf", "remove", "-y", "custodia"])
            elif shutil.which("zypper"):
                rc = run_cmd(["zypper", "--non-interactive", "remove", "custodia"])
            elif shutil.which("rpm"):
                rc = run_cmd(["rpm", "-e", "custodia"])
            else:
                emit("ERR:Gerenciador de pacotes não encontrado.")
                rc = 1
            emit(f"DONE:{rc}")
        else:
            emit("DONE:1")
    except Exception as exc:
        emit("ERR:" + str(exc))
        emit("DONE:1")
"""


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
    return info.get("PRETTY_NAME") or info.get("NAME") or "Linux"


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
        "title": f"Custódia v{v}",
        "published_at": "",
        "url": f"https://github.com/{REPO}/releases/tag/v{v}",
        "body": (
            "### Novidades da versão\n"
            "- Novo instalador gráfico leve com escolha de versão e formato.\n"
            "- Suporte nativo para Ubuntu, Debian, Pop!_OS e Mint (.deb), além de Fedora (.rpm) e portátil.\n"
            "- Aba Compartilhados comigo, criação de links e convites por e-mail.\n"
            "- Retomada de transferências grandes, filtros na fila e salvamento automático nas Configurações."
        ),
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
                "title": str(rel.get("name") or tag).strip(),
                "published_at": str(rel.get("published_at") or "")[:10],
                "url": str(rel.get("html_url") or RELEASES_URL),
                "body": str(rel.get("body") or "").strip(),
                "assets": assets_map,
            })

    return parsed if parsed else [build_fallback_release()]


def format_release_notes_plain(body: str) -> str:
    if not body:
        return "Nenhuma nota de lançamento disponível para esta versão."
    lines: list[str] = []
    for raw_line in body.splitlines():
        line = raw_line.strip()
        if not line:
            if lines and lines[-1] != "":
                lines.append("")
            continue
        line = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", line)
        line = line.replace("**", "").replace("`", "")
        if line.startswith("### "):
            if lines and lines[-1] != "":
                lines.append("")
            lines.append(line[4:].upper())
        elif line.startswith("## "):
            if lines and lines[-1] != "":
                lines.append("")
            lines.append(line[3:].upper())
        elif line.startswith("- ") or line.startswith("* "):
            lines.append("• " + line[2:])
        else:
            lines.append(line)
    return "\n".join(lines).strip()


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


def _humanize_pkg_status(raw_line: str) -> str | None:
    low = raw_line.lower()
    if any(k in low for k in ("preparando", "preparing", "transação", "transaction", "reading database")):
        return "Preparando arquivos do aplicativo..."
    if any(k in low for k in ("unpacking", "desempacotando", "installing", "instalando", "reinstalling", "upgrading", "atualizando")):
        return "Copiando arquivos para o sistema..."
    if any(k in low for k in ("setting up", "configurando", "verificando", "verifying", "running scriptlet", "triggers")):
        return "Configurando atalhos e integração..."
    if any(k in low for k in ("complete", "concluído", "done")):
        return "Finalizando instalação..."
    return None


def save_install_log(log_text: str) -> Path:
    downloads_dir = Path.home() / "Downloads"
    target_dir = downloads_dir if downloads_dir.is_dir() else Path.home()
    out_path = target_dir / "custodia-instalacao-log.txt"
    header = (
        f"Registro de Instalação - Custódia\n"
        f"Sistema: {detect_distro_label()}\n"
        f"{'-' * 48}\n"
    )
    body = log_text.strip() or "(nenhum registro gravado até o momento)"
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
        log("Lista de verificação não disponível para esta versão; prosseguindo.")
        return

    req = urllib.request.Request(sums_url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            content = resp.read().decode("utf-8", errors="replace")
    except Exception as exc:
        log(f"Aviso: não foi possível baixar a verificação de segurança ({exc}).")
        return

    expected = ""
    for line in content.splitlines():
        parts = line.strip().split()
        if len(parts) >= 2 and parts[-1].lstrip("*") == asset_name:
            expected = parts[0].lower()
            break

    if not expected:
        log(f"Aviso: código de segurança de {asset_name} não encontrado.")
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
        raise RuntimeError("O arquivo baixado parece corrompido. Tente baixar novamente.")
    log(f"Arquivo íntegro e verificado ({actual[:12]}...).")


class PrivilegedSession:
    """Keeps a single pkexec root worker alive so the password is only asked once."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._proc: subprocess.Popen[str] | None = None
        self._ready = False
        self._cancelled = False

    def start_upfront(self) -> bool:
        with self._lock:
            if self._proc is not None and self._proc.poll() is None:
                return True
            if os.geteuid() == 0:
                self._proc = subprocess.Popen(
                    ["/usr/bin/env", "python3", "-u", "-c", PRIVILEGED_HELPER_CODE],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    errors="replace",
                )
            elif shutil.which("pkexec"):
                self._proc = subprocess.Popen(
                    ["pkexec", "/usr/bin/env", "python3", "-u", "-c", PRIVILEGED_HELPER_CODE],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    errors="replace",
                )
            else:
                return False

            assert self._proc.stdout is not None
            first = self._proc.stdout.readline().strip()
            if first == "READY":
                self._ready = True
                self._cancelled = False
                return True

            self.close()
            self._cancelled = True
            return False

    @property
    def is_ready(self) -> bool:
        return self._ready and self._proc is not None and self._proc.poll() is None

    def run_package_install(
        self,
        action: str,
        pkg_path: Path | None,
        log: Callable[[str], None],
        on_human_status: Callable[[str], None] | None = None,
    ) -> None:
        if not self.is_ready:
            ok = self.start_upfront()
            if not ok:
                raise RuntimeError("Permissão administrativa não concedida.")

        assert self._proc is not None and self._proc.stdin is not None and self._proc.stdout is not None
        payload = json.dumps({"action": action, "path": str(pkg_path) if pkg_path else ""}) + "\n"
        self._proc.stdin.write(payload)
        self._proc.stdin.flush()

        tail_errors: list[str] = []
        rc = 1
        while True:
            line = self._proc.stdout.readline()
            if not line:
                break
            clean = line.strip()
            if clean.startswith("DONE:"):
                try:
                    rc = int(clean.split(":", 1)[1])
                except ValueError:
                    rc = 1
                break
            if clean.startswith("ERR:"):
                msg = clean[4:]
                tail_errors.append(msg)
                log(msg)
            elif clean.startswith("OUT:"):
                raw_out = clean[4:]
                log(raw_out)
                tail_errors.append(raw_out)
                if len(tail_errors) > 10:
                    tail_errors.pop(0)
                human = _humanize_pkg_status(raw_out)
                if human and on_human_status is not None:
                    on_human_status(human)

        if rc != 0:
            detail = tail_errors[-1] if tail_errors else "erro na instalação"
            raise RuntimeError(f"Não foi possível concluir a instalação ({detail}).")

    def close(self) -> None:
        with self._lock:
            self._ready = False
            if self._proc is not None:
                try:
                    if self._proc.stdin:
                        self._proc.stdin.close()
                except Exception:
                    pass
                try:
                    self._proc.terminate()
                except Exception:
                    pass
                self._proc = None


def install_appimage(
    pkg_path: Path,
    log: Callable[[str], None],
    on_human_status: Callable[[str], None] | None = None,
) -> None:
    home = Path.home()
    install_dir = home / ".local" / "opt" / "custodia"
    bin_dir = home / ".local" / "bin"
    apps_dir = home / ".local" / "share" / "applications"
    autostart_dir = home / ".config" / "autostart"

    for d in (install_dir, bin_dir, apps_dir, autostart_dir):
        d.mkdir(parents=True, exist_ok=True)

    target_app = install_dir / "Custodia.AppImage"
    if on_human_status is not None:
        on_human_status("Copiando aplicativo para a pasta do usuário...")
    shutil.copy2(pkg_path, target_app)
    target_app.chmod(target_app.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    wrapper = bin_dir / "custodia"
    wrapper.write_text(
        f'#!/usr/bin/env bash\nexec "{target_app}" "$@"\n',
        encoding="utf-8",
    )
    wrapper.chmod(wrapper.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    if on_human_status is not None:
        on_human_status("Configurando atalhos e inicialização...")
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
    log(f"Aplicativo instalado em {target_app}")


def parse_semver_tuple(raw: str | None) -> tuple[int, ...]:
    if not raw:
        return ()
    cleaned = raw.strip().lstrip("vV").split("-", 1)[0]
    parts: list[int] = []
    for seg in cleaned.split("."):
        digits = "".join(ch for ch in seg if ch.isdigit())
        if digits:
            parts.append(int(digits))
    return tuple(parts)


def detect_installed_custodia() -> dict | None:
    if shutil.which("rpm"):
        res = subprocess.run(
            ["rpm", "-q", "--qf", "%{VERSION}\n", "custodia"],
            capture_output=True,
            text=True,
            check=False,
        )
        if res.returncode == 0 and res.stdout.strip():
            return {"format": "rpm", "version": res.stdout.strip().splitlines()[0].strip()}

    if shutil.which("dpkg-query"):
        res = subprocess.run(
            ["dpkg-query", "-W", "-f=${Status}\t${Version}\n", "custodia"],
            capture_output=True,
            text=True,
            check=False,
        )
        if res.returncode == 0 and "\t" in res.stdout:
            status_part, ver_part = res.stdout.strip().split("\t", 1)
            if "install ok installed" in status_part:
                return {"format": "deb", "version": ver_part.strip()}

    home = Path.home()
    local_app = home / ".local" / "opt" / "custodia" / "Custodia.AppImage"
    if local_app.is_file():
        return {"format": "appimage", "version": None}

    return None


def uninstall_appimage(
    log: Callable[[str], None],
    on_human_status: Callable[[str], None] | None = None,
) -> None:
    home = Path.home()
    if on_human_status is not None:
        on_human_status("Removendo arquivos e atalhos locais...")
    for target in (
        home / ".local" / "opt" / "custodia" / "Custodia.AppImage",
        home / ".local" / "bin" / "custodia",
        home / ".local" / "share" / "applications" / "custodia.desktop",
        home / ".config" / "autostart" / "custodia.desktop",
    ):
        target.unlink(missing_ok=True)
    app_dir = home / ".local" / "opt" / "custodia"
    if app_dir.is_dir():
        shutil.rmtree(app_dir, ignore_errors=True)
    apps_dir = home / ".local" / "share" / "applications"
    if shutil.which("update-desktop-database") and apps_dir.is_dir():
        subprocess.run(["update-desktop-database", str(apps_dir)], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    log("Aplicativo portátil e atalhos removidos do usuário.")


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
        log(f"Arquivo do instalador removido: {target_path.name}")
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


def _resolve_icon_search_dir() -> Path | None:
    here = Path(__file__).resolve()
    appdir_icons = here.parent.parent / "share" / "icons"
    if appdir_icons.is_dir():
        return appdir_icons
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
    color: #E2E2E9;
    font-family: Inter, "Cantarell", "Noto Sans", system-ui, sans-serif;
}

.hero-bg {
    background-image:
        radial-gradient(ellipse at 15% 15%, rgba(168, 199, 250, 0.14), rgba(168, 199, 250, 0) 60%),
        radial-gradient(ellipse at 85% 35%, rgba(55, 73, 85, 0.45), rgba(55, 73, 85, 0) 65%),
        linear-gradient(to bottom, #171A1F, #111318);
}

headerbar.installer-topbar {
    background-color: rgba(17, 19, 24, 0.92);
    background-image: none;
    border: none;
    border-bottom: 1px solid rgba(68, 71, 79, 0.45);
    box-shadow: none;
    padding: 0 20px;
    min-height: 48px;
}

.topbar-brand {
    font-weight: 700;
    font-size: 14px;
    color: #E2E2E9;
}

.topbar-version {
    font-family: "JetBrains Mono", monospace;
    font-size: 11.5px;
    font-weight: 600;
    background-color: #282A2F;
    color: #C4C6D0;
    border-radius: 999px;
    padding: 2px 10px;
    margin-left: 4px;
}

button.topbar-link {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    color: #C4C6D0;
    font-size: 12.5px;
    font-weight: 500;
    border-radius: 999px;
    padding: 5px 12px;
    margin-left: 4px;
}

button.topbar-link:hover {
    background-color: #282A2F;
    color: #A8C7FA;
}

button.topbar-icon-btn {
    background-color: #1D2024;
    background-image: none;
    border: none;
    box-shadow: none;
    border-radius: 999px;
    min-width: 30px;
    min-height: 30px;
    padding: 4px;
}

button.topbar-icon-btn:hover {
    background-color: #282A2F;
}

button.topbar-close-btn {
    background-color: #282A2F;
    background-image: none;
    border: none;
    box-shadow: none;
    border-radius: 999px;
    min-width: 30px;
    min-height: 30px;
    padding: 4px;
}

button.topbar-close-btn:hover {
    background-color: #33363D;
}

.hero-brand-sub {
    font-size: 12px;
    font-weight: 600;
    color: #A8C7FA;
}

.hero-title {
    font-size: 24px;
    font-weight: 700;
    color: #E2E2E9;
}

.hero-subtitle {
    font-size: 13.5px;
    color: #C4C6D0;
}

.hero-icon-blue {
    background-color: #374955;
    border-radius: 14px;
    min-width: 52px;
    min-height: 52px;
    padding: 0;
}

.hero-icon-green {
    background-color: #1B3927;
    border-radius: 14px;
    min-width: 52px;
    min-height: 52px;
    padding: 0;
}

.installer-card {
    background-color: rgba(29, 32, 36, 0.92);
    border: 1px solid rgba(68, 71, 79, 0.35);
    border-radius: 16px;
}

.card-section {
    padding: 20px 24px;
}

.card-section-tight {
    padding: 10px 18px 18px 18px;
}

.card-footer-row {
    padding: 14px 24px;
}

.h-divider {
    background-color: rgba(68, 71, 79, 0.45);
    min-height: 1px;
}

.support-divider {
    background-color: rgba(68, 71, 79, 0.35);
    min-height: 1px;
}

.field-label {
    font-size: 13px;
    font-weight: 600;
    color: #C4C6D0;
}

.warning-banner {
    background-color: #3B2E14;
    border: 1px solid rgba(245, 194, 107, 0.35);
    border-radius: 12px;
    padding: 11px 14px;
}

.warning-text {
    font-size: 12.5px;
    color: #FFE0A3;
}

button.fmt-row {
    background-color: #282A2F;
    background-image: none;
    border: 1.5px solid transparent;
    border-radius: 12px;
    padding: 13px 16px;
    box-shadow: none;
}

button.fmt-row:hover {
    background-color: #2F3238;
}

button.fmt-row.selected {
    background-color: #203149;
    border-color: #A8C7FA;
}

.fmt-title {
    font-size: 14px;
    font-weight: 700;
    color: #E2E2E9;
}

.fmt-sub {
    font-size: 12px;
    color: #C4C6D0;
}

.badge-rec {
    background-color: #374955;
    color: #D3E4F3;
    border-radius: 999px;
    padding: 2px 10px;
    font-size: 11px;
    font-weight: 600;
}

.badge-success-pill {
    background-color: #1B3927;
    color: #6DD58C;
    border-radius: 999px;
    padding: 3px 11px;
    font-size: 11.5px;
    font-weight: 600;
}

.fmt-dot {
    min-width: 20px;
    min-height: 20px;
    border-radius: 999px;
    border: 1.5px solid #8E9099;
    background-color: transparent;
}

.fmt-dot.selected {
    background-color: #A8C7FA;
    border-color: #A8C7FA;
}

.distro-hint {
    font-size: 12.5px;
    color: #C4C6D0;
}

button.btn-primary {
    background-color: #A8C7FA;
    background-image: none;
    color: #062E6F;
    border: none;
    border-radius: 20px;
    padding: 10px 24px;
    font-weight: 700;
    font-size: 13.5px;
    box-shadow: none;
}

button.btn-primary:hover {
    background-color: #B9D2FB;
}

button.btn-secondary {
    background-color: #374955;
    background-image: none;
    color: #D3E4F3;
    border: none;
    border-radius: 20px;
    padding: 9px 20px;
    font-weight: 600;
    font-size: 13px;
    box-shadow: none;
}

button.btn-secondary:hover {
    background-color: #425665;
}

button.btn-danger {
    background-color: #4A1C20;
    background-image: none;
    color: #FFB4AB;
    border: none;
    border-radius: 20px;
    padding: 9px 20px;
    font-weight: 600;
    font-size: 13px;
    box-shadow: none;
}

button.btn-danger:hover {
    background-color: #5B2328;
}

button.btn-danger-outlined {
    background-color: transparent;
    background-image: none;
    color: #FFB4AB;
    border: 1px solid rgba(255, 180, 171, 0.35);
    border-radius: 20px;
    padding: 9px 18px;
    font-weight: 600;
    font-size: 13px;
    box-shadow: none;
}

button.btn-danger-outlined:hover {
    background-color: #4A1C20;
    border-color: transparent;
}

.installed-banner {
    background-color: #16394B;
    border: none;
    border-radius: 12px;
    padding: 10px 14px;
    color: #C2E7FF;
    font-size: 12.5px;
    font-weight: 500;
}

button.btn-ghost {
    background-color: #282A2F;
    background-image: none;
    border: none;
    box-shadow: none;
    border-radius: 20px;
    color: #C4C6D0;
    font-size: 12.5px;
    font-weight: 600;
    padding: 6px 14px;
}

button.btn-ghost:hover {
    background-color: #33363D;
    color: #E2E2E9;
}

button.btn-cancel {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    border-radius: 20px;
    color: #FFB4AB;
    font-size: 13px;
    font-weight: 600;
    padding: 6px 14px;
}

button.btn-cancel:hover {
    background-color: #4A1C20;
}

progressbar.slim-progress trough {
    background-color: #33363D;
    background-image: none;
    border: none;
    border-radius: 999px;
    min-height: 8px;
}

progressbar.slim-progress progress {
    background-color: #A8C7FA;
    background-image: none;
    border: none;
    border-radius: 999px;
    min-height: 8px;
}

.stats-mono {
    font-family: "JetBrains Mono", monospace;
    font-size: 12px;
    color: #C4C6D0;
}

.stats-pct {
    font-family: "JetBrains Mono", monospace;
    font-size: 12.5px;
    font-weight: 700;
    color: #A8C7FA;
}

.step-row {
    padding: 10px 14px;
    border-radius: 12px;
}

.step-row.active {
    background-color: #282A2F;
    border-left: 3px solid #A8C7FA;
    padding: 12px 14px;
}

.step-circle-done {
    background-color: #1B3927;
    border-radius: 999px;
    min-width: 24px;
    min-height: 24px;
    padding: 0;
}

.step-dot-pending {
    background-color: #6B6F7B;
    border-radius: 999px;
    min-width: 6px;
    min-height: 6px;
}

spinner.step-spinner {
    color: #A8C7FA;
}

.step-title-done {
    font-size: 13.5px;
    color: #C4C6D0;
}

.step-title-active {
    font-size: 14px;
    font-weight: 600;
    color: #E2E2E9;
}

.step-title-pending {
    font-size: 13.5px;
    color: #9296A4;
}

.step-sub {
    font-size: 12px;
    color: #9296A4;
}

.done-item-row {
    background-color: #282A2F;
    border-radius: 12px;
    padding: 12px 16px;
}

.done-item-title {
    font-size: 13.5px;
    font-weight: 600;
    color: #E2E2E9;
}

.done-item-sub {
    font-size: 12px;
    color: #C4C6D0;
}

.log-box {
    background-color: #111318;
    border: none;
    border-radius: 12px;
    padding: 10px 12px;
    font-family: "JetBrains Mono", monospace;
    font-size: 11px;
    color: #C4C6D0;
}

.notes-view {
    background-color: #111318;
    border-radius: 12px;
    padding: 14px 16px;
    font-size: 13px;
    color: #E2E2E9;
}

.error-box {
    background-color: #4A1C20;
    border: none;
    border-radius: 12px;
    padding: 12px 14px;
    color: #FFDAD6;
    font-size: 12.5px;
}

button.footer-link {
    background: transparent;
    background-image: none;
    border: none;
    box-shadow: none;
    color: #9296A4;
    font-size: 12px;
    border-radius: 20px;
    padding: 3px 8px;
}

button.footer-link:hover {
    color: #A8C7FA;
}

.dialog-card {
    background-color: #1D2024;
    color: #E2E2E9;
    border-radius: 20px;
    padding: 22px;
}

.dialog-title {
    font-size: 17px;
    font-weight: 700;
    color: #E2E2E9;
}

.dialog-body {
    font-size: 13px;
    color: #C4C6D0;
}

popover.info-popover > contents {
    background-color: #282A2F;
    background-image: none;
    border: 1px solid #44474F;
    border-radius: 16px;
    color: #E2E2E9;
    padding: 14px 16px;
}
"""

GTK4_EXTRA_CSS = """
dropdown > button {
    background-color: #282A2F;
    background-image: none;
    border: 1px solid #44474F;
    border-radius: 12px;
    color: #E2E2E9;
    padding: 8px 14px;
}
"""

GTK3_EXTRA_CSS = """
combobox button {
    background-color: #282A2F;
    background-image: none;
    border: 1px solid #44474F;
    border-radius: 12px;
    color: #E2E2E9;
    padding: 6px 14px;
}
"""


def _load_css(provider, css_text: str) -> None:
    if hasattr(provider, "load_from_string"):
        provider.load_from_string(css_text)
    else:
        provider.load_from_data(css_text.encode("utf-8"))


def _svg_gtk4_image(svg_markup: str, size: int):
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

    widget.set_hexpand(False)
    widget.set_vexpand(False)
    widget.set_halign(Gtk.Align.CENTER)
    widget.set_valign(Gtk.Align.CENTER)
    return widget


def _fixed_badge_gtk4(svg_markup: str, svg_size: int, box_size: int, css_class: str):
    from gi.repository import Gtk  # type: ignore

    cb = Gtk.CenterBox()
    if css_class:
        cb.add_css_class(css_class)
    cb.set_size_request(box_size, box_size)
    cb.set_hexpand(False)
    cb.set_vexpand(False)
    cb.set_halign(Gtk.Align.START)
    cb.set_valign(Gtk.Align.CENTER)
    cb.set_center_widget(_svg_gtk4_image(svg_markup, svg_size))
    return cb


def _hero_backdrop_gtk4():
    from gi.repository import Gdk, GLib, Gtk  # type: ignore

    try:
        tex = Gdk.Texture.new_from_bytes(GLib.Bytes.new(SVG_HERO_BACKDROP.encode("utf-8")))
    except Exception:
        pb = _svg_to_pixbuf(SVG_HERO_BACKDROP, 1200, 800)
        _, png_bytes = pb.save_to_bufferv("png", [], [])
        tex = Gdk.Texture.new_from_bytes(GLib.Bytes.new(png_bytes))

    pic = Gtk.Picture.new_for_paintable(tex)
    if hasattr(Gtk, "ContentFit") and hasattr(pic, "set_content_fit"):
        pic.set_content_fit(Gtk.ContentFit.COVER)
    if hasattr(pic, "set_can_shrink"):
        pic.set_can_shrink(True)
    pic.set_hexpand(True)
    pic.set_vexpand(True)
    pic.set_can_target(False)
    return pic


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


def _left_label_gtk4(text: str, css_class: str, wrap: bool = False):
    from gi.repository import Gtk  # type: ignore

    lbl = Gtk.Label(label=text)
    lbl.set_xalign(0.0)
    lbl.set_halign(Gtk.Align.START)
    if wrap:
        lbl.set_wrap(True)
    lbl.add_css_class(css_class)
    return lbl


def run_gtk4() -> bool:
    try:
        import gi
        gi.require_version("Gdk", "4.0")
        gi.require_version("Gtk", "4.0")
        gi.require_version("GdkPixbuf", "2.0")
        from gi.repository import Gdk, Gio, GLib, Gtk, Pango  # type: ignore
    except Exception:
        return False

    class CustodiaInstallerApp(Gtk.Application):
        def __init__(self) -> None:
            super().__init__(
                application_id="com.tundralab.CustodiaInstaller",
                flags=Gio.ApplicationFlags.NON_UNIQUE,
            )
            self.releases: list[dict] = [build_fallback_release()]
            self.recommended_fmt = detect_default_format()
            self.installed_info = detect_installed_custodia()
            self.selected_fmt = (
                self.installed_info["format"]
                if self.installed_info is not None
                else self.recommended_fmt
            )
            self.distro_label = detect_distro_label()
            self.cancel_event = threading.Event()
            self.install_started_at = 0.0
            self.timer_source_id: int | None = None
            self.anim_source_id: int | None = None
            self.installing = False
            self.all_log_lines: list[str] = []
            self.target_fraction = 0.0
            self.display_fraction = 0.0
            self.priv_session = PrivilegedSession()

            self.fmt_buttons: dict[str, Gtk.Button] = {}
            self.fmt_dots: dict[str, Gtk.Box] = {}
            self.fmt_checks: dict[str, Gtk.Widget] = {}
            self.step_widgets: list[dict] = []
            self.save_log_buttons: list[Gtk.Button] = []

        def do_activate(self) -> None:
            display = Gdk.Display.get_default()
            settings = Gtk.Settings.get_default()
            if settings is not None:
                if hasattr(Gtk, "InterfaceColorScheme") and settings.find_property("gtk-interface-color-scheme"):
                    settings.set_property("gtk-interface-color-scheme", Gtk.InterfaceColorScheme.DARK)
                else:
                    settings.set_property("gtk-application-prefer-dark-theme", True)

            search_dir = _resolve_icon_search_dir()
            if search_dir is not None and display is not None:
                Gtk.IconTheme.get_for_display(display).add_search_path(str(search_dir))
            Gtk.Window.set_default_icon_name("custodia-installer")

            provider = Gtk.CssProvider()
            _load_css(provider, SHARED_CSS + GTK4_EXTRA_CSS)
            if display is not None:
                Gtk.StyleContext.add_provider_for_display(
                    display, provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION
                )

            self.win = Gtk.ApplicationWindow(application=self)
            self.win.set_title("Custódia")
            self.win.set_icon_name("custodia-installer")
            self.win.set_default_size(880, 640)
            self.win.set_resizable(True)
            self.win.add_css_class("installer-root")
            self.win.connect("close-request", self._on_window_close_request)

            header = Gtk.HeaderBar()
            header.add_css_class("installer-topbar")
            header.set_show_title_buttons(False)
            header.set_title_widget(Gtk.Box())

            brand_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            brand_box.set_valign(Gtk.Align.CENTER)
            brand_box.append(_app_icon_gtk4_image(22))

            brand_lbl = Gtk.Label(label="Custódia")
            brand_lbl.add_css_class("topbar-brand")
            self.topbar_ver = Gtk.Label(label=f"v{FALLBACK_VERSION}")
            self.topbar_ver.add_css_class("topbar-version")
            whats_new = Gtk.Button(label="O que há de novo")
            whats_new.add_css_class("topbar-link")
            whats_new.connect("clicked", self._show_whats_new_dialog)
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
            pop_box.append(_left_label_gtk4("Instalador oficial Custódia", "field-label"))
            pop_box.append(
                _left_label_gtk4(
                    f"Sistema: {self.distro_label}\nPacote indicado: .{self.recommended_fmt}\nVerificação automática de integridade",
                    "fmt-sub",
                )
            )
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
            self.stack.set_transition_type(Gtk.StackTransitionType.SLIDE_LEFT_RIGHT)
            self.stack.set_transition_duration(240)
            self.stack.set_vexpand(True)
            self.stack.set_hexpand(True)

            self.stack.add_named(self._build_select_page(), "select")
            self.stack.add_named(self._build_install_page(), "install")
            self.stack.add_named(self._build_done_page(), "done")
            self.stack.set_visible_child_name("select")

            # Overlay puts the full-bleed website hero backdrop behind every screen.
            overlay = Gtk.Overlay()
            overlay.add_css_class("hero-bg")
            overlay.set_child(_hero_backdrop_gtk4())
            overlay.add_overlay(self.stack)

            self.win.set_child(overlay)
            self.win.present()

            threading.Thread(target=self._load_releases_bg, daemon=True).start()
            if not os.environ.get("CUSTODIA_NO_EARLY_AUTH"):
                GLib.timeout_add(220, self._trigger_upfront_auth)

        def _trigger_upfront_auth(self) -> bool:
            threading.Thread(target=self.priv_session.start_upfront, daemon=True).start()
            return False

        def _on_window_close_request(self, *_args) -> bool:
            if self.installing:
                self._confirm_cancel(quit_after=True)
                return True
            self.priv_session.close()
            return False

        def _request_quit(self) -> None:
            if self.installing:
                self._confirm_cancel(quit_after=True)
            else:
                self.priv_session.close()
                self.quit()

        def _show_whats_new_dialog(self, *_args) -> None:
            rel = self._selected_release()
            dlg = Gtk.Window(transient_for=self.win, modal=True, title=f"Novidades: {rel['tag']}")
            dlg.set_default_size(580, 470)
            dlg.add_css_class("installer-root")

            box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
            box.add_css_class("dialog-card")

            hdr = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            hdr.append(_app_icon_gtk4_image(36))
            hdr_txt = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
            hdr_txt.set_hexpand(True)
            title_lbl = _left_label_gtk4(rel.get("title") or f"Custódia {rel['tag']}", "dialog-title")
            date_txt = f"Publicado em {rel['published_at']}" if rel.get("published_at") else "Lançamento oficial no GitHub"
            sub_lbl = _left_label_gtk4(date_txt, "distro-hint")
            hdr_txt.append(title_lbl)
            hdr_txt.append(sub_lbl)
            hdr.append(hdr_txt)
            box.append(hdr)

            scroll = Gtk.ScrolledWindow()
            scroll.set_vexpand(True)
            scroll.set_min_content_height(260)
            tv = Gtk.TextView()
            tv.set_editable(False)
            tv.set_cursor_visible(False)
            tv.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
            tv.add_css_class("notes-view")
            tv.get_buffer().set_text(format_release_notes_plain(rel.get("body", "")))
            scroll.set_child(tv)
            box.append(scroll)

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            gh_btn = Gtk.Button(label="Abrir no GitHub")
            gh_btn.add_css_class("btn-ghost")
            gh_btn.connect("clicked", lambda *_: webbrowser.open(rel.get("url") or RELEASES_URL))
            spacer = Gtk.Box()
            spacer.set_hexpand(True)
            close_btn = Gtk.Button(label="Fechar")
            close_btn.add_css_class("btn-primary")
            close_btn.connect("clicked", lambda *_: dlg.close())
            btn_row.append(gh_btn)
            btn_row.append(spacer)
            btn_row.append(close_btn)
            box.append(btn_row)

            dlg.set_child(box)
            dlg.present()

        def _confirm_cancel(self, quit_after: bool = False) -> None:
            dlg = Gtk.Window(transient_for=self.win, modal=True, title="Cancelar instalação")
            dlg.set_resizable(False)
            dlg.set_default_size(400, -1)
            dlg.add_css_class("installer-root")

            box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
            box.add_css_class("dialog-card")
            box.append(_left_label_gtk4("Cancelar a instalação agora?", "dialog-title"))
            box.append(_left_label_gtk4("O envio será interrompido e nada parcial ficará salvo.", "dialog-body", wrap=True))

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            btn_row.set_halign(Gtk.Align.END)
            btn_row.set_margin_top(8)

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
                    self.priv_session.close()
                    self.quit()

            confirm_btn.connect("clicked", _do_abort)
            btn_row.append(keep_btn)
            btn_row.append(confirm_btn)
            box.append(btn_row)
            dlg.set_child(box)
            dlg.present()

        def _full_shell(self) -> tuple[Gtk.ScrolledWindow, Gtk.Box]:
            scroll = Gtk.ScrolledWindow()
            scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
            scroll.set_hexpand(True)
            scroll.set_vexpand(True)

            col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=16)
            col.set_hexpand(True)
            col.set_vexpand(True)
            col.set_valign(Gtk.Align.CENTER)
            col.set_halign(Gtk.Align.FILL)
            col.set_margin_top(24)
            col.set_margin_bottom(22)
            col.set_margin_start(38)
            col.set_margin_end(38)

            scroll.set_child(col)
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
            wrap.set_hexpand(True)
            wrap.append(self._make_divider("support-divider"))

            row1 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
            row1.set_margin_top(4)

            save_btn = Gtk.Button(label="Baixar registro da instalação")
            save_btn.add_css_class("footer-link")
            save_btn.connect("clicked", self._on_save_log_clicked)
            self.save_log_buttons.append(save_btn)
            row1.append(save_btn)

            for label, url in (
                ("Guia de instalação", GUIDE_URL),
                ("Solução de problemas", TROUBLESHOOTING_URL),
                ("Relatar um problema", ISSUES_URL),
                ("Comunidade", DISCUSSIONS_URL),
            ):
                btn = Gtk.Button(label=label)
                btn.add_css_class("footer-link")
                btn.connect("clicked", lambda _b, u=url: webbrowser.open(u))
                row1.append(btn)

            wrap.append(row1)
            return wrap

        def _build_select_page(self) -> Gtk.Widget:
            shell, col = self._full_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            hero.set_hexpand(True)
            icon_img = _app_icon_gtk4_image(58)
            icon_img.set_valign(Gtk.Align.CENTER)

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            hero_text.set_hexpand(True)
            hero_text.set_valign(Gtk.Align.CENTER)
            hero_text.append(_left_label_gtk4("CUSTÓDIA BY TUNDRALAB  •  SEUS ARQUIVOS. SEMPRE SEGUROS.", "hero-brand-sub"))
            hero_text.append(_left_label_gtk4("Instalar no seu Linux", "hero-title"))
            hero_text.append(
                _left_label_gtk4(
                    "Já deixamos marcada a melhor opção para o seu computador. Ao terminar, o instalador se apaga sozinho.",
                    "hero-subtitle",
                    wrap=True,
                )
            )
            hero.append(icon_img)
            hero.append(hero_text)
            col.append(hero)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.add_css_class("installer-card")
            card.set_hexpand(True)

            top_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            top_block.add_css_class("card-section")

            ver_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            ver_lbl = _left_label_gtk4("Versão para instalar", "field-label")
            ver_lbl.set_hexpand(True)
            ver_lbl.set_valign(Gtk.Align.CENTER)
            self.ver_model = Gtk.StringList.new([f"v{FALLBACK_VERSION} (Mais recente)"])
            self.ver_dropdown = Gtk.DropDown(model=self.ver_model)
            self.ver_dropdown.set_selected(0)
            self.ver_dropdown.connect("notify::selected", self._on_release_changed)
            ver_row.append(ver_lbl)
            ver_row.append(self.ver_dropdown)
            top_block.append(ver_row)

            self.installed_banner = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            self.installed_banner.add_css_class("installed-banner")
            if self.installed_info is not None:
                inst_ver = f"v{self.installed_info['version']} " if self.installed_info.get("version") else ""
                inst_fmt = ".AppImage" if self.installed_info["format"] == "appimage" else f".{self.installed_info['format']}"
                banner_txt = f"Custódia {inst_ver}({inst_fmt}) instalado neste computador. Escolha Atualizar ou Desinstalar."
                self.installed_banner.set_visible(True)
            else:
                banner_txt = ""
                self.installed_banner.set_visible(False)
            self.installed_banner.append(_left_label_gtk4(banner_txt, "installed-banner", wrap=True))
            top_block.append(self.installed_banner)

            # Warning shown when user picks an older version instead of the latest.
            self.old_ver_banner = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            self.old_ver_banner.add_css_class("warning-banner")
            self.old_ver_banner.set_visible(False)
            warn_ico = _svg_gtk4_image(SVG_WARNING, 18)
            warn_ico.set_valign(Gtk.Align.CENTER)
            self.old_ver_lbl = _left_label_gtk4("", "warning-text", wrap=True)
            self.old_ver_lbl.set_hexpand(True)
            self.old_ver_banner.append(warn_ico)
            self.old_ver_banner.append(self.old_ver_lbl)
            top_block.append(self.old_ver_banner)

            top_block.append(_left_label_gtk4("Escolha o formato para o seu sistema", "field-label"))

            fmt_list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            fmt_list.set_hexpand(True)
            for key, name, desc in FORMATS:
                btn = Gtk.Button()
                btn.add_css_class("fmt-row")
                btn.set_hexpand(True)
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                row.set_valign(Gtk.Align.CENTER)

                dot = Gtk.CenterBox()
                dot.add_css_class("fmt-dot")
                dot.set_size_request(20, 20)
                dot.set_hexpand(False)
                dot.set_vexpand(False)
                dot.set_halign(Gtk.Align.START)
                dot.set_valign(Gtk.Align.CENTER)
                chk = _svg_gtk4_image(SVG_CHECK_ON_PRIMARY, 11)
                dot.set_center_widget(chk)

                text_col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                text_col.set_hexpand(True)
                name_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
                t_lbl = _left_label_gtk4(name, "fmt-title")
                name_row.append(t_lbl)
                if key == self.recommended_fmt:
                    rec = Gtk.Label(label="Recomendado")
                    rec.add_css_class("badge-rec")
                    name_row.append(rec)
                text_col.append(name_row)
                text_col.append(_left_label_gtk4(desc, "fmt-sub"))

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
            distro_lbl = _left_label_gtk4(f"Seu sistema: {self.distro_label}", "distro-hint")
            distro_lbl.set_hexpand(True)
            distro_lbl.set_ellipsize(Pango.EllipsizeMode.END)

            self.uninstall_btn = Gtk.Button(label="Desinstalar")
            self.uninstall_btn.add_css_class("btn-danger-outlined")
            self.uninstall_btn.connect("clicked", self._on_uninstall_clicked)
            self.uninstall_btn.set_visible(self.installed_info is not None)

            self.install_btn = Gtk.Button(label="Instalar agora")
            self.install_btn.add_css_class("btn-primary")
            self.install_btn.connect("clicked", self._on_start_install)

            action_row.append(distro_lbl)
            action_row.append(self.uninstall_btn)
            action_row.append(self.install_btn)
            card.append(action_row)

            col.append(card)
            col.append(self._build_support_links())
            self._select_format(self.selected_fmt)
            self._refresh_action_buttons(f"v{FALLBACK_VERSION}")
            return shell

        def _build_install_page(self) -> Gtk.Widget:
            shell, col = self._full_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            hero.set_hexpand(True)
            icon_tile = _fixed_badge_gtk4(SVG_DOWNLOAD, 22, 52, "hero-icon-blue")

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            hero_text.set_hexpand(True)
            hero_text.set_valign(Gtk.Align.CENTER)
            self.inst_title = _left_label_gtk4("Instalando o Custódia...", "hero-title")
            self.inst_subtitle = _left_label_gtk4(
                "Estamos baixando e preparando o aplicativo no seu computador.",
                "hero-subtitle",
                wrap=True,
            )
            hero_text.append(self.inst_title)
            hero_text.append(self.inst_subtitle)

            self.inst_target_pill = Gtk.Label(label=f"v{FALLBACK_VERSION}")
            self.inst_target_pill.add_css_class("badge-rec")
            self.inst_target_pill.set_valign(Gtk.Align.CENTER)
            self.inst_target_pill.set_hexpand(False)

            hero.append(icon_tile)
            hero.append(hero_text)
            hero.append(self.inst_target_pill)
            col.append(hero)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.add_css_class("installer-card")
            card.set_hexpand(True)

            prog_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            prog_block.add_css_class("card-section")

            self.progress_bar = Gtk.ProgressBar()
            self.progress_bar.add_css_class("slim-progress")
            self.progress_bar.set_fraction(0.0)
            prog_block.append(self.progress_bar)

            stats_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
            self.stats_pct = Gtk.Label(label="0%", xalign=0)
            self.stats_pct.set_halign(Gtk.Align.START)
            self.stats_pct.add_css_class("stats-pct")
            self.stats_bytes = Gtk.Label(label="", xalign=0)
            self.stats_bytes.set_halign(Gtk.Align.START)
            self.stats_bytes.add_css_class("stats-mono")
            self.stats_bytes.set_hexpand(True)
            self.stats_speed = Gtk.Label(label="", xalign=1)
            self.stats_speed.set_halign(Gtk.Align.END)
            self.stats_speed.add_css_class("stats-mono")
            stats_row.append(self.stats_pct)
            stats_row.append(self.stats_bytes)
            stats_row.append(self.stats_speed)
            prog_block.append(stats_row)
            card.append(prog_block)

            steps_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            steps_block.add_css_class("card-section-tight")
            self.step_widgets = []
            for label_text in STEPS:
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
                row.add_css_class("step-row")
                row.set_hexpand(True)

                icon_stack = Gtk.Stack()
                icon_stack.set_valign(Gtk.Align.CENTER)
                icon_stack.set_halign(Gtk.Align.START)
                icon_stack.set_hexpand(False)
                icon_stack.set_vexpand(False)
                icon_stack.set_size_request(24, 24)

                pending_wrap = Gtk.CenterBox()
                pending_wrap.set_size_request(24, 24)
                pending_wrap.set_hexpand(False)
                pending_wrap.set_vexpand(False)
                dot_p = Gtk.Box()
                dot_p.add_css_class("step-dot-pending")
                dot_p.set_size_request(6, 6)
                dot_p.set_halign(Gtk.Align.CENTER)
                dot_p.set_valign(Gtk.Align.CENTER)
                pending_wrap.set_center_widget(dot_p)

                spinner_wrap = Gtk.CenterBox()
                spinner_wrap.set_size_request(24, 24)
                spinner_wrap.set_hexpand(False)
                spinner_wrap.set_vexpand(False)
                spinner = Gtk.Spinner()
                spinner.add_css_class("step-spinner")
                spinner.set_size_request(18, 18)
                spinner_wrap.set_center_widget(spinner)

                done_wrap = _fixed_badge_gtk4(SVG_CHECK_SUCCESS, 12, 24, "step-circle-done")

                icon_stack.add_named(pending_wrap, "pending")
                icon_stack.add_named(spinner_wrap, "active")
                icon_stack.add_named(done_wrap, "done")
                icon_stack.set_visible_child_name("pending")

                text_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                text_box.set_hexpand(True)
                text_box.set_valign(Gtk.Align.CENTER)
                t_lbl = _left_label_gtk4(label_text, "step-title-pending")
                sub_lbl = _left_label_gtk4("", "step-sub")
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

            self.error_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            self.error_box.add_css_class("error-box")
            self.error_box.set_margin_start(18)
            self.error_box.set_margin_end(18)
            self.error_box.set_margin_bottom(14)
            self.error_box.set_visible(False)
            self.error_lbl = _left_label_gtk4("", "dialog-body", wrap=True)
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

            self.log_revealer = Gtk.Revealer()
            self.log_revealer.set_transition_type(Gtk.RevealerTransitionType.SLIDE_DOWN)
            self.log_revealer.set_transition_duration(200)
            self.log_revealer.set_reveal_child(False)
            log_wrap = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
            log_wrap.set_margin_start(18)
            log_wrap.set_margin_end(18)
            log_wrap.set_margin_bottom(14)
            log_scroll = Gtk.ScrolledWindow()
            log_scroll.set_min_content_height(110)
            log_scroll.set_max_content_height(150)
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

            self.elapsed_lbl = Gtk.Label(label="0 s")
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
            shell, col = self._full_shell()

            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            hero.set_hexpand(True)
            icon_tile = _fixed_badge_gtk4(SVG_CHECK_GREEN_LG, 24, 52, "hero-icon-green")

            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            hero_text.set_hexpand(True)
            hero_text.set_valign(Gtk.Align.CENTER)

            top_badge_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            self.done_pill = Gtk.Label(label="Instalação concluída")
            self.done_pill.add_css_class("badge-success-pill")
            top_badge_row.append(self.done_pill)
            hero_text.append(top_badge_row)
            self.done_title = _left_label_gtk4("Tudo pronto! O Custódia foi instalado", "hero-title")
            hero_text.append(self.done_title)
            self.done_sub = _left_label_gtk4(
                "Você já pode começar a sincronizar seus arquivos no Google Drive.",
                "hero-subtitle",
                wrap=True,
            )
            hero_text.append(self.done_sub)

            done_logo = _app_icon_gtk4_image(52)
            done_logo.set_valign(Gtk.Align.CENTER)
            done_logo.set_hexpand(False)

            hero.append(icon_tile)
            hero.append(hero_text)
            hero.append(done_logo)
            col.append(hero)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.add_css_class("installer-card")
            card.set_hexpand(True)

            summary_list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            summary_list.add_css_class("card-section")

            self.done_item_ver = self._make_done_row("Aplicativo pronto", "Custódia instalado no sistema")
            self.done_item_shortcut = self._make_done_row("Atalhos criados", "Disponível no menu de aplicativos do sistema")
            self.done_item_cleanup = self._make_done_row("Limpeza concluída", "O arquivo do instalador foi removido automaticamente")

            summary_list.append(self.done_item_ver["box"])
            summary_list.append(self.done_item_shortcut["box"])
            summary_list.append(self.done_item_cleanup["box"])
            card.append(summary_list)
            card.append(self._make_divider())

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            btn_row.add_css_class("card-footer-row")
            btn_row.set_hexpand(True)

            brand_end = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            brand_end.set_hexpand(True)
            brand_end.set_valign(Gtk.Align.CENTER)
            brand_end.append(_app_icon_gtk4_image(24))
            brand_end.append(_left_label_gtk4("Custódia by TundraLab", "distro-hint"))

            close_btn = Gtk.Button(label="Fechar")
            close_btn.add_css_class("btn-secondary")
            close_btn.connect("clicked", lambda *_: self._request_quit())
            self.done_open_btn = Gtk.Button(label="Abrir Custódia agora")
            self.done_open_btn.add_css_class("btn-primary")
            self.done_open_btn.connect("clicked", self._on_open_and_close)

            btn_row.append(brand_end)
            btn_row.append(close_btn)
            btn_row.append(self.done_open_btn)
            card.append(btn_row)

            col.append(card)
            col.append(self._build_support_links())
            return shell

        def _make_done_row(self, title: str, subtitle: str) -> dict:
            row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
            row.add_css_class("done-item-row")
            row.set_hexpand(True)

            badge = _fixed_badge_gtk4(SVG_CHECK_SUCCESS, 12, 24, "step-circle-done")

            txt = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
            txt.set_hexpand(True)
            t_lbl = _left_label_gtk4(title, "done-item-title")
            s_lbl = _left_label_gtk4(subtitle, "done-item-sub", wrap=True)
            txt.append(t_lbl)
            txt.append(s_lbl)

            row.append(badge)
            row.append(txt)
            return {"box": row, "title": t_lbl, "sub": s_lbl}

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
            idx = int(self.ver_dropdown.get_selected())
            rel = self._selected_release()
            self.topbar_ver.set_label(rel["tag"])

            latest_tag = self.releases[0]["tag"]
            if idx > 0:
                self.old_ver_lbl.set_label(
                    f"Você escolheu uma versão anterior ({rel['tag']}). Recomendamos instalar a versão mais recente ({latest_tag}) para ter todas as novidades e correções."
                )
                self.old_ver_banner.set_visible(True)
            else:
                self.old_ver_banner.set_visible(False)

            assets = rel.get("assets") or {}
            for k, btn in self.fmt_buttons.items():
                available = k in assets
                btn.set_sensitive(available)
                btn.set_tooltip_text("" if available else f"Não disponível na versão {rel['tag']}")
            if self.selected_fmt not in assets:
                for k in (self.recommended_fmt, "deb", "rpm", "appimage"):
                    if k in assets:
                        self._select_format(k)
                        break
            self._refresh_action_buttons(rel["tag"])

        def _refresh_action_buttons(self, target_tag: str) -> None:
            if self.installed_info is None:
                self.install_btn.set_label("Instalar agora")
                return
            inst_t = parse_semver_tuple(self.installed_info.get("version"))
            tgt_t = parse_semver_tuple(target_tag)
            if inst_t and tgt_t and tgt_t > inst_t:
                self.install_btn.set_label("Atualizar agora")
            elif inst_t and tgt_t and tgt_t < inst_t:
                self.install_btn.set_label("Instalar versão selecionada")
            else:
                self.install_btn.set_label("Reinstalar / Atualizar")

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
                if 0 <= idx < len(self.step_widgets) and sub_text:
                    sub = self.step_widgets[idx]["sub"]
                    sub.set_label(sub_text)
                    sub.set_visible(True)
                return False

            GLib.idle_add(_ui)

        def _set_progress(self, fraction: float, bytes_text: str = "", speed_text: str | None = None) -> None:
            self.target_fraction = max(0.0, min(1.0, fraction))

            def _ui() -> bool:
                if bytes_text:
                    self.stats_bytes.set_label(f"·  {bytes_text}")
                if speed_text is not None:
                    self.stats_speed.set_label(speed_text)
                return False

            GLib.idle_add(_ui)

        def _tick_anim(self) -> bool:
            if not self.installing and abs(self.target_fraction - self.display_fraction) < 0.004:
                self.display_fraction = self.target_fraction
                self.progress_bar.set_fraction(self.display_fraction)
                self.stats_pct.set_label(f"{int(round(self.display_fraction * 100))}%")
                self.anim_source_id = None
                return False
            diff = self.target_fraction - self.display_fraction
            if abs(diff) < 0.003:
                self.display_fraction = self.target_fraction
            else:
                self.display_fraction += diff * 0.22
            self.progress_bar.set_fraction(self.display_fraction)
            self.stats_pct.set_label(f"{int(round(self.display_fraction * 100))}%")
            return True

        def _stop_timers(self) -> None:
            if self.timer_source_id is not None:
                GLib.source_remove(self.timer_source_id)
                self.timer_source_id = None
            if self.anim_source_id is not None:
                GLib.source_remove(self.anim_source_id)
                self.anim_source_id = None

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

            self._stop_timers()
            self.cancel_event.clear()
            self.installing = True
            self.install_started_at = time.monotonic()
            self.target_fraction = 0.0
            self.display_fraction = 0.0
            self.all_log_lines.clear()
            self.error_box.set_visible(False)
            self.cancel_btn.set_sensitive(True)
            self.log_buffer.set_text("")
            self.stats_pct.set_label("0%")
            self.stats_bytes.set_label("")
            self.stats_speed.set_label("")
            self.progress_bar.set_fraction(0.0)
            self.elapsed_lbl.set_label("0 s decorridos")
            fmt_label = ".AppImage" if fmt == "appimage" else f".{fmt}"
            is_update = self.installed_info is not None
            self.inst_title.set_label("Atualizando o Custódia..." if is_update else "Instalando o Custódia...")
            self.inst_subtitle.set_label(
                "Estamos baixando e preparando o aplicativo no seu computador."
            )
            self.inst_target_pill.set_label(f"{rel['tag']}  •  {fmt_label}")
            self.step_widgets[1]["title"].set_label(f"Baixando o Custódia {rel['tag']}")
            self.step_widgets[2]["title"].set_label(STEPS[2])
            self.step_widgets[3]["title"].set_label(STEPS[3])

            self.stack.set_visible_child_name("install")
            self.timer_source_id = GLib.timeout_add(1000, self._tick_elapsed)
            self.anim_source_id = GLib.timeout_add(33, self._tick_anim)

            threading.Thread(
                target=self._install_worker,
                args=(rel, fmt, asset),
                daemon=True,
            ).start()

        def _install_worker(self, rel: dict, fmt: str, asset: dict) -> None:
            tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-install-"))
            os.chmod(tmp_dir, 0o755)
            try:
                self._set_step(0, f"Verificando compatibilidade em {self.distro_label}")
                self._set_progress(0.04, speed_text="Preparando...")
                self._append_log(f"Sistema detectado: {self.distro_label}")
                stop_running_custodia()

                pkg_path = tmp_dir / asset["name"]
                self._set_step(1, f"Versão {rel['tag']}")
                self._append_log(f"Baixando {asset['url']}")

                def on_dl(done: int, total: int, speed: float, eta: float) -> None:
                    frac = 0.08 + 0.62 * (done / total) if total > 0 else 0.35
                    b_txt = f"{format_bytes(done)} / {format_bytes(total)}" if total > 0 else format_bytes(done)
                    spd = f"{speed / (1024 * 1024):.1f} MB/s".replace(".", ",")
                    eta_s = format_eta(eta)
                    s_txt = f"{spd} · {eta_s}" if eta_s else spd
                    self._set_progress(frac, b_txt, s_txt)

                download_file(asset["url"], pkg_path, on_dl, self.cancel_event, int(asset.get("size") or 0))
                os.chmod(pkg_path, 0o644)

                self._set_step(2, "Confirmando autenticidade do download")
                self._set_progress(0.75, speed_text="Verificando...")
                sums_asset = (rel.get("assets") or {}).get("sums")
                verify_sha256(pkg_path, sums_asset["url"] if sums_asset else None, asset["name"], self._append_log)

                self._set_step(3, "Aplicando pacote no computador...")
                self._set_progress(0.84, speed_text="Instalando...")
                on_sub = lambda text: self._set_step_sub(3, text)
                if fmt == "deb":
                    self.priv_session.run_package_install("install_deb", pkg_path, self._append_log, on_sub)
                elif fmt == "rpm":
                    self.priv_session.run_package_install("install_rpm", pkg_path, self._append_log, on_sub)
                else:
                    install_appimage(pkg_path, self._append_log, on_sub)

                self._set_step(4, "Removendo arquivos temporários")
                self._set_progress(0.97, speed_text="Finalizando...")
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
            self.done_pill.set_label(f"Custódia {tag} pronto para usar")
            self.done_title.set_label("Tudo pronto! O Custódia foi configurado")
            self.done_sub.set_label("Você já pode começar a sincronizar seus arquivos no Google Drive.")
            self.done_item_ver["title"].set_label("Aplicativo pronto")
            self.done_item_ver["sub"].set_label(f"Versão {tag} configurada no {self.distro_label}")
            self.done_item_shortcut["box"].set_visible(True)
            self.done_open_btn.set_visible(True)
            if deleted:
                self.done_item_cleanup["title"].set_label("Instalador removido automaticamente")
                self.done_item_cleanup["sub"].set_label("O arquivo usado na instalação já foi apagado da sua pasta")
            else:
                self.done_item_cleanup["title"].set_label("Processo finalizado")
                self.done_item_cleanup["sub"].set_label("Tudo configurado e pronto para abrir")
            self.stack.set_visible_child_name("done")
            return False

        def _on_uninstall_clicked(self, *_args) -> None:
            if self.installed_info is None:
                return
            dlg = Gtk.Window(transient_for=self.win, modal=True, title="Desinstalar Custódia")
            dlg.set_resizable(False)
            dlg.set_default_size(430, -1)
            dlg.add_css_class("installer-root")

            box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
            box.add_css_class("dialog-card")
            box.append(_left_label_gtk4("Desinstalar o Custódia agora?", "dialog-title"))
            box.append(
                _left_label_gtk4(
                    "O aplicativo será removido do sistema. Seus arquivos locais e no Google Drive permanecem intactos.",
                    "dialog-body",
                    wrap=True,
                )
            )

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            btn_row.set_halign(Gtk.Align.END)
            btn_row.set_margin_top(8)

            cancel_btn = Gtk.Button(label="Cancelar")
            cancel_btn.add_css_class("btn-secondary")
            cancel_btn.connect("clicked", lambda *_: dlg.close())

            confirm_btn = Gtk.Button(label="Desinstalar")
            confirm_btn.add_css_class("btn-danger")

            def _start_uninstall(*_a) -> None:
                dlg.close()
                self._start_uninstall_flow()

            confirm_btn.connect("clicked", _start_uninstall)
            btn_row.append(cancel_btn)
            btn_row.append(confirm_btn)
            box.append(btn_row)
            dlg.set_child(box)
            dlg.present()

        def _start_uninstall_flow(self) -> None:
            if self.installed_info is None:
                return
            self._stop_timers()
            self.cancel_event.clear()
            self.installing = True
            self.install_started_at = time.monotonic()
            self.target_fraction = 0.0
            self.display_fraction = 0.0
            self.all_log_lines.clear()
            self.error_box.set_visible(False)
            self.cancel_btn.set_sensitive(False)
            self.log_buffer.set_text("")
            self.stats_pct.set_label("0%")
            self.stats_bytes.set_label("")
            self.stats_speed.set_label("")
            self.progress_bar.set_fraction(0.0)
            self.elapsed_lbl.set_label("0 s decorridos")
            inst_fmt = self.installed_info["format"]
            fmt_label = ".AppImage" if inst_fmt == "appimage" else f".{inst_fmt}"
            self.inst_title.set_label("Desinstalando o Custódia...")
            self.inst_subtitle.set_label("Removendo o aplicativo instalado com segurança.")
            self.inst_target_pill.set_label(f"Remoção  •  {fmt_label}")
            self.step_widgets[1]["title"].set_label("Encerrando aplicativo em execução")
            self.step_widgets[2]["title"].set_label("Preparando desinstalação")
            self.step_widgets[3]["title"].set_label("Removendo aplicativo do sistema")

            self.stack.set_visible_child_name("install")
            self.timer_source_id = GLib.timeout_add(1000, self._tick_elapsed)
            self.anim_source_id = GLib.timeout_add(33, self._tick_anim)

            threading.Thread(
                target=self._uninstall_worker,
                args=(inst_fmt,),
                daemon=True,
            ).start()

        def _uninstall_worker(self, inst_fmt: str) -> None:
            try:
                self._set_step(0, f"Verificando instalação em {self.distro_label}")
                self._set_progress(0.12, speed_text="Verificando...")
                self._set_step(1, "Fechando janelas abertas do Custódia")
                stop_running_custodia()
                self._set_progress(0.32, speed_text="Preparando...")
                self._set_step(2, "Iniciando remoção segura")
                self._set_progress(0.45, speed_text="Removendo...")
                self._set_step(3, "Desinstalando pacotes e atalhos...")
                on_sub = lambda text: self._set_step_sub(3, text)
                if inst_fmt == "deb":
                    self.priv_session.run_package_install("uninstall_deb", None, self._append_log, on_sub)
                elif inst_fmt == "rpm":
                    self.priv_session.run_package_install("uninstall_rpm", None, self._append_log, on_sub)
                else:
                    uninstall_appimage(self._append_log, on_sub)
                self._set_step(4, "Removendo arquivo do instalador")
                self._set_progress(0.95, speed_text="Finalizando...")
                deleted = self_delete_installer(self._append_log)
                self._set_step(5)
                self._set_progress(1.0, speed_text="Concluído")
                self.installing = False
                GLib.idle_add(self._show_uninstall_done, inst_fmt, deleted)
            except Exception as exc:
                self.installing = False
                self._append_log(f"ERRO: {exc}")
                GLib.idle_add(self._show_error, str(exc))

        def _show_uninstall_done(self, inst_fmt: str, deleted: bool) -> bool:
            fmt_label = ".AppImage" if inst_fmt == "appimage" else f".{inst_fmt}"
            self.done_pill.set_label("Desinstalação concluída")
            self.done_title.set_label("Custódia removido do computador")
            self.done_sub.set_label("Seus arquivos pessoais e backups no Google Drive foram mantidos intactos.")
            self.done_item_ver["title"].set_label("Aplicativo removido")
            self.done_item_ver["sub"].set_label(f"Pacote {fmt_label} desinstalado do {self.distro_label}")
            self.done_item_shortcut["box"].set_visible(False)
            self.done_open_btn.set_visible(False)
            if deleted:
                self.done_item_cleanup["title"].set_label("Instalador removido automaticamente")
                self.done_item_cleanup["sub"].set_label("O arquivo temporário usado já foi apagado")
            else:
                self.done_item_cleanup["title"].set_label("Limpeza concluída")
                self.done_item_cleanup["sub"].set_label("Você pode fechar esta janela com segurança")
            self.stack.set_visible_child_name("done")
            return False

        def _show_error(self, message: str) -> bool:
            self._stop_timers()
            self.error_lbl.set_label(message)
            self.error_box.set_visible(True)
            self.cancel_btn.set_sensitive(True)
            for w in self.step_widgets:
                w["spinner"].stop()
            return False

        def _on_open_and_close(self, *_args) -> None:
            launch_custodia()
            self.priv_session.close()
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


def _left_label_gtk3(text: str, css_class: str, wrap: bool = False):
    from gi.repository import Gtk  # type: ignore

    lbl = Gtk.Label(label=text, xalign=0)
    lbl.set_halign(Gtk.Align.START)
    if wrap:
        lbl.set_line_wrap(True)
    lbl.get_style_context().add_class(css_class)
    return lbl


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
            self.installed_info = detect_installed_custodia()
            self.selected_fmt = (
                self.installed_info["format"]
                if self.installed_info is not None
                else self.recommended_fmt
            )
            self.distro_label = detect_distro_label()
            self.cancel_event = threading.Event()
            self.install_started_at = 0.0
            self.timer_source_id: int | None = None
            self.anim_source_id: int | None = None
            self.installing = False
            self.all_log_lines: list[str] = []
            self.target_fraction = 0.0
            self.display_fraction = 0.0
            self.priv_session = PrivilegedSession()

            self.fmt_buttons: dict[str, Gtk.Button] = {}
            self.fmt_dots: dict[str, Gtk.Box] = {}
            self.fmt_checks: dict[str, Gtk.Widget] = {}
            self.step_widgets: list[dict] = []
            self.save_log_buttons: list[Gtk.Button] = []

            self.win = Gtk.Window(title="Custódia")
            self.win.set_default_size(880, 640)
            icon_path = _resolve_app_icon_path()
            if icon_path is not None:
                try:
                    self.win.set_icon_from_file(str(icon_path))
                except Exception:
                    pass
            self.win.get_style_context().add_class("installer-root")
            self.win.connect("delete-event", self._on_delete)

            header = Gtk.HeaderBar()
            header.get_style_context().add_class("installer-topbar")
            header.set_show_close_button(False)
            header.set_custom_title(Gtk.Box())

            brand_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            brand_box.pack_start(_app_icon_gtk3_image(22), False, False, 0)
            brand_lbl = Gtk.Label(label="Custódia")
            brand_lbl.get_style_context().add_class("topbar-brand")
            self.topbar_ver = Gtk.Label(label=f"v{FALLBACK_VERSION}")
            self.topbar_ver.get_style_context().add_class("topbar-version")
            whats_new = Gtk.Button(label="O que há de novo")
            whats_new.get_style_context().add_class("topbar-link")
            whats_new.connect("clicked", self._show_whats_new_dialog)
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
            pop_box.pack_start(_left_label_gtk3("Instalador oficial Custódia", "field-label"), False, False, 0)
            pop_box.pack_start(
                _left_label_gtk3(
                    f"Sistema: {self.distro_label}\nPacote indicado: .{self.recommended_fmt}\nVerificação automática de integridade",
                    "fmt-sub",
                ),
                False,
                False,
                0,
            )
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
            self.stack.set_transition_type(Gtk.StackTransitionType.SLIDE_LEFT_RIGHT)
            self.stack.set_transition_duration(240)
            self.stack.add_named(self._build_select_page(), "select")
            self.stack.add_named(self._build_install_page(), "install")
            self.stack.add_named(self._build_done_page(), "done")
            self.stack.set_visible_child_name("select")

            overlay = Gtk.Overlay()
            overlay.get_style_context().add_class("hero-bg")
            overlay.add(_svg_gtk3_image(SVG_HERO_BACKDROP, 940))
            overlay.add_overlay(self.stack)

            self.win.add(overlay)
            self.win.show_all()
            self.old_ver_banner.set_visible(False)
            self.installed_banner.set_visible(self.installed_info is not None)
            self.uninstall_btn.set_visible(self.installed_info is not None)
            for k, chk in self.fmt_checks.items():
                chk.set_visible(k == self.selected_fmt)
            self.error_box.set_visible(False)
            for w in self.step_widgets:
                w["sub"].set_visible(False)

            threading.Thread(target=self._load_releases_bg, daemon=True).start()
            if not os.environ.get("CUSTODIA_NO_EARLY_AUTH"):
                GLib.timeout_add(220, self._trigger_upfront_auth)

        def _trigger_upfront_auth(self) -> bool:
            threading.Thread(target=self.priv_session.start_upfront, daemon=True).start()
            return False

        def _on_delete(self, *_args) -> bool:
            if self.installing:
                self._confirm_cancel(quit_after=True)
                return True
            self.priv_session.close()
            Gtk.main_quit()
            return False

        def _request_quit(self) -> None:
            if self.installing:
                self._confirm_cancel(quit_after=True)
            else:
                self.priv_session.close()
                Gtk.main_quit()

        def _show_whats_new_dialog(self, *_args) -> None:
            rel = self._selected_release()
            dlg = Gtk.Window(transient_for=self.win, modal=True, title=f"Novidades: {rel['tag']}")
            dlg.set_default_size(580, 470)
            dlg.get_style_context().add_class("installer-root")

            box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
            box.get_style_context().add_class("dialog-card")

            hdr = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            hdr.pack_start(_app_icon_gtk3_image(36), False, False, 0)
            hdr_txt = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
            hdr_txt.pack_start(_left_label_gtk3(rel.get("title") or f"Custódia {rel['tag']}", "dialog-title"), False, False, 0)
            date_txt = f"Publicado em {rel['published_at']}" if rel.get("published_at") else "Lançamento oficial no GitHub"
            hdr_txt.pack_start(_left_label_gtk3(date_txt, "distro-hint"), False, False, 0)
            hdr.pack_start(hdr_txt, True, True, 0)
            box.pack_start(hdr, False, False, 0)

            scroll = Gtk.ScrolledWindow()
            scroll.set_min_content_height(260)
            tv = Gtk.TextView()
            tv.set_editable(False)
            tv.set_cursor_visible(False)
            tv.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
            tv.get_style_context().add_class("notes-view")
            tv.get_buffer().set_text(format_release_notes_plain(rel.get("body", "")))
            scroll.add(tv)
            box.pack_start(scroll, True, True, 0)

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            gh_btn = Gtk.Button(label="Abrir no GitHub")
            gh_btn.get_style_context().add_class("btn-ghost")
            gh_btn.connect("clicked", lambda *_: webbrowser.open(rel.get("url") or RELEASES_URL))
            close_btn = Gtk.Button(label="Fechar")
            close_btn.get_style_context().add_class("btn-primary")
            close_btn.connect("clicked", lambda *_: dlg.destroy())
            btn_row.pack_start(gh_btn, False, False, 0)
            btn_row.pack_end(close_btn, False, False, 0)
            box.pack_start(btn_row, False, False, 0)

            dlg.add(box)
            dlg.show_all()

        def _confirm_cancel(self, quit_after: bool = False) -> None:
            dlg = Gtk.MessageDialog(
                transient_for=self.win,
                modal=True,
                message_type=Gtk.MessageType.QUESTION,
                buttons=Gtk.ButtonsType.NONE,
                text="Cancelar a instalação agora?",
            )
            dlg.format_secondary_text("O processo atual será interrompido e nada parcial ficará salvo.")
            dlg.add_button("Continuar", Gtk.ResponseType.CANCEL)
            dlg.add_button("Cancelar instalação", Gtk.ResponseType.OK)
            resp = dlg.run()
            dlg.destroy()
            if resp == Gtk.ResponseType.OK:
                self.cancel_event.set()
                self.cancel_btn.set_sensitive(False)
                if quit_after:
                    self.installing = False
                    self.priv_session.close()
                    Gtk.main_quit()

        def _full_shell(self):
            scroll = Gtk.ScrolledWindow()
            scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
            col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=16)
            col.set_valign(Gtk.Align.CENTER)
            col.set_margin_top(24)
            col.set_margin_bottom(22)
            col.set_margin_start(38)
            col.set_margin_end(38)
            scroll.add(col)
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
            row1 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
            save_btn = Gtk.Button(label="Baixar registro da instalação")
            save_btn.get_style_context().add_class("footer-link")
            save_btn.connect("clicked", self._on_save_log_clicked)
            self.save_log_buttons.append(save_btn)
            row1.pack_start(save_btn, False, False, 0)
            for label, url in (
                ("Guia de instalação", GUIDE_URL),
                ("Solução de problemas", TROUBLESHOOTING_URL),
                ("Relatar um problema", ISSUES_URL),
                ("Comunidade", DISCUSSIONS_URL),
            ):
                btn = Gtk.Button(label=label)
                btn.get_style_context().add_class("footer-link")
                btn.connect("clicked", lambda _b, u=url: webbrowser.open(u))
                row1.pack_start(btn, False, False, 0)
            wrap.pack_start(row1, False, False, 0)
            return wrap

        def _build_select_page(self):
            shell, col = self._full_shell()
            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            hero.pack_start(_app_icon_gtk3_image(58), False, False, 0)
            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            hero_text.pack_start(_left_label_gtk3("CUSTÓDIA BY TUNDRALAB  •  SEUS ARQUIVOS. SEMPRE SEGUROS.", "hero-brand-sub"), False, False, 0)
            hero_text.pack_start(_left_label_gtk3("Instalar no seu Linux", "hero-title"), False, False, 0)
            hero_text.pack_start(
                _left_label_gtk3(
                    "Já deixamos marcada a melhor opção para o seu computador. Ao terminar, o instalador se apaga sozinho.",
                    "hero-subtitle",
                    wrap=True,
                ),
                False,
                False,
                0,
            )
            hero.pack_start(hero_text, True, True, 0)
            col.pack_start(hero, False, False, 0)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.get_style_context().add_class("installer-card")
            top_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            top_block.get_style_context().add_class("card-section")

            ver_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            ver_lbl = _left_label_gtk3("Versão para instalar", "field-label")
            self.ver_combo = Gtk.ComboBoxText()
            self.ver_combo.append_text(f"v{FALLBACK_VERSION} (Mais recente)")
            self.ver_combo.set_active(0)
            self.ver_combo.connect("changed", self._on_release_changed)
            ver_row.pack_start(ver_lbl, True, True, 0)
            ver_row.pack_start(self.ver_combo, False, False, 0)
            top_block.pack_start(ver_row, False, False, 0)

            self.installed_banner = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            self.installed_banner.get_style_context().add_class("installed-banner")
            if self.installed_info is not None:
                inst_ver = f"v{self.installed_info['version']} " if self.installed_info.get("version") else ""
                inst_fmt = ".AppImage" if self.installed_info["format"] == "appimage" else f".{self.installed_info['format']}"
                banner_txt = f"Custódia {inst_ver}({inst_fmt}) instalado neste computador. Escolha Atualizar ou Desinstalar."
            else:
                banner_txt = ""
            self.installed_banner.pack_start(_left_label_gtk3(banner_txt, "installed-banner", wrap=True), True, True, 0)
            top_block.pack_start(self.installed_banner, False, False, 0)

            self.old_ver_banner = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            self.old_ver_banner.get_style_context().add_class("warning-banner")
            self.old_ver_lbl = _left_label_gtk3("", "warning-text", wrap=True)
            self.old_ver_banner.pack_start(_svg_gtk3_image(SVG_WARNING, 18), False, False, 0)
            self.old_ver_banner.pack_start(self.old_ver_lbl, True, True, 0)
            top_block.pack_start(self.old_ver_banner, False, False, 0)

            top_block.pack_start(_left_label_gtk3("Escolha o formato para o seu sistema", "field-label"), False, False, 0)

            fmt_list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            for key, name, desc in FORMATS:
                btn = Gtk.Button()
                btn.get_style_context().add_class("fmt-row")
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                dot = Gtk.Box()
                dot.get_style_context().add_class("fmt-dot")
                dot.set_size_request(20, 20)
                dot.set_valign(Gtk.Align.CENTER)
                chk = _svg_gtk3_image(SVG_CHECK_ON_PRIMARY, 11)
                dot.pack_start(chk, True, True, 0)
                text_col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                name_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
                name_row.pack_start(_left_label_gtk3(name, "fmt-title"), False, False, 0)
                if key == self.recommended_fmt:
                    rec = Gtk.Label(label="Recomendado")
                    rec.get_style_context().add_class("badge-rec")
                    name_row.pack_start(rec, False, False, 0)
                text_col.pack_start(name_row, False, False, 0)
                text_col.pack_start(_left_label_gtk3(desc, "fmt-sub"), False, False, 0)
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
            distro_lbl = _left_label_gtk3(f"Seu sistema: {self.distro_label}", "distro-hint")
            distro_lbl.set_ellipsize(Pango.EllipsizeMode.END)
            self.uninstall_btn = Gtk.Button(label="Desinstalar")
            self.uninstall_btn.get_style_context().add_class("btn-danger-outlined")
            self.uninstall_btn.connect("clicked", self._on_uninstall_clicked)
            self.install_btn = Gtk.Button(label="Instalar agora")
            self.install_btn.get_style_context().add_class("btn-primary")
            self.install_btn.connect("clicked", self._on_start_install)
            action_row.pack_start(distro_lbl, True, True, 0)
            action_row.pack_start(self.uninstall_btn, False, False, 0)
            action_row.pack_start(self.install_btn, False, False, 0)
            card.pack_start(action_row, False, False, 0)

            col.pack_start(card, False, False, 0)
            col.pack_start(self._build_support_links(), False, False, 0)
            self._select_format(self.selected_fmt)
            self._refresh_action_buttons(f"v{FALLBACK_VERSION}")
            return shell

        def _build_install_page(self):
            shell, col = self._full_shell()
            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.get_style_context().add_class("hero-icon-blue")
            icon_tile.set_size_request(52, 52)
            icon_tile.set_valign(Gtk.Align.CENTER)
            icon_tile.pack_start(_svg_gtk3_image(SVG_DOWNLOAD, 22), True, True, 0)
            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            self.inst_title = _left_label_gtk3("Instalando o Custódia...", "hero-title")
            self.inst_subtitle = _left_label_gtk3("Estamos baixando e preparando o aplicativo no seu computador.", "hero-subtitle", wrap=True)
            hero_text.pack_start(self.inst_title, False, False, 0)
            hero_text.pack_start(self.inst_subtitle, False, False, 0)
            hero.pack_start(icon_tile, False, False, 0)
            hero.pack_start(hero_text, True, True, 0)
            col.pack_start(hero, False, False, 0)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.get_style_context().add_class("installer-card")
            prog_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            prog_block.get_style_context().add_class("card-section")

            self.progress_bar = Gtk.ProgressBar()
            self.progress_bar.get_style_context().add_class("slim-progress")
            prog_block.pack_start(self.progress_bar, False, False, 0)

            stats_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
            self.stats_pct = _left_label_gtk3("0%", "stats-pct")
            self.stats_bytes = _left_label_gtk3("", "stats-mono")
            self.stats_speed = Gtk.Label(label="", xalign=1)
            self.stats_speed.set_halign(Gtk.Align.END)
            self.stats_speed.get_style_context().add_class("stats-mono")
            stats_row.pack_start(self.stats_pct, False, False, 0)
            stats_row.pack_start(self.stats_bytes, True, True, 0)
            stats_row.pack_start(self.stats_speed, False, False, 0)
            prog_block.pack_start(stats_row, False, False, 0)
            card.pack_start(prog_block, False, False, 0)

            steps_block = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            steps_block.get_style_context().add_class("card-section-tight")
            self.step_widgets = []
            for label_text in STEPS:
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
                row.get_style_context().add_class("step-row")
                icon_stack = Gtk.Stack()
                icon_stack.set_size_request(24, 24)
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
                done_wrap.set_size_request(24, 24)
                done_wrap.set_valign(Gtk.Align.CENTER)
                done_wrap.pack_start(_svg_gtk3_image(SVG_CHECK_SUCCESS, 12), True, True, 0)
                icon_stack.add_named(pending_wrap, "pending")
                icon_stack.add_named(spinner, "active")
                icon_stack.add_named(done_wrap, "done")
                icon_stack.set_visible_child_name("pending")

                text_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
                t_lbl = _left_label_gtk3(label_text, "step-title-pending")
                sub_lbl = _left_label_gtk3("", "step-sub")
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
            self.error_box.set_margin_start(18)
            self.error_box.set_margin_end(18)
            self.error_box.set_margin_bottom(14)
            self.error_lbl = _left_label_gtk3("", "dialog-body", wrap=True)
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
            log_wrap.set_margin_start(18)
            log_wrap.set_margin_end(18)
            log_wrap.set_margin_bottom(14)
            log_scroll = Gtk.ScrolledWindow()
            log_scroll.set_min_content_height(110)
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

        def _make_done_row(self, title: str, subtitle: str) -> dict:
            row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
            row.get_style_context().add_class("done-item-row")
            badge = Gtk.Box()
            badge.get_style_context().add_class("step-circle-done")
            badge.set_size_request(24, 24)
            badge.set_valign(Gtk.Align.CENTER)
            badge.pack_start(_svg_gtk3_image(SVG_CHECK_SUCCESS, 12), True, True, 0)
            txt = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
            t_lbl = _left_label_gtk3(title, "done-item-title")
            s_lbl = _left_label_gtk3(subtitle, "done-item-sub", wrap=True)
            txt.pack_start(t_lbl, False, False, 0)
            txt.pack_start(s_lbl, False, False, 0)
            row.pack_start(badge, False, False, 0)
            row.pack_start(txt, True, True, 0)
            return {"box": row, "title": t_lbl, "sub": s_lbl}

        def _build_done_page(self):
            shell, col = self._full_shell()
            hero = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            icon_tile = Gtk.Box()
            icon_tile.get_style_context().add_class("hero-icon-green")
            icon_tile.set_size_request(52, 52)
            icon_tile.set_valign(Gtk.Align.CENTER)
            icon_tile.pack_start(_svg_gtk3_image(SVG_CHECK_GREEN_LG, 24), True, True, 0)
            hero_text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            self.done_pill = Gtk.Label(label="Instalação concluída")
            self.done_pill.set_halign(Gtk.Align.START)
            self.done_pill.get_style_context().add_class("badge-success-pill")
            hero_text.pack_start(self.done_pill, False, False, 0)
            self.done_title = _left_label_gtk3("Tudo pronto! O Custódia foi instalado", "hero-title")
            hero_text.pack_start(self.done_title, False, False, 0)
            self.done_sub = _left_label_gtk3(
                "Você já pode começar a sincronizar seus arquivos no Google Drive.",
                "hero-subtitle",
                wrap=True,
            )
            hero_text.pack_start(self.done_sub, False, False, 0)
            hero.pack_start(icon_tile, False, False, 0)
            hero.pack_start(hero_text, True, True, 0)
            col.pack_start(hero, False, False, 0)

            card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
            card.get_style_context().add_class("installer-card")
            sbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            sbox.get_style_context().add_class("card-section")
            self.done_item_ver = self._make_done_row("Aplicativo pronto", "Custódia instalado no sistema")
            self.done_item_shortcut = self._make_done_row("Atalhos criados", "Disponível no menu de aplicativos do sistema")
            self.done_item_cleanup = self._make_done_row("Limpeza concluída", "O arquivo do instalador foi removido automaticamente")
            sbox.pack_start(self.done_item_ver["box"], False, False, 0)
            sbox.pack_start(self.done_item_shortcut["box"], False, False, 0)
            sbox.pack_start(self.done_item_cleanup["box"], False, False, 0)
            card.pack_start(sbox, False, False, 0)
            card.pack_start(self._make_divider(), False, False, 0)

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            btn_row.get_style_context().add_class("card-footer-row")
            btn_row.pack_start(_app_icon_gtk3_image(24), False, False, 0)
            btn_row.pack_start(_left_label_gtk3("Custódia by TundraLab", "distro-hint"), True, True, 0)
            self.done_open_btn = Gtk.Button(label="Abrir Custódia agora")
            self.done_open_btn.get_style_context().add_class("btn-primary")
            self.done_open_btn.connect("clicked", self._on_open_and_close)
            close_btn = Gtk.Button(label="Fechar")
            close_btn.get_style_context().add_class("btn-secondary")
            close_btn.connect("clicked", lambda *_: self._request_quit())
            btn_row.pack_end(self.done_open_btn, False, False, 0)
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
            idx = int(self.ver_combo.get_active())
            rel = self._selected_release()
            self.topbar_ver.set_text(rel["tag"])

            latest_tag = self.releases[0]["tag"]
            if idx > 0:
                self.old_ver_lbl.set_text(
                    f"Você escolheu uma versão anterior ({rel['tag']}). Recomendamos instalar a versão mais recente ({latest_tag}) para ter todas as novidades e correções."
                )
                self.old_ver_banner.set_visible(True)
            else:
                self.old_ver_banner.set_visible(False)

            assets = rel.get("assets") or {}
            for k, btn in self.fmt_buttons.items():
                available = k in assets
                btn.set_sensitive(available)
                btn.set_tooltip_text("" if available else f"Não disponível na versão {rel['tag']}")
            if self.selected_fmt not in assets:
                for k in (self.recommended_fmt, "deb", "rpm", "appimage"):
                    if k in assets:
                        self._select_format(k)
                        break
            self._refresh_action_buttons(rel["tag"])

        def _refresh_action_buttons(self, target_tag: str) -> None:
            if self.installed_info is None:
                self.install_btn.set_label("Instalar agora")
                return
            inst_t = parse_semver_tuple(self.installed_info.get("version"))
            tgt_t = parse_semver_tuple(target_tag)
            if inst_t and tgt_t and tgt_t > inst_t:
                self.install_btn.set_label("Atualizar agora")
            elif inst_t and tgt_t and tgt_t < inst_t:
                self.install_btn.set_label("Instalar versão selecionada")
            else:
                self.install_btn.set_label("Reinstalar / Atualizar")

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
            self.target_fraction = max(0.0, min(1.0, fraction))

            def _ui() -> bool:
                if bytes_text:
                    self.stats_bytes.set_text(f"·  {bytes_text}")
                if speed_text is not None:
                    self.stats_speed.set_text(speed_text)
                return False

            GLib.idle_add(_ui)

        def _tick_anim(self) -> bool:
            if not self.installing and abs(self.target_fraction - self.display_fraction) < 0.004:
                self.display_fraction = self.target_fraction
                self.progress_bar.set_fraction(self.display_fraction)
                self.stats_pct.set_text(f"{int(round(self.display_fraction * 100))}%")
                self.anim_source_id = None
                return False
            diff = self.target_fraction - self.display_fraction
            if abs(diff) < 0.003:
                self.display_fraction = self.target_fraction
            else:
                self.display_fraction += diff * 0.22
            self.progress_bar.set_fraction(self.display_fraction)
            self.stats_pct.set_text(f"{int(round(self.display_fraction * 100))}%")
            return True

        def _stop_timers(self) -> None:
            if self.timer_source_id is not None:
                GLib.source_remove(self.timer_source_id)
                self.timer_source_id = None
            if self.anim_source_id is not None:
                GLib.source_remove(self.anim_source_id)
                self.anim_source_id = None

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
            self._stop_timers()
            self.cancel_event.clear()
            self.installing = True
            self.install_started_at = time.monotonic()
            self.target_fraction = 0.0
            self.display_fraction = 0.0
            self.all_log_lines.clear()
            self.error_box.set_visible(False)
            self.cancel_btn.set_sensitive(True)
            self.log_buffer.set_text("")
            self.stats_pct.set_text("0%")
            self.stats_bytes.set_text("")
            self.stats_speed.set_text("")
            self.progress_bar.set_fraction(0.0)
            self.elapsed_lbl.set_text("0 s decorridos")
            self.inst_title.set_text("Atualizando o Custódia..." if self.installed_info is not None else "Instalando o Custódia...")
            self.inst_subtitle.set_text("Estamos baixando e preparando o aplicativo no seu computador.")
            self.step_widgets[1]["title"].set_text(f"Baixando o Custódia {rel['tag']}")
            self.step_widgets[2]["title"].set_text(STEPS[2])
            self.step_widgets[3]["title"].set_text(STEPS[3])
            self.stack.set_visible_child_name("install")
            self.timer_source_id = GLib.timeout_add(1000, self._tick_elapsed)
            self.anim_source_id = GLib.timeout_add(33, self._tick_anim)

            threading.Thread(
                target=self._install_worker,
                args=(rel, fmt, asset),
                daemon=True,
            ).start()

        def _install_worker(self, rel: dict, fmt: str, asset: dict) -> None:
            tmp_dir = Path(tempfile.mkdtemp(prefix="custodia-install-"))
            os.chmod(tmp_dir, 0o755)
            try:
                self._set_step(0, f"Verificando compatibilidade em {self.distro_label}")
                self._set_progress(0.04, speed_text="Preparando...")
                self._append_log(f"Sistema detectado: {self.distro_label}")
                stop_running_custodia()

                pkg_path = tmp_dir / asset["name"]
                self._set_step(1, f"Versão {rel['tag']}")
                self._append_log(f"Baixando {asset['url']}")

                def on_dl(done: int, total: int, speed: float, eta: float) -> None:
                    frac = 0.08 + 0.62 * (done / total) if total > 0 else 0.35
                    b_txt = f"{format_bytes(done)} / {format_bytes(total)}" if total > 0 else format_bytes(done)
                    spd = f"{speed / (1024 * 1024):.1f} MB/s".replace(".", ",")
                    eta_s = format_eta(eta)
                    s_txt = f"{spd} · {eta_s}" if eta_s else spd
                    self._set_progress(frac, b_txt, s_txt)

                download_file(asset["url"], pkg_path, on_dl, self.cancel_event, int(asset.get("size") or 0))
                os.chmod(pkg_path, 0o644)

                self._set_step(2, "Confirmando autenticidade do download")
                self._set_progress(0.75, speed_text="Verificando...")
                sums_asset = (rel.get("assets") or {}).get("sums")
                verify_sha256(pkg_path, sums_asset["url"] if sums_asset else None, asset["name"], self._append_log)

                self._set_step(3, "Aplicando pacote no computador...")
                self._set_progress(0.84, speed_text="Instalando...")
                on_sub = lambda text: self._set_step_sub(3, text)
                if fmt == "deb":
                    self.priv_session.run_package_install("install_deb", pkg_path, self._append_log, on_sub)
                elif fmt == "rpm":
                    self.priv_session.run_package_install("install_rpm", pkg_path, self._append_log, on_sub)
                else:
                    install_appimage(pkg_path, self._append_log, on_sub)

                self._set_step(4, "Removendo arquivos temporários")
                self._set_progress(0.97, speed_text="Finalizando...")
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
            self.done_pill.set_text(f"Custódia {tag} pronto para usar")
            self.done_title.set_text("Tudo pronto! O Custódia foi configurado")
            self.done_sub.set_text("Você já pode começar a sincronizar seus arquivos no Google Drive.")
            self.done_item_ver["title"].set_text("Aplicativo pronto")
            self.done_item_ver["sub"].set_text(f"Versão {tag} configurada no {self.distro_label}")
            self.done_item_shortcut["box"].set_visible(True)
            self.done_open_btn.set_visible(True)
            if deleted:
                self.done_item_cleanup["title"].set_text("Instalador removido automaticamente")
                self.done_item_cleanup["sub"].set_text("O arquivo usado na instalação já foi apagado da sua pasta")
            else:
                self.done_item_cleanup["title"].set_text("Processo finalizado")
                self.done_item_cleanup["sub"].set_text("Tudo configurado e pronto para abrir")
            self.stack.set_visible_child_name("done")
            return False

        def _on_uninstall_clicked(self, *_args) -> None:
            if self.installed_info is None:
                return
            dlg = Gtk.MessageDialog(
                transient_for=self.win,
                modal=True,
                message_type=Gtk.MessageType.WARNING,
                buttons=Gtk.ButtonsType.NONE,
                text="Desinstalar o Custódia agora?",
            )
            dlg.format_secondary_text(
                "O aplicativo será removido do sistema. Seus arquivos locais e no Google Drive permanecem intactos."
            )
            dlg.add_button("Cancelar", Gtk.ResponseType.CANCEL)
            dlg.add_button("Desinstalar", Gtk.ResponseType.OK)
            resp = dlg.run()
            dlg.destroy()
            if resp == Gtk.ResponseType.OK:
                self._start_uninstall_flow()

        def _start_uninstall_flow(self) -> None:
            if self.installed_info is None:
                return
            self._stop_timers()
            self.cancel_event.clear()
            self.installing = True
            self.install_started_at = time.monotonic()
            self.target_fraction = 0.0
            self.display_fraction = 0.0
            self.all_log_lines.clear()
            self.error_box.set_visible(False)
            self.cancel_btn.set_sensitive(False)
            self.log_buffer.set_text("")
            self.stats_pct.set_text("0%")
            self.stats_bytes.set_text("")
            self.stats_speed.set_text("")
            self.progress_bar.set_fraction(0.0)
            self.elapsed_lbl.set_text("0 s decorridos")
            inst_fmt = self.installed_info["format"]
            self.inst_title.set_text("Desinstalando o Custódia...")
            self.inst_subtitle.set_text("Removendo o aplicativo instalado com segurança.")
            self.step_widgets[1]["title"].set_text("Encerrando aplicativo em execução")
            self.step_widgets[2]["title"].set_text("Preparando desinstalação")
            self.step_widgets[3]["title"].set_text("Removendo aplicativo do sistema")
            self.stack.set_visible_child_name("install")
            self.timer_source_id = GLib.timeout_add(1000, self._tick_elapsed)
            self.anim_source_id = GLib.timeout_add(33, self._tick_anim)

            threading.Thread(
                target=self._uninstall_worker,
                args=(inst_fmt,),
                daemon=True,
            ).start()

        def _uninstall_worker(self, inst_fmt: str) -> None:
            try:
                self._set_step(0, f"Verificando instalação em {self.distro_label}")
                self._set_progress(0.12, speed_text="Verificando...")
                self._set_step(1, "Fechando janelas abertas do Custódia")
                stop_running_custodia()
                self._set_progress(0.32, speed_text="Preparando...")
                self._set_step(2, "Iniciando remoção segura")
                self._set_progress(0.45, speed_text="Removendo...")
                self._set_step(3, "Desinstalando pacotes e atalhos...")
                on_sub = lambda text: self._set_step_sub(3, text)
                if inst_fmt == "deb":
                    self.priv_session.run_package_install("uninstall_deb", None, self._append_log, on_sub)
                elif inst_fmt == "rpm":
                    self.priv_session.run_package_install("uninstall_rpm", None, self._append_log, on_sub)
                else:
                    uninstall_appimage(self._append_log, on_sub)
                self._set_step(4, "Removendo arquivo do instalador")
                self._set_progress(0.95, speed_text="Finalizando...")
                deleted = self_delete_installer(self._append_log)
                self._set_step(5)
                self._set_progress(1.0, speed_text="Concluído")
                self.installing = False
                GLib.idle_add(self._show_uninstall_done, inst_fmt, deleted)
            except Exception as exc:
                self.installing = False
                self._append_log(f"ERRO: {exc}")
                GLib.idle_add(self._show_error, str(exc))

        def _show_uninstall_done(self, inst_fmt: str, deleted: bool) -> bool:
            fmt_label = ".AppImage" if inst_fmt == "appimage" else f".{inst_fmt}"
            self.done_pill.set_text("Desinstalação concluída")
            self.done_title.set_text("Custódia removido do computador")
            self.done_sub.set_text("Seus arquivos pessoais e backups no Google Drive foram mantidos intactos.")
            self.done_item_ver["title"].set_text("Aplicativo removido")
            self.done_item_ver["sub"].set_text(f"Pacote {fmt_label} desinstalado do {self.distro_label}")
            self.done_item_shortcut["box"].set_visible(False)
            self.done_open_btn.set_visible(False)
            if deleted:
                self.done_item_cleanup["title"].set_text("Instalador removido automaticamente")
                self.done_item_cleanup["sub"].set_text("O arquivo temporário usado já foi apagado")
            else:
                self.done_item_cleanup["title"].set_text("Limpeza concluída")
                self.done_item_cleanup["sub"].set_text("Você pode fechar esta janela com segurança")
            self.stack.set_visible_child_name("done")
            return False

        def _show_error(self, message: str) -> bool:
            self._stop_timers()
            self.error_lbl.set_text(message)
            self.error_box.set_visible(True)
            self.cancel_btn.set_sensitive(True)
            for w in self.step_widgets:
                w["spinner"].stop()
            return False

        def _on_open_and_close(self, *_args) -> None:
            launch_custodia()
            self.priv_session.close()
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
    installed = detect_installed_custodia()
    rec_fmt = detect_default_format()
    print(f"Sistema detectado: {detect_distro_label()} (recomendado: .{rec_fmt})")
    if installed is not None:
        inst_ver = f" v{installed['version']}" if installed.get("version") else ""
        print(f"Instalação encontrada:{inst_ver} (.{installed['format']})\n")
        op = input("Escolha uma ação: [1] Atualizar/Reinstalar  [2] Desinstalar [1]: ").strip()
        if op == "2":
            priv = PrivilegedSession()
            try:
                stop_running_custodia()
                if installed["format"] == "deb":
                    priv.run_package_install("uninstall_deb", None, print)
                elif installed["format"] == "rpm":
                    priv.run_package_install("uninstall_rpm", None, print)
                else:
                    uninstall_appimage(print)
                self_delete_installer(print)
                print("Custódia desinstalado com sucesso!")
            finally:
                priv.close()
            return
    else:
        print()

    rels = fetch_releases()

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
    priv = PrivilegedSession()
    try:
        pkg = tmp_dir / asset["name"]
        print(f"Baixando {asset['name']}...")
        download_file(asset["url"], pkg, lambda *_: None, threading.Event(), int(asset.get("size") or 0))
        os.chmod(pkg, 0o644)
        sums = rel["assets"].get("sums")
        verify_sha256(pkg, sums["url"] if sums else None, asset["name"], print)
        stop_running_custodia()
        if fmt == "deb":
            priv.run_package_install("install_deb", pkg, print)
        elif fmt == "rpm":
            priv.run_package_install("install_rpm", pkg, print)
        else:
            install_appimage(pkg, print)
        self_delete_installer(print)
        print("Custódia instalado com sucesso!")
    finally:
        priv.close()
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
