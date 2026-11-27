#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/src/Custodia.Desktop/Custodia.Desktop.csproj"
ASSETS_DIR="$(dirname "${PROJECT_PATH}")/Assets"

VERSION="${CUSTODIA_VERSION:-${TUNDRA_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${PROJECT_PATH}" | head -n 1)}}"
VERSION="${VERSION:-4.0.0}"
ARCH="amd64"

BUILD_ROOT="${REPO_ROOT}/packaging/linux/build"
PUBLISH_DIR="${BUILD_ROOT}/publish"
DEB_STAGE="${BUILD_ROOT}/deb-stage"
DIST_DIR="${REPO_ROOT}/dist"
OUT_DEB="${DIST_DIR}/Custodia-${VERSION}_${ARCH}.deb"

mkdir -p "${PUBLISH_DIR}" "${DIST_DIR}"

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

rm -rf "${DEB_STAGE}"
mkdir -p "${DEB_STAGE}/opt/custodia" \
         "${DEB_STAGE}/usr/bin" \
         "${DEB_STAGE}/usr/share/applications" \
         "${DEB_STAGE}/usr/share/icons/hicolor/256x256/apps" \
         "${DEB_STAGE}/usr/share/icons/hicolor/scalable/apps" \
         "${DEB_STAGE}/usr/share/licenses/custodia"

cp -a "${PUBLISH_DIR}/." "${DEB_STAGE}/opt/custodia/"
chmod 755 "${DEB_STAGE}/opt/custodia/Custodia"

cat > "${DEB_STAGE}/usr/bin/custodia" <<'EOF'
#!/usr/bin/env bash
exec "/opt/custodia/Custodia" "$@"
EOF
chmod 755 "${DEB_STAGE}/usr/bin/custodia"

cp "${ASSETS_DIR}/custodia-icon.png" "${DEB_STAGE}/usr/share/icons/hicolor/256x256/apps/custodia.png"
cp "${ASSETS_DIR}/custodia-icon.svg" "${DEB_STAGE}/usr/share/icons/hicolor/scalable/apps/custodia.svg"
cp "${REPO_ROOT}/LICENSE" "${DEB_STAGE}/usr/share/licenses/custodia/LICENSE"

cat > "${DEB_STAGE}/usr/share/applications/custodia.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Version=1.0
Name=Custódia
GenericName=Google Drive Client
Comment=Custódia by TundraLab - Seus arquivos. Sempre seguros.
Exec=/usr/bin/custodia
Icon=custodia
Terminal=false
Categories=Network;Utility;
StartupWMClass=Custodia
X-TundraLab-Vendor=TundraLab
EOF

cat > "${BUILD_ROOT}/postinstall-deb.sh" <<'EOF'
#!/bin/sh
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t -q /usr/share/icons/hicolor || true
fi
exit 0
EOF

NFPM_BIN=""
if command -v nfpm >/dev/null 2>&1; then
  NFPM_BIN="nfpm"
else
  CACHE_DIR="${HOME}/.cache/tundradrive-build"
  mkdir -p "${CACHE_DIR}"
  NFPM_VERSION="2.41.1"
  NFPM_BIN="${CACHE_DIR}/nfpm-${NFPM_VERSION}"
  if [[ ! -x "${NFPM_BIN}" ]]; then
    echo "==> [Custódia by TundraLab] Baixando empacotador DEB nfpm v${NFPM_VERSION}..."
    TMP_TAR="${CACHE_DIR}/nfpm.tar.gz"
    curl -fsSL -L -o "${TMP_TAR}" \
      "https://github.com/goreleaser/nfpm/releases/download/v${NFPM_VERSION}/nfpm_${NFPM_VERSION}_Linux_x86_64.tar.gz"
    tar -xzf "${TMP_TAR}" -C "${CACHE_DIR}" nfpm
    mv "${CACHE_DIR}/nfpm" "${NFPM_BIN}"
    chmod +x "${NFPM_BIN}"
    rm -f "${TMP_TAR}"
  fi
fi

cat > "${BUILD_ROOT}/nfpm-deb.yaml" <<EOF
name: "custodia"
arch: "${ARCH}"
platform: "linux"
version: "${VERSION}"
section: "utils"
priority: "optional"
maintainer: "TundraLab <Gustavo Henrique Novaes Gomes>"
vendor: "TundraLab"
homepage: "https://github.com/TundraLab/Custodia"
license: "Proprietary"
replaces:
  - tundradrive
provides:
  - tundradrive
conflicts:
  - tundradrive
description: |
  Custodia by TundraLab - Seus arquivos. Sempre seguros.
  Cliente nativo do Google Drive para Linux com sincronizacao bidirecional, backup unidirecional, gerenciador multimidia e API REST local.
contents:
  - src: "${DEB_STAGE}/opt/custodia"
    dst: "/opt/custodia"
  - src: "${DEB_STAGE}/usr/bin/custodia"
    dst: "/usr/bin/custodia"
    file_info:
      mode: 0755
  - src: "${DEB_STAGE}/usr/share/applications/custodia.desktop"
    dst: "/usr/share/applications/custodia.desktop"
    file_info:
      mode: 0644
  - src: "${DEB_STAGE}/usr/share/icons/hicolor/256x256/apps/custodia.png"
    dst: "/usr/share/icons/hicolor/256x256/apps/custodia.png"
    file_info:
      mode: 0644
  - src: "${DEB_STAGE}/usr/share/icons/hicolor/scalable/apps/custodia.svg"
    dst: "/usr/share/icons/hicolor/scalable/apps/custodia.svg"
    file_info:
      mode: 0644
  - src: "${DEB_STAGE}/usr/share/licenses/custodia/LICENSE"
    dst: "/usr/share/licenses/custodia/LICENSE"
    file_info:
      mode: 0644
scripts:
  postinstall: "${BUILD_ROOT}/postinstall-deb.sh"
  postremove: "${BUILD_ROOT}/postinstall-deb.sh"
EOF

echo "==> [Custódia by TundraLab] Gerando pacote DEB: ${OUT_DEB}..."
rm -f "${OUT_DEB}"
"${NFPM_BIN}" package --config "${BUILD_ROOT}/nfpm-deb.yaml" --packager deb --target "${OUT_DEB}"

echo "==> Pacote DEB pronto: ${OUT_DEB} ($(du -h "${OUT_DEB}" | cut -f1))"
