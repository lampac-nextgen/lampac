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
ARCH=""
PUBLISH_URL=""
CLEANUP_PATHS=()

log_info() {
  printf '[%s] %s\n' "$SCRIPT_NAME" "$*"
}

log_err() {
  printf '[%s] ERROR: %s\n' "$SCRIPT_NAME" "$*" >&2
}

usage() {
  cat << EOF
Usage: $SCRIPT_NAME [OPTIONS]

Install, update, or remove Lampac NextGen on Debian/Ubuntu (x86_64 or arm64).

Environment (optional):
  LAMPAC_GITHUB_REPO   GitHub owner/repo for releases (default: $GITHUB_REPO)
  LAMPAC_INSTALL_ROOT  Install directory (default: $INSTALL_ROOT)
  LAMPAC_USER          Service account name (default: $LAMPAC_USER)
  LAMPAC_UID           Preferred UID (default: 1000). If that UID exists, a free UID is chosen.
  LAMPAC_GID           Preferred GID for the service group (default: 1000). If taken, a free GID is chosen.
  LAMPAC_PORT          HTTP port for post-install hint (default: $LISTEN_PORT)

Options:
  --update       Replace app files from latest release, restart service
  --dry-run      Show what would be updated/deleted without applying changes
  --pre-release  Use latest GitHub pre-release asset ($RELEASE_ZIP_NAME)
  --remove       Remove systemd unit, user, and install directory
  -h, --help     Show this help and exit

Examples:
  curl -fsSL https://raw.githubusercontent.com/$GITHUB_REPO/main/install.sh | bash
  $SCRIPT_NAME
  $SCRIPT_NAME --update
  LAMPAC_GITHUB_REPO=myfork/lampac $SCRIPT_NAME
EOF
}

cleanup() {
  local path
  (( ${#CLEANUP_PATHS[@]} )) || return 0
  for path in "${CLEANUP_PATHS[@]}"; do
    if [[ -e "$path" ]]; then
      log_info "Removing temporary path: $path"
      rm -rf "$path"
    fi
  done
}

detect_arch() {
  case "$(uname -m)" in
    x86_64) echo "amd64" ;;
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
  for p in libicu76 libicu74 libicu72 libicu70 libicu67; do
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

ensure_chromium_repo_ubuntu() {
  log_info "Ubuntu: adding xtradeb/apps PPA for Chromium..."
  if ! command -v add-apt-repository >/dev/null 2>&1; then
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      software-properties-common
  fi
  DEBIAN_FRONTEND=noninteractive add-apt-repository -y ppa:xtradeb/apps
}

install_os_packages() {
  log_info "Installing OS packages (ca-certificates, curl, chromium, fonts, ICU, unzip)..."
  apt-get update -qq
  if is_ubuntu; then
    ensure_chromium_repo_ubuntu
    apt-get update -qq
  fi
  local icu_pkg
  icu_pkg="$(pick_libicu_package)"
  DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    chromium \
    fontconfig \
    libnspr4 \
    unzip \
    "$icu_pkg"
  apt-get clean -qq
  rm -rf /var/lib/apt/lists/*
}

install_aspnetcore_runtime() {
  if [[ -x "${DOTNET_INSTALL_DIR}/dotnet" ]] \
    && "${DOTNET_INSTALL_DIR}/dotnet" --list-runtimes 2>/dev/null | grep -q 'Microsoft.AspNetCore.App 10.'; then
    log_info "ASP.NET Core 10 runtime already present under $DOTNET_INSTALL_DIR"
    return 0
  fi
  log_info "Installing ASP.NET Core runtime $DOTNET_CHANNEL to $DOTNET_INSTALL_DIR..."
  local installer="/tmp/dotnet-install-$$.sh"
  CLEANUP_PATHS+=("$installer")
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  chmod +x "$installer"
  bash "$installer" --channel "$DOTNET_CHANNEL" --runtime aspnetcore --install-dir "$DOTNET_INSTALL_DIR"
}

uid_in_use() {
  getent passwd "$1" &>/dev/null
}

gid_in_use() {
  getent group "$1" &>/dev/null
}

ensure_service_user() {
  local prefer_uid="${LAMPAC_UID:-1000}"
  local prefer_gid="${LAMPAC_GID:-1000}"

  if getent group "$LAMPAC_USER" &>/dev/null; then
    log_info "Group $LAMPAC_USER already exists"
  else
    if gid_in_use "$prefer_gid"; then
      local holder
      holder="$(getent group "$prefer_gid" | cut -d: -f1)"
      log_info "GID $prefer_gid is already used by group \"$holder\"; creating $LAMPAC_USER with a system-assigned gid..."
      groupadd -r "$LAMPAC_USER"
    else
      log_info "Creating group $LAMPAC_USER with gid $prefer_gid (matches Docker image when free)..."
      groupadd -r -g "$prefer_gid" "$LAMPAC_USER"
    fi
  fi

  if getent passwd "$LAMPAC_USER" &>/dev/null; then
    log_info "User $LAMPAC_USER already exists"
    return 0
  fi

  if uid_in_use "$prefer_uid"; then
    local holder
    holder="$(getent passwd "$prefer_uid" | cut -d: -f1)"
    log_info "UID $prefer_uid is already used by user \"$holder\"; creating $LAMPAC_USER with a system-assigned uid..."
    useradd -r -g "$LAMPAC_USER" -d "$INSTALL_ROOT" -s /usr/sbin/nologin "$LAMPAC_USER"
    return 0
  fi

  log_info "Creating system user $LAMPAC_USER with uid $prefer_uid (home $INSTALL_ROOT)..."
  useradd -r -u "$prefer_uid" -g "$LAMPAC_USER" -d "$INSTALL_ROOT" -s /usr/sbin/nologin "$LAMPAC_USER"
}

set_install_ownership() {
  log_info "Setting ownership to ${LAMPAC_USER}:${LAMPAC_USER} on $INSTALL_ROOT"
  chown -R "${LAMPAC_USER}:${LAMPAC_USER}" "$INSTALL_ROOT"
}

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
    "wwwroot/*.js"

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
      # Пропускаем пустые строки и комментарии
      [[ -z "$line" || "$line" == \#* ]] && continue
      _out+=("$line")
    done < "$excludes_file"
  fi
}

download_and_extract_to_staging() {
  local staging_dir="$1"
  local tmp_zip
  tmp_zip="$(mktemp /tmp/lampac-nextgen.XXXXXX.zip)"
  CLEANUP_PATHS+=("$tmp_zip")

  log_info "Downloading: $PUBLISH_URL"
  if ! curl -fSL --retry 3 -o "$tmp_zip" "$PUBLISH_URL"; then
    log_err "Download failed."
    return 1
  fi
  if [[ ! -s "$tmp_zip" ]]; then
    log_err "Downloaded file is empty."
    return 1
  fi

  log_info "Extracting to staging: $staging_dir ..."
  unzip -oq "$tmp_zip" -d "$staging_dir"
  rm -f "$tmp_zip"

  # Если архив содержит корневую папку (например, lampac-nextgen/),
  # переносим файлы в корень staging_dir
  local subdirs
  subdirs=$(find "$staging_dir" -mindepth 1 -maxdepth 1 -type d | wc -l)
  if [[ "$subdirs" -eq 1 ]]; then
    local only_subdir
    only_subdir=$(find "$staging_dir" -mindepth 1 -maxdepth 1 -type d | head -n1)
    log_info "Detected archive root directory: $only_subdir — moving contents to staging root"
    shopt -s dotglob nullglob
    mv "$only_subdir"/* "$staging_dir"/ 2>/dev/null || true
    shopt -u dotglob nullglob
    rmdir "$only_subdir" 2>/dev/null || true
  fi

  if [[ ! -f "${staging_dir}/Core.dll" ]]; then
    log_err "Expected Core.dll in $staging_dir after extract — check release layout."
    return 1
  fi
}

install_app() {
  local tmp_zip
  tmp_zip="$(mktemp /tmp/lampac-nextgen.XXXXXX.zip)"
  CLEANUP_PATHS+=("$tmp_zip")
  log_info "Downloading: $PUBLISH_URL"
  if ! curl -fSL --retry 3 -o "$tmp_zip" "$PUBLISH_URL"; then
    log_err "Download failed."
    exit 1
  fi
  log_info "Extracting to $INSTALL_ROOT ..."
  mkdir -p "$INSTALL_ROOT"
  unzip -oq "$tmp_zip" -d "$INSTALL_ROOT"
  rm -f "$tmp_zip"
  if [[ ! -f "${INSTALL_ROOT}/Core.dll" ]]; then
    log_err "Expected Core.dll in $INSTALL_ROOT after extract — check release layout."
    exit 1
  fi
  log_info "Application installed to $INSTALL_ROOT"
}

do_update() {
  if [[ ! -d "$INSTALL_ROOT" ]] || [[ ! -f "${INSTALL_ROOT}/Core.dll" ]]; then
    log_err "Install not found at $INSTALL_ROOT. Run install first."
    exit 1
  fi

  # rsync обязателен
  if ! command -v rsync >/dev/null 2>&1; then
    log_info "rsync not found, installing..."
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends rsync
  fi

  ensure_service_user

  # Скачиваем и распаковываем релиз во временную staging-директорию
  local staging_dir
  staging_dir="$(mktemp -d /tmp/lampac-update-stage.XXXXXX)"
  CLEANUP_PATHS+=("$staging_dir")

  if ! download_and_extract_to_staging "$staging_dir"; then
    log_err "Update failed."
    exit 1
  fi

  # Копируем сам скрипт в staging, чтобы rsync --delete его не удалил из INSTALL_ROOT
  if [[ -f "${INSTALL_ROOT}/${UPDATE_SCRIPT_NAME}" ]]; then
    cp -a "${INSTALL_ROOT}/${UPDATE_SCRIPT_NAME}" "${staging_dir}/${UPDATE_SCRIPT_NAME}"
  fi

  # Копируем excludes.conf в staging, чтобы rsync --delete его не удалил
  if [[ -f "${INSTALL_ROOT}/excludes.conf" ]]; then
    cp -a "${INSTALL_ROOT}/excludes.conf" "${staging_dir}/excludes.conf"
  fi

  # Собираем exclude-аргументы
  local -a RSYNC_EXCLUDES=()
  build_rsync_excludes RSYNC_EXCLUDES

  local rsync_exclude_args=()
  for excl in "${RSYNC_EXCLUDES[@]}"; do
    rsync_exclude_args+=(--exclude="$excl")
  done

  if [[ "$DRY_RUN" -eq 1 ]]; then
    # --dry-run: показываем что изменится, ничего не применяем
    log_info "=== DRY-RUN: изменения не применяются, сервис не останавливается ==="

    # rsync --itemize-changes формат:
    # *deleting     = файл будет удалён
    # >f..t......   = файл будет обновлён (received)
    # <f..t......   = файл будет удалён на источнике (локально)
    local rsync_output
    rsync_output=$(rsync -a --delete --dry-run --itemize-changes \
      "${rsync_exclude_args[@]}" \
      "${staging_dir}/" \
      "${INSTALL_ROOT}/" \
      2>/dev/null || true)

    log_info "Что будет удалено из $INSTALL_ROOT (файлы не из релиза):"
    local del_files
    del_files=$(echo "$rsync_output" | awk '/\*deleting/ && !/\/$/ {sub(/\*deleting +/, ""); print}')
    if [[ -n "$del_files" ]]; then
      echo "$del_files" | while IFS= read -r f; do
        log_info "  DEL $f"
      done
    else
      log_info "  (ничего удалять не нужно)"
    fi

    log_info "Что будет добавлено / обновлено:"
    local upd_files
    upd_files=$(echo "$rsync_output" | grep -v '*deleting' | grep -v '^$' | grep -v '/$' | grep '^.>')
    if [[ -n "$upd_files" ]]; then
      echo "$upd_files" | while IFS= read -r f; do
        log_info "  UPD $(echo "$f" | awk '{sub(/^.. ........... /, ""); print}')"
      done
    else
      log_info "  (нет новых или изменённых файлов)"
    fi

    log_info "=== DRY-RUN завершён. Для реального обновления запустите без --dry-run ==="
    return 0
  fi

  # Реальное обновление
  log_info "Stopping $SERVICE_NAME..."
  systemctl stop "$SERVICE_NAME" 2>/dev/null || true

  log_info "Syncing release into $INSTALL_ROOT (rsync --delete, user paths excluded)..."
  rsync -a --delete \
    "${rsync_exclude_args[@]}" \
    "${staging_dir}/" \
    "${INSTALL_ROOT}/"

  set_install_ownership

  log_info "Starting $SERVICE_NAME..."
  systemctl start "$SERVICE_NAME"
  log_info "Update complete."
}

install_systemd_unit() {
  log_info "Installing systemd unit: $SYSTEMD_UNIT_PATH"
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
  systemctl enable "$SERVICE_NAME"
}

remove_service() {
  if [[ ! -f "$SYSTEMD_UNIT_PATH" ]]; then
    log_info "Service unit not found, skipping"
    return 0
  fi
  log_info "Stopping and disabling $SERVICE_NAME..."
  systemctl stop "$SERVICE_NAME" 2>/dev/null || true
  systemctl disable "$SERVICE_NAME" 2>/dev/null || true
  rm -f "$SYSTEMD_UNIT_PATH"
  systemctl daemon-reload
}

remove_user_and_group() {
  if getent passwd "$LAMPAC_USER" &>/dev/null; then
    log_info "Removing user $LAMPAC_USER..."
    userdel "$LAMPAC_USER" 2>/dev/null || true
  fi
  if getent group "$LAMPAC_USER" &>/dev/null; then
    log_info "Removing group $LAMPAC_USER..."
    groupdel "$LAMPAC_USER" 2>/dev/null || true
  fi
}

remove_app() {
  if [[ ! -d "$INSTALL_ROOT" ]]; then
    log_info "Install directory missing, skipping"
    return 0
  fi
  log_info "Removing $INSTALL_ROOT ..."
  rm -rf "$INSTALL_ROOT"
}

do_remove() {
  log_info "Removing Lampac NextGen..."
  remove_service
  remove_app
  remove_user_and_group
  log_info "Removal complete."
}

start_service() {
  log_info "Starting $SERVICE_NAME..."
  systemctl start "$SERVICE_NAME"
}

print_post_install() {
  cat << EOF

################################################################

Installation complete.

  - Data / config: $INSTALL_ROOT (e.g. init.conf alongside Core.dll)
  - Service:       systemctl status $SERVICE_NAME
  - Restart:       systemctl restart $SERVICE_NAME
  - Logs:          journalctl -u $SERVICE_NAME -f
  - Default port:  $LISTEN_PORT (set in app config if you use another)

################################################################

EOF
}

main() {
  trap cleanup EXIT
  require_root "$@"
  parse_args "$@"

  if [[ "$(uname -s)" != "Linux" ]]; then
    log_err "This script supports Linux only."
    exit 1
  fi
  ARCH=$(detect_arch)
  log_info "Machine architecture: $ARCH (release zip is portable / framework-dependent)"

  if [[ "$REMOVE" -eq 1 ]]; then
    do_remove
    exit 0
  fi

  if [[ "$PRE_RELEASE" -eq 1 ]]; then
    log_info "Resolving latest pre-release asset..."
    get_prerelease_zip_url
  else
    PUBLISH_URL="https://github.com/${GITHUB_REPO}/releases/latest/download/${RELEASE_ZIP_NAME}"
  fi
  log_info "Release URL: $PUBLISH_URL"

  install_os_packages
  install_aspnetcore_runtime

  if [[ "$UPDATE" -eq 1 ]]; then
    do_update
    exit 0
  fi

  log_info "Installing Lampac NextGen to $INSTALL_ROOT ..."
  ensure_service_user
  install_app
  install_systemd_unit
  set_install_ownership
  start_service
  print_post_install
}

main "$@"
