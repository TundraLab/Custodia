#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ASSETS_DIR="${REPO_ROOT}/src/Custodia.Desktop/Assets"
SITE_BANNER="${REPO_ROOT}/site/wwwroot/assets/custodia-promo.jpg"
SRC_INSTALLER="${SCRIPT_DIR}/custodia-installer.py"
DIST_DIR="${REPO_ROOT}/dist"
OUT_APPIMAGE="${DIST_DIR}/Custodia-Installer-x86_64.AppImage"
ARCH="x86_64"

mkdir -p "${DIST_DIR}"
python3 -m py_compile "${SRC_INSTALLER}"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

APPDIR="${TMP_DIR}/CustodiaInstaller.AppDir"
mkdir -p "${APPDIR}/usr/bin" \
         "${APPDIR}/usr/share/applications" \
         "${APPDIR}/usr/share/icons/hicolor/256x256/apps" \
         "${APPDIR}/usr/share/icons/hicolor/scalable/apps"

cp -f "${SRC_INSTALLER}" "${APPDIR}/usr/bin/custodia-installer.py"
chmod 755 "${APPDIR}/usr/bin/custodia-installer.py"

if [[ -f "${ASSETS_DIR}/custodia-icon.png" ]]; then
  cp -f "${ASSETS_DIR}/custodia-icon.png" "${APPDIR}/custodia-installer.png"
  cp -f "${ASSETS_DIR}/custodia-icon.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/custodia-installer.png"
  cp -f "${ASSETS_DIR}/custodia-icon.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/com.tundralab.CustodiaInstaller.png"
fi
if [[ -f "${ASSETS_DIR}/custodia-icon.svg" ]]; then
  cp -f "${ASSETS_DIR}/custodia-icon.svg" "${APPDIR}/usr/share/icons/hicolor/scalable/apps/custodia-installer.svg"
  cp -f "${ASSETS_DIR}/custodia-icon.svg" "${APPDIR}/usr/share/icons/hicolor/scalable/apps/com.tundralab.CustodiaInstaller.svg"
fi
if [[ -f "${SITE_BANNER}" ]]; then
  cp -f "${SITE_BANNER}" "${APPDIR}/custodia-banner.jpg"
fi

cat > "${APPDIR}/custodia-installer.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Instalador do Custódia
Comment=Instale o Custódia no seu Linux
Exec=AppRun
Icon=custodia-installer
Terminal=false
Categories=Utility;
StartupWMClass=com.tundralab.CustodiaInstaller
EOF
cp -f "${APPDIR}/custodia-installer.desktop" "${APPDIR}/usr/share/applications/custodia-installer.desktop"

cat > "${APPDIR}/AppRun" <<'EOF'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "$0")")"
export XDG_DATA_DIRS="${HERE}/usr/share:${XDG_DATA_DIRS:-/usr/local/share:/usr/share}"
if [[ -f "${HERE}/custodia-installer.png" ]]; then
  export CUSTODIA_INSTALLER_ICON="${HERE}/custodia-installer.png"
elif [[ -f "${HERE}/usr/share/icons/hicolor/scalable/apps/custodia-installer.svg" ]]; then
  export CUSTODIA_INSTALLER_ICON="${HERE}/usr/share/icons/hicolor/scalable/apps/custodia-installer.svg"
fi
if [[ -f "${HERE}/custodia-banner.jpg" ]]; then
  export CUSTODIA_INSTALLER_BANNER="${HERE}/custodia-banner.jpg"
fi
exec /usr/bin/env python3 "${HERE}/usr/bin/custodia-installer.py" "$@"
EOF
chmod -R a+rX "${APPDIR}"
chmod 755 "${APPDIR}/AppRun"

APPIMAGETOOL=""
if command -v appimagetool >/dev/null 2>&1; then
  APPIMAGETOOL="appimagetool"
else
  CACHE_DIR="${HOME}/.cache/tundradrive-build"
  mkdir -p "${CACHE_DIR}"
  TOOL_BIN="${CACHE_DIR}/appimagetool-x86_64.AppImage"
  if [[ ! -x "${TOOL_BIN}" ]]; then
    echo "==> Baixando appimagetool-x86_64.AppImage..."
    curl -fsSL -L -o "${TOOL_BIN}" \
      "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "${TOOL_BIN}"
  fi
  APPIMAGETOOL="${TOOL_BIN}"
fi

echo "==> [Custódia by TundraLab] Gerando ${OUT_APPIMAGE}..."
rm -f "${OUT_APPIMAGE}"
if [[ "${APPIMAGETOOL}" == *.AppImage ]]; then
  ARCH="${ARCH}" "${APPIMAGETOOL}" --appimage-extract-and-run "${APPDIR}" "${OUT_APPIMAGE}"
else
  ARCH="${ARCH}" "${APPIMAGETOOL}" "${APPDIR}" "${OUT_APPIMAGE}"
fi

chmod +x "${OUT_APPIMAGE}"
echo "==> Instalador AppImage pronto: ${OUT_APPIMAGE} ($(du -h "${OUT_APPIMAGE}" | cut -f1))"
