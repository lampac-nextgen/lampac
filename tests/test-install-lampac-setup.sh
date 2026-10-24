#!/usr/bin/env bash
#
# Portable regression tests for install-lampac-setup.sh (the lampac setup
# manager). No real network, no real systemd, no root privileges required:
# systemctl, curl, jq, sudo, journalctl are mocked; rsync/unzip/tar/chown and
# the user/group removal tools are mocked as FORBIDDEN so the test fails if the
# wrapper ever implements lifecycle operations itself.
#
# Usage:  bash tests/test-install-lampac-setup.sh
#
set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WRAPPER="$ROOT_DIR/install-lampac-setup.sh"

PASS=0
FAIL=0
CASE_N=0
CASE=''
MOCK_BIN=''
STATE=''
RC=0

TESTS_TMP="$(mktemp -d "${TMPDIR:-/tmp}/lampac-setup-tests.XXXXXX")"
# Nested TMPDIR so the wrapper's mktemp files land inside the test tree.
export TMPDIR="$TESTS_TMP"
trap '[[ "${KEEP_TMP:-}" == 1 ]] || rm -rf "$TESTS_TMP"' EXIT

fail() { printf 'FAIL: %s\n' "$*"; FAIL=$((FAIL + 1)); }
pass() { printf 'ok:   %s\n' "$*"; PASS=$((PASS + 1)); }
assert_eq() {
  if [[ "$1" == "$2" ]]; then pass "$3"; else fail "$3 (expected [$2], got [$1])"; fi
}
assert_rc() {
  if [[ "$1" == "$2" ]]; then pass "$3"; else fail "$3 (expected rc=$1, got rc=$2)"; fi
}
assert_file_absent() {
  if [[ -e "$1" ]]; then fail "$2 (exists: $1)"; else pass "$2"; fi
}
assert_contains() {
  if grep -qF -- "$2" <<<"$1"; then pass "$3"; else fail "$3 (missing [$2])"; fi
}

# ─── Mock generation ──────────────────────────────────────────────────────────

make_stub() { # $1 = name; stub text with %STATE% placeholder on stdin
  sed "s|%STATE%|$STATE|g" > "$MOCK_BIN/$1"
  chmod +x "$MOCK_BIN/$1"
}

make_systemctl_stub() {
  make_stub systemctl <<'EOF'
#!/usr/bin/env bash
STATE="%STATE%"
LOG="$STATE/systemctl.log"
SUB="${1:-}"
shift || true
printf '%s %s\n' "$SUB" "$*" >> "$LOG"
unit="${1:-}"
shift || true
cfg="$STATE/units/$unit.env"
get() {
  local key="$1" val=''
  if [[ -r "$cfg" ]]; then val="$(sed -n "s/^$key=//p" "$cfg" | head -n 1)"; fi
  printf '%s' "$val"
}
case "$SUB" in
  show)
    prop=''
    while (($#)); do
      case "$1" in
        -p) prop="$2"; shift 2 ;;
        *) shift ;;
      esac
    done
    v="$(get "$prop")"
    if [[ -z "$v" && ! -r "$cfg" && "$prop" == LoadState ]]; then v='not-found'; fi
    printf '%s\n' "$v" ;;
  is-active)
    v="$(get IS_ACTIVE)"
    [[ -n "$v" ]] || v='unknown'
    printf '%s\n' "$v"
    [[ "$v" == active ]] || exit 3 ;;
  is-enabled)
    v="$(get IS_ENABLED)"
    [[ -n "$v" ]] || v='disabled'
    printf '%s\n' "$v"
    [[ "$v" == enabled ]] || exit 1 ;;
  *) exit 0 ;;
esac
EOF
}

make_curl_stub() {
  make_stub curl <<'EOF'
#!/usr/bin/env bash
STATE="%STATE%"
LOG="$STATE/curl.log"
out=''
url=''
while (($#)); do
  case "$1" in
    -o) out="$2"; shift 2 ;;
    -H|--retry|-A|-D|--connect-timeout|--max-time) shift 2 ;;
    -*) shift ;;
    *) url="$1"; shift ;;
  esac
done
printf '%s OUT=%s\n' "$url" "${out:-stdout}" >> "$LOG"
case "$url" in
  https://raw.githubusercontent.com/*/install.sh)
    if [[ -z "$out" ]]; then
      echo 'stub-curl: install.sh download must use -o' >&2
      exit 1
    fi
    cat > "$out" <<ENDSTUB
#!/usr/bin/env bash
# Test stub of the official install.sh (documents --update --tag --remove).
REC="$STATE/installer-calls.log"
printf 'ARGS=%s\n' "\$*" >> "\$REC"
printf 'ROOT=%s\n' "\${LAMPAC_INSTALL_ROOT-<unset>}" >> "\$REC"
printf 'USER=%s\n' "\${LAMPAC_USER-<unset>}" >> "\$REC"
printf 'SERVICE=%s\n' "\${LAMPAC_SERVICE_NAME-<unset>}" >> "\$REC"
ENDSTUB
    exit 0 ;;
  https://api.github.com/repos/*/releases*)
    cat "$STATE/releases.json" 2>/dev/null; exit 0 ;;
  *)
    printf 'UNEXPECTED %s\n' "$url" >> "$LOG"
    echo "stub-curl: unexpected URL: $url" >&2
    exit 1 ;;
esac
EOF
}

make_generic_stubs() {
  make_stub jq <<'EOF'
#!/usr/bin/env bash
# Minimal jq subset: prints "tag_name" values found on stdin, one per line.
grep -o '"tag_name"[[:space:]]*:[[:space:]]*"[^"]*"' \
  | sed -E 's/^[^"]*"tag_name"[[:space:]]*:[[:space:]]*"//; s/".*$//'
EOF
  make_stub sudo <<'EOF'
#!/usr/bin/env bash
if [[ "${1:-}" == -E ]]; then shift; fi
exec "$@"
EOF
  make_stub journalctl <<'EOF'
#!/usr/bin/env bash
STATE="%STATE%"
printf '%s\n' "$*" >> "$STATE/journalctl.log"
printf 'mock journalctl: %s\n' "$*"
EOF
  local tool
  for tool in rsync unzip tar chown chgrp userdel groupdel useradd groupadd; do
    make_stub "$tool" <<'EOF'
#!/usr/bin/env bash
STATE="%STATE%"
printf 'FORBIDDEN %s %s\n' "$(basename "$0")" "$*" >> "$STATE/violations.log"
echo "test stub: $0 must not be called by the wrapper" >&2
exit 1
EOF
  done
}

new_case() {
  CASE_N=$((CASE_N + 1))
  CASE="$TESTS_TMP/case-$CASE_N"
  mkdir -p "$CASE/bin" "$CASE/state/units" "$CASE/roots"
  MOCK_BIN="$CASE/bin"
  STATE="$CASE/state"
  make_systemctl_stub
  make_curl_stub
  make_generic_stubs
  : > "$STATE/releases.json"
}

mk_unit() { # $1 unit, $2 workdir, $3 user, $4 execstart, $5 is_active
  cat > "$STATE/units/$1.env" <<UNIT
LoadState=loaded
FragmentPath=/etc/systemd/system/$1
WorkingDirectory=$2
User=$3
ExecStart=$4
IS_ACTIVE=$5
IS_ENABLED=enabled
UNIT
  mkdir -p "$2"
  : > "$2/Core.dll"
}

mk_app() { # $1 workdir, $2 optional version.txt content
  mkdir -p "$1"
  : > "$1/Core.dll"
  [[ $# -ge 2 ]] && printf '%s\n' "$2" > "$1/version.txt"
  return 0
}

run_w() { # wrapper args from "$@"; stdin content in STDIN_DATA ('' allowed)
  local input="${STDIN_DATA:-}"
  ( export PATH="$MOCK_BIN:$PATH"
    export TMPDIR="$TESTS_TMP"
    cd "$CASE" || exit 99
    if [[ -n "$input" ]]; then
      printf '%s' "$input" | bash "$WRAPPER" "$@"
    else
      bash "$WRAPPER" "$@" </dev/null
    fi
  ) > "$CASE/stdout" 2> "$CASE/stderr"
  RC=$?
  return 0
}

check_no_violations() {
  if [[ -s "$STATE/violations.log" ]]; then
    fail "forbidden lifecycle tool invoked in case $CASE_N:
$(cat "$STATE/violations.log")"
  else
    pass "no forbidden lifecycle tools in case $CASE_N"
  fi
}

check_no_repository_conf() {
  local found=0
  while IFS= read -r -d '' f; do
    found=1
    fail "repository.conf was created: $f"
  done < <(find "$CASE" -name 'repository.conf' -print0 2>/dev/null)
  (( found == 0 )) && pass "no repository.conf created (case $CASE_N)"
  return 0
}

case_summary() {
  printf '\n'
  if (( FAIL == 0 )); then
    printf 'ALL TESTS PASSED: %d checks\n' "$PASS"
  else
    printf '%d checks passed, %d FAILED\n' "$PASS" "$FAIL"
    exit 1
  fi
}

# ─── 0. Syntax and hygiene ────────────────────────────────────────────────────

if bash -n "$WRAPPER" && bash -n "${BASH_SOURCE[0]}"; then
  pass 'bash -n passes for wrapper and test suite'
else
  fail 'bash -n reported a syntax error'
fi
if grep -q 'repository.conf' "$WRAPPER"; then
  fail 'wrapper source references repository.conf'
else
  pass 'wrapper source never references repository.conf'
fi
if grep -qE '^\s*(rsync|unzip|tar|chown) ' "$WRAPPER"; then
  fail 'wrapper source contains lifecycle implementation commands'
else
  pass 'wrapper source contains no extraction/rsync logic'
fi

# ─── 1. update: delegation with derived env, default service ─────────────────

new_case
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
STDIN_DATA=$'UPDATE\n' run_w update
assert_rc 0 "$RC" 'update exits 0 after UPDATE confirmation'
if [[ -f "$STATE/installer-calls.log" ]]; then
  assert_eq "$(head -n 1 "$STATE/installer-calls.log")" 'ARGS=--update' 'downstream args are exactly --update'
  assert_contains "$(cat "$STATE/installer-calls.log")" "ROOT=$CASE/roots/app" 'LAMPAC_INSTALL_ROOT derived from unit WorkingDirectory'
  assert_contains "$(cat "$STATE/installer-calls.log")" 'USER=lampac' 'LAMPAC_USER derived from unit User'
  assert_contains "$(cat "$STATE/installer-calls.log")" 'SERVICE=lampac' 'LAMPAC_SERVICE_NAME passed to installer'
  assert_eq "$(grep -c '^ARGS=' "$STATE/installer-calls.log")" '1' 'official installer invoked exactly once'
else
  fail 'official installer was never invoked for update'
fi
assert_contains "$(cat "$STATE/curl.log")" \
  'https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh OUT=' \
  'official install.sh downloaded from upstream raw URL into temp file'
assert_file_absent "$CASE/roots/app/repository.conf" 'no repository.conf after update'
check_no_violations
check_no_repository_conf

# update confirmation refusal: wrong token aborts before any download/invocation
new_case
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
STDIN_DATA=$'nope\n' run_w update
if (( RC != 0 )); then pass 'update refuses wrong confirmation (rc!=0)'; else fail 'update accepted wrong confirmation'; fi
assert_file_absent "$STATE/installer-calls.log" 'no installer invocation on wrong confirmation'
check_no_violations

# ─── 2. custom service via LAMPAC_SERVICE_NAME (and .service suffix) ─────────

new_case
mk_unit lampac-nextgen.service "$CASE/roots/ng" lampacng "/usr/bin/dotnet $CASE/roots/ng/Core.dll" inactive
LAMPAC_SERVICE_NAME='lampac-nextgen.service' STDIN_DATA=$'UPDATE\n' run_w update
assert_rc 0 "$RC" 'custom service update exits 0'
if [[ -f "$STATE/installer-calls.log" ]]; then
  assert_eq "$(head -n 1 "$STATE/installer-calls.log")" 'ARGS=--update' 'custom service args exactly --update'
  assert_contains "$(cat "$STATE/installer-calls.log")" "ROOT=$CASE/roots/ng" 'custom service root derived from its unit'
  assert_contains "$(cat "$STATE/installer-calls.log")" 'USER=lampacng' 'custom service user derived from its unit'
  assert_contains "$(cat "$STATE/installer-calls.log")" 'SERVICE=lampac-nextgen' 'custom service name passed bare to installer'
else
  fail 'installer never invoked for custom service update'
fi
assert_contains "$(cat "$STATE/systemctl.log")" 'show lampac-nextgen.service' 'systemctl queried for the custom unit'
check_no_violations

# missing / invalid service names are refused safely
new_case
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
LAMPAC_SERVICE_NAME='../evil' STDIN_DATA='' run_w update
if (( RC != 0 )); then pass 'malformed LAMPAC_SERVICE_NAME refused'; else fail 'malformed LAMPAC_SERVICE_NAME accepted'; fi
if [[ -s "$STATE/systemctl.log" ]]; then fail 'systemctl consulted for malformed service name'; else pass 'systemctl not consulted for malformed name'; fi
LAMPAC_SERVICE_NAME='ghost' STDIN_DATA='' run_w update
if (( RC != 0 )); then pass 'unknown service refused'; else fail 'unknown service accepted'; fi
assert_file_absent "$STATE/installer-calls.log" 'no installer invocation for unknown service'
check_no_violations

# unit safety: non-Lampac unit (ExecStart without Core.dll) is refused
new_case
mk_unit myapp.service "$CASE/roots/other" root '/usr/sbin/nginx -g daemon' active
LAMPAC_SERVICE_NAME=myapp STDIN_DATA=$'UPDATE\n' run_w update
if (( RC != 0 )); then pass 'non-Lampac unit refused'; else fail 'non-Lampac unit accepted'; fi
assert_file_absent "$STATE/installer-calls.log" 'no installer invocation for non-Lampac unit'
check_no_violations

# ─── 3. rollback: --tag forwarding, validation, interactive selection ────────

new_case
mk_app "$CASE/roots/app" 1.81.0
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
cat > "$STATE/releases.json" <<'JSON'
{"tag_name": "v1.81.0", "draft": false, "prerelease": false}
{"tag_name": "v1.80.0", "draft": false, "prerelease": false}
{"tag_name": "v1.79.0", "draft": false, "prerelease": false}
JSON
STDIN_DATA=$'ROLLBACK\n' run_w rollback --tag v1.80.0
assert_rc 0 "$RC" 'rollback --tag exits 0 after confirmation'
if [[ -f "$STATE/installer-calls.log" ]]; then
  assert_eq "$(head -n 1 "$STATE/installer-calls.log")" 'ARGS=--update --tag v1.80.0' 'rollback args exactly --update --tag v1.80.0'
else
  fail 'installer never invoked for rollback --tag'
fi
check_no_violations

# upstream accepts both v-prefixed and bare tag forms
new_case
mk_app "$CASE/roots/app" 1.81.0
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
cat > "$STATE/releases.json" <<'JSON'
{"tag_name": "v1.81.0", "draft": false, "prerelease": false}
{"tag_name": "v1.80.0", "draft": false, "prerelease": false}
JSON
STDIN_DATA=$'ROLLBACK\n' run_w rollback --tag 1.80.0
assert_rc 0 "$RC" 'rollback accepts a published bare tag'
assert_eq "$(head -n 1 "$STATE/installer-calls.log")" 'ARGS=--update --tag 1.80.0' 'bare tag is forwarded unchanged to installer'
check_no_violations

# unknown tag (valid format, not published) refused; invalid format refused
new_case
mk_app "$CASE/roots/app" 1.81.0
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
cp "$TESTS_TMP/case-$((CASE_N - 1))/state/releases.json" "$STATE/releases.json" 2>/dev/null || true
STDIN_DATA=$'ROLLBACK\n' run_w rollback --tag v9.9.9
if (( RC != 0 )); then pass 'unpublished tag refused'; else fail 'unpublished tag accepted'; fi
assert_file_absent "$STATE/installer-calls.log" 'no installer invocation for unpublished tag'
STDIN_DATA='' run_w rollback --tag $'bad tag; rm -rf /'
if (( RC != 0 )); then pass 'malformed tag refused'; else fail 'malformed tag accepted'; fi
assert_file_absent "$STATE/installer-calls.log" 'no installer invocation for malformed tag'
check_no_violations

# interactive rollback: only older published tags offered, selection forwarded
new_case
mk_app "$CASE/roots/app" 1.81.0
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
cat > "$STATE/releases.json" <<'JSON'
{"tag_name": "v1.81.0", "draft": false, "prerelease": false}
{"tag_name": "v1.80.0", "draft": false, "prerelease": false}
{"tag_name": "v1.79.0", "draft": false, "prerelease": false}
JSON
STDIN_DATA=$'2\nROLLBACK\n' run_w rollback
assert_rc 0 "$RC" 'interactive rollback exits 0'
if [[ -f "$STATE/installer-calls.log" ]]; then
  assert_eq "$(head -n 1 "$STATE/installer-calls.log")" 'ARGS=--update --tag v1.79.0' 'interactive rollback picks older tag and forwards --update --tag'
else
  fail 'installer never invoked for interactive rollback'
fi
if grep -qF 'v1.81.0' "$CASE/stdout" && grep -qE '    1\) v1\.80\.0' "$CASE/stdout"; then
  fail 'current version offered as a rollback choice'
else
  pass 'current version excluded from rollback choices'
fi
check_no_violations

# rollback without version.txt lists all published tags
new_case
mk_app "$CASE/roots/app"
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
cat > "$STATE/releases.json" <<'JSON'
{"tag_name": "v1.81.0", "draft": false, "prerelease": false}
{"tag_name": "v1.80.0", "draft": false, "prerelease": false}
JSON
STDIN_DATA=$'1\nROLLBACK\n' run_w rollback
assert_rc 0 "$RC" 'rollback with unknown installed version exits 0'
assert_eq "$(head -n 1 "$STATE/installer-calls.log" 2>/dev/null)" 'ARGS=--update --tag v1.81.0' 'unknown-version rollback offers published tags'
check_no_violations

# ─── 4. remove: exact --remove, double confirmation, refusal ─────────────────

new_case
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
STDIN_DATA=$'REMOVE\n'"$CASE/roots/app"$'\n' run_w remove
assert_rc 0 "$RC" 'remove exits 0 after REMOVE + path confirmations'
if [[ -f "$STATE/installer-calls.log" ]]; then
  assert_eq "$(head -n 1 "$STATE/installer-calls.log")" 'ARGS=--remove' 'remove args exactly --remove'
else
  fail 'installer never invoked for remove'
fi
assert_file_absent "$CASE/roots/app/Core.dll-renamed" 'Core.dll untouched by wrapper'
check_no_violations

new_case
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
STDIN_DATA=$'REMOVE\ngoodbye\n' run_w remove
if (( RC != 0 )); then pass 'remove refuses wrong path confirmation'; else fail 'remove accepted wrong path confirmation'; fi
assert_file_absent "$STATE/installer-calls.log" 'no installer invocation on failed remove confirmation'
STDIN_DATA=$'remove\n' run_w remove
if (( RC != 0 )); then pass 'remove is case-sensitive on REMOVE token'; else fail 'remove accepted lowercase token'; fi
STDIN_DATA='' run_w remove
if (( RC != 0 )); then pass 'remove fails closed on empty stdin (non-interactive)'; else fail 'remove proceeded without confirmation'; fi
assert_file_absent "$STATE/installer-calls.log" 'no installer invocation without confirmation'
check_no_violations

# custom service remove
new_case
mk_unit lampac-nextgen.service "$CASE/roots/ng" lampacng "/usr/bin/dotnet $CASE/roots/ng/Core.dll" inactive
LAMPAC_SERVICE_NAME='lampac-nextgen' STDIN_DATA=$'REMOVE\n'"$CASE/roots/ng"$'\n' run_w remove
assert_rc 0 "$RC" 'custom service remove exits 0'
assert_eq "$(head -n 1 "$STATE/installer-calls.log" 2>/dev/null)" 'ARGS=--remove' 'custom service remove passes --remove'
assert_contains "$(cat "$STATE/installer-calls.log")" 'SERVICE=lampac-nextgen' 'custom service name passed on remove'
check_no_violations

# ─── 5. service operations use the selected unit ─────────────────────────────

new_case
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
STDIN_DATA='' run_w restart
assert_rc 0 "$RC" 'restart exits 0'
assert_contains "$(cat "$STATE/systemctl.log")" 'restart lampac.service' 'restart targets lampac.service'
STDIN_DATA='' run_w stop
assert_contains "$(cat "$STATE/systemctl.log")" 'stop lampac.service' 'stop targets lampac.service'
STDIN_DATA='' run_w start
assert_contains "$(cat "$STATE/systemctl.log")" 'start lampac.service' 'start targets lampac.service'
STDIN_DATA='' run_w status
assert_rc 0 "$RC" 'status exits 0'
STDIN_DATA='' run_w log
assert_rc 0 "$RC" 'log exits 0'
assert_contains "$(cat "$STATE/journalctl.log")" '-u lampac.service' 'journalctl targets the selected unit'
check_no_violations

new_case
mk_unit lampac-nextgen.service "$CASE/roots/ng" lampacng "/usr/bin/dotnet $CASE/roots/ng/Core.dll" inactive
LAMPAC_SERVICE_NAME='lampac-nextgen' STDIN_DATA='' run_w restart
assert_rc 0 "$RC" 'custom service restart exits 0'
assert_contains "$(cat "$STATE/systemctl.log")" 'restart lampac-nextgen.service' 'restart targets the custom unit'
LAMPAC_SERVICE_NAME='lampac-nextgen' STDIN_DATA='' run_w stop
assert_contains "$(cat "$STATE/systemctl.log")" 'stop lampac-nextgen.service' 'stop targets the custom unit'
check_no_violations

# ─── 6. update flag forwarding (documented upstream flags) ───────────────────

new_case
mk_app "$CASE/roots/app" 1.81.0
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
STDIN_DATA=$'UPDATE\n' run_w update --tag v1.2.3 --dry-run
assert_rc 0 "$RC" 'update with forwarded flags exits 0'
assert_eq "$(head -n 1 "$STATE/installer-calls.log" 2>/dev/null)" 'ARGS=--update --tag v1.2.3 --dry-run' 'forwarded flags keep --update first'
check_no_violations

# ─── 7. safe self-install: never overwrites an unrelated lampac ──────────────

new_case
mkdir -p "$CASE/bin"
printf '#!/bin/sh\necho unrelated-tool\n' > "$CASE/bin/lampac"
chmod +x "$CASE/bin/lampac"
LAMPAC_SETUP_COMMAND_PATH="$CASE/bin/lampac" STDIN_DATA='' run_w install
if (( RC != 0 )); then pass 'install refuses unrelated /usr/local/bin/lampac'; else fail 'install overwrote unrelated command'; fi
assert_eq "$(cat "$CASE/bin/lampac")" '#!/bin/sh
echo unrelated-tool' 'unrelated lampac file untouched'

new_case
mkdir -p "$CASE/bin"
cat > "$CASE/bin/lampac" <<'EOF'
#!/usr/bin/env bash
# Lampac NextGen setup command (managed by installer)
EOF
LAMPAC_SETUP_COMMAND_PATH="$CASE/bin/lampac" STDIN_DATA='' run_w install
assert_rc 0 "$RC" 'install replaces previous managed wrapper copy'
if grep -qF '# lampac-setup-manager wrapper' "$CASE/bin/lampac"; then pass 'managed copy replaced with new wrapper'; else fail 'wrapper marker missing after replace'; fi

new_case
mkdir -p "$CASE/bin"
LAMPAC_SETUP_COMMAND_PATH="$CASE/bin/lampac" STDIN_DATA='' run_w install
assert_rc 0 "$RC" 'fresh install exits 0'
assert_eq "$(head -n 1 "$CASE/bin/lampac")" '#!/usr/bin/env bash' 'fresh install writes wrapper script'
if grep -qF 'LAMPAC_SERVICE_NAME=lampac-nextgen' "$CASE/bin/lampac"; then pass 'usage documents custom service selection'; else fail 'usage lacks custom service documentation'; fi

# ─── 8. interactive menu is available but refuses non-interactive use ────────

new_case
mk_unit lampac.service "$CASE/roots/app" lampac "/usr/share/dotnet/dotnet $CASE/roots/app/Core.dll" active
STDIN_DATA='' run_w setup
if (( RC != 0 )); then pass 'setup menu fails closed when stdin is not a TTY'; else fail 'setup menu ran non-interactively'; fi
assert_contains "$(cat "$CASE/stderr")" 'non-interactive' 'setup explains direct commands for non-interactive use'

STDIN_DATA='' run_w 'bogus-command'
if (( RC != 0 )); then pass 'unknown command exits non-zero'; else fail 'unknown command accepted'; fi
assert_contains "$(cat "$CASE/stderr")" 'LAMPAC_SERVICE_NAME' 'usage mentions LAMPAC_SERVICE_NAME'

case_summary