#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
DIST_DIR="${REPO_ROOT}/dist"
SRC_STUB="${SCRIPT_DIR}/elf-launcher.S"
SRC_INSTALLER="${SCRIPT_DIR}/custodia-installer.py"
OUT_INSTALLER_RUN="${DIST_DIR}/Custodia-Installer-x86_64.run"

mkdir -p "${DIST_DIR}"

echo "==> [Custódia by TundraLab] Compilando instalador nativo leve ${OUT_INSTALLER_RUN}..."
python3 -m py_compile "${SRC_INSTALLER}"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

as --64 "${SRC_STUB}" -o "${TMP_DIR}/elf-launcher.o"
ld -static --strip-all "${TMP_DIR}/elf-launcher.o" -o "${TMP_DIR}/launcher.elf"

cat "${TMP_DIR}/launcher.elf" > "${OUT_INSTALLER_RUN}"
printf '\n#===CUSTODIA_PAYLOAD===\n' >> "${OUT_INSTALLER_RUN}"
cat "${SRC_INSTALLER}" >> "${OUT_INSTALLER_RUN}"
chmod +x "${OUT_INSTALLER_RUN}"

echo "==> Instalador pronto: ${OUT_INSTALLER_RUN} ($(du -h "${OUT_INSTALLER_RUN}" | cut -f1), $(xdg-mime query filetype "${OUT_INSTALLER_RUN}" 2>/dev/null || echo 'application/x-executable'))"
