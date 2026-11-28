#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
DIST_DIR="${REPO_ROOT}/dist"
SRC_INSTALLER="${SCRIPT_DIR}/custodia-installer.py"
OUT_INSTALLER_RUN="${DIST_DIR}/Custodia-Installer-x86_64.run"

mkdir -p "${DIST_DIR}"

echo "==> [Custódia by TundraLab] Gerando instalador leve ${OUT_INSTALLER_RUN}..."
python3 -m py_compile "${SRC_INSTALLER}"
cp -f "${SRC_INSTALLER}" "${OUT_INSTALLER_RUN}"
chmod +x "${OUT_INSTALLER_RUN}"

echo "==> Instalador leve pronto: ${OUT_INSTALLER_RUN} ($(du -h "${OUT_INSTALLER_RUN}" | cut -f1))"
