#!/usr/bin/env bash
# D2D Audit device agent for macOS and Linux (lightweight alternative to the d2d-agent binary).
# Collects disk encryption, screen lock, anti-malware, password manager, OS updates and firewall
# posture and sends a signed check-in. Same JSON schema and HMAC signing as the .NET agent.
#
#   d2d-agent.sh --dry-run
#   d2d-agent.sh --enroll --server https://app.example.com --token d2de_...
#   d2d-agent.sh                  # signed check-in using the saved config
set -euo pipefail

AGENT_VERSION="1.0.0"
MAX_LOCK_SECONDS=900
UPDATE_WINDOW_DAYS=45
CONFIG="${D2D_AGENT_CONFIG:-$HOME/.config/d2daudit/agent.json}"
MANAGERS=("1Password" "Bitwarden" "LastPass" "KeePassXC" "KeePass" "Dashlane" "Keeper" "NordPass" "Proton Pass")

DRY_RUN=0
ENROLL=0
SERVER=""
TOKEN=""
FROM=""
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --enroll|enroll) ENROLL=1 ;;
    run) ;;
    --server) SERVER="$2"; shift ;;
    --token) TOKEN="$2"; shift ;;
    --from) FROM="$2"; shift ;;
    --config) CONFIG="$2"; shift ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done

json_escape() {
  local s="${1//\\/\\\\}"
  s="${s//\"/\\\"}"
  s="${s//$'\n'/ }"
  s="${s//$'\r'/}"
  s="${s//$'\t'/ }"
  printf '%s' "$s"
}

CHECKS=()
check() { CHECKS+=("{\"key\":\"$1\",\"pass\":$2,\"detail\":\"$(json_escape "$3")\"}"); }
has() { command -v "$1" >/dev/null 2>&1; }

PLATFORM="linux"
[ "$(uname -s)" = "Darwin" ] && PLATFORM="macos"

days_since() {
  local now
  now=$(date +%s)
  echo $(( (now - $1) / 86400 ))
}

# ---------- macOS ----------
mac_checks() {
  local fv
  fv=$(fdesetup status 2>/dev/null || true)
  case "$fv" in
    *"FileVault is On"*) check diskEncryption true "FileVault is on" ;;
    *"Encryption in progress"*) check diskEncryption true "FileVault encryption in progress" ;;
    *) check diskEncryption false "FileVault is off" ;;
  esac

  local idle ask delay
  idle=$(defaults -currentHost read com.apple.screensaver idleTime 2>/dev/null || echo "")
  ask=$(defaults read com.apple.screensaver askForPassword 2>/dev/null || echo "")
  delay=$(defaults read com.apple.screensaver askForPasswordDelay 2>/dev/null || echo 0)
  if [ -z "$ask" ] && sysadminctl -screenLock status 2>&1 | grep -qi "immediate\|seconds"; then ask=1; fi
  if [ "${ask:-0}" != "1" ]; then
    check screenLock false "Screen saver does not require a password"
  elif [ -z "$idle" ] || [ "$idle" = "0" ]; then
    check screenLock false "Screen saver never starts"
  elif [ $(( ${idle%.*} + ${delay%.*} )) -le $MAX_LOCK_SECONDS ]; then
    check screenLock true "Locks after $(( ${idle%.*} + ${delay%.*} ))s"
  else
    check screenLock false "Locks after $(( ${idle%.*} + ${delay%.*} ))s, limit ${MAX_LOCK_SECONDS}s"
  fi

  local av=""
  for app in "CrowdStrike Falcon" "SentinelOne" "Sophos" "Malwarebytes" "Microsoft Defender"; do
    if ls /Applications 2>/dev/null | grep -qi "${app// /.*}"; then av="$app"; break; fi
  done
  if [ -n "$av" ]; then
    check antivirus true "$av installed"
  elif [ -d /Library/Apple/System/Library/CoreServices/XProtect.bundle ] || [ -d /System/Library/CoreServices/XProtect.bundle ]; then
    check antivirus true "XProtect present"
  else
    check antivirus false "No anti-malware found"
  fi

  local auto last
  auto=$(defaults read /Library/Preferences/com.apple.SoftwareUpdate AutomaticallyInstallMacOSUpdates 2>/dev/null || echo "")
  last=$(defaults read /Library/Preferences/com.apple.SoftwareUpdate LastFullSuccessfulDate 2>/dev/null || echo "")
  if [ "$auto" = "0" ]; then
    check osUpdates false "Automatic macOS updates are off"
  elif [ -n "$last" ]; then
    local epoch age
    epoch=$(date -j -f "%Y-%m-%d %H:%M:%S %z" "$last" +%s 2>/dev/null || echo 0)
    age=$(days_since "$epoch")
    if [ "$age" -le $UPDATE_WINDOW_DAYS ]; then check osUpdates true "Last update check $age days ago"
    else check osUpdates false "Last update check $age days ago, limit $UPDATE_WINDOW_DAYS"; fi
  else
    check osUpdates false "No software update history found"
  fi

  local fw
  fw=$(/usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate 2>/dev/null || true)
  case "$fw" in
    *enabled*) check firewall true "Application firewall enabled" ;;
    *) check firewall false "Application firewall disabled" ;;
  esac
}

# ---------- Linux ----------
linux_checks() {
  if lsblk -o TYPE 2>/dev/null | grep -q crypt || grep -qs "crypt" /proc/mounts || [ -s /etc/crypttab ]; then
    check diskEncryption true "LUKS encrypted volume present"
  else
    check diskEncryption false "No LUKS encrypted volume found"
  fi

  if has gsettings; then
    local enabled delay
    enabled=$(gsettings get org.gnome.desktop.screensaver lock-enabled 2>/dev/null || echo "")
    delay=$(gsettings get org.gnome.desktop.session idle-delay 2>/dev/null | awk '{print $NF}' || echo 0)
    if [ "$enabled" != "true" ]; then check screenLock false "Screen lock is disabled"
    elif [ "${delay:-0}" = "0" ]; then check screenLock false "Screen never blanks"
    elif [ "$delay" -le $MAX_LOCK_SECONDS ]; then check screenLock true "Locks after ${delay}s"
    else check screenLock false "Locks after ${delay}s, limit ${MAX_LOCK_SECONDS}s"; fi
  else
    check screenLock false "Screen lock settings unavailable (no gsettings)"
  fi

  local av=""
  for unit in falcon-sensor sentinelone mdatp clamav-daemon clamd@scan clamd; do
    if systemctl is-active --quiet "$unit" 2>/dev/null; then av="$unit"; break; fi
  done
  if [ -n "$av" ]; then check antivirus true "$av running"; else check antivirus false "No anti-malware service running"; fi

  if has apt-get; then
    local stamp=/var/lib/apt/periodic/update-success-stamp
    [ -f "$stamp" ] || stamp=/var/lib/apt/lists
    local pending
    pending=$(apt-get -s upgrade 2>/dev/null | grep -c '^Inst .*security' || true)
    local age
    age=$(days_since "$(stat -c %Y "$stamp" 2>/dev/null || echo 0)")
    if [ "$pending" -gt 0 ]; then check osUpdates false "$pending security updates pending"
    elif [ "$age" -le $UPDATE_WINDOW_DAYS ]; then check osUpdates true "No pending security updates; last refresh $age days ago"
    else check osUpdates false "Package lists last refreshed $age days ago"; fi
  elif has dnf; then
    local count
    count=$(dnf -q updateinfo list --security 2>/dev/null | grep -c . || true)
    if [ "$count" -gt 0 ]; then check osUpdates false "$count security updates pending"
    else check osUpdates true "No pending security updates"; fi
  else
    check osUpdates false "Package manager not supported"
  fi

  if has ufw && ufw status 2>/dev/null | grep -q "Status: active"; then check firewall true "ufw active"
  elif systemctl is-active --quiet firewalld 2>/dev/null; then check firewall true "firewalld active"
  elif has nft && [ "$(nft list ruleset 2>/dev/null | grep -c 'hook input')" -gt 0 ]; then check firewall true "nftables input chain present"
  else check firewall false "No active host firewall (ufw, firewalld or nftables)"; fi
}

password_manager() {
  local places=("/Applications" "$HOME/Applications" "/usr/share/applications" "$HOME/.local/share/applications" "/var/lib/flatpak/exports/share/applications" "/snap/bin" "/opt")
  for m in "${MANAGERS[@]}"; do
    for p in "${places[@]}"; do
      if [ -d "$p" ] && ls "$p" 2>/dev/null | grep -qi "${m// /}"; then check passwordManager true "$m"; return; fi
    done
    if has "$(echo "$m" | tr '[:upper:]' '[:lower:]' | tr -d ' ')"; then check passwordManager true "$m"; return; fi
  done
  check passwordManager false "No approved password manager (1Password, Bitwarden, LastPass, KeePass) found"
}

serial_number() {
  local s=""
  if [ "$PLATFORM" = "macos" ]; then
    s=$(ioreg -c IOPlatformExpertDevice -d 2 2>/dev/null | awk -F\" '/IOPlatformSerialNumber/{print $4}')
  else
    s=$(cat /sys/class/dmi/id/product_serial 2>/dev/null || true)
    [ -z "$s" ] && s=$(cat /etc/machine-id 2>/dev/null | cut -c1-16)
  fi
  case "$s" in ""|"To Be Filled By O.E.M."|"Default string"|"System Serial Number") s=$(hostname) ;; esac
  printf '%s' "$s" | tr -d '[:space:]'
}

os_name() {
  if [ "$PLATFORM" = "macos" ]; then echo "macOS $(sw_vers -productVersion 2>/dev/null)"
  elif [ -r /etc/os-release ]; then (. /etc/os-release; echo "${PRETTY_NAME:-Linux}")
  else uname -sr; fi
}

payload() {
  CHECKS=()
  if [ "$PLATFORM" = "macos" ]; then mac_checks; else linux_checks; fi
  password_manager
  # Keep the documented order: diskEncryption, screenLock, antivirus, passwordManager, osUpdates, firewall.
  local ordered=() k c
  for k in diskEncryption screenLock antivirus passwordManager osUpdates firewall; do
    for c in "${CHECKS[@]}"; do case "$c" in *"\"key\":\"$k\""*) ordered+=("$c") ;; esac; done
  done
  local joined
  joined=$(IFS=,; echo "${ordered[*]}")
  printf '{"schemaVersion":1,"agentVersion":"%s","agentKind":"bash","serial":"%s","hostname":"%s","operatingSystem":"%s","platform":"%s","collectedAt":"%s","checks":[%s]}' \
    "$AGENT_VERSION" "$(json_escape "$(serial_number)")" "$(json_escape "$(hostname)")" "$(json_escape "$(os_name)")" "$PLATFORM" \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$joined"
}

json_field() { sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" | head -n1; }

sign() {
  # key = lowercase hex sha256 of the secret; signature = v1=hex(HMAC-SHA256(key, "<ts>.<body>"))
  local key
  key=$(printf '%s' "$1" | openssl dgst -sha256 -r | awk '{print $1}')
  printf 'v1=%s' "$(printf '%s.%s' "$2" "$3" | openssl dgst -sha256 -hmac "$key" -r | awk '{print $1}')"
}

if [ "$DRY_RUN" = "1" ]; then
  payload
  echo
  exit 0
fi

for tool in curl openssl; do has "$tool" || { echo "$tool is required" >&2; exit 1; }; done

if [ "$ENROLL" = "1" ]; then
  if [ -n "$FROM" ]; then
    [ -z "$SERVER" ] && SERVER=$(json_field server < "$FROM")
    [ -z "$TOKEN" ] && TOKEN=$(json_field token < "$FROM")
  fi
  [ -n "$SERVER" ] && [ -n "$TOKEN" ] || { echo "Enroll needs --server and --token (or --from d2d-agent.json)" >&2; exit 2; }
  SERVER="${SERVER%/}"
  case "$SERVER" in
    https://*|http://localhost*|http://127.0.0.1*) ;;
    *) echo "The server must be https:// (http only for localhost)" >&2; exit 2 ;;
  esac
  request=$(printf '{"token":"%s","serial":"%s","hostname":"%s","operatingSystem":"%s","platform":"%s","agentKind":"bash","agentVersion":"%s"}' \
    "$(json_escape "$TOKEN")" "$(json_escape "$(serial_number)")" "$(json_escape "$(hostname)")" "$(json_escape "$(os_name)")" "$PLATFORM" "$AGENT_VERSION")
  result=$(curl -fsS -X POST "$SERVER/api/v1/agent/enroll" -H 'Content-Type: application/json' -H 'X-D2D-Request: 1' --data "$request")
  mkdir -p "$(dirname "$CONFIG")"
  umask 077
  printf '{"server":"%s","deviceId":"%s","secret":"%s","checkInPath":"%s"}\n' "$SERVER" \
    "$(echo "$result" | json_field deviceId)" "$(echo "$result" | json_field secret)" "$(echo "$result" | json_field checkInPath)" > "$CONFIG"
  chmod 600 "$CONFIG"
  echo "Enrolled for $(echo "$result" | json_field personEmail). Config saved to $CONFIG."
fi

[ -f "$CONFIG" ] || { echo "This device is not enrolled. Run: d2d-agent.sh --enroll --server <url> --token <token>" >&2; exit 1; }
server=$(json_field server < "$CONFIG")
device=$(json_field deviceId < "$CONFIG")
secret=$(json_field secret < "$CONFIG")
path=$(json_field checkInPath < "$CONFIG")
[ -z "$path" ] && path="/api/v1/agent/check-in"
body=$(payload)
ts=$(date +%s)
curl -fsS -X POST "$server$path" -H 'Content-Type: application/json' -H 'X-D2D-Request: 1' \
  -H "X-D2D-Device: $device" -H "X-D2D-Timestamp: $ts" -H "X-D2D-Signature: $(sign "$secret" "$ts" "$body")" --data "$body"
echo
