#!/usr/bin/env bash
#
# Lampac NextGen setup manager — a thin, safe wrapper around the official
# upstream installer (install.sh published in the lampac-nextgen/lampac repo).
#
# Architecture constraints of this wrapper (intentional, reviewer-approved):
#   * This script implements NO lifecycle logic of its own: no release archive
#     download/extraction, no rsync, no ownership changes, no backup snapshots,
#     no independent removal. Core.dll is never renamed or rewritten.
#   * update / rollback / remove download the CURRENT official install.sh into
#     a temporary file and invoke it with its documented arguments:
#       update    ->  --update
#       rollback  ->  --update --tag <selected valid tag>
#       remove    ->  --remove
#   * Installation parameters are derived from the selected actual systemd unit
#     and passed to the official installer as LAMPAC_INSTALL_ROOT,
#     LAMPAC_USER and LAMPAC_SERVICE_NAME.
#   * The service is selected with LAMPAC_SERVICE_NAME=<unit>. "lampac" is only
#     the default; any non-default unit installed by the official installer is
#     supported the same way (see usage below).
#   * `lampac setup` is an optional interactive menu that is a thin UI on top
#     of the same wrapper actions.
#
set -uo pipefail

readonly WRAPPER_VERSION='2.0.0'
readonly WRAPPER_MARKER='# lampac-setup-manager wrapper'
readonly LEGACY_MARKER='# Lampac NextGen setup command (managed by installer)'
readonly COMMAND_PATH="${LAMPAC_SETUP_COMMAND_PATH:-/usr/local/bin/lampac}"
readonly DEFAULT_SERVICE='lampac'
readonly DEFAULT_REPO='lampac-nextgen/lampac'
readonly SERVICE_NAME_PATTERN='^[A-Za-z0-9][A-Za-z0-9_.@:-]{0,127}$'
readonly TAG_PATTERN='^v?[0-9]+([._+-][0-9A-Za-z.-]+){0,10}$'
readonly RELEASES_PER_PAGE=100
readonly MAX_MENU_CHOICES=15
readonly MAX_INPUT_LENGTH=256

STATE_SERVICE=''
STATE_UNIT=''
STATE_ROOT=''
STATE_USER=''
STATE_INSTALLER=''

error() { printf 'Error: %s\n' "$*" >&2; }
info() { printf '  %s\n' "$*"; }

trim() {
  local s="$1"
  s="${s#"${s%%[![:space:]]*}"}"
  s="${s%"${s##*[![:space:]]}"}"
  printf '%s' "$s"
}

usage() {
  cat <<USAGE_EOF
Lampac NextGen setup manager v$WRAPPER_VERSION — thin wrapper around the official installer.

This command implements no install/update/rollback/remove logic itself. It downloads the
current official install.sh into a temporary file and invokes it with documented arguments,
passing installation parameters derived from the selected systemd unit
(LAMPAC_INSTALL_ROOT, LAMPAC_USER, LAMPAC_SERVICE_NAME).

Usage:
  bash install-lampac-setup.sh        Install this command as $COMMAND_PATH
  lampac setup                        Interactive menu (thin UI)
  lampac update [--tag VER] [--dry-run] [--force] [--pre-release] [-v]
                                      Official installer: --update [same flags]
  lampac rollback [--tag VER]         Official installer: --update --tag VER
                                      Without --tag, published releases are listed
                                      for interactive selection (valid tags only).
  lampac remove                       Official installer: --remove
  lampac start|stop|restart           Manage the selected systemd unit
  lampac status                       Show the selected unit status
  lampac log                          Show the last 50 log lines of the unit

Selecting a non-default service (any unit installed by the official installer with a
custom service name is supported; "lampac" is only the default):

  LAMPAC_SERVICE_NAME=lampac-nextgen lampac setup
  LAMPAC_SERVICE_NAME=lampac-nextgen lampac update
  LAMPAC_SERVICE_NAME=lampac-nextgen lampac remove

Optional environment:
  LAMPAC_SERVICE_NAME   systemd unit name (with or without the .service suffix)
  LAMPAC_GITHUB_REPO    owner/repo used for the official installer and releases
                        (default: $DEFAULT_REPO)

All lifecycle operations require explicit confirmation. The official installer is then
invoked verbatim and may ask for its own confirmations and root privileges.
USAGE_EOF
}

# ─── Service selection and unit derivation ───────────────────────────────────
# Resolves the selected systemd unit and derives LAMPAC_INSTALL_ROOT (the unit
# WorkingDirectory) and LAMPAC_USER (the unit User). The unit must be a native
# unit written by the official installer and must run Core.dll from its install
# root, which keeps the wrapper from managing unrelated services.

resolve_context() {
  local name="${LAMPAC_SERVICE_NAME:-$DEFAULT_SERVICE}"
  local loadstate fragment workdir user exec_start

  [[ "$name" =~ $SERVICE_NAME_PATTERN ]] \
    || { error "Invalid LAMPAC_SERVICE_NAME '$name'."; return 1; }
  name="${name%.service}"
  STATE_SERVICE="$name"
  STATE_UNIT="$name.service"

  loadstate="$(systemctl show "$STATE_UNIT" -p LoadState --value 2>/dev/null)"
  if [[ "$loadstate" != 'loaded' ]]; then
    error "systemd unit $STATE_UNIT is not loaded on this host (LAMPAC_SERVICE_NAME selects the unit)."
    return 1
  fi

  fragment="$(systemctl show "$STATE_UNIT" -p FragmentPath --value 2>/dev/null)"
  if [[ "$fragment" != "/etc/systemd/system/$STATE_UNIT" || ! -f "$fragment" || -L "$fragment" ]]; then
    error "Unit $STATE_UNIT is not a native unit at /etc/systemd/system/$STATE_UNIT; refusing to manage it."
    return 1
  fi

  workdir="$(systemctl show "$STATE_UNIT" -p WorkingDirectory --value 2>/dev/null)"
  user="$(systemctl show "$STATE_UNIT" -p User --value 2>/dev/null)"
  exec_start="$(systemctl show "$STATE_UNIT" -p ExecStart --value 2>/dev/null)"
  [[ -n "$workdir" && -n "$user" ]] \
    || { error "Unit $STATE_UNIT does not declare WorkingDirectory/User; cannot derive installation parameters."; return 1; }

  [[ "$workdir" == /* && "$workdir" != '/' && -d "$workdir" ]] \
    || { error "Unit $STATE_UNIT declares an unusable WorkingDirectory '$workdir'."; return 1; }
  case "$workdir" in
    /|/bin|/boot|/dev|/etc|/home|/lib|/lib32|/lib64|/libx32|/media|/mnt|/opt|/proc|/root|/run|/sbin|/srv|/sys|/tmp|/usr|/var)
      error "Refusing to manage a unit rooted at top-level directory $workdir."
      return 1 ;;
  esac
  [[ "$workdir" == /opt/* || "$workdir" == /*/*/* ]] \
    || { error "Install root '$workdir' is not a nested directory; refusing."; return 1; }
  [[ "$(readlink -f -- "$workdir" 2>/dev/null)" == "$workdir" ]] \
    || { error "Install root '$workdir' is a symlink; refusing."; return 1; }
  [[ "$exec_start" == *" $workdir/Core.dll"* ]] \
    || { error "Unit $STATE_UNIT does not run Core.dll from $workdir — it does not look like a Lampac NextGen installation created by the official installer; refusing."; return 1; }

  STATE_ROOT="$workdir"
  STATE_USER="$user"
  return 0
}

require_core_in_root() {
  [[ -f "$STATE_ROOT/Core.dll" ]] \
    || { error "Install root $STATE_ROOT does not contain Core.dll; refusing to proceed."; return 1; }
}

# ─── Official installer delegation ───────────────────────────────────────────

cleanup() {
  if [[ -n "$STATE_INSTALLER" && -f "$STATE_INSTALLER" ]]; then
    rm -f -- "$STATE_INSTALLER"
  fi
  STATE_INSTALLER=''
  return 0
}
trap cleanup EXIT

fetch_upstream_installer() {
  local repo="${LAMPAC_GITHUB_REPO:-$DEFAULT_REPO}"
  local url first_line

  [[ "$repo" =~ ^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$ ]] \
    || { error "Invalid LAMPAC_GITHUB_REPO '$repo'."; return 1; }
  command -v curl >/dev/null 2>&1 || { error 'curl is required to download the official installer.'; return 1; }

  STATE_INSTALLER="$(mktemp "${TMPDIR:-/tmp}/lampac-official-install.sh.XXXXXX")" \
    || { error 'Failed to create a temporary file for the official installer.'; return 1; }

  url="https://raw.githubusercontent.com/${repo}/main/install.sh"
  info "Downloading the current official installer: $url"
  if ! curl -fsSL --retry 2 -o "$STATE_INSTALLER" "$url"; then
    error "Failed to download the official install.sh from ${repo}."
    return 1
  fi
  [[ -s "$STATE_INSTALLER" ]] || { error 'The downloaded install.sh is empty.'; return 1; }

  first_line="$(head -n 1 -- "$STATE_INSTALLER" 2>/dev/null)"
  if [[ "$first_line" != '#!'* ]]; then
    error 'The downloaded file does not look like a shell script.'
    return 1
  fi
  if ! grep -q -- '--update' "$STATE_INSTALLER" || ! grep -q -- '--remove' "$STATE_INSTALLER"; then
    error 'The downloaded file does not look like the official Lampac install.sh.'
    return 1
  fi
  return 0
}

# run_upstream_installer ARGS... — downloads install.sh once and runs it with
# exactly the given documented arguments plus derived LAMPAC_* environment.
run_upstream_installer() {
  local rc=0
  fetch_upstream_installer || return 1

  export LAMPAC_INSTALL_ROOT="$STATE_ROOT"
  export LAMPAC_USER="$STATE_USER"
  export LAMPAC_SERVICE_NAME="$STATE_SERVICE"

  info "Invocation: bash <downloaded install.sh> $*"
  info "Installer environment: LAMPAC_SERVICE_NAME=$STATE_SERVICE LAMPAC_INSTALL_ROOT=$STATE_ROOT LAMPAC_USER=$STATE_USER"
  bash "$STATE_INSTALLER" "$@" || rc=$?
  return "$rc"
}

# ─── Confirmation (destructive operations) ───────────────────────────────────

confirm_token() {
  local token="$1"
  shift
  printf '\n  %s\n' "$*"
  printf '  Type %s to confirm (anything else aborts): ' "$token"
  local answer
  if ! IFS= read -r answer; then
    printf '\n'
    error 'No confirmation received; operation aborted.'
    return 1
  fi
  [[ "${#answer}" -le "$MAX_INPUT_LENGTH" ]] \
    || { error 'Confirmation input is too long; operation aborted.'; return 1; }
  [[ "$answer" == "$token" ]] \
    || { error 'Confirmation does not match; operation cancelled.'; return 1; }
  return 0
}

run_privileged() {
  if (( EUID == 0 )); then
    "$@"
  elif command -v sudo >/dev/null 2>&1; then
    sudo "$@"
  else
    error "Administrator privileges are required for: $*"
    return 1
  fi
}

# ─── Release tags (rollback selection) ───────────────────────────────────────

validate_release_tag() {
  [[ -n "$1" && "${#1}" -le 64 && "$1" =~ $TAG_PATTERN ]] \
    || { error "Invalid release tag '$1'."; return 1; }
  return 0
}

list_published_tags() {
  local repo="${LAMPAC_GITHUB_REPO:-$DEFAULT_REPO}"
  local json
  [[ "$repo" =~ ^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$ ]] \
    || { error "Invalid LAMPAC_GITHUB_REPO '$repo'."; return 1; }
  command -v curl >/dev/null 2>&1 || { error 'curl is required to list published releases.'; return 1; }
  command -v jq >/dev/null 2>&1 || { error 'jq is required to list published releases; install it or pass --tag explicitly.'; return 1; }
  json="$(curl -fsSL -H 'Accept: application/vnd.github+json' \
    "https://api.github.com/repos/${repo}/releases?per_page=${RELEASES_PER_PAGE}" 2>/dev/null)" \
    || { error "Failed to fetch the release list from GitHub (repo ${repo})."; return 1; }
  printf '%s' "$json" | jq -r '.[] | select(.draft == false and .prerelease == false) | .tag_name' 2>/dev/null
}

read_installed_version() {
  local v
  [[ -r "$STATE_ROOT/version.txt" ]] || return 1
  v="$(head -n 1 -- "$STATE_ROOT/version.txt" 2>/dev/null)"
  v="$(trim "$v")"
  [[ "$v" =~ $TAG_PATTERN ]] || return 1
  printf '%s' "$v"
}

version_is_older() {
  local candidate="${1#v}" reference="${2#v}" first
  [[ "$candidate" != "$reference" ]] || return 1
  first="$(printf '%s\n%s\n' "$candidate" "$reference" | sort -V 2>/dev/null | sed -n '1p')"
  [[ "$first" == "$candidate" ]]
}

tag_in_list() {
  local needle="$1" normalized t
  normalized="${needle#v}"
  shift
  for t in "$@"; do
    [[ "${t#v}" == "$normalized" ]] && return 0
  done
  return 1
}

select_tag_interactively() {
  local current tag answer
  local -a tags=() choices=()
  mapfile -t tags < <(list_published_tags)
  (( ${#tags[@]} > 0 )) || { error 'No published releases found.'; return 1; }

  current="$(read_installed_version || true)"
  if [[ -n "$current" ]]; then
    for tag in "${tags[@]}"; do
      version_is_older "$tag" "$current" && choices+=("$tag")
    done
    if (( ${#choices[@]} == 0 )); then
      error "Installed version $current is not newer than any published release; nothing to roll back to."
      return 1
    fi
  else
    info "Installed version unknown ($STATE_ROOT/version.txt missing or unparsable); listing all published releases."
    choices=("${tags[@]}")
  fi

  if (( ${#choices[@]} > MAX_MENU_CHOICES )); then
    choices=("${choices[@]:0:MAX_MENU_CHOICES}")
  fi

  printf '\n  Select the release to roll back to (valid published tags):\n\n'
  local i
  for ((i = 0; i < ${#choices[@]}; i++)); do
    printf '    %d) %s\n' "$((i + 1))" "${choices[$i]}"
  done
  printf '    0) Cancel\n\n  Version number: '
  while true; do
    if ! IFS= read -r answer; then
      printf '\n'
      error 'No selection made; rollback cancelled.'
      return 1
    fi
    case "$answer" in
      0) info 'Rollback cancelled.'; return 1 ;;
      ''|*[!0-9]*) error 'Enter a version number from the list, or 0 to cancel.' ;;
      *)
        (( ${#answer} <= 3 )) || { error 'Input too long.'; continue; }
        (( 10#$answer >= 1 && 10#$answer <= ${#choices[@]} )) || { error 'There is no such entry.'; continue; }
        SELECTED_TAG="${choices[$((10#$answer - 1))]}"
        return 0 ;;
    esac
    printf '  Version number: '
  done
}

# ─── Wrapper commands ─────────────────────────────────────────────────────────

cmd_update() {
  local -a pass=()
  while (( $# )); do
    case "$1" in
      --tag)
        if [[ $# -lt 2 || -z "${2:-}" || "${2:0:1}" == '-' ]]; then
          error '--tag requires a version argument (e.g. --tag v1.2.3).'
          return 2
        fi
        validate_release_tag "$2" || return 2
        pass+=(--tag "$2")
        shift 2 ;;
      --dry-run|--force|--pre-release|-v|--verbose)
        pass+=("$1")
        shift ;;
      *)
        error "Unknown option for update: $1"
        return 2 ;;
    esac
  done

  resolve_context || return 1
  require_core_in_root || return 1

  printf '\n  Update via the official installer:\n'
  info "Service:      $STATE_UNIT"
  info "Install root: $STATE_ROOT"
  info "Arguments:    --update${pass[*]:+ ${pass[*]}}"
  confirm_token UPDATE \
    "Update Lampac NextGen (service $STATE_UNIT) by delegating to the official installer? The installer restarts the service." \
    || { info 'Update cancelled.'; return 1; }

  run_upstream_installer --update ${pass[@]+"${pass[@]}"}
}

cmd_rollback() {
  local tag=''
  while (( $# )); do
    case "$1" in
      --tag)
        if [[ $# -lt 2 || -z "${2:-}" || "${2:0:1}" == '-' ]]; then
          error '--tag requires a version argument (e.g. --tag v1.2.3).'
          return 2
        fi
        tag="$2"
        shift 2 ;;
      *)
        error "Unknown option for rollback: $1"
        return 2 ;;
    esac
  done

  resolve_context || return 1
  require_core_in_root || return 1

  local -a tags=()
  mapfile -t tags < <(list_published_tags)
  (( ${#tags[@]} > 0 )) || { error 'No published releases found; rollback is not possible.'; return 1; }

  if [[ -z "$tag" ]]; then
    SELECTED_TAG=''
    select_tag_interactively || return 1
    tag="$SELECTED_TAG"
    validate_release_tag "$tag" || return 1
  else
    validate_release_tag "$tag" || return 1
    if ! tag_in_list "$tag" "${tags[@]}"; then
      error "Tag $tag is not a published release (valid tags only are accepted)."
      return 1
    fi
  fi

  printf '\n  Rollback via the official installer:\n'
  info "Service:      $STATE_UNIT"
  info "Install root: $STATE_ROOT"
  info "Arguments:    --update --tag $tag"
  confirm_token ROLLBACK \
    "Roll back Lampac NextGen (service $STATE_UNIT) to $tag by delegating to the official installer? The installer restarts the service." \
    || { info 'Rollback cancelled.'; return 1; }

  run_upstream_installer --update --tag "$tag"
}

cmd_remove() {
  resolve_context || return 1

  printf '\n  Removal via the official installer (--remove):\n'
  info "Service:      $STATE_UNIT"
  info "Install root: $STATE_ROOT"
  info 'The official installer removes the systemd unit, the install directory and the service account. This wrapper runs no removal of its own; the installer may ask for its own confirmation.'
  confirm_token REMOVE \
    "Remove Lampac NextGen unit $STATE_UNIT and install root $STATE_ROOT by delegating to the official installer?" \
    || { info 'Removal cancelled.'; return 1; }
  confirm_token "$STATE_ROOT" \
    "Second confirmation: type the full install root path ($STATE_ROOT) to proceed:" \
    || { info 'Removal cancelled.'; return 1; }

  run_upstream_installer --remove
}

cmd_service() {
  local action="$1"
  resolve_context || return 1
  run_privileged systemctl "$action" "$STATE_UNIT"
}

cmd_status() {
  resolve_context || return 1
  systemctl status "$STATE_UNIT"
}

cmd_logs() {
  resolve_context || return 1
  run_privileged journalctl -u "$STATE_UNIT" -n 50 --no-pager
}

# ─── Interactive menu (thin UI) ───────────────────────────────────────────────

setup_menu() {
  if [[ ! -t 0 || ! -t 1 ]]; then
    error "'lampac setup' is an interactive menu; in non-interactive shells use direct commands: lampac update|rollback|remove|start|stop|restart|status|log."
    return 2
  fi
  trap 'printf "\nInterrupted.\n"; exit 130' INT
  resolve_context || return 1

  local state version answer
  while true; do
    state="$(systemctl is-active "$STATE_UNIT" 2>/dev/null)"
    state="${state:-unknown}"
    version="$(read_installed_version 2>/dev/null)"
    version="${version:-N/A}"
    if [[ "$state" == active ]]; then
      state='running'
    elif [[ "$state" == inactive ]]; then
      state='stopped'
    fi

    printf '\n  Lampac NextGen setup manager v%s (thin wrapper around the official installer)\n\n' "$WRAPPER_VERSION"
    printf '  Service:      systemctl unit  %s\n' "$STATE_UNIT"
    printf '  Install root: %s\n' "$STATE_ROOT"
    printf '  Version:      %s\n' "$version"
    printf '  State:        %s\n\n' "$state"
    printf '    1) Restart the service\n'
    if [[ "$(systemctl is-active "$STATE_UNIT" 2>/dev/null)" == active ]]; then
      printf '    2) Stop the service\n'
    else
      printf '    2) Start the service\n'
    fi
    printf '    3) Update via the official installer\n'
    printf '    4) Roll back to an earlier release via the official installer\n'
    printf '    5) Remove via the official installer\n'
    printf '    6) Show the last 50 log lines\n'
    printf '    0) Exit\n\n  Choose an action: '

    if ! IFS= read -r answer; then
      printf '\n'
      return 0
    fi
    (( ${#answer} <= 4 )) || { error 'Invalid input.'; continue; }
    case "$answer" in
      1) run_privileged systemctl restart "$STATE_UNIT" && info 'Restart requested.' ;;
      2)
        if [[ "$(systemctl is-active "$STATE_UNIT" 2>/dev/null)" == active ]]; then
          run_privileged systemctl stop "$STATE_UNIT" && info 'Stop requested.'
        else
          run_privileged systemctl start "$STATE_UNIT" && info 'Start requested.'
        fi ;;
      3) cmd_update ;;
      4) cmd_rollback ;;
      5) cmd_remove && return 0 ;;
      6) cmd_logs ;;
      0|q|Q) return 0 ;;
      *) error "Unknown choice '$answer'." ;;
    esac
  done
}

# ─── Self-install of the wrapper command ──────────────────────────────────────

cmd_install_self() {
  local src
  [[ "$(uname -s 2>/dev/null)" == 'Linux' ]] || { error 'The setup manager supports Linux only.'; return 1; }
  command -v systemctl >/dev/null 2>&1 || { error 'systemd (systemctl) is required to manage Lampac.'; return 1; }

  src="$(readlink -f -- "${BASH_SOURCE[0]}" 2>/dev/null)" \
    || { error 'Failed to resolve the wrapper file location.'; return 1; }

  if (( EUID != 0 )); then
    if [[ "${LAMPAC_SETUP_ELEVATION:-}" == 1 ]]; then
      info 'Running without root privileges; the target directory must already be writable.'
    else
      command -v sudo >/dev/null 2>&1 \
        || { error "Installing into $(dirname "$COMMAND_PATH") requires root or sudo."; return 1; }
      export LAMPAC_SETUP_ELEVATION=1
      exec sudo -E bash "$src" install
    fi
  fi

  if [[ "$src" == "$COMMAND_PATH" ]]; then
    info "The wrapper command is already installed at $COMMAND_PATH."
    return 0
  fi

  install -d -m 755 "$(dirname "$COMMAND_PATH")" \
    || { error "Failed to create $(dirname "$COMMAND_PATH")."; return 1; }

  if [[ -e "$COMMAND_PATH" || -L "$COMMAND_PATH" ]]; then
    if [[ -L "$COMMAND_PATH" ]]; then
      error "$COMMAND_PATH exists as a symlink; refusing to overwrite a foreign file."
      return 1
    fi
    if grep -Fq "$WRAPPER_MARKER" "$COMMAND_PATH" || grep -Fq "$LEGACY_MARKER" "$COMMAND_PATH"; then
      info "Existing wrapper copy found at $COMMAND_PATH; it will be replaced."
    else
      error "$COMMAND_PATH already exists and does not belong to the lampac setup manager; refusing to overwrite an unrelated command."
      return 1
    fi
  fi

  local temporary
  temporary="$(mktemp "$(dirname "$COMMAND_PATH")/.lampac.XXXXXX")" \
    || { error 'Failed to create a temporary file for installation.'; return 1; }
  if ! install -m 755 "$src" "$temporary"; then
    rm -f -- "$temporary"
    error "Failed to install the wrapper command to $COMMAND_PATH."
    return 1
  fi
  if ! mv -f -- "$temporary" "$COMMAND_PATH"; then
    rm -f -- "$temporary"
    error "Failed to finalize installation of $COMMAND_PATH."
    return 1
  fi

  info "Installed: $COMMAND_PATH"
  info "Run 'lampac setup' for the interactive menu; see 'lampac --help'."
  info "Non-default service example: LAMPAC_SERVICE_NAME=lampac-nextgen lampac setup"
  return 0
}

# ─── Dispatch ─────────────────────────────────────────────────────────────────

main() {
  local cmd="${1:-}"
  case "$cmd" in
    '')
      if [[ "$(readlink -f -- "${BASH_SOURCE[0]}" 2>/dev/null)" == "$COMMAND_PATH" ]]; then
        usage
        return 0
      fi
      cmd_install_self ;;
    install) shift; cmd_install_self "$@" ;;
    setup)
      shift
      if (( $# )); then
        error "'setup' takes no arguments; use direct commands: lampac update|rollback|remove|start|stop|restart|status|log."
        return 2
      fi
      setup_menu ;;
    update) shift; cmd_update "$@" ;;
    rollback) shift; cmd_rollback "$@" ;;
    remove) shift; cmd_remove "$@" ;;
    start|stop|restart) cmd_service "$1" ;;
    status) cmd_status ;;
    log|logs) cmd_logs ;;
    help|-h|--help) usage; return 0 ;;
    version|--version) printf 'lampac-setup-manager %s\n' "$WRAPPER_VERSION"; return 0 ;;
    *) error "Unknown command: $cmd"; usage >&2; return 2 ;;
  esac
}

main "$@"