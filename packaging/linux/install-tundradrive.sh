#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# TundraDrive v2.7.3 - Instalador Oficial de Terminal by TundraLab
# Autor e Desenvolvedor: Gustavo Henrique Novaes Gomes (@GustaHerin)
# Licença: Proprietária / Todos os Direitos Reservados
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." 2>/dev/null && pwd || pwd)"
DIST_DIR="${REPO_ROOT}/dist"

VERSION="${TUNDRA_VERSION:-2.7.3}"
REPO_SLUG="TundraLab/TundraDrive"
RELEASE_BASE_URL="https://github.com/${REPO_SLUG}/releases/latest/download"
RAW_BASE_URL="https://raw.githubusercontent.com/${REPO_SLUG}/main"
DOWNLOAD_DIR="${TUNDRA_DOWNLOAD_DIR:-${PWD}}"

resolve_latest_release_version() {
  if [[ -n "${TUNDRA_VERSION:-}" ]] || [[ "${TUNDRA_OFFLINE:-0}" == "1" ]]; then
    return 0
  fi

  if [[ -d "${DIST_DIR}" ]] && compgen -G "${DIST_DIR}/TundraDrive-*.rpm" >/dev/null 2>&1; then
    return 0
  fi

  if command -v curl >/dev/null 2>&1; then
    local latest_url tag
    latest_url="$(curl -fsSLI -o /dev/null -w '%{url_effective}' --max-time 3 "https://github.com/${REPO_SLUG}/releases/latest" 2>/dev/null || true)"
    tag="${latest_url##*/}"
    if [[ "${tag}" =~ ^v([0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.]+)?)$ ]]; then
      VERSION="${BASH_REMATCH[1]}"
    fi
  fi
}

resolve_latest_release_version
RELEASE_TAG="v${VERSION}"
APPIMAGE_ASSET="TundraDrive-${VERSION}-x86_64.AppImage"
RPM_ASSET="TundraDrive-${VERSION}-1.x86_64.rpm"
SUMS_ASSET="SHA256SUMS.txt"

if [[ -t 1 ]] && [[ -z "${NO_COLOR:-}" ]]; then
  BOLD="\033[1m"
  DIM="\033[2m"
  BLUE="\033[38;5;39m"
  INDIGO="\033[38;5;75m"
  CYAN="\033[38;5;51m"
  GREEN="\033[38;5;82m"
  YELLOW="\033[38;5;220m"
  RED="\033[38;5;196m"
  WHITE="\033[97m"
  RESET="\033[0m"
else
  BOLD="" DIM="" BLUE="" INDIGO="" CYAN="" GREEN="" YELLOW="" RED="" WHITE="" RESET=""
fi

detect_os_name() {
  if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    echo "${PRETTY_NAME:-${NAME:-Linux}}"
  elif [[ -n "${TERMUX_VERSION:-}" ]]; then
    echo "Android Termux ${TERMUX_VERSION}"
  else
    uname -s
  fi
}

detect_desktop_env() {
  if [[ -n "${TERMUX_VERSION:-}" ]]; then
    echo "Termux (CLI)"
  elif [[ -n "${XDG_CURRENT_DESKTOP:-}" ]]; then
    echo "${XDG_CURRENT_DESKTOP}"
  elif [[ -n "${WAYLAND_DISPLAY:-}" || -n "${DISPLAY:-}" ]]; then
    echo "Desktop Linux"
  else
    echo "Terminal / SSH"
  fi
}

get_rpm_status() {
  if command -v rpm >/dev/null 2>&1 && rpm -q tundradrive >/dev/null 2>&1; then
    rpm -q --qf "Instalado (v%{VERSION}-%{RELEASE})" tundradrive 2>/dev/null || echo "Instalado"
  else
    echo "Não instalado"
  fi
}

get_appimage_status() {
  if [[ -x "${HOME}/.local/opt/tundradrive/TundraDrive.AppImage" ]]; then
    echo "Instalado (~/.local/opt/tundradrive)"
  else
    echo "Não instalado"
  fi
}

is_fedora_like() {
  command -v dnf >/dev/null 2>&1 || [[ -f /etc/fedora-release ]] || [[ -f /etc/redhat-release ]]
}

box_line() {
  local plain="$1"
  local styled="${2:-$1}"
  local inner_width=72
  local len=${#plain}
  local pad=$(( inner_width - len ))
  if (( pad < 0 )); then
    plain="${plain:0:$((inner_width - 3))}..."
    styled="${plain}"
    pad=0
  fi
  printf "${BLUE}│${RESET}  %b%*s${BLUE}│${RESET}\n" "${styled}" "${pad}" ""
}

print_header() {
  local clear_screen="${1:-0}"
  if [[ "${clear_screen}" == "1" ]] && [[ -t 1 ]] && command -v clear >/dev/null 2>&1; then
    clear || true
  fi

  local os_name de_name arch_name rpm_st app_st
  os_name="$(detect_os_name)"
  de_name="$(detect_desktop_env)"
  arch_name="$(uname -m)"
  rpm_st="$(get_rpm_status)"
  app_st="$(get_appimage_status)"

  local title_plain="TundraDrive v${VERSION}  •  Instalador Oficial by TundraLab"
  local title_styled="${BOLD}${WHITE}TundraDrive v${VERSION}${RESET}  ${CYAN}•  Instalador Oficial by TundraLab${RESET}"
  local dev_plain="Desenvolvedor: Gustavo Henrique Novaes Gomes (@GustaHerin)"
  local repo_plain="Repositório:   https://github.com/${REPO_SLUG}"
  local sys_plain="Sistema:   ${os_name} (${arch_name})  •  ${de_name}"
  local sys_styled="${INDIGO}Sistema:${RESET}   ${os_name} (${arch_name})  ${DIM}•${RESET}  ${de_name}"
  local pkg_plain="Status:    .rpm [${rpm_st}]  |  .AppImage [${app_st}]"
  local pkg_styled="${INDIGO}Status:${RESET}    .rpm [${rpm_st}]  ${DIM}|${RESET}  .AppImage [${app_st}]"

  echo -e "${BLUE}╭──────────────────────────────────────────────────────────────────────────╮${RESET}"
  box_line "${title_plain}" "${title_styled}"
  box_line "${dev_plain}" "${DIM}${dev_plain}${RESET}"
  box_line "${repo_plain}" "${DIM}${repo_plain}${RESET}"
  echo -e "${BLUE}├──────────────────────────────────────────────────────────────────────────┤${RESET}"
  box_line "${sys_plain}" "${sys_styled}"
  box_line "${pkg_plain}" "${pkg_styled}"
  echo -e "${BLUE}╰──────────────────────────────────────────────────────────────────────────╯${RESET}"
  echo ""
}

step_info() {
  echo -e "${CYAN}[▸]${RESET} ${BOLD}$1${RESET}"
}

step_ok() {
  echo -e "${GREEN}[✓]${RESET} $1"
}

step_warn() {
  echo -e "${YELLOW}[!]${RESET} $1"
}

step_err() {
  echo -e "${RED}[✗]${RESET} $1" >&2
}

ACTIVE_BG_PID=""
ACTIVE_TMP_STAGE=""
cleanup_ui() {
  if [[ -n "${ACTIVE_BG_PID:-}" ]]; then
    kill "${ACTIVE_BG_PID}" >/dev/null 2>&1 || true
  fi
  if [[ -n "${ACTIVE_TMP_STAGE:-}" && -d "${ACTIVE_TMP_STAGE}" ]]; then
    rm -rf "${ACTIVE_TMP_STAGE}" >/dev/null 2>&1 || true
  fi
  if [[ -t 2 ]]; then
    printf "\033[?25h" >&2 || true
  fi
}
trap cleanup_ui EXIT INT TERM

repeat_char() {
  local count="$1" ch="$2" out="" i
  for ((i = 0; i < count; i++)); do
    out+="${ch}"
  done
  printf '%s' "${out}"
}

run_with_spinner() {
  local label="$1"
  shift

  local tmp_log
  tmp_log="$(mktemp /tmp/tundra-step.XXXXXX)"
  "$@" >"${tmp_log}" 2>&1 &
  local cmd_pid=$!
  ACTIVE_BG_PID="${cmd_pid}"

  local frames=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
  local code_bars=('⣾' '⣽' '⣻' '⢿' '⡿' '⣟' '⣯' '⣷')
  local start_ts
  start_ts="$(date +%s)"
  local idx=0

  if [[ -t 2 ]]; then
    printf "\033[?25l" >&2 || true
    while kill -0 "${cmd_pid}" 2>/dev/null; do
      local now_ts elapsed f c
      now_ts="$(date +%s)"
      elapsed=$(( now_ts - start_ts ))
      f="${frames[$(( idx % ${#frames[@]} ))]}"
      c="${code_bars[$(( idx % ${#code_bars[@]} ))]}"
      printf "\r\033[K  ${CYAN}%s${RESET} ${DIM}%s${RESET}  %s ${DIM}(%02d:%02d)${RESET}" \
        "${f}" "${c}" "${label}" $(( elapsed / 60 )) $(( elapsed % 60 )) >&2
      idx=$(( idx + 1 ))
      sleep 0.1
    done
    printf "\r\033[K" >&2
    printf "\033[?25h" >&2 || true
  else
    echo -e "${CYAN}[▸]${RESET} ${label}..." >&2
  fi

  local rc=0
  wait "${cmd_pid}" || rc=$?
  ACTIVE_BG_PID=""
  local total_elapsed=$(( $(date +%s) - start_ts ))

  if (( rc != 0 )); then
    step_err "${label} falhou (código ${rc})."
    tail -n 15 "${tmp_log}" >&2 || true
    rm -f "${tmp_log}"
    return "${rc}"
  fi

  rm -f "${tmp_log}"
  step_ok "${label} ${DIM}(${total_elapsed}s)${RESET}"
  return 0
}

find_appimage() {
  for candidate in \
    "${DOWNLOAD_DIR}/${APPIMAGE_ASSET}" \
    "${DIST_DIR}/${APPIMAGE_ASSET}" \
    "${SCRIPT_DIR}/${APPIMAGE_ASSET}"; do
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
    "${SCRIPT_DIR}/${RPM_ASSET}"; do
    if [[ -f "${candidate}" ]]; then
      readlink -f "${candidate}"
      return 0
    fi
  done
  return 1
}

download_with_curl_animated() {
  local url="$1"
  local tmp_file="$2"
  local label="$3"

  local hdr_file err_file
  hdr_file="$(mktemp /tmp/tundra-hdr.XXXXXX)"
  err_file="$(mktemp /tmp/tundra-curl.XXXXXX)"
  : > "${tmp_file}"

  curl -fsSL --dump-header "${hdr_file}" "${url}" -o "${tmp_file}" 2>"${err_file}" &
  local dl_pid=$!
  ACTIVE_BG_PID="${dl_pid}"

  local frames=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
  local code_bars=('⣾' '⣽' '⣻' '⢿' '⡿' '⣟' '⣯' '⣷')
  local start_ts
  start_ts="$(date +%s)"
  local idx=0

  if [[ -t 2 ]]; then
    printf "\033[?25l" >&2 || true
    while kill -0 "${dl_pid}" 2>/dev/null; do
      local now_ts elapsed bytes total_bytes f c
      now_ts="$(date +%s)"
      elapsed=$(( now_ts - start_ts ))
      (( elapsed < 1 )) && elapsed=1
      if [[ -f "${tmp_file}" ]]; then
        bytes="$(stat -c%s "${tmp_file}" 2>/dev/null || echo 0)"
      else
        bytes=0
      fi
      total_bytes="$(awk 'tolower($1)=="content-length:" { v=$2 } END { gsub(/\r/, "", v); print v+0 }' "${hdr_file}" 2>/dev/null || echo 0)"
      f="${frames[$(( idx % ${#frames[@]} ))]}"
      c="${code_bars[$(( idx % ${#code_bars[@]} ))]}"

      if (( bytes <= 0 )); then
        printf "\r\033[K  ${CYAN}%s %s${RESET}  Conectando ao GitHub Releases... ${DIM}(%02d:%02d)${RESET}" \
          "${f}" "${c}" $(( elapsed / 60 )) $(( elapsed % 60 )) >&2
      elif (( total_bytes > 0 )); then
        local pct fill empty bar_filled bar_empty stats
        pct=$(( bytes * 100 / total_bytes ))
        (( pct > 100 )) && pct=100
        fill=$(( pct * 24 / 100 ))
        empty=$(( 24 - fill ))
        bar_filled="$(repeat_char "${fill}" "█")"
        bar_empty="$(repeat_char "${empty}" "░")"
        stats="$(awk -v b="${bytes}" -v t="${total_bytes}" -v e="${elapsed}" \
          'BEGIN { printf "%.1f / %.1f MB  •  %.1f MB/s", b/1048576, t/1048576, (b/1048576)/e }')"
        printf "\r\033[K  ${CYAN}%s %s${RESET}  [${GREEN}%s${DIM}%s${RESET}] ${BOLD}%3d%%${RESET}  %s" \
          "${f}" "${c}" "${bar_filled}" "${bar_empty}" "${pct}" "${stats}" >&2
      else
        local mb_line
        mb_line="$(awk -v b="${bytes}" -v e="${elapsed}" 'BEGIN { printf "%.1f MB  •  %.1f MB/s", b/1048576, (b/1048576)/e }')"
        printf "\r\033[K  ${CYAN}%s %s${RESET}  Baixando ${BOLD}%s${RESET}  %s ${DIM}(%02d:%02d)${RESET}" \
          "${f}" "${c}" "${label}" "${mb_line}" $(( elapsed / 60 )) $(( elapsed % 60 )) >&2
      fi

      idx=$(( idx + 1 ))
      sleep 0.1
    done
    printf "\r\033[K" >&2
    printf "\033[?25h" >&2 || true
  else
    echo -e "${CYAN}[▸]${RESET} Baixando ${BOLD}${label}${RESET}..." >&2
  fi

  local rc=0
  wait "${dl_pid}" || rc=$?
  ACTIVE_BG_PID=""
  rm -f "${hdr_file}"
  if (( rc != 0 )); then
    cat "${err_file}" >&2 || true
    rm -f "${err_file}"
    return "${rc}"
  fi
  rm -f "${err_file}"

  local final_bytes final_mb total_sec
  final_bytes="$(stat -c%s "${tmp_file}" 2>/dev/null || echo 0)"
  final_mb="$(awk -v b="${final_bytes}" 'BEGIN { printf "%.1f MB", b/1048576 }')"
  total_sec=$(( $(date +%s) - start_ts ))
  step_ok "Download concluído: ${BOLD}${label}${RESET} ${DIM}(${final_mb} em ${total_sec}s)${RESET}" >&2
  return 0
}

download_release_asset() {
  local asset_name="$1"
  local dest_dir="${2:-${DOWNLOAD_DIR}}"
  mkdir -p "${dest_dir}"
  local dest_path="${dest_dir}/${asset_name}"
  local url="${RELEASE_BASE_URL}/${asset_name}"

  echo -e "${CYAN}[▸]${RESET} Origem: ${DIM}${url}${RESET}" >&2
  if command -v curl >/dev/null 2>&1 && download_with_curl_animated "${url}" "${dest_path}.tmp" "${asset_name}"; then
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
    echo -e "${YELLOW}[!]${RESET} Tentando download autenticado via GitHub CLI (gh)..." >&2
    if run_with_spinner "Baixando ${asset_name} via gh" gh release download "${RELEASE_TAG}" -R "${REPO_SLUG}" -p "${asset_name}" -D "${dest_dir}" --clobber >&2; then
      readlink -f "${dest_path}"
      return 0
    fi
  fi

  return 1
}

verify_sha256_if_available() {
  local file_path="$1"
  local file_name
  file_name="$(basename "${file_path}")"
  local file_dir
  file_dir="$(dirname "${file_path}")"

  if ! command -v sha256sum >/dev/null 2>&1; then
    step_warn "sha256sum indisponível; pulando verificação de integridade."
    return 0
  fi

  local expected_hash=""
  local local_sums="${file_dir}/${SUMS_ASSET}"
  if [[ "${TUNDRA_VERIFY_REMOTE:-0}" != "1" ]] && [[ -f "${local_sums}" ]]; then
    expected_hash="$(awk -v name="${file_name}" '$2 == name { print $1; exit }' "${local_sums}")"
  fi

  if [[ -z "${expected_hash}" ]]; then
    local remote_sums
    remote_sums="$(mktemp /tmp/tundra-sha256.XXXXXX)"
    if curl -fsSL "${RELEASE_BASE_URL}/${SUMS_ASSET}" -o "${remote_sums}" 2>/dev/null; then
      expected_hash="$(awk -v name="${file_name}" '$2 == name { print $1; exit }' "${remote_sums}")"
      rm -f "${remote_sums}"
    else
      rm -f "${remote_sums}"
      step_warn "Não foi possível obter ${SUMS_ASSET}; seguindo sem verificação remota."
      return 0
    fi
  fi

  local actual_hash

  if [[ -z "${expected_hash}" ]]; then
    if [[ "${TUNDRA_SKIP_SHA256:-0}" == "1" ]]; then
      step_warn "Hash de ${file_name} não encontrado em ${SUMS_ASSET} (ignorado por TUNDRA_SKIP_SHA256=1)."
      return 0
    fi
    step_err "Hash de ${file_name} não encontrado em ${SUMS_ASSET}. Download recusado por segurança."
    return 1
  fi

  actual_hash="$(sha256sum "${file_path}" | awk '{ print $1 }')"
  if [[ "${expected_hash}" != "${actual_hash}" ]]; then
    step_err "Falha na verificação SHA-256 para ${file_name}!"
    step_err "Esperado: ${expected_hash}"
    step_err "Obtido:   ${actual_hash}"
    return 1
  fi

  step_ok "Integridade SHA-256 confirmada (${actual_hash:0:12}...)"
  return 0
}

resolve_or_fetch_asset() {
  local find_fn="$1"
  local asset_name="$2"
  local build_script="$3"
  local target_dir="${4:-${DOWNLOAD_DIR}}"

  local found
  found="$("${find_fn}" || true)"
  if [[ -n "${found}" ]]; then
    local found_dir
    found_dir="$(dirname "${found}")"
    if [[ "${found_dir}" == "${DIST_DIR}" ]] || TUNDRA_VERIFY_REMOTE=1 verify_sha256_if_available "${found}" >/dev/null 2>&1; then
      echo "${found}"
      return 0
    fi
    step_warn "Cópia local de ${asset_name} está desatualizada ou corrompida; baixando a versão oficial mais recente..." >&2
  fi

  if download_release_asset "${asset_name}" "${target_dir}"; then
    return 0
  fi

  if [[ -x "${SCRIPT_DIR}/${build_script}" ]]; then
    step_warn "Gerando ${asset_name} localmente pelo código-fonte..." >&2
    "${SCRIPT_DIR}/${build_script}" >&2
    "${find_fn}"
    return 0
  fi

  step_err "Não foi possível baixar nem localizar ${asset_name}."
  return 1
}

resolve_or_fetch_appimage() {
  resolve_or_fetch_asset find_appimage "${APPIMAGE_ASSET}" "build-appimage.sh" "${1:-${DOWNLOAD_DIR}}"
}

resolve_or_fetch_rpm() {
  resolve_or_fetch_asset find_rpm "${RPM_ASSET}" "build-rpm.sh" "${1:-${DOWNLOAD_DIR}}"
}

refresh_desktop_caches() {
  local apps_dir="${HOME}/.local/share/applications"
  local hicolor_dir="${HOME}/.local/share/icons/hicolor"
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "${apps_dir}" >/dev/null 2>&1 || true
  command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t "${hicolor_dir}" >/dev/null 2>&1 || true
  command -v kbuildsycoca6 >/dev/null 2>&1 && kbuildsycoca6 --noincremental >/dev/null 2>&1 || true
  command -v kbuildsycoca5 >/dev/null 2>&1 && kbuildsycoca5 --noincremental >/dev/null 2>&1 || true
}

write_user_desktop_files() {
  local exec_cmd="$1"
  local with_autostart="${2:-0}"

  local apps_dir="${HOME}/.local/share/applications"
  local autostart_dir="${HOME}/.config/autostart"
  local icon_png_dir="${HOME}/.local/share/icons/hicolor/256x256/apps"
  local icon_svg_dir="${HOME}/.local/share/icons/hicolor/scalable/apps"

  mkdir -p "${apps_dir}" "${icon_png_dir}" "${icon_svg_dir}"

  if [[ -f "${REPO_ROOT}/src/TundraDrive.Desktop/Assets/tundradrive-icon.png" ]]; then
    cp -f "${REPO_ROOT}/src/TundraDrive.Desktop/Assets/tundradrive-icon.png" "${icon_png_dir}/tundradrive.png"
    cp -f "${REPO_ROOT}/src/TundraDrive.Desktop/Assets/tundradrive-icon.svg" "${icon_svg_dir}/tundradrive.svg"
  elif command -v curl >/dev/null 2>&1; then
    curl -fsSL "${RAW_BASE_URL}/src/TundraDrive.Desktop/Assets/tundradrive-icon.png" -o "${icon_png_dir}/tundradrive.png" 2>/dev/null || true
    curl -fsSL "${RAW_BASE_URL}/src/TundraDrive.Desktop/Assets/tundradrive-icon.svg" -o "${icon_svg_dir}/tundradrive.svg" 2>/dev/null || true
  fi

  local icon_ref="tundradrive"
  if [[ -f "${icon_png_dir}/tundradrive.png" ]]; then
    icon_ref="${icon_png_dir}/tundradrive.png"
  fi

  local desktop_entry
  desktop_entry="[Desktop Entry]
Type=Application
Version=1.0
Name=TundraDrive
GenericName=Google Drive Client
Comment=TundraDrive by TundraLab - Cliente do Google Drive para Linux
Icon=${icon_ref}
Exec=${exec_cmd}
Terminal=false
Categories=Network;FileTransfer;Utility;
StartupWMClass=TundraDrive
X-TundraLab-Vendor=TundraLab"

  printf '%s\n' "${desktop_entry}" > "${apps_dir}/tundradrive.desktop"
  chmod 755 "${apps_dir}/tundradrive.desktop"

  if [[ "${with_autostart}" == "1" ]]; then
    mkdir -p "${autostart_dir}"
    printf '%s\n' "${desktop_entry}" > "${autostart_dir}/tundradrive.desktop"
    step_ok "Inicialização automática configurada em ${autostart_dir}/tundradrive.desktop"
  fi

  refresh_desktop_caches
}

prompt_yes_no() {
  local prompt_text="$1"
  local default_ans="${2:-S}"
  local reply=""
  read -rp "${prompt_text} " reply || true
  reply="${reply:-${default_ans}}"
  [[ "${reply}" =~ ^[SsYy]$ ]]
}

find_running_tundra_pids() {
  local uid self_pid
  uid="$(id -u)"
  self_pid="$$"
  {
    pgrep -u "${uid}" -x "TundraDrive" 2>/dev/null || true
    pgrep -u "${uid}" -f '(/opt/tundradrive/TundraDrive|/usr/bin/tundradrive|TundraDrive.*\.AppImage|/tmp/\.mount_Tundra)' 2>/dev/null || true
  } | awk -v self="${self_pid}" '$1 != self && $1 ~ /^[0-9]+$/ { print $1 }' | sort -u
}

stop_running_instance_if_any() {
  local uid sock_path pids
  uid="$(id -u)"
  sock_path="${XDG_RUNTIME_DIR:-/tmp}/tundradrive.sock"
  [[ -S "${sock_path}" ]] || sock_path="/tmp/tundradrive-${uid}.sock"

  if [[ -S "${sock_path}" ]] && command -v python3 >/dev/null 2>&1; then
    python3 -c '
import socket, sys
try:
    s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    s.settimeout(1.0)
    s.connect(sys.argv[1])
    s.sendall(b"QUIT\n")
    s.recv(16)
    s.close()
except Exception:
    pass
' "${sock_path}" >/dev/null 2>&1 || true
    sleep 0.5
  fi

  pids="$(find_running_tundra_pids)"
  if [[ -z "${pids}" ]]; then
    return 0
  fi

  step_info "Encerrando instância em execução do TundraDrive..."
  # shellcheck disable=SC2086
  kill -TERM ${pids} >/dev/null 2>&1 || true

  for _ in $(seq 1 20); do
    pids="$(find_running_tundra_pids)"
    if [[ -z "${pids}" ]]; then
      return 0
    fi
    sleep 0.2
  done

  pids="$(find_running_tundra_pids)"
  if [[ -n "${pids}" ]]; then
    # shellcheck disable=SC2086
    kill -KILL ${pids} >/dev/null 2>&1 || true
    sleep 0.3
  fi
}

offer_launch_app() {
  local launch_cmd="$1"
  if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    return 0
  fi

  if [[ -t 0 ]] && prompt_yes_no "Deseja abrir o TundraDrive agora? [S/n]:" "S"; then
    nohup bash -c "exec ${launch_cmd}" >/dev/null 2>&1 &
    step_ok "TundraDrive iniciado em segundo plano."
  fi
}

install_rpm_flow() {
  local interactive="${1:-0}"
  echo ""
  local stage_dir
  stage_dir="$(mktemp -d /tmp/tundra-stage.XXXXXX)"
  ACTIVE_TMP_STAGE="${stage_dir}"

  step_info "[1/4] Obtendo pacote oficial .rpm..."
  local rpm_path
  rpm_path="$(resolve_or_fetch_rpm "${stage_dir}")"

  step_info "[2/4] Verificando integridade do pacote..."
  verify_sha256_if_available "${rpm_path}"

  local enable_autostart=1
  local install_media=0
  if [[ "${interactive}" == "1" ]] && [[ -t 0 ]]; then
    prompt_yes_no "Ativar inicialização automática ao ligar o PC? [S/n]:" "S" || enable_autostart=0
    if command -v dnf >/dev/null 2>&1; then
      prompt_yes_no "Instalar extras recomendados de pré-visualização (ffmpeg, poppler, libreoffice)? [s/N]:" "N" && install_media=1 || true
    fi
  fi

  step_info "[3/4] Instalando pacote RPM no sistema (/opt/tundradrive)..."
  stop_running_instance_if_any
  sudo -v
  if command -v dnf >/dev/null 2>&1; then
    local dnf_action="install"
    local installed_evr=""
    local target_evr=""
    if command -v rpm >/dev/null 2>&1 && rpm -q tundradrive >/dev/null 2>&1; then
      installed_evr="$(rpm -q --qf '%{VERSION}-%{RELEASE}' tundradrive 2>/dev/null || true)"
      target_evr="$(rpm -qp --qf '%{VERSION}-%{RELEASE}' "${rpm_path}" 2>/dev/null || echo "${VERSION}-1")"
      if [[ "${installed_evr}" == "${target_evr}" ]]; then
        dnf_action="reinstall"
      else
        dnf_action="upgrade"
      fi
    fi
    run_with_spinner "Aplicando pacote ${RPM_ASSET} via DNF" sudo dnf "${dnf_action}" -y "${rpm_path}"
    if [[ "${install_media}" == "1" ]]; then
      run_with_spinner "Instalando pacotes opcionais de mídia" sudo dnf install -y poppler-utils ghostscript ffmpeg libreoffice ImageMagick || true
    fi
  else
    run_with_spinner "Aplicando pacote ${RPM_ASSET} via RPM" sudo rpm -Uvh --replacepkgs "${rpm_path}"
  fi

  # Remove eventual instalação local anterior em AppImage para evitar conflito de atalho
  rm -rf "${HOME}/.local/opt/tundradrive"
  rm -f "${HOME}/.local/bin/tundradrive"

  step_info "[4/4] Atualizando menu de aplicativos e ícones..."
  write_user_desktop_files "\"/usr/bin/tundradrive\"" "${enable_autostart}"

  if [[ -n "${stage_dir:-}" && -d "${stage_dir}" ]]; then
    rm -rf "${stage_dir}"
    ACTIVE_TMP_STAGE=""
  fi

  echo ""
  step_ok "${BOLD}TundraDrive v${VERSION} (.rpm) instalado com sucesso!${RESET}"
  echo -e "  ${DIM}Executável: /usr/bin/tundradrive  |  Menu: TundraDrive${RESET}"
  if [[ "${interactive}" == "1" ]]; then
    offer_launch_app "\"/usr/bin/tundradrive\""
  fi
}

install_appimage_flow() {
  local interactive="${1:-0}"
  echo ""
  local stage_dir
  stage_dir="$(mktemp -d /tmp/tundra-stage.XXXXXX)"
  ACTIVE_TMP_STAGE="${stage_dir}"

  step_info "[1/4] Obtendo pacote oficial .AppImage..."
  local appimage_path
  appimage_path="$(resolve_or_fetch_appimage "${stage_dir}")"

  step_info "[2/4] Verificando integridade do pacote..."
  verify_sha256_if_available "${appimage_path}"

  local enable_autostart=1
  if [[ "${interactive}" == "1" ]] && [[ -t 0 ]]; then
    prompt_yes_no "Ativar inicialização automática ao ligar o PC? [S/n]:" "S" || enable_autostart=0
  fi

  step_info "[3/4] Instalando AppImage integrado em ~/.local/opt/tundradrive..."
  stop_running_instance_if_any
  local target_dir="${HOME}/.local/opt/tundradrive"
  local target_bin="${target_dir}/TundraDrive.AppImage"
  local local_bin_dir="${HOME}/.local/bin"

  mkdir -p "${target_dir}" "${local_bin_dir}"
  if [[ "$(readlink -f "${appimage_path}")" != "$(readlink -f "${target_bin}" 2>/dev/null || echo "")" ]]; then
    cp -f "${appimage_path}" "${target_bin}"
  fi
  chmod 755 "${target_bin}"

  cat > "${local_bin_dir}/tundradrive" <<EOF
#!/usr/bin/env bash
exec "${target_bin}" "\$@"
EOF
  chmod 755 "${local_bin_dir}/tundradrive"

  step_info "[4/4] Registrando atalhos, ícones e menu (GNOME / COSMIC / KDE Plasma)..."
  write_user_desktop_files "\"${target_bin}\"" "${enable_autostart}"

  if [[ -n "${stage_dir:-}" && -d "${stage_dir}" ]]; then
    rm -rf "${stage_dir}"
    ACTIVE_TMP_STAGE=""
  fi

  echo ""
  step_ok "${BOLD}TundraDrive v${VERSION} (.AppImage) instalado com sucesso!${RESET}"
  echo -e "  ${DIM}Arquivo: ${target_bin}  |  Comando: ~/.local/bin/tundradrive${RESET}"
  if [[ "${interactive}" == "1" ]]; then
    offer_launch_app "\"${target_bin}\""
  fi
}

configure_extras_menu() {
  echo ""
  echo -e "${BOLD}Opções de integração e sistema:${RESET}"
  echo -e "  ${CYAN}1)${RESET} Ativar inicialização automática (XDG Autostart)"
  echo -e "  ${CYAN}2)${RESET} Desativar inicialização automática"
  echo -e "  ${CYAN}3)${RESET} Instalar pacotes extras de mídia (PDF, Office, vídeo e miniaturas)"
  echo -e "  ${CYAN}0)${RESET} Voltar"
  read -rp "Escolha [0]: " sub || true
  case "${sub:-0}" in
    1)
      if command -v rpm >/dev/null 2>&1 && rpm -q tundradrive >/dev/null 2>&1; then
        write_user_desktop_files "\"/usr/bin/tundradrive\"" 1
      elif [[ -x "${HOME}/.local/opt/tundradrive/TundraDrive.AppImage" ]]; then
        write_user_desktop_files "\"${HOME}/.local/opt/tundradrive/TundraDrive.AppImage\"" 1
      else
        step_warn "Instale primeiro o .rpm ou o .AppImage antes de configurar o autostart."
      fi
      ;;
    2)
      rm -f "${HOME}/.config/autostart/tundradrive.desktop"
      step_ok "Inicialização automática removida."
      ;;
    3)
      if command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y poppler-utils ghostscript ffmpeg libreoffice ImageMagick
      elif command -v apt >/dev/null 2>&1; then
        sudo apt update && sudo apt install -y poppler-utils ghostscript ffmpeg libreoffice imagemagick
      else
        step_warn "Gerenciador dnf/apt não encontrado; instale manualmente: poppler-utils ghostscript ffmpeg libreoffice."
      fi
      ;;
    *) ;;
  esac
}

uninstall_flow() {
  echo ""
  stop_running_instance_if_any
  step_info "Removendo instalação do TundraDrive (dados em ~/.config/tundradrive serão preservados)..."
  rm -rf "${HOME}/.local/opt/tundradrive"
  rm -f "${HOME}/.local/bin/tundradrive" \
        "${HOME}/.local/share/applications/tundradrive.desktop" \
        "${HOME}/.config/autostart/tundradrive.desktop"
  if command -v rpm >/dev/null 2>&1 && rpm -q tundradrive >/dev/null 2>&1; then
    sudo rpm -e tundradrive
    step_ok "Pacote RPM tundradrive removido do sistema."
  fi
  refresh_desktop_caches
  step_ok "TundraDrive desinstalado com sucesso."
}

download_menu() {
  echo ""
  echo -e "${BOLD}Baixar arquivos oficiais para ${WHITE}${DOWNLOAD_DIR}${RESET}:"
  echo -e "  ${CYAN}1)${RESET} Somente ${RPM_ASSET} (Fedora / RHEL / openSUSE)"
  echo -e "  ${CYAN}2)${RESET} Somente ${APPIMAGE_ASSET} (Universal x86_64)"
  echo -e "  ${CYAN}3)${RESET} Baixar ambos + ${SUMS_ASSET}"
  echo -e "  ${CYAN}0)${RESET} Voltar"
  read -rp "Escolha [3]: " dl_choice || true
  case "${dl_choice:-3}" in
    1)
      local p; p="$(resolve_or_fetch_rpm)"
      verify_sha256_if_available "${p}"
      ;;
    2)
      local p; p="$(resolve_or_fetch_appimage)"
      verify_sha256_if_available "${p}"
      ;;
    3)
      download_release_asset "${SUMS_ASSET}" "${DOWNLOAD_DIR}" >/dev/null 2>&1 || true
      local r a
      r="$(resolve_or_fetch_rpm)"; verify_sha256_if_available "${r}"
      a="$(resolve_or_fetch_appimage)"; verify_sha256_if_available "${a}"
      ;;
    *) ;;
  esac
}

update_flow() {
  local interactive="${1:-0}"
  echo ""
  step_info "Verificando instalação atual para atualizar para v${VERSION}..."
  if command -v rpm >/dev/null 2>&1 && rpm -q tundradrive >/dev/null 2>&1; then
    install_rpm_flow "${interactive}"
  elif [[ -x "${HOME}/.local/opt/tundradrive/TundraDrive.AppImage" ]]; then
    install_appimage_flow "${interactive}"
  elif is_fedora_like; then
    step_warn "Nenhuma instalação anterior detectada; instalando pacote .rpm v${VERSION}..."
    install_rpm_flow "${interactive}"
  else
    step_warn "Nenhuma instalação anterior detectada; instalando pacote .AppImage v${VERSION}..."
    install_appimage_flow "${interactive}"
  fi
}

show_help() {
  print_header 0
  echo "Uso: ./install-tundradrive.sh [OPÇÃO]"
  echo ""
  echo "Opções:"
  echo "  (sem argumentos)    Abre o menu interativo do instalador"
  echo "  --update            Atualiza a instalação existente para a versão mais recente (v${VERSION})"
  echo "  --rpm               Baixa, valida SHA-256 e instala/atualiza o pacote .rpm"
  echo "  --appimage          Baixa, valida SHA-256 e instala/atualiza o .AppImage integrado"
  echo "  --download-rpm      Baixa apenas o arquivo .rpm no diretório atual"
  echo "  --download-appimage Baixa apenas o arquivo .AppImage no diretório atual"
  echo "  --download-only     Baixa ambos (.rpm e .AppImage) no diretório atual"
  echo "  --uninstall         Remove a instalação mantendo suas configurações"
  echo "  --build-all         Compila .AppImage e .rpm localmente (requer SDK .NET 10)"
  echo "  --help              Exibe esta ajuda"
}

MODE="${1:---menu}"

case "${MODE}" in
  --help|-h)
    show_help
    exit 0
    ;;
  --update)
    print_header 0
    update_flow 0
    exit 0
    ;;
  --rpm)
    print_header 0
    install_rpm_flow 0
    exit 0
    ;;
  --appimage)
    print_header 0
    install_appimage_flow 0
    exit 0
    ;;
  --download-rpm)
    print_header 0
    p="$(resolve_or_fetch_rpm)"
    verify_sha256_if_available "${p}"
    step_ok "Salvo em: ${p}"
    exit 0
    ;;
  --download-appimage)
    print_header 0
    p="$(resolve_or_fetch_appimage)"
    verify_sha256_if_available "${p}"
    step_ok "Salvo em: ${p}"
    exit 0
    ;;
  --download-only)
    print_header 0
    download_release_asset "${SUMS_ASSET}" "${DOWNLOAD_DIR}" >/dev/null 2>&1 || true
    r="$(resolve_or_fetch_rpm)"; verify_sha256_if_available "${r}"
    a="$(resolve_or_fetch_appimage)"; verify_sha256_if_available "${a}"
    exit 0
    ;;
  --uninstall)
    print_header 0
    uninstall_flow
    exit 0
    ;;
  --build-all)
    print_header 0
    "${SCRIPT_DIR}/build-appimage.sh"
    FORCE_PUBLISH=0 "${SCRIPT_DIR}/build-rpm.sh"
    exit 0
    ;;
  --menu|--cli|--gui)
    ;;
  *)
    step_err "Opção desconhecida: ${MODE}"
    show_help
    exit 1
    ;;
esac

default_choice="2"
rpm_tag=""
app_tag="${GREEN}(Recomendado)${RESET}"
if is_fedora_like; then
  default_choice="1"
  rpm_tag="${GREEN}(Recomendado no seu sistema)${RESET}"
  app_tag=""
fi

while true; do
  print_header 1
  echo -e "${BOLD}Escolha uma ação:${RESET}"
  echo -e "  ${CYAN}1)${RESET} Instalar ou atualizar pacote nativo ${BOLD}.rpm${RESET} (Fedora / RHEL / openSUSE) ${rpm_tag}"
  echo -e "  ${CYAN}2)${RESET} Instalar ou atualizar pacote ${BOLD}.AppImage${RESET} integrado ao usuário (Pop!_OS / Ubuntu / universal) ${app_tag}"
  echo -e "  ${CYAN}3)${RESET} Atualizar instalação atual automaticamente para ${BOLD}v${VERSION}${RESET}"
  echo -e "  ${CYAN}4)${RESET} Apenas baixar pacotes (${DIM}.rpm${RESET} / ${DIM}.AppImage${RESET}) nesta pasta"
  echo -e "  ${CYAN}5)${RESET} Configurar autostart e extras de mídia"
  echo -e "  ${CYAN}6)${RESET} Desinstalar TundraDrive"
  echo -e "  ${CYAN}0)${RESET} Sair"
  echo ""
  read -rp "Opção [${default_choice}]: " CHOICE || break
  CHOICE="${CHOICE:-${default_choice}}"

  case "${CHOICE}" in
    1)
      install_rpm_flow 1
      read -rp "Pressione Enter para voltar ao menu..." _ || true
      ;;
    2)
      install_appimage_flow 1
      read -rp "Pressione Enter para voltar ao menu..." _ || true
      ;;
    3)
      update_flow 1
      read -rp "Pressione Enter para voltar ao menu..." _ || true
      ;;
    4)
      download_menu
      read -rp "Pressione Enter para voltar ao menu..." _ || true
      ;;
    5)
      configure_extras_menu
      read -rp "Pressione Enter para voltar ao menu..." _ || true
      ;;
    6)
      uninstall_flow
      read -rp "Pressione Enter para voltar ao menu..." _ || true
      ;;
    0|q|Q)
      echo -e "${DIM}Até logo!${RESET}"
      exit 0
      ;;
    *)
      step_warn "Opção inválida."
      sleep 1
      ;;
  esac
done
