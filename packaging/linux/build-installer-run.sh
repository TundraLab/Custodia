#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/src/Custodia.Desktop/Custodia.Desktop.csproj"

VERSION="${CUSTODIA_VERSION:-${TUNDRA_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${PROJECT_PATH}" | head -n 1)}}"
VERSION="${VERSION:-4.0.0}"
ARCH="x86_64"

BUILD_ROOT="${REPO_ROOT}/packaging/linux/build"
PUBLISH_DIR="${BUILD_ROOT}/publish"
DIST_DIR="${REPO_ROOT}/dist"
PAYLOAD_TAR="${BUILD_ROOT}/custodia-installer-payload.tar.gz"
OUT_INSTALLER_RUN="${DIST_DIR}/Custodia-Installer-${ARCH}.run"

mkdir -p "${PUBLISH_DIR}" "${DIST_DIR}"

if [[ ! -x "${PUBLISH_DIR}/Custodia" ]] || [[ "${FORCE_PUBLISH:-1}" == "1" ]]; then
  echo "==> [Custódia by TundraLab] Publicando instalador gráfico ${VERSION} (linux-x64 self-contained)..."
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

echo "==> [Custódia by TundraLab] Empacotando instalador nativo autodescompactável ${OUT_INSTALLER_RUN}..."
rm -f "${PAYLOAD_TAR}" "${OUT_INSTALLER_RUN}"
tar -czf "${PAYLOAD_TAR}" -C "${PUBLISH_DIR}" .

cat > "${OUT_INSTALLER_RUN}" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

SELF_PATH="$(readlink -f "$0" 2>/dev/null || realpath "$0" 2>/dev/null || printf '%s' "$0")"
TMP_DIR="$(mktemp -d /tmp/custodia-installer.XXXXXX)"
trap 'rm -rf "${TMP_DIR}"' EXIT

ARCHIVE_LINE="$(awk '/^__CUSTODIA_ARCHIVE_BELOW__$/ {print NR + 1; exit 0;}' "$0")"
tail -n "+${ARCHIVE_LINE}" "$0" | tar -xzf - -C "${TMP_DIR}"
chmod +x "${TMP_DIR}/Custodia"

export CUSTODIA_INSTALLER_SELF_PATH="${SELF_PATH}"
"${TMP_DIR}/Custodia" --installer "$@"
exit $?
__CUSTODIA_ARCHIVE_BELOW__
EOF

cat "${PAYLOAD_TAR}" >> "${OUT_INSTALLER_RUN}"
chmod +x "${OUT_INSTALLER_RUN}"
rm -f "${PAYLOAD_TAR}"

echo "==> Instalador nativo pronto: ${OUT_INSTALLER_RUN} ($(du -h "${OUT_INSTALLER_RUN}" | cut -f1))"
