#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# Instalador Proprietário TundraDrive by TundraLab (Linux .rpm & .AppImage)
# Desenvolvedor: Gustavo Henrique Novaes Gomes (@GustaHerin) • TundraLab
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." 2>/dev/null && pwd || pwd)"
DIST_DIR="${REPO_ROOT}/dist"
VERSION="2.6.0"

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
  echo -e "   Repositório:   https://github.com/TundraLab/TundraDrive"
  echo -e "${BLUE}======================================================================${RESET}"
}

find_appimage() {
  for candidate in \
    "${DIST_DIR}/TundraDrive-${VERSION}-x86_64.AppImage" \
    "${SCRIPT_DIR}/TundraDrive-${VERSION}-x86_64.AppImage" \
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
    "${DIST_DIR}/TundraDrive-${VERSION}-1.x86_64.rpm" \
    "${SCRIPT_DIR}/TundraDrive-${VERSION}-1.x86_64.rpm" \
    ./TundraDrive-*.rpm \
    "${DIST_DIR}"/TundraDrive-*.rpm; do
    if [[ -f "${candidate}" ]]; then
      readlink -f "${candidate}"
      return 0
    fi
  done
  return 1
}

launch_gui_installer() {
  local publish_bin="${REPO_ROOT}/packaging/linux/build/publish/TundraDrive"
  local debug_bin="${REPO_ROOT}/src/TundraDrive.Desktop/bin/Debug/net10.0/TundraDrive"
  local appimage_bin=""
  appimage_bin="$(find_appimage || true)"

  if [[ -x "${publish_bin}" ]]; then
    echo -e "${CYAN}==> Abrindo Instalador Gráfico TundraDrive by TundraLab...${RESET}"
    exec "${publish_bin}" --installer "$@"
  elif [[ -n "${appimage_bin}" && -x "${appimage_bin}" ]]; then
    echo -e "${CYAN}==> Abrindo Instalador Gráfico TundraDrive by TundraLab (via AppImage)...${RESET}"
    exec "${appimage_bin}" --appimage-extract-and-run --installer "$@"
  elif [[ -x "${debug_bin}" ]]; then
    echo -e "${CYAN}==> Abrindo Instalador Gráfico TundraDrive by TundraLab...${RESET}"
    exec "${debug_bin}" --installer "$@"
  fi
  return 1
}

install_appimage_cli() {
  local appimage_path
  appimage_path="$(find_appimage || true)"
  if [[ -z "${appimage_path}" ]]; then
    echo -e "${YELLOW}==> Pacote .AppImage ainda não encontrado em dist/. Gerando agora...${RESET}"
    "${SCRIPT_DIR}/build-appimage.sh"
    appimage_path="$(find_appimage)"
  fi

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
Icon=${icon_png_dir}/tundradrive.png
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
  rpm_path="$(find_rpm || true)"
  if [[ -z "${rpm_path}" ]]; then
    echo -e "${YELLOW}==> Pacote .rpm ainda não encontrado em dist/. Gerando agora...${RESET}"
    "${SCRIPT_DIR}/build-rpm.sh"
    rpm_path="$(find_rpm)"
  fi

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

case "${MODE}" in
  --gui)
    print_banner
    if [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]]; then
      if launch_gui_installer; then
        exit 0
      fi
    fi
    ;;
  --appimage)
    print_banner
    install_appimage_cli
    exit 0
    ;;
  --rpm)
    print_banner
    install_rpm_cli
    exit 0
    ;;
  --uninstall)
    print_banner
    uninstall_cli
    exit 0
    ;;
  --build-all)
    print_banner
    "${SCRIPT_DIR}/build-appimage.sh"
    FORCE_PUBLISH=0 "${SCRIPT_DIR}/build-rpm.sh"
    exit 0
    ;;
esac

print_banner
echo "Escolha o modo de instalação:"
echo "  1) Instalar Pacote .AppImage Integrado (Usuário local, sem root)"
echo "  2) Instalar Pacote Nativo .rpm (Sistema Fedora / RHEL / openSUSE)"
echo "  3) Compilar/Gerar pacotes .rpm e .AppImage em dist/"
echo "  4) Desinstalar TundraDrive"
echo "  0) Sair"
read -rp "Opção [1]: " CHOICE
CHOICE="${CHOICE:-1}"

case "${CHOICE}" in
  1) install_appimage_cli ;;
  2) install_rpm_cli ;;
  3)
    "${SCRIPT_DIR}/build-appimage.sh"
    FORCE_PUBLISH=0 "${SCRIPT_DIR}/build-rpm.sh"
    ;;
  4) uninstall_cli ;;
  *) exit 0 ;;
esac
