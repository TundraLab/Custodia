#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/src/TundraDrive.Desktop/TundraDrive.Desktop.csproj"
ASSETS_DIR="${REPO_ROOT}/src/TundraDrive.Desktop/Assets"

VERSION="${TUNDRA_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${PROJECT_PATH}" | head -n 1)}"
VERSION="${VERSION:-2.6.0}"
RELEASE="1"
ARCH="x86_64"

BUILD_ROOT="${REPO_ROOT}/packaging/linux/build"
PUBLISH_DIR="${BUILD_ROOT}/publish"
RPM_STAGE="${BUILD_ROOT}/rpm-stage"
DIST_DIR="${REPO_ROOT}/dist"
OUT_RPM="${DIST_DIR}/TundraDrive-${VERSION}-${RELEASE}.${ARCH}.rpm"

mkdir -p "${PUBLISH_DIR}" "${DIST_DIR}"

if [[ ! -x "${PUBLISH_DIR}/TundraDrive" ]] || [[ "${FORCE_PUBLISH:-1}" == "1" ]]; then
  echo "==> [TundraDrive by TundraLab] Publicando TundraDrive ${VERSION} (linux-x64 self-contained)..."
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

rm -rf "${RPM_STAGE}"
mkdir -p "${RPM_STAGE}/opt/tundradrive" \
         "${RPM_STAGE}/usr/bin" \
         "${RPM_STAGE}/usr/share/applications" \
         "${RPM_STAGE}/usr/share/icons/hicolor/256x256/apps" \
         "${RPM_STAGE}/usr/share/icons/hicolor/scalable/apps" \
         "${RPM_STAGE}/usr/share/licenses/tundradrive"

cp -a "${PUBLISH_DIR}/." "${RPM_STAGE}/opt/tundradrive/"
chmod 755 "${RPM_STAGE}/opt/tundradrive/TundraDrive"

cat > "${RPM_STAGE}/usr/bin/tundradrive" <<'EOF'
#!/usr/bin/env bash
exec "/opt/tundradrive/TundraDrive" "$@"
EOF
chmod 755 "${RPM_STAGE}/usr/bin/tundradrive"

cat > "${RPM_STAGE}/usr/bin/tundradrive-installer" <<'EOF'
#!/usr/bin/env bash
exec "/opt/tundradrive/TundraDrive" --installer "$@"
EOF
chmod 755 "${RPM_STAGE}/usr/bin/tundradrive-installer"

cp "${ASSETS_DIR}/tundradrive-icon.png" "${RPM_STAGE}/usr/share/icons/hicolor/256x256/apps/tundradrive.png"
cp "${ASSETS_DIR}/tundradrive-icon.svg" "${RPM_STAGE}/usr/share/icons/hicolor/scalable/apps/tundradrive.svg"
cp "${REPO_ROOT}/LICENSE" "${RPM_STAGE}/usr/share/licenses/tundradrive/LICENSE"

cat > "${RPM_STAGE}/usr/share/applications/tundradrive.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Version=1.0
Name=TundraDrive
GenericName=Google Drive Client
Comment=TundraDrive by TundraLab - Cliente do Google Drive para Linux
Exec=/usr/bin/tundradrive
Icon=tundradrive
Terminal=false
Categories=Network;FileTransfer;Utility;
StartupWMClass=TundraDrive
X-TundraLab-Vendor=TundraLab
EOF

cat > "${BUILD_ROOT}/postinstall.sh" <<'EOF'
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
    echo "==> [TundraDrive by TundraLab] Baixando empacotador RPM nfpm v${NFPM_VERSION}..."
    TMP_TAR="${CACHE_DIR}/nfpm.tar.gz"
    curl -fsSL -L -o "${TMP_TAR}" \
      "https://github.com/goreleaser/nfpm/releases/download/v${NFPM_VERSION}/nfpm_${NFPM_VERSION}_Linux_x86_64.tar.gz"
    tar -xzf "${TMP_TAR}" -C "${CACHE_DIR}" nfpm
    mv "${CACHE_DIR}/nfpm" "${NFPM_BIN}"
    chmod +x "${NFPM_BIN}"
    rm -f "${TMP_TAR}"
  fi
fi

cat > "${BUILD_ROOT}/nfpm.yaml" <<EOF
name: "tundradrive"
arch: "${ARCH}"
platform: "linux"
version: "${VERSION}"
release: "${RELEASE}"
section: "utils"
priority: "optional"
maintainer: "TundraLab <Gustavo Henrique Novaes Gomes>"
vendor: "TundraLab"
homepage: "https://github.com/TundraLab/TundraDrive"
license: "MIT"
description: |
  TundraDrive by TundraLab - Cliente nativo do Google Drive para Linux.
  Sincronizacao bidirecional, backup unidirecional, gerenciador multimidia e API REST local.
  Idealizado e desenvolvido por Gustavo Henrique Novaes Gomes (@GustaHerin) na TundraLab.
rpm:
  summary: "TundraDrive by TundraLab - Cliente do Google Drive para Linux"
  packager: "TundraLab <Gustavo Henrique Novaes Gomes>"
  compression: "gzip"
contents:
  - src: "${RPM_STAGE}/opt/tundradrive"
    dst: "/opt/tundradrive"
  - src: "${RPM_STAGE}/usr/bin/tundradrive"
    dst: "/usr/bin/tundradrive"
    file_info:
      mode: 0755
  - src: "${RPM_STAGE}/usr/bin/tundradrive-installer"
    dst: "/usr/bin/tundradrive-installer"
    file_info:
      mode: 0755
  - src: "${RPM_STAGE}/usr/share/applications/tundradrive.desktop"
    dst: "/usr/share/applications/tundradrive.desktop"
    file_info:
      mode: 0644
  - src: "${RPM_STAGE}/usr/share/icons/hicolor/256x256/apps/tundradrive.png"
    dst: "/usr/share/icons/hicolor/256x256/apps/tundradrive.png"
    file_info:
      mode: 0644
  - src: "${RPM_STAGE}/usr/share/icons/hicolor/scalable/apps/tundradrive.svg"
    dst: "/usr/share/icons/hicolor/scalable/apps/tundradrive.svg"
    file_info:
      mode: 0644
  - src: "${RPM_STAGE}/usr/share/licenses/tundradrive/LICENSE"
    dst: "/usr/share/licenses/tundradrive/LICENSE"
    file_info:
      mode: 0644
scripts:
  postinstall: "${BUILD_ROOT}/postinstall.sh"
  postremove: "${BUILD_ROOT}/postinstall.sh"
EOF

echo "==> [TundraDrive by TundraLab] Gerando pacote RPM: ${OUT_RPM}..."
rm -f "${OUT_RPM}"
"${NFPM_BIN}" package --config "${BUILD_ROOT}/nfpm.yaml" --packager rpm --target "${OUT_RPM}"

echo "==> Pacote RPM pronto: ${OUT_RPM} ($(du -h "${OUT_RPM}" | cut -f1))"
if command -v rpm >/dev/null 2>&1; then
  rpm -qip "${OUT_RPM}" | head -n 20
fi
