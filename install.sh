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
# Имя файлика для хранения установленной версии Лампака
readonly VERSION_FILE_NAME="version.txt"

REMOVE=0
UPDATE=0
DRY_RUN=0
PRE_RELEASE=0
VERBOSE=0
FORCE=0
TARGET_VERSION=""
ARCH=""
PUBLISH_URL=""
CLEANUP_PATHS=()
# Set while service is stopped during --update; EXIT trap may restart it.
_UPDATE_SERVICE_STOPPED=0

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
  printf '  %s%-24s%s %-34s %s(default: %s)%s\n' \
    "$C_CYAN" "LAMPAC_GITHUB_REPO" "$C_RESET" "GitHub owner/repo for releases" "$C_DIM" "$GITHUB_REPO" "$C_RESET"
  printf '  %s%-24s%s %-34s %s(default: %s)%s\n' \
    "$C_CYAN" "LAMPAC_INSTALL_ROOT" "$C_RESET" "Install directory" "$C_DIM" "$INSTALL_ROOT" "$C_RESET"
  printf '  %s%-24s%s %-34s %s(default: %s)%s\n' \
    "$C_CYAN" "LAMPAC_USER" "$C_RESET" "Service account name" "$C_DIM" "$LAMPAC_USER" "$C_RESET"
  printf '  %s%-24s%s %-34s %s(default: %s)%s\n' \
    "$C_CYAN" "LAMPAC_UID" "$C_RESET" "Preferred UID" "$C_DIM" "1000" "$C_RESET"
  printf '  %s%-24s%s %-34s %s(default: %s)%s\n' \
    "$C_CYAN" "LAMPAC_GID" "$C_RESET" "Preferred GID" "$C_DIM" "1000" "$C_RESET"
  printf '  %s%-24s%s %-34s %s(default: %s)%s\n' \
    "$C_CYAN" "LAMPAC_PORT" "$C_RESET" "HTTP port hint" "$C_DIM" "$LISTEN_PORT" "$C_RESET"
  printf '  %s%-24s%s Skip %s--remove%s confirmation when set to 1\n' \
    "$C_CYAN" "LAMPAC_CONFIRM_REMOVE" "$C_RESET" "$C_RED" "$C_RESET"
  printf '\n'

  printf '%sOptions:%s\n' "$C_BOLD" "$C_RESET"
  printf '  %s%-16s%s %s\n' "$C_GREEN"  "--update"      "$C_RESET" "Replace app files from latest (or --tag) release"
  printf '  %s%-16s%s %s\n' "$C_GREEN"  "--tag VER"     "$C_RESET" "Install or update a specific release tag"
  printf '  %s%-16s%s %s\n' "$C_YELLOW" "--force"       "$C_RESET" "Reinstall even if the desired version is already installed"
  printf '  %s%-16s%s %s\n' "$C_YELLOW" "--dry-run"     "$C_RESET" "Show what would change without applying updates"
  printf '  %s%-16s%s %s\n' "$C_YELLOW" "--pre-release" "$C_RESET" "Use latest GitHub pre-release (${RELEASE_ZIP_NAME})"
  printf '  %s%-16s%s %s\n' "$C_RED"    "--remove"      "$C_RESET" "Remove systemd unit, user, and install directory"
  printf '  %s%-16s%s %s\n' "$C_BLUE"   "-v, --verbose" "$C_RESET" "Show full output of all commands (for debugging)"
  printf '  %s%-16s%s %s\n' "$C_BLUE"   "-h, --help"    "$C_RESET" "Show this help and exit"
  printf '  %s%-16s%s %s\n' "$C_BLUE"   "--version"     "$C_RESET" "Show installed version"
  printf '\n'

  printf '%sExamples:%s\n' "$C_BOLD" "$C_RESET"
  printf '  %s# install (latest)%s\n' "$C_DIM" "$C_RESET"
  printf '  curl -fsSL https://raw.githubusercontent.com/%s/main/install.sh | bash\n' "$GITHUB_REPO"
  printf '  %s# install / pin a tag%s\n' "$C_DIM" "$C_RESET"
  printf '  %s --tag v1.2.3\n' "$SCRIPT_NAME"
  printf '  %s# update / downgrade%s\n' "$C_DIM" "$C_RESET"
  printf '  %s --update\n' "$SCRIPT_NAME"
  printf '  %s --update --tag v1.2.3\n' "$SCRIPT_NAME"
  printf '  %s# force reinstall same version%s\n' "$C_DIM" "$C_RESET"
  printf '  %s --update --force\n' "$SCRIPT_NAME"
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
  ensure_jq
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
      --tag)
        if [[ $# -lt 2 || -z "${2:-}" || "$2" == -* ]]; then
          log_err "--tag requires a version argument (e.g. --tag v1.2.3)."
          usage >&2
          exit 1
        fi
        TARGET_VERSION="$2"
        shift 2
        ;;
      --force)
        FORCE=1
        shift
        ;;
      -v|--verbose)
        VERBOSE=1
        shift
        ;;
      --version)
        show_version
        exit 0
        ;;
      *)
        log_err "Unknown option: $1"
        usage >&2
        exit 1
        ;;
    esac
  done

  if [[ -n "$TARGET_VERSION" && "$PRE_RELEASE" -eq 1 ]]; then
    log_err "--tag and --pre-release are mutually exclusive."
    exit 1
  fi
}

# Re-exec under sudo. Materializes the script when run via curl|bash (/dev/fd/…).
require_root() {
  if [[ ${EUID} -eq 0 ]]; then
    return 0
  fi

  if ! command -v sudo >/dev/null 2>&1; then
    log_err "This script must run as root (sudo not found)."
    exit 1
  fi

  local src="${BASH_SOURCE[0]:-$0}"
  local tmp=""

  # Regular on-disk script — sudo it directly.
  if [[ -f "$src" && -r "$src" && "$src" != /dev/fd/* && "$src" != /proc/self/fd/* ]]; then
    exec sudo -E bash "$src" "$@"
  fi

  # Piped / process-substitution: copy to a temp file first (fd is process-local).
  if [[ -r "$src" ]]; then
    tmp="$(mktemp /tmp/lampac-install.XXXXXX.sh)"
    cat "$src" > "$tmp"
    chmod 700 "$tmp"
    exec sudo -E bash "$tmp" "$@"
  fi

  log_err "Cannot re-exec as root (script source unreadable). Download install.sh and run: sudo bash install.sh …"
  exit 1
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

# Install jq early when needed for --pre-release (before full package step).
ensure_jq() {
  if command -v jq >/dev/null 2>&1; then
    return 0
  fi
  if [[ ${EUID} -ne 0 ]]; then
    log_err "jq is required but not installed."
    exit 1
  fi
  log_info "Installing jq..."
  if ! DEBIAN_FRONTEND=noninteractive apt-get update -qq \
    || ! DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends jq -qq; then
    log_err "Failed to install jq (required for --pre-release)."
    exit 1
  fi
  log_ok "jq installed"
}

# Extract "tag_name" from a GitHub release JSON blob without jq.
json_extract_tag_name() {
  local json="$1"
  printf '%s' "$json" \
    | grep -o '"tag_name"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -n1 \
    | sed 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/'
}

# ─── Version ───────────────────────────────────────────────────────────

# Убрать ведущий "v" для сравнения и записи в version.txt
normalize_version() {
  local v="$1"
  v="${v#"${v%%[![:space:]]*}"}"
  v="${v%"${v##*[![:space:]]}"}"
  echo "${v#v}"
}

# Проверить, что asset релиза доступен по URL (без скачивания всего zip)
release_asset_exists() {
  local url="$1"
  local code
  code=$(curl -fsSIL -o /dev/null -w '%{http_code}' "$url" 2>/dev/null) || return 1
  [[ "$code" == "200" ]]
}

# Разрешить PUBLISH_URL для конкретного тега (с fallback v / без v)
resolve_tag_publish_url() {
  local input="$1"
  local -a candidates=()
  local tag url

  candidates+=("$input")
  if [[ "$input" == v* ]]; then
    candidates+=("${input#v}")
  else
    candidates+=("v${input}")
  fi

  for tag in "${candidates[@]}"; do
    url="https://github.com/${GITHUB_REPO}/releases/download/${tag}/${RELEASE_ZIP_NAME}"
    if release_asset_exists "$url"; then
      PUBLISH_URL="$url"
      return 0
    fi
  done

  log_err "Release tag not found for ${GITHUB_REPO}: ${input} (asset ${RELEASE_ZIP_NAME})."
  exit 1
}

# Выбрать PUBLISH_URL: --tag, --pre-release или latest
resolve_publish_url() {
  if [[ -n "$TARGET_VERSION" ]]; then
    spinner_start "Resolving release tag ${TARGET_VERSION}..."
    resolve_tag_publish_url "$TARGET_VERSION"
    spinner_ok "Release URL resolved"
  elif [[ "$PRE_RELEASE" -eq 1 ]]; then
    spinner_start "Resolving latest pre-release asset..."
    get_prerelease_zip_url
    spinner_ok "Pre-release URL resolved"
  else
    PUBLISH_URL="https://github.com/${GITHUB_REPO}/releases/latest/download/${RELEASE_ZIP_NAME}"
  fi
}

# Получить номер последнего [пре-]релиза Лампака с гитхаба
get_release_version() {
  if ! command -v curl >/dev/null 2>&1; then
    log_err "curl is required to get release version."
    exit 1
  fi

  local version json api_url

  if [[ "$PRE_RELEASE" -eq 1 ]]; then
    ensure_jq
    api_url="https://api.github.com/repos/${GITHUB_REPO}/releases"
    version=$(curl -sSL -H 'Accept: application/vnd.github+json' "$api_url" \
      | jq -r '.[] | select(.prerelease == true) | .tag_name' | head -n1) || true
  else
    # Same source of truth as PUBLISH_URL (.../releases/latest/download/...)
    api_url="https://api.github.com/repos/${GITHUB_REPO}/releases/latest"
    json=$(curl -sSL -H 'Accept: application/vnd.github+json' "$api_url") || true
    version="$(json_extract_tag_name "${json:-}")"
  fi

  version="$(normalize_version "${version:-}")"
  if [[ -z "${version:-}" ]]; then
    log_err "Could not determine release version from ${api_url}."
    exit 1
  fi
  echo "$version"
}

# Получить номер установленной версии Лампака из файлика
get_installed_version() {
  local version_file="${INSTALL_ROOT}/${VERSION_FILE_NAME}"
  if [[ -f "$version_file" ]]; then
    normalize_version "$(cat "$version_file")"
  else
    echo "N/A"
  fi
}

# Показать баннер и установленную версию Лампака для опции --version
show_version() {
  print_banner
  if [[ -d "$INSTALL_ROOT" ]] && [[ -f "${INSTALL_ROOT}/Core.dll" ]]; then
    printf '  %sInstalled Version:%s %s\n' "$C_BOLD" "$C_RESET" "$(get_installed_version)"
  else
    echo "Lampac is not installed."
  fi
  printf '\n'
}

# Сохранить версию в файлик, чтобы было что потом показывать
save_installed_version() {
  local version
  version="$(normalize_version "$1")"
  local version_file="${INSTALL_ROOT}/${VERSION_FILE_NAME}"
  echo "$version" > "$version_file"
}

# Не продолжать, если желаемая версия уже установлена (если нет --force)
confirm_same_version_or_exit() {
  local desired
  desired="$(normalize_version "$1")"
  local installed
  installed="$(get_installed_version)"

  if [[ "$installed" == "N/A" || "$installed" != "$desired" ]]; then
    return 0
  fi

  if [[ "$FORCE" -eq 1 ]]; then
    log_warn "Version ${desired} is already installed — continuing due to --force."
    return 0
  fi

  if [[ "$DRY_RUN" -eq 1 ]]; then
    log_ok "Version ${desired} is already installed — nothing to do (dry-run)."
    printf '  Use %s--force%s to preview a reinstall of the same version.\n\n' "$C_YELLOW" "$C_RESET"
    exit 0
  fi

  printf '\n  %sVersion %s is already installed.%s\n' "$C_YELLOW" "$desired" "$C_RESET"
  printf '  Continue anyway? [y/N]: '

  if [[ ! -r /dev/tty ]]; then
    printf '\n'
    log_err "Cannot prompt for confirmation (no TTY). Use --force to reinstall the same version."
    exit 1
  fi

  local answer
  read -r answer </dev/tty || true
  case "${answer:-}" in
    y|Y|yes|YES)
      log_warn "Reinstalling version ${desired}."
      return 0
      ;;
    *)
      log_ok "Skipped — already at version ${desired}."
      printf '\n'
      exit 0
      ;;
  esac
}


# ─── Install steps ───────────────────────────────────────────────────────────

install_google_chrome() {
  if command -v google-chrome-stable >/dev/null 2>&1; then
    ln -sf /usr/bin/google-chrome-stable /usr/bin/chromium
    log_skip "Google Chrome already installed — skipping"
    return 0
  fi

  local chrome_url chrome_deb
  case "$ARCH" in
    arm64) chrome_url="https://dl.google.com/linux/direct/google-chrome-stable_current_arm64.deb" ;;
    amd64) chrome_url="https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb" ;;
    *)
      log_err "Unsupported architecture for Google Chrome: $ARCH"
      exit 1
      ;;
  esac

  chrome_deb="/tmp/google-chrome-stable-$$.deb"
  CLEANUP_PATHS+=("$chrome_deb")

  run_quiet "Downloading Google Chrome ($ARCH)" \
    curl -fSL -o "$chrome_deb" "$chrome_url"
  run_quiet "Installing Google Chrome" \
    env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "$chrome_deb"
  ln -sf /usr/bin/google-chrome-stable /usr/bin/chromium
}

install_os_packages() {
  run_quiet "Updating package lists" \
    apt-get update

  local icu_pkg
  icu_pkg="$(pick_libicu_package)"

  run_quiet "Installing system packages (curl, jq, fonts, GStreamer, ICU, ImageMagick, unzip, rsync)" \
    env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      ca-certificates curl jq fontconfig \
      gstreamer1.0-libav gstreamer1.0-plugins-bad gstreamer1.0-plugins-base \
      gstreamer1.0-plugins-base-apps gstreamer1.0-plugins-good gstreamer1.0-plugins-ugly \
      gstreamer1.0-tools \
      imagemagick libgstreamer-plugins-base1.0-0 libgstreamer1.0-0 \
      libjpeg-dev libnspr4 libpng-dev libwebp-dev unzip rsync "$icu_pkg"

  install_google_chrome

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
    useradd -r -g "$LAMPAC_USER" -d "$INSTALL_ROOT" -s /usr/sbin/nologin "$LAMPAC_USER"
  else
    useradd -r -u "$prefer_uid" -g "$LAMPAC_USER" -d "$INSTALL_ROOT" -s /usr/sbin/nologin "$LAMPAC_USER"
  fi

  if ! getent passwd "$LAMPAC_USER" &>/dev/null; then
    log_err "Failed to create user ${LAMPAC_USER}."
    exit 1
  fi
  log_ok "User ${LAMPAC_USER} created (home ${INSTALL_ROOT})"
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

    # Домашняя директория пользователя lampac (Chrome nssdb, сертификаты и т.д.)
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

    # Пользовательские файлы и кеш в wwwroot/ (темы, кнопки, lampa-main и т.д.)
    "wwwroot/"

    # Пользовательские плагины и состояние
    "plugins/override/"
    "notifications_date.txt"

    # Файл с пользовательскими дополнительными исключениями
    "excludes.conf"

    # Файл установленной версии (не удалять при обновлении)
    "${VERSION_FILE_NAME}"
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
  local release_version="$1"

  local staging_dir
  staging_dir="$(mktemp -d /tmp/lampac-install-stage.XXXXXX)"
  CLEANUP_PATHS+=("$staging_dir")

  if ! download_and_extract_to_staging "$staging_dir"; then
    log_err "Install aborted — download or extraction failed."
    exit 1
  fi

  mkdir -p "$INSTALL_ROOT"

  run_quiet "Installing application files to ${INSTALL_ROOT}" \
    bash -c "shopt -s dotglob nullglob; cp -a '${staging_dir}'/* '${INSTALL_ROOT}'/"

  if [[ ! -f "${INSTALL_ROOT}/Core.dll" ]]; then
    log_err "Expected Core.dll not found in ${INSTALL_ROOT} — check release layout"
    exit 1
  fi

  save_installed_version "$release_version"
}

# ─── Update ──────────────────────────────────────────────────────────────────

# Best-effort restart if --update stopped the service and then failed.
_update_restart_service_if_needed() {
  if [[ "${_UPDATE_SERVICE_STOPPED}" -eq 1 ]]; then
    log_warn "Update interrupted — attempting to restart ${SERVICE_NAME}..."
    systemctl start "$SERVICE_NAME" 2>/dev/null || true
    _UPDATE_SERVICE_STOPPED=0
  fi
}

do_update() {
  local new_version="$1"

  if [[ ! -d "$INSTALL_ROOT" ]] || [[ ! -f "${INSTALL_ROOT}/Core.dll" ]]; then
    log_err "Installation not found at ${INSTALL_ROOT} — run without --update first."
    exit 1
  fi

  if ! command -v rsync >/dev/null 2>&1; then
    run_quiet "Updating package lists" apt-get update
    run_quiet "Installing rsync" \
      env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends rsync
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
  trap '_update_restart_service_if_needed; cleanup' EXIT

  spinner_start "Stopping ${SERVICE_NAME}..."
  systemctl stop "$SERVICE_NAME" 2>/dev/null || true
  _UPDATE_SERVICE_STOPPED=1
  spinner_ok "Service stopped"

  if ! rsync -a --delete \
      "${rsync_exclude_args[@]}" \
      "${staging_dir}/" \
      "${INSTALL_ROOT}/"; then
    log_err "Syncing release files (rsync --delete) — failed"
    exit 1
  fi
  log_ok "Syncing release files (rsync --delete)"

  set_install_ownership

  save_installed_version "$new_version"

  spinner_start "Starting ${SERVICE_NAME}..."
  systemctl start "$SERVICE_NAME"
  _UPDATE_SERVICE_STOPPED=0
  spinner_ok "Service started"

  # Restore the global cleanup-only trap from main.
  trap cleanup EXIT
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
Environment=CHROMIUM_PATH=/usr/bin/google-chrome-stable
Environment="CHROMIUM_FLAGS=--no-sandbox --disable-setuid-sandbox --disable-dev-shm-usage"
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
  # Help/version must work without sudo (and before pipe re-exec).
  parse_args "$@"
  require_root "$@"

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

  resolve_publish_url

  local mode_label="Install"
  [[ "$UPDATE" -eq 1 ]] && mode_label="Update"
  [[ "$DRY_RUN" -eq 1 ]] && mode_label="Dry-run update"

  printf '  %sMode:%s      %s\n'         "$C_BOLD" "$C_RESET" "$mode_label"
  printf '  %sArch:%s      %s\n'         "$C_BOLD" "$C_RESET" "$ARCH"
  printf '  %sDirectory:%s %s\n'         "$C_BOLD" "$C_RESET" "$INSTALL_ROOT"
  printf '  %sRelease:%s   %s\n'         "$C_BOLD" "$C_RESET" "$PUBLISH_URL"

  # Узнаём и показываем версии под баннером
  local release_version
  if [[ -n "$TARGET_VERSION" ]]; then
    release_version="$(normalize_version "$TARGET_VERSION")"
  else
    release_version="$(get_release_version)"
  fi
  if [[ "$UPDATE" -eq 1 ]] || [[ -d "$INSTALL_ROOT" && -f "${INSTALL_ROOT}/Core.dll" ]]; then
    printf '  %sInstalled Version:%s   %s\n' "$C_BOLD" "$C_RESET" "$(get_installed_version)"
  fi
  printf '  %sRelease Version:%s     %s\n' "$C_GREEN" "$C_RESET" "$release_version"

  confirm_same_version_or_exit "$release_version"

  # Dry-run update: preview rsync only — skip apt / runtime install.
  if [[ "$DRY_RUN" -eq 1 && "$UPDATE" -eq 1 ]]; then
    do_update "$release_version"
    exit 0
  fi

  local total_steps=4
  [[ "$UPDATE" -eq 1 ]] && total_steps=3

  step 1 "$total_steps" "System packages"
  install_os_packages

  step 2 "$total_steps" "ASP.NET Core runtime"
  install_aspnetcore_runtime

  if [[ "$UPDATE" -eq 1 ]]; then
    step 3 "$total_steps" "Update application"
    do_update "$release_version"
    print_post_update
    exit 0
  fi

  step 3 "$total_steps" "Service user"
  ensure_service_user

  step 4 "$total_steps" "Application"
  install_app "$release_version"
  install_systemd_unit
  set_install_ownership
  start_service
  print_post_install
}

main "$@"
