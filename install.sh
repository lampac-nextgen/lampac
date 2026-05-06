#!/usr/bin/env bash
#
# Lampac NextGen — native install for Debian / Ubuntu (amd64, arm64).
# Downloads the GitHub release zip (same payload CI publishes), creates a system user,
# installs .NET ASP.NET Core 10 runtime + OS deps aligned with the Docker runner image,
# and registers a systemd unit.
#
# Update mode (--update): uses rsync --delete to remove old files not in the release,
# preserving user data via exclude patterns.
#
# Run as any user; sudo is used when needed.
#
set -euo pipefail

readonly SCRIPT_NAME="${0##*/}"
readonly INSTALL_ROOT="${LAMPAC_INSTALL_ROOT:-/opt/lampac}"
readonly LAMPAC_USER="${LAMPAC_USER:-lampac}"
readonly SERVICE_NAME="${LAMPAC_SERVICE_NAME:-lampac}"
readonly SYSTEMD_UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
# Override with LAMPAC_GITHUB_REPO=owner/name if your releases live elsewhere
readonly GITHUB_REPO="${LAMPAC_GITHUB_REPO:-lampac-nextgen/lampac}"
readonly RELEASE_ZIP_NAME="lampac-nextgen.zip"
readonly DOTNET_INSTALL_DIR="${LAMPAC_DOTNET_ROOT:-/usr/share/dotnet}"
readonly DOTNET_CHANNEL="${LAMPAC_DOTNET_CHANNEL:-10.0}"
readonly LISTEN_PORT="${LAMPAC_PORT:-9118}"
# Имя скрипта — исключается из синхронизации при обновлении
readonly UPDATE_SCRIPT_NAME="install.sh"

REMOVE=0
UPDATE=0
DRY_RUN=0
PRE_RELEASE=0
VERBOSE=0
ARCH=""
PUBLISH_URL=""
CLEANUP_PATHS=()

# ─── Colors ──────────────────────────────────────────────────────────────────

_tty_escape() { printf '\033[%sm' "$1"; }

if [[ -t 1 ]]; then
  C_RESET=$(_tty_escape 0)
  C_BOLD=$(_tty_escape 1)
  C_DIM=$(_tty_escape 2)
  C_RED=$(_tty_escape "1;31")
  C_GREEN=$(_tty_escape "1;32")
  C_YELLOW=$(_tty_escape "1;33")
  C_BLUE=$(_tty_escape "1;34")
  C_CYAN=$(_tty_escape "1;36")
  C_WHITE=$(_tty_escape "1;37")
  C_GRAY=$(_tty_escape "0;37")
else
  C_RESET="" C_BOLD="" C_DIM="" C_RED="" C_GREEN="" C_YELLOW=""
  C_BLUE="" C_CYAN="" C_WHITE="" C_GRAY=""
fi

# ─── Logging ─────────────────────────────────────────────────────────────────

log_info()    { printf '  %s→%s  %s\n'     "$C_BLUE"   "$C_RESET" "$*"; }
log_ok()      { printf '  %s✓%s  %s\n'     "$C_GREEN"  "$C_RESET" "$*"; }
log_warn()    { printf '  %s⚠%s  %s\n'     "$C_YELLOW" "$C_RESET" "$*" >&2; }
log_err()     { printf '  %s✗%s  %s\n'     "$C_RED"    "$C_RESET" "$*" >&2; }
log_skip()    { printf '  %s·%s  %s%s%s\n' "$C_GRAY"   "$C_RESET" "$C_DIM" "$*" "$C_RESET"; }
log_del()     { printf '  %s−%s  %s%s%s\n' "$C_RED"    "$C_RESET" "$C_DIM" "$*" "$C_RESET"; }
log_upd()     { printf '  %s+%s  %s\n'     "$C_GREEN"  "$C_RESET" "$*"; }

run_quiet() {
  local label="$1"; shift
  if [[ "$VERBOSE" -eq 1 ]]; then
    log_info "$label"
    if ! "$@"; then
      log_err "$label — failed"
      exit 1
    fi
    log_ok "$label"
    return 0
  fi
  local _log exit_code
  _log="$(mktemp)"
  spinner_start "$label"
  if ! "$@" >"$_log" 2>&1; then
    exit_code=$?
    spinner_stop
    log_err "$label"
    printf '\n' >&2
    tail -20 "$_log" | sed 's/^/    /' >&2
    printf '\n' >&2
    rm -f "$_log"
    exit "$exit_code"
  fi
  spinner_ok "$label"
  rm -f "$_log"
}

step() {
  local n="$1" total="$2" label="$3"
  local cols prefix pad_len padding
  cols=$(tput cols 2>/dev/null || echo 80)
  prefix="━━━ [${n}/${total}] ${label} "
  pad_len=$(( cols / 2 - ${#prefix} ))
  (( pad_len < 2 )) && pad_len=2
  padding=$(printf '━%.0s' $(seq 1 $pad_len))
  printf '\n%s━━━ [%s%d/%d%s] %s%s%s %s%s\n' \
    "$C_BLUE" "$C_WHITE" "$n" "$total" "$C_BLUE" "$C_WHITE" "$label" "$C_BLUE" "$padding" "$C_RESET"
}

# ─── Spinner ─────────────────────────────────────────────────────────────────

_SPINNER_PID=""

spinner_start() {
  [[ ! -t 1 ]] && return
  local msg="${1:-Working...}"
  local frames=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
  local start=$SECONDS
  (
    local i=0
    while true; do
      local elapsed=$(( SECONDS - start ))
      printf '\r  %s%s%s  %s %s(%ds)%s' \
        "$C_CYAN" "${frames[$((i % ${#frames[@]}))]}" "$C_RESET" \
        "$msg" "$C_DIM" "$elapsed" "$C_RESET"
      sleep 0.1
      (( i++ )) || true
    done
  ) &
  _SPINNER_PID=$!
  disown "$_SPINNER_PID" 2>/dev/null || true
}

spinner_stop() {
  [[ -z "$_SPINNER_PID" ]] && return
  kill "$_SPINNER_PID" 2>/dev/null || true
  wait "$_SPINNER_PID" 2>/dev/null || true
  _SPINNER_PID=""
  printf '\r\033[K'
}

spinner_ok() {
  spinner_stop
  log_ok "$1"
}

spinner_err() {
  spinner_stop
  log_err "$1"
}

# ─── Banner ──────────────────────────────────────────────────────────────────

print_banner() {
  printf '\n'
  printf '%s  ██╗      █████╗ ███╗   ███╗██████╗  █████╗  ██████╗%s\n'  "$C_CYAN" "$C_RESET"
  printf '%s  ██║     ██╔══██╗████╗ ████║██╔══██╗██╔══██╗██╔════╝%s\n'  "$C_CYAN" "$C_RESET"
  printf '%s  ██║     ███████║██╔████╔██║██████╔╝███████║██║%s\n'        "$C_CYAN" "$C_RESET"
  printf '%s  ██║     ██╔══██║██║╚██╔╝██║██╔═══╝ ██╔══██║██║%s\n'       "$C_CYAN" "$C_RESET"
  printf '%s  ███████╗██║  ██║██║ ╚═╝ ██║██║     ██║  ██║╚██████╗%s\n'  "$C_CYAN" "$C_RESET"
  printf '%s  ╚══════╝╚═╝  ╚═╝╚═╝     ╚═╝╚═╝     ╚═╝  ╚═╝ ╚═════╝%s\n' "$C_CYAN" "$C_RESET"
  printf '%s                                          %sNextGen%s\n'      "$C_CYAN" "$C_WHITE" "$C_RESET"
  printf '\n'
}

# ─── Usage ───────────────────────────────────────────────────────────────────

usage() {
  print_banner
  printf '%sUsage:%s  %s [OPTIONS]\n\n' "$C_BOLD" "$C_RESET" "$SCRIPT_NAME"
  printf 'Install, update, or remove Lampac NextGen on Debian/Ubuntu (x86_64 or arm64).\n\n'

  printf '%sEnvironment (optional):%s\n' "$C_BOLD" "$C_RESET"
  printf '  %sLAMPAC_GITHUB_REPO%s   GitHub owner/repo for releases  %s(default: %s)%s\n' \
    "$C_CYAN" "$C_RESET" "$C_DIM" "$GITHUB_REPO" "$C_RESET"
  printf '  %sLAMPAC_INSTALL_ROOT%s  Install directory               %s(default: %s)%s\n' \
    "$C_CYAN" "$C_RESET" "$C_DIM" "$INSTALL_ROOT" "$C_RESET"
  printf '  %sLAMPAC_USER%s          Service account name            %s(default: %s)%s\n' \
    "$C_CYAN" "$C_RESET" "$C_DIM" "$LAMPAC_USER" "$C_RESET"
  printf '  %sLAMPAC_UID%s           Preferred UID                   %s(default: 1000)%s\n' \
    "$C_CYAN" "$C_RESET" "$C_DIM" "$C_RESET"
  printf '  %sLAMPAC_GID%s           Preferred GID                   %s(default: 1000)%s\n' \
    "$C_CYAN" "$C_RESET" "$C_DIM" "$C_RESET"
  printf '  %sLAMPAC_PORT%s          HTTP port hint                  %s(default: %s)%s\n' \
    "$C_CYAN" "$C_RESET" "$C_DIM" "$LISTEN_PORT" "$C_RESET"
  printf '  %sLAMPAC_CONFIRM_REMOVE%s  Set to 1 to skip the %s--remove%s confirmation prompt %s(non-interactive)%s\n' \
    "$C_CYAN" "$C_RESET" "$C_GREEN" "$C_RESET" "$C_DIM" "$C_RESET"
  printf '\n'

  printf '%sOptions:%s\n' "$C_BOLD" "$C_RESET"
  printf '  %s--update%s       Replace app files from latest release, restart service\n' "$C_GREEN" "$C_RESET"
  printf '  %s--dry-run%s      Show what would be updated/deleted without applying changes\n' "$C_YELLOW" "$C_RESET"
  printf '  %s--pre-release%s  Use latest GitHub pre-release asset (%s)\n' "$C_YELLOW" "$C_RESET" "$RELEASE_ZIP_NAME"
  printf '  %s--remove%s       Remove systemd unit, user, and install directory\n' "$C_RED" "$C_RESET"
  printf '  %s--verbose%s      Show full output of all commands (for debugging)\n' "$C_BLUE" "$C_RESET"
  printf '  %s-h, --help%s     Show this help and exit\n' "$C_BLUE" "$C_RESET"
  printf '\n'

  printf '%sExamples:%s\n' "$C_BOLD" "$C_RESET"
  printf '  %scurl -fsSL https://raw.githubusercontent.com/%s/main/install.sh | bash%s\n' \
    "$C_DIM" "$GITHUB_REPO" "$C_RESET"
  printf '  %s%s%s\n'           "$C_DIM" "$SCRIPT_NAME" "$C_RESET"
  printf '  %s%s --update%s\n'  "$C_DIM" "$SCRIPT_NAME" "$C_RESET"
  printf '\n'
}

# ─── Cleanup ─────────────────────────────────────────────────────────────────

cleanup() {
  spinner_stop
  local path
  (( ${#CLEANUP_PATHS[@]} )) || return 0
  for path in "${CLEANUP_PATHS[@]}"; do
    if [[ -e "$path" ]]; then
      rm -rf "$path"
    fi
  done
}

# ─── Helpers ─────────────────────────────────────────────────────────────────

detect_arch() {
  case "$(uname -m)" in
    x86_64)       echo "amd64" ;;
    aarch64|arm64) echo "arm64" ;;
    *)
      log_err "Unsupported architecture: $(uname -m). Supported: amd64, arm64."
      exit 1
      ;;
  esac
}

get_prerelease_zip_url() {
  if ! command -v curl >/dev/null 2>&1; then
    log_err "curl is required for --pre-release."
    exit 1
  fi
  if ! command -v jq >/dev/null 2>&1; then
    log_err "jq is required for --pre-release."
    exit 1
  fi
  local api_url="https://api.github.com/repos/${GITHUB_REPO}/releases"
  local url
  url=$(curl -sSL -H 'Accept: application/vnd.github+json' "$api_url" \
    | jq -r --arg name "$RELEASE_ZIP_NAME" \
      '.[] | select(.prerelease == true) | .assets[] | select(.name == $name) | .browser_download_url' \
    | head -n1) || true
  if [[ -z "${url:-}" ]]; then
    log_err "No pre-release asset named $RELEASE_ZIP_NAME found for $GITHUB_REPO."
    exit 1
  fi
  PUBLISH_URL="$url"
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -h|--help)
        usage
        exit 0
        ;;
      --dry-run)
        DRY_RUN=1
        shift
        ;;
      --pre-release)
        PRE_RELEASE=1
        shift
        ;;
      --remove)
        REMOVE=1
        shift
        ;;
      --update)
        UPDATE=1
        shift
        ;;
      --verbose|-v)
        VERBOSE=1
        shift
        ;;
      *)
        log_err "Unknown option: $1"
        usage >&2
        exit 1
        ;;
    esac
  done
}

require_root() {
  if [[ ${EUID} -ne 0 ]]; then
    exec sudo -E "$0" "$@"
  fi
}

pick_libicu_package() {
  local p
  for p in libicu78 libicu76 libicu74 libicu72 libicu70 libicu67; do
    if apt-cache show "$p" &>/dev/null; then
      echo "$p"
      return 0
    fi
  done
  log_err "Could not find a suitable libicu package in apt caches."
  exit 1
}

is_ubuntu() {
  [[ -r /etc/os-release ]] || return 1
  # shellcheck source=/dev/null
  . /etc/os-release
  [[ "${ID:-}" == "ubuntu" ]]
}

# ─── Install steps ───────────────────────────────────────────────────────────

ensure_chromium_repo_ubuntu() {
  if ! command -v add-apt-repository >/dev/null 2>&1; then
    log_info "Installing software-properties-common..."
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      software-properties-common -qq 2>/dev/null || true
  fi
  log_info "Adding xtradeb/apps PPA for Chromium..."
  DEBIAN_FRONTEND=noninteractive add-apt-repository -y ppa:xtradeb/apps -qq 2>/dev/null || true
  log_ok "PPA added"
}

install_os_packages() {
  run_quiet "Updating package lists" \
    apt-get update

  if is_ubuntu; then
    ensure_chromium_repo_ubuntu
    run_quiet "Updating package lists (after PPA)" \
      apt-get update
  fi

  local icu_pkg
  icu_pkg="$(pick_libicu_package)"

  run_quiet "Installing system packages (chromium, curl, fonts, ICU, unzip)" \
    env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      ca-certificates curl chromium fontconfig libnspr4 unzip "$icu_pkg"
  apt-get clean -qq 2>/dev/null || true
  rm -rf /var/lib/apt/lists/*
}

install_aspnetcore_runtime() {
  if [[ -x "${DOTNET_INSTALL_DIR}/dotnet" ]] \
    && "${DOTNET_INSTALL_DIR}/dotnet" --list-runtimes 2>/dev/null | grep -q 'Microsoft.AspNetCore.App 10.'; then
    log_skip "ASP.NET Core 10 runtime already present — skipping"
    return 0
  fi

  local installer="/tmp/dotnet-install-$$.sh"
  CLEANUP_PATHS+=("$installer")

  run_quiet "Downloading dotnet-install.sh" \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  chmod +x "$installer"

  run_quiet "Installing ASP.NET Core ${DOTNET_CHANNEL} runtime" \
    bash "$installer" --channel "$DOTNET_CHANNEL" --runtime aspnetcore \
      --install-dir "$DOTNET_INSTALL_DIR"
}

uid_in_use() { getent passwd "$1" &>/dev/null; }
gid_in_use() { getent group  "$1" &>/dev/null; }

ensure_service_user() {
  local prefer_uid="${LAMPAC_UID:-1000}"
  local prefer_gid="${LAMPAC_GID:-1000}"

  if getent group "$LAMPAC_USER" &>/dev/null; then
    log_skip "Group ${LAMPAC_USER} already exists"
  else
    if gid_in_use "$prefer_gid"; then
      local holder
      holder="$(getent group "$prefer_gid" | cut -d: -f1)"
      log_warn "GID ${prefer_gid} taken by \"${holder}\" — using system-assigned GID"
      groupadd -r "$LAMPAC_USER"
    else
      groupadd -r -g "$prefer_gid" "$LAMPAC_USER"
      log_ok "Group ${LAMPAC_USER} created (gid ${prefer_gid})"
    fi
  fi

  if getent passwd "$LAMPAC_USER" &>/dev/null; then
    log_skip "User ${LAMPAC_USER} already exists"
    return 0
  fi

  if uid_in_use "$prefer_uid"; then
    local holder
    holder="$(getent passwd "$prefer_uid" | cut -d: -f1)"
    log_warn "UID ${prefer_uid} taken by \"${holder}\" — using system-assigned UID"
    useradd -r -g "$LAMPAC_USER" -d "$INSTALL_ROOT" -s /usr/sbin/nologin "$LAMPAC_USER" 2>/dev/null || true
    return 0
  fi

  useradd -r -u "$prefer_uid" -g "$LAMPAC_USER" -d "$INSTALL_ROOT" -s /usr/sbin/nologin "$LAMPAC_USER" 2>/dev/null || true
  log_ok "User ${LAMPAC_USER} created (uid ${prefer_uid}, home ${INSTALL_ROOT})"
}

set_install_ownership() {
  chown -R "${LAMPAC_USER}:${LAMPAC_USER}" "$INSTALL_ROOT"
  log_ok "Ownership set to ${LAMPAC_USER}:${LAMPAC_USER}"
}

# ─── Build rsync excludes ────────────────────────────────────────────────────

build_rsync_excludes() {
  # Пути относительно INSTALL_ROOT, которые rsync никогда не должен трогать.
  local -n _out="$1"
  _out=(
    # Этот скрипт — не удалять его при обновлении
    "$UPDATE_SCRIPT_NAME"

    # Пользовательский конфиг
    "init.conf"
    "init.yaml"

    # Пользовательские Roslyn-модули
    "mods/"

    # Локальные базы данных (не поставляются релизом)
    "data/kinoukr.json"
    "data/PizdatoeDb.json"

    # SQLite — состояние Sync/SISI/TimeCode
    "*.db"
    "*.db-shm"
    "*.db-wal"

    # Runtime-данные
    "logs/"
    "cache/"

    # TorrServer — бинарь и состояние управляются отдельно
    "TorrServer"
    "torrserver/"
    "data/ts/"

    # Домашняя директория пользователя lampac (chromium nssdb, сертификаты и т.д.)
    ".local/"
    ".aspnet/"
    ".claude/"
    ".config/"
    ".playwright/"

    # Пользовательские данные приложения
    "users.json"
    "passwd"
    "current.conf"
    "database/"

    # Пользовательские .js в корне wwwroot/ (темы, кнопки и т.д.)
    "wwwroot/"

    # Старая папка lampa-main (не входит в новый релиз, но может быть нужна)
    "wwwroot/lampa-main/"

    # Пользовательские плагины и состояние
    "plugins/override/"
    "notifications_date.txt"

    # Файл с пользовательскими дополнительными исключениями
    "excludes.conf"
  )

  # Дополнительные исключения из excludes.conf (если файл существует)
  local excludes_file="${INSTALL_ROOT}/excludes.conf"
  if [[ -f "$excludes_file" ]]; then
    local line
    while IFS= read -r line || [[ -n "$line" ]]; do
      [[ -z "$line" || "$line" == \#* ]] && continue
      _out+=("$line")
    done < "$excludes_file"
  fi
}

# ─── Download / extract ──────────────────────────────────────────────────────

download_and_extract_to_staging() {
  local staging_dir="$1"
  local tmp_zip
  tmp_zip="$(mktemp /tmp/lampac-nextgen.XXXXXX.zip)"
  CLEANUP_PATHS+=("$tmp_zip")

  spinner_start "Downloading release archive..."
  if ! curl -fSL --retry 3 -o "$tmp_zip" "$PUBLISH_URL" 2>/dev/null; then
    spinner_err "Download failed"
    return 1
  fi
  if [[ ! -s "$tmp_zip" ]]; then
    spinner_err "Downloaded file is empty"
    return 1
  fi
  spinner_ok "Archive downloaded"

  run_quiet "Extracting to staging directory" \
    bash -c "unzip -oq '$tmp_zip' -d '$staging_dir' </dev/null"
  rm -f "$tmp_zip"

  # Если архив содержит корневую папку — переносим файлы в корень staging_dir
  local subdirs
  subdirs=$(find "$staging_dir" -mindepth 1 -maxdepth 1 -type d | wc -l)
  if [[ "$subdirs" -eq 1 ]]; then
    local only_subdir
    only_subdir=$(find "$staging_dir" -mindepth 1 -maxdepth 1 -type d | head -n1)
    shopt -s dotglob nullglob
    mv "$only_subdir"/* "$staging_dir"/ 2>/dev/null || true
    shopt -u dotglob nullglob
    rmdir "$only_subdir" 2>/dev/null || true
  fi

  if [[ ! -f "${staging_dir}/Core.dll" ]]; then
    spinner_err "Expected Core.dll not found — check release layout"
    return 1
  fi
  spinner_ok "Archive extracted"
}

install_app() {
  local tmp_zip
  tmp_zip="$(mktemp /tmp/lampac-nextgen.XXXXXX.zip)"
  CLEANUP_PATHS+=("$tmp_zip")

  run_quiet "Downloading release archive" \
    curl -fSL --retry 3 -o "$tmp_zip" "$PUBLISH_URL"

  mkdir -p "$INSTALL_ROOT"

  run_quiet "Extracting to ${INSTALL_ROOT}" \
    bash -c "unzip -oq '$tmp_zip' -d '$INSTALL_ROOT' </dev/null"
  rm -f "$tmp_zip"

  if [[ ! -f "${INSTALL_ROOT}/Core.dll" ]]; then
    log_err "Expected Core.dll not found in ${INSTALL_ROOT} — check release layout"
    exit 1
  fi
}

# ─── Update ──────────────────────────────────────────────────────────────────

do_update() {
  if [[ ! -d "$INSTALL_ROOT" ]] || [[ ! -f "${INSTALL_ROOT}/Core.dll" ]]; then
    log_err "Installation not found at ${INSTALL_ROOT} — run without --update first."
    exit 1
  fi

  if ! command -v rsync >/dev/null 2>&1; then
    spinner_start "Installing rsync..."
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends rsync -qq 2>/dev/null
    spinner_ok "rsync installed"
  fi

  ensure_service_user

  local staging_dir
  staging_dir="$(mktemp -d /tmp/lampac-update-stage.XXXXXX)"
  CLEANUP_PATHS+=("$staging_dir")

  if ! download_and_extract_to_staging "$staging_dir"; then
    log_err "Update aborted — download or extraction failed."
    exit 1
  fi

  # Копируем сам скрипт и excludes.conf в staging, чтобы rsync --delete их не удалил
  [[ -f "${INSTALL_ROOT}/${UPDATE_SCRIPT_NAME}" ]] && \
    cp -a "${INSTALL_ROOT}/${UPDATE_SCRIPT_NAME}" "${staging_dir}/${UPDATE_SCRIPT_NAME}"
  [[ -f "${INSTALL_ROOT}/excludes.conf" ]] && \
    cp -a "${INSTALL_ROOT}/excludes.conf" "${staging_dir}/excludes.conf"

  local -a RSYNC_EXCLUDES=()
  build_rsync_excludes RSYNC_EXCLUDES

  local rsync_exclude_args=()
  for excl in "${RSYNC_EXCLUDES[@]}"; do
    rsync_exclude_args+=(--exclude="$excl")
  done

  if [[ "$DRY_RUN" -eq 1 ]]; then
    printf '\n  %s┌─ DRY-RUN — no changes will be applied ─────────────────┐%s\n' "$C_YELLOW" "$C_RESET"

    local rsync_output
    rsync_output=$(rsync -a --delete --dry-run --itemize-changes \
      "${rsync_exclude_args[@]}" \
      "${staging_dir}/" \
      "${INSTALL_ROOT}/" \
      2>/dev/null || true)

    local del_files upd_files
    del_files=$(printf '%s' "$rsync_output" | awk '/\*deleting/ && !/\/$/ {sub(/\*deleting +/, ""); print}')
    upd_files=$(printf '%s' "$rsync_output" | grep -v '\*deleting' | grep -v '^$' | grep -v '/$' | grep '^.>' || true)

    printf '\n  %s  Files to be removed:%s\n' "$C_BOLD" "$C_RESET"
    if [[ -n "$del_files" ]]; then
      while IFS= read -r f; do
        log_del "$f"
      done <<< "$del_files"
    else
      log_skip "(nothing to remove)"
    fi

    printf '\n  %s  Files to be added / updated:%s\n' "$C_BOLD" "$C_RESET"
    if [[ -n "$upd_files" ]]; then
      while IFS= read -r f; do
        log_upd "$(printf '%s' "$f" | awk '{sub(/^.[ >][ f]......... /, ""); print}')"
      done <<< "$upd_files"
    else
      log_skip "(no new or changed files)"
    fi

    printf '\n  %s└─ Run without --dry-run to apply changes ───────────────┘%s\n\n' "$C_YELLOW" "$C_RESET"
    return 0
  fi

  # Реальное обновление
  spinner_start "Stopping ${SERVICE_NAME}..."
  systemctl stop "$SERVICE_NAME" 2>/dev/null || true
  spinner_ok "Service stopped"

  run_quiet "Syncing release files (rsync --delete)" \
    rsync -a --delete \
      "${rsync_exclude_args[@]}" \
      "${staging_dir}/" \
      "${INSTALL_ROOT}/"

  set_install_ownership

  spinner_start "Starting ${SERVICE_NAME}..."
  systemctl start "$SERVICE_NAME"
  spinner_ok "Service started"
}

# ─── Systemd ─────────────────────────────────────────────────────────────────

install_systemd_unit() {
  cat << EOF > "$SYSTEMD_UNIT_PATH"
[Unit]
Description=Lampac NextGen
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
User=$LAMPAC_USER
Group=$LAMPAC_USER
WorkingDirectory=$INSTALL_ROOT
Environment=DOTNET_ROOT=$DOTNET_INSTALL_DIR
Environment=PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:$DOTNET_INSTALL_DIR
Environment=DOTNET_RUNNING_IN_CONTAINER=false
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=CHROMIUM_PATH=/usr/bin/chromium
Environment=CHROMIUM_FLAGS=--no-sandbox --disable-setuid-sandbox --disable-dev-shm-usage
ExecStart=$DOTNET_INSTALL_DIR/dotnet $INSTALL_ROOT/Core.dll
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF
  chmod 644 "$SYSTEMD_UNIT_PATH"
  systemctl daemon-reload
  systemctl enable "$SERVICE_NAME" >/dev/null 2>&1
  log_ok "systemd unit installed and enabled → ${SYSTEMD_UNIT_PATH}"
}

# ─── Remove ──────────────────────────────────────────────────────────────────

remove_service() {
  if [[ ! -f "$SYSTEMD_UNIT_PATH" ]]; then
    log_skip "Service unit not found — skipping"
    return 0
  fi
  spinner_start "Stopping and disabling ${SERVICE_NAME}..."
  systemctl stop    "$SERVICE_NAME" 2>/dev/null || true
  systemctl disable "$SERVICE_NAME" 2>/dev/null || true
  rm -f "$SYSTEMD_UNIT_PATH"
  systemctl daemon-reload
  spinner_ok "Service removed"
}

remove_user_and_group() {
  if getent passwd "$LAMPAC_USER" &>/dev/null; then
    spinner_start "Removing user ${LAMPAC_USER}..."
    userdel "$LAMPAC_USER" 2>/dev/null || true
    spinner_ok "User ${LAMPAC_USER} removed"
  fi
  if getent group "$LAMPAC_USER" &>/dev/null; then
    spinner_start "Removing group ${LAMPAC_USER}..."
    groupdel "$LAMPAC_USER" 2>/dev/null || true
    spinner_ok "Group ${LAMPAC_USER} removed"
  fi
}

remove_app() {
  if [[ ! -d "$INSTALL_ROOT" ]]; then
    log_skip "Install directory not found — skipping"
    return 0
  fi
  spinner_start "Removing ${INSTALL_ROOT}..."
  rm -rf "$INSTALL_ROOT"
  spinner_ok "${INSTALL_ROOT} removed"
}

do_remove() {
  printf '\n  %s⚠  This will permanently delete the Lampac NextGen installation.%s\n' "$C_YELLOW" "$C_RESET"
  printf '  %sDirectory:%s %s\n' "$C_BOLD" "$C_RESET" "$INSTALL_ROOT"
  printf '  %sService:%s   %s\n\n' "$C_BOLD" "$C_RESET" "$SERVICE_NAME"
  printf '  Press %sEnter%s to continue, or %sCtrl-C%s to abort: ' \
    "$C_BOLD" "$C_RESET" "$C_RED" "$C_RESET"
  if [[ "${LAMPAC_CONFIRM_REMOVE:-}" != "1" ]]; then
    if [[ -r /dev/tty ]]; then
      read -r </dev/tty
    else
      log_err "Cannot prompt for confirmation (no TTY). Use an interactive shell, or set LAMPAC_CONFIRM_REMOVE=1 for non-interactive removal."
      exit 1
    fi
  fi

  remove_service
  remove_app
  remove_user_and_group

  printf '\n  %s✓  Lampac NextGen has been removed.%s\n\n' "$C_GREEN" "$C_RESET"
}

start_service() {
  spinner_start "Starting ${SERVICE_NAME}..."
  systemctl start "$SERVICE_NAME"
  spinner_ok "Service started"
}

# ─── Post-install ─────────────────────────────────────────────────────────────

print_post_install() {
  local ip
  ip=$(hostname -I 2>/dev/null | awk '{print $1}')
  [[ -z "$ip" ]] && ip="<your-ip>"

  local url="http://${ip}:${LISTEN_PORT}"

  printf '\n'
  printf '%s  ─── Installation complete ───%s\n' "$C_GREEN" "$C_RESET"
  printf '\n'
  printf '  %sConfig%s    %s\n' "$C_CYAN" "$C_RESET" "${INSTALL_ROOT}/init.conf"
  printf '  %sService%s   %s\n' "$C_CYAN" "$C_RESET" "systemctl status ${SERVICE_NAME}"
  printf '  %sRestart%s   %s\n' "$C_CYAN" "$C_RESET" "systemctl restart ${SERVICE_NAME}"
  printf '  %sLogs%s      %s\n' "$C_CYAN" "$C_RESET" "journalctl -u ${SERVICE_NAME} -f"
  printf '  %sURL%s       %s\n' "$C_CYAN" "$C_RESET" "${url}"
  printf '\n'
}

print_post_update() {
  printf '\n  %s✓  Lampac NextGen updated and restarted successfully.%s\n' "$C_GREEN" "$C_RESET"
  printf '  %sLogs:%s journalctl -u %s -f\n\n' "$C_CYAN" "$C_RESET" "$SERVICE_NAME"
}

# ─── Main ─────────────────────────────────────────────────────────────────────

main() {
  trap cleanup EXIT
  require_root "$@"
  parse_args "$@"

  if [[ "$(uname -s)" != "Linux" ]]; then
    log_err "This script supports Linux only."
    exit 1
  fi

  ARCH=$(detect_arch)

  if [[ "$REMOVE" -eq 1 ]]; then
    print_banner
    do_remove
    exit 0
  fi

  print_banner

  if [[ "$PRE_RELEASE" -eq 1 ]]; then
    spinner_start "Resolving latest pre-release asset..."
    get_prerelease_zip_url
    spinner_ok "Pre-release URL resolved"
  else
    PUBLISH_URL="https://github.com/${GITHUB_REPO}/releases/latest/download/${RELEASE_ZIP_NAME}"
  fi

  local mode_label="Install"
  [[ "$UPDATE" -eq 1 ]] && mode_label="Update"
  [[ "$DRY_RUN" -eq 1 ]] && mode_label="Dry-run update"

  printf '  %sMode:%s      %s\n'         "$C_BOLD" "$C_RESET" "$mode_label"
  printf '  %sArch:%s      %s\n'         "$C_BOLD" "$C_RESET" "$ARCH"
  printf '  %sDirectory:%s %s\n'         "$C_BOLD" "$C_RESET" "$INSTALL_ROOT"
  printf '  %sRelease:%s   %s\n'         "$C_BOLD" "$C_RESET" "$PUBLISH_URL"

  local total_steps=4
  [[ "$UPDATE" -eq 1 ]] && total_steps=3

  step 1 "$total_steps" "System packages"
  install_os_packages

  step 2 "$total_steps" "ASP.NET Core runtime"
  install_aspnetcore_runtime

  if [[ "$UPDATE" -eq 1 ]]; then
    step 3 "$total_steps" "Update application"
    do_update
    print_post_update
    exit 0
  fi

  step 3 "$total_steps" "Service user"
  ensure_service_user

  step 4 "$total_steps" "Application"
  install_app
  install_systemd_unit
  set_install_ownership
  start_service
  print_post_install
}

main "$@"
