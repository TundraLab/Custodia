#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/src/TundraDrive.Desktop/TundraDrive.Desktop.csproj"
if [[ ! -f "${PROJECT_PATH}" ]]; then
  PROJECT_PATH="${REPO_ROOT}/src/Custodia.Desktop/Custodia.Desktop.csproj"
fi
ASSETS_DIR="$(dirname "${PROJECT_PATH}")/Assets"

VERSION="${CUSTODIA_VERSION:-${TUNDRA_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${PROJECT_PATH}" | head -n 1)}}"
VERSION="${VERSION:-2.8.0}"
ARCH="x86_64"

BUILD_ROOT="${REPO_ROOT}/packaging/linux/build"
PUBLISH_DIR="${BUILD_ROOT}/publish"
APPDIR="${BUILD_ROOT}/Custodia.AppDir"
DIST_DIR="${REPO_ROOT}/dist"
OUT_APPIMAGE="${DIST_DIR}/Custodia-${VERSION}-${ARCH}.AppImage"

rm -rf "${APPDIR}"
mkdir -p "${PUBLISH_DIR}" "${APPDIR}/usr/bin" \
         "${APPDIR}/usr/share/applications" \
         "${APPDIR}/usr/share/icons/hicolor/256x256/apps" \
         "${APPDIR}/usr/share/icons/hicolor/scalable/apps" \
         "${DIST_DIR}"

if [[ ! -x "${PUBLISH_DIR}/Custodia" ]] || [[ "${FORCE_PUBLISH:-1}" == "1" ]]; then
  echo "==> [Custódia by TundraLab] Publicando Custódia ${VERSION} (linux-x64 self-contained)..."
  rm -rf "${PUBLISH_DIR}"
  mkdir -p "${PUBLISH_DIR}"
  dotnet publish "${PROJECT_PATH}" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "${PUBLISH_DIR}"
fi

cp -a "${PUBLISH_DIR}/." "${APPDIR}/usr/bin/"
chmod +x "${APPDIR}/usr/bin/Custodia"

cp "${ASSETS_DIR}/custodia-icon.png" "${APPDIR}/custodia.png"
cp "${ASSETS_DIR}/custodia-icon.png" "${APPDIR}/.DirIcon"
cp "${ASSETS_DIR}/custodia-icon.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/custodia.png"
cp "${ASSETS_DIR}/custodia-icon.svg" "${APPDIR}/usr/share/icons/hicolor/scalable/apps/custodia.svg"

cat > "${APPDIR}/custodia.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Version=1.0
Name=Custódia
GenericName=Google Drive Client
Comment=Custódia by TundraLab - Seus arquivos. Sempre seguros.
Exec=Custodia
Icon=custodia
Terminal=false
Categories=Network;Utility;
StartupWMClass=Custodia
X-TundraLab-Vendor=TundraLab
EOF

cp "${APPDIR}/custodia.desktop" "${APPDIR}/usr/share/applications/custodia.desktop"

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "${APPDIR}/custodia.desktop"
fi

cat > "${APPDIR}/AppRun" <<'EOF'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "${0}")")"
export PATH="${HERE}/usr/bin:${PATH}"
exec "${HERE}/usr/bin/Custodia" "$@"
EOF
chmod -R a+rX "${APPDIR}"
chmod 755 "${APPDIR}/AppRun" "${APPDIR}/usr/bin/Custodia"

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
echo "==> AppImage pronto: ${OUT_APPIMAGE} ($(du -h "${OUT_APPIMAGE}" | cut -f1))"
