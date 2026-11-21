#!/usr/bin/env bash
# Installs the D2D Audit agent for the current user.
#   macOS: LaunchAgent (~/Library/LaunchAgents/com.d2daudit.agent.plist), every 6 hours and at login.
#   Linux: systemd user timer (d2d-agent.timer), every 6 hours and 5 minutes after login.
# Usage: ./install.sh [--server URL --token TOKEN | --from d2d-agent.json] [--binary /path/to/d2d-agent]
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PREFIX="$HOME/.local/share/d2daudit"
BINARY=""
ENROLL_ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --binary) BINARY="$2"; shift ;;
    --server|--token|--from) ENROLL_ARGS+=("$1" "$2"); shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done

mkdir -p "$PREFIX"
if [ -n "$BINARY" ]; then
  install -m 755 "$BINARY" "$PREFIX/d2d-agent"
  RUN=("$PREFIX/d2d-agent" run)
  ENROLL=("$PREFIX/d2d-agent" enroll)
else
  install -m 755 "$HERE/d2d-agent.sh" "$PREFIX/d2d-agent.sh"
  RUN=("$PREFIX/d2d-agent.sh")
  ENROLL=("$PREFIX/d2d-agent.sh" --enroll)
fi

if [ ${#ENROLL_ARGS[@]} -gt 0 ]; then
  "${ENROLL[@]}" "${ENROLL_ARGS[@]}"
fi

if [ "$(uname -s)" = "Darwin" ]; then
  PLIST="$HOME/Library/LaunchAgents/com.d2daudit.agent.plist"
  mkdir -p "$(dirname "$PLIST")" "$HOME/Library/Logs"
  awk -v home="$HOME" -v args="${RUN[*]}" '
    /__PROGRAM_ARGUMENTS__/ { n = split(args, a, " "); for (i = 1; i <= n; i++) print "    <string>" a[i] "</string>"; next }
    { gsub(/__HOME__/, home); print }
  ' "$HERE/com.d2daudit.agent.plist" > "$PLIST"
  launchctl unload "$PLIST" 2>/dev/null || true
  launchctl load -w "$PLIST"
  echo "Installed LaunchAgent $PLIST"
else
  UNIT_DIR="$HOME/.config/systemd/user"
  mkdir -p "$UNIT_DIR"
  sed "s#__EXEC__#${RUN[*]}#" "$HERE/d2d-agent.service" > "$UNIT_DIR/d2d-agent.service"
  cp "$HERE/d2d-agent.timer" "$UNIT_DIR/d2d-agent.timer"
  systemctl --user daemon-reload
  systemctl --user enable --now d2d-agent.timer
  echo "Installed systemd user timer d2d-agent.timer"
fi
