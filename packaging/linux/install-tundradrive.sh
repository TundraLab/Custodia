#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# Instalador Proprietário TundraDrive by TundraLab (Linux .rpm & .AppImage)
# Desenvolvedor: Gustavo Henrique Novaes Gomes (@GustaHerin) • TundraLab
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." 2>/dev/null && pwd || pwd)"
DIST_DIR="${REPO_ROOT}/dist"
VERSION="${TUNDRA_VERSION:-2.6.0}"
RELEASE_TAG="v${VERSION}"
REPO_SLUG="TundraLab/TundraDrive"
RELEASE_BASE_URL="https://github.com/${REPO_SLUG}/releases/latest/download"

APPIMAGE_ASSET="TundraDrive-${VERSION}-x86_64.AppImage"
RPM_ASSET="TundraDrive-${VERSION}-1.x86_64.rpm"
DOWNLOAD_DIR="${TUNDRA_DOWNLOAD_DIR:-${PWD}}"

BLUE="\033[1;34m"
CYAN="\033[1;36m"
GREEN="\033[1;32m"
YELLOW="\033[1;33m"
RED="\033[1;31m"
RESET="\033[0m"

print_banner() {
  echo -e "${BLUE}======================================================================${RESET}"
  echo -e "${CYAN}   TundraDrive v${VERSION} • Instalador Proprietário by TundraLab${RESET}"
  echo -e "   Desenvolvedor: Gustavo Henrique Novaes Gomes (@GustaHerin)"
  echo -e "   Repositório:   https://github.com/${REPO_SLUG}"
  echo -e "${BLUE}======================================================================${RESET}"
}

find_appimage() {
  for candidate in \
    "${DOWNLOAD_DIR}/${APPIMAGE_ASSET}" \
    "${DIST_DIR}/${APPIMAGE_ASSET}" \
    "${SCRIPT_DIR}/${APPIMAGE_ASSET}" \
    ./TundraDrive-*.AppImage \
    "${DIST_DIR}"/TundraDrive-*.AppImage; do
    if [[ -f "${candidate}" ]]; then
      readlink -f "${candidate}"
      return 0
    fi
  done
  return 1
}

find_rpm() {
  for candidate in \
    "${DOWNLOAD_DIR}/${RPM_ASSET}" \
    "${DIST_DIR}/${RPM_ASSET}" \
    "${SCRIPT_DIR}/${RPM_ASSET}" \
    ./TundraDrive-*.rpm \
    "${DIST_DIR}"/TundraDrive-*.rpm; do
    if [[ -f "${candidate}" ]]; then
      readlink -f "${candidate}"
      return 0
    fi
  done
  return 1
}

download_release_asset() {
  local asset_name="$1"
  local dest_dir="${2:-${DOWNLOAD_DIR}}"
  mkdir -p "${dest_dir}"
  local dest_path="${dest_dir}/${asset_name}"
  local url="${RELEASE_BASE_URL}/${asset_name}"

  echo -e "${CYAN}==> Baixando ${asset_name} de ${url}...${RESET}" >&2
  if command -v curl >/dev/null 2>&1 && curl -fL --progress-bar "${url}" -o "${dest_path}.tmp"; then
    mv -f "${dest_path}.tmp" "${dest_path}"
    readlink -f "${dest_path}"
    return 0
  fi
  rm -f "${dest_path}.tmp"

  if command -v wget >/dev/null 2>&1 && wget -q --show-progress -O "${dest_path}.tmp" "${url}"; then
    mv -f "${dest_path}.tmp" "${dest_path}"
    readlink -f "${dest_path}"
    return 0
  fi
  rm -f "${dest_path}.tmp"

  if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
    echo -e "${YELLOW}==> Tentando download autenticado via GitHub CLI (gh)...${RESET}" >&2
    if gh release download "${RELEASE_TAG}" -R "${REPO_SLUG}" -p "${asset_name}" -D "${dest_dir}" --clobber; then
      readlink -f "${dest_path}"
      return 0
    fi
  fi

  return 1
}

resolve_or_fetch_appimage() {
  local found
  found="$(find_appimage || true)"
  if [[ -n "${found}" ]]; then
    echo "${found}"
    return 0
  fi

  if download_release_asset "${APPIMAGE_ASSET}"; then
    return 0
  fi

  if [[ -x "${SCRIPT_DIR}/build-appimage.sh" ]]; then
    echo -e "${YELLOW}==> Gerando ${APPIMAGE_ASSET} localmente pelo código-fonte...${RESET}" >&2
    "${SCRIPT_DIR}/build-appimage.sh" >&2
    find_appimage
    return 0
  fi

  echo -e "${RED}ERRO: Não foi possível baixar nem localizar ${APPIMAGE_ASSET}.${RESET}" >&2
  return 1
}

resolve_or_fetch_rpm() {
  local found
  found="$(find_rpm || true)"
  if [[ -n "${found}" ]]; then
    echo "${found}"
    return 0
  fi

  if download_release_asset "${RPM_ASSET}"; then
    return 0
  fi

  if [[ -x "${SCRIPT_DIR}/build-rpm.sh" ]]; then
    echo -e "${YELLOW}==> Gerando ${RPM_ASSET} localmente pelo código-fonte...${RESET}" >&2
    "${SCRIPT_DIR}/build-rpm.sh" >&2
    find_rpm
    return 0
  fi

  echo -e "${RED}ERRO: Não foi possível baixar nem localizar ${RPM_ASSET}.${RESET}" >&2
  return 1
}

launch_gui_installer() {
  local publish_bin="${REPO_ROOT}/packaging/linux/build/publish/TundraDrive"
  local debug_bin="${REPO_ROOT}/src/TundraDrive.Desktop/bin/Debug/net10.0/TundraDrive"
  local gui_cache_dir="${XDG_CACHE_HOME:-${HOME}/.cache}/tundradrive-installer"
  local cached_appimage="${gui_cache_dir}/${APPIMAGE_ASSET}"
  local appimage_bin=""

  if [[ -x "${publish_bin}" ]]; then
    echo -e "${CYAN}==> Abrindo Instalador Gráfico TundraDrive by TundraLab...${RESET}"
    exec "${publish_bin}" --installer "$@"
  fi

  if [[ -n "${TERMUX_VERSION:-}" ]] || [[ "$(uname -m)" != "x86_64" ]]; then
    return 1
  fi

  appimage_bin="$(find_appimage || true)"
  if [[ -z "${appimage_bin}" ]] && [[ -f "${cached_appimage}" ]]; then
    appimage_bin="${cached_appimage}"
  fi

  if [[ -z "${appimage_bin}" ]]; then
    echo -e "${CYAN}==> Preparando interface gráfica do Instalador TundraDrive by TundraLab...${RESET}"
    appimage_bin="$(download_release_asset "${APPIMAGE_ASSET}" "${gui_cache_dir}" || true)"
    if [[ -z "${appimage_bin}" ]]; then
      appimage_bin="$(resolve_or_fetch_appimage || true)"
    fi
  fi

  if [[ -n "${appimage_bin}" ]]; then
    chmod +x "${appimage_bin}"
    echo -e "${CYAN}==> Abrindo Instalador Gráfico TundraDrive by TundraLab...${RESET}"
    exec "${appimage_bin}" --appimage-extract-and-run --installer "$@"
  elif [[ -x "${debug_bin}" ]]; then
    echo -e "${CYAN}==> Abrindo Instalador Gráfico TundraDrive by TundraLab...${RESET}"
    exec "${debug_bin}" --installer "$@"
  fi
  return 1
}

install_appimage_cli() {
  local appimage_path
  appimage_path="$(resolve_or_fetch_appimage)"

  local target_dir="${HOME}/.local/opt/tundradrive"
  local target_bin="${target_dir}/TundraDrive.AppImage"
  local local_bin_dir="${HOME}/.local/bin"
  local apps_dir="${HOME}/.local/share/applications"
  local icon_png_dir="${HOME}/.local/share/icons/hicolor/256x256/apps"
  local icon_svg_dir="${HOME}/.local/share/icons/hicolor/scalable/apps"

  mkdir -p "${target_dir}" "${local_bin_dir}" "${apps_dir}" "${icon_png_dir}" "${icon_svg_dir}"
  cp -f "${appimage_path}" "${target_bin}"
  chmod 755 "${target_bin}"

  cat > "${local_bin_dir}/tundradrive" <<EOF
#!/usr/bin/env bash
exec "${target_bin}" "\$@"
EOF
  chmod 755 "${local_bin_dir}/tundradrive"

  if [[ -f "${REPO_ROOT}/src/TundraDrive.Desktop/Assets/tundradrive-icon.png" ]]; then
    cp -f "${REPO_ROOT}/src/TundraDrive.Desktop/Assets/tundradrive-icon.png" "${icon_png_dir}/tundradrive.png"
    cp -f "${REPO_ROOT}/src/TundraDrive.Desktop/Assets/tundradrive-icon.svg" "${icon_svg_dir}/tundradrive.svg"
  fi

  cat > "${apps_dir}/tundradrive.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=TundraDrive
GenericName=Google Drive Client
Comment=TundraDrive by TundraLab - Cliente do Google Drive para Linux
Icon=tundradrive
Exec="${target_bin}"
Terminal=false
Categories=Network;FileTransfer;Utility;
StartupWMClass=TundraDrive
X-TundraLab-Vendor=TundraLab
EOF
  chmod 755 "${apps_dir}/tundradrive.desktop"

  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "${apps_dir}" || true
  command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t "${HOME}/.local/share/icons/hicolor" || true
  command -v kbuildsycoca6 >/dev/null 2>&1 && kbuildsycoca6 --noincremental || true

  echo -e "${GREEN}==> AppImage TundraDrive by TundraLab instalado com sucesso em ${target_bin}!${RESET}"
}

install_rpm_cli() {
  local rpm_path
  rpm_path="$(resolve_or_fetch_rpm)"

  echo -e "${CYAN}==> Instalando pacote RPM: ${rpm_path}${RESET}"
  if command -v dnf >/dev/null 2>&1; then
    sudo dnf install -y "${rpm_path}"
  else
    sudo rpm -Uvh --replacepkgs "${rpm_path}"
  fi
  echo -e "${GREEN}==> Pacote RPM TundraDrive by TundraLab instalado com sucesso!${RESET}"
}

uninstall_cli() {
  echo -e "${YELLOW}==> Removendo instalação do TundraDrive...${RESET}"
  rm -rf "${HOME}/.local/opt/tundradrive"
  rm -f "${HOME}/.local/bin/tundradrive" \
        "${HOME}/.local/share/applications/tundradrive.desktop" \
        "${HOME}/.config/autostart/tundradrive.desktop"
  if command -v rpm >/dev/null 2>&1 && rpm -q tundradrive >/dev/null 2>&1; then
    sudo rpm -e tundradrive
  fi
  echo -e "${GREEN}==> TundraDrive removido com sucesso.${RESET}"
}

MODE="${1:---gui}"
print_banner

case "${MODE}" in
  --gui)
    if [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]]; then
      if launch_gui_installer; then
        exit 0
      fi
    fi
    ;;
  --cli)
    ;;
  --appimage)
    install_appimage_cli
    exit 0
    ;;
  --rpm)
    install_rpm_cli
    exit 0
    ;;
  --download-appimage)
    resolve_or_fetch_appimage
    exit 0
    ;;
  --download-rpm)
    resolve_or_fetch_rpm
    exit 0
    ;;
  --download-only)
    resolve_or_fetch_appimage
    resolve_or_fetch_rpm
    exit 0
    ;;
  --uninstall)
    uninstall_cli
    exit 0
    ;;
  --build-all)
    "${SCRIPT_DIR}/build-appimage.sh"
    FORCE_PUBLISH=0 "${SCRIPT_DIR}/build-rpm.sh"
    exit 0
    ;;
esac

echo "Escolha o modo de instalação:"
echo "  1) Baixar e instalar Pacote .AppImage Integrado (Usuário local, sem root)"
echo "  2) Baixar e instalar Pacote Nativo .rpm (Sistema Fedora / RHEL / openSUSE)"
echo "  3) Apenas baixar ambos os pacotes (.AppImage e .rpm) no diretório atual"
echo "  4) Desinstalar TundraDrive"
echo "  0) Sair"
read -rp "Opção [1]: " CHOICE
CHOICE="${CHOICE:-1}"

case "${CHOICE}" in
  1) install_appimage_cli ;;
  2) install_rpm_cli ;;
  3)
    resolve_or_fetch_appimage
    resolve_or_fetch_rpm
    ;;
  4) uninstall_cli ;;
  *) exit 0 ;;
esac
