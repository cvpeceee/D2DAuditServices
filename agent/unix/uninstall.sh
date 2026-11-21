#!/usr/bin/env bash
# Removes the D2D Audit agent schedule, files and device credential for the current user.
set -euo pipefail

if [ "$(uname -s)" = "Darwin" ]; then
  PLIST="$HOME/Library/LaunchAgents/com.d2daudit.agent.plist"
  launchctl unload "$PLIST" 2>/dev/null || true
  rm -f "$PLIST"
else
  systemctl --user disable --now d2d-agent.timer 2>/dev/null || true
  rm -f "$HOME/.config/systemd/user/d2d-agent.service" "$HOME/.config/systemd/user/d2d-agent.timer"
  systemctl --user daemon-reload 2>/dev/null || true
fi
rm -rf "$HOME/.local/share/d2daudit"
rm -f "${D2D_AGENT_CONFIG:-$HOME/.config/d2daudit/agent.json}"
echo "D2D Audit agent removed. Ask an admin to revoke the device in People > Computers."
