#!/usr/bin/env bash
# Drive Space Engineers for a live test without touching the desktop session: the game runs in a
# headless gamescope, input is XTEST on gamescope's own XWayland display, and frames are read back
# with XGetImage. See README.md for the commands and what each one waits for.
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
state="$repo/out/live-game"
xse="$here/bin/xse"
app=244850
mkdir -p "$state"

ini_value() { sed -n "s/^$1=//p" "$repo/GrappleHook/mdk.local.ini" 2>/dev/null | tr -d '\r' | head -1; }

# Paths: the game and the Proton prefix come from mdk.local.ini, which deploys the mod already.
bin64="${LIVE_BIN64:-$(ini_value binarypath)}"
mods="${LIVE_MODS:-$(ini_value output)}"
game="$(dirname "$bin64")"
roaming="$(dirname "$mods")"
compat="${mods%%/pfx/*}"
library="${compat%/compatdata/*}"
steam="${LIVE_STEAM:-$HOME/.local/share/Steam}"

proton_dir() {
  [ -n "${LIVE_PROTON:-}" ] && { echo "$LIVE_PROTON"; return; }
  # Only the tool Steam maps to this app. Never guess: another Proton rewrites the prefix.
  local name
  # The game's own mapping, else the global default ("0"), which is what Steam itself falls back to.
  local key
  for key in "$app" 0; do
    name=$(awk -v app="\"$key\"" '/"CompatToolMapping"/{m=1} m&&$1==app{f=1} f&&$1=="\"name\""{gsub(/"/,"",$2); print $2; exit}' \
      "$steam/config/config.vdf" 2>/dev/null)
    [ -n "$name" ] && break
  done
  [ -n "$name" ] || return
  for d in "$steam/compatibilitytools.d/$name" "$steam/steamapps/common/$name" "$library/common/$name"; do
    [ -x "$d/proton" ] && { echo "$d"; return; }
  done
}

# The prefix records the Proton that last ran it; a different one upgrades or downgrades it.
prefix_matches() {
  local want have
  want=$(sed -n 's/^CURRENT_PREFIX_VERSION="\(.*\)"/\1/p' "$1/proton")
  have=$(head -1 "$compat/version" 2>/dev/null)
  [ -z "$have" ] || [ "$want" = "$have" ] || [ "${LIVE_ALLOW_PREFIX_CHANGE:-}" = 1 ] && return 0
  echo "$want would rewrite the prefix, which $have last ran; set LIVE_PROTON, or LIVE_ALLOW_PREFIX_CHANGE=1 to accept" >&2
  return 1
}

runtime_dir() {
  [ -n "${LIVE_RUNTIME:-}" ] && { echo "$LIVE_RUNTIME"; return; }
  for d in SteamLinuxRuntime_4 SteamLinuxRuntime_sniper; do
    [ -x "$steam/steamapps/common/$d/_v2-entry-point" ] && { echo "$steam/steamapps/common/$d"; return; }
  done
}

display() { cat "$state/display" 2>/dev/null; }
window() { DISPLAY=$(display) "$xse" find "Space Engineers" | head -1; }
newest_log() { ls -t "$roaming"/SpaceEngineers_*.log 2>/dev/null | head -1; }
# Through Pulsar the game is ../Pulsar/Legacy.exe; bare, Proton shows it on a drive letter (S:\...).
game_pid() { pgrep -f '^(\.\./Pulsar/Legacy\.exe|/[^ ]*/Bin64/SpaceEngineers\.exe|[A-Z]:\\.*\\Bin64\\SpaceEngineers\.exe) ' | head -1; }
gamescope_running() { pgrep -f '^gamescope ' >/dev/null; }

build() {
  mkdir -p "$here/bin"
  [ "$xse" -nt "$here/xse.c" ] || gcc -O2 -o "$xse" "$here/xse.c" -lXtst -lX11
}

start() {
  build
  gamescope_running && { echo "gamescope is already running; live.sh stop first" >&2; return 1; }
  pgrep -f "AppId=$app" >/dev/null && { echo "the game is running outside this harness; close it first" >&2; return 1; }
  local proton runtime launcher
  proton=$(proton_dir); runtime=$(runtime_dir)
  [ -x "$proton/proton" ] && [ -n "$runtime" ] || { echo "no Proton mapped for $app ($proton) or no Steam Linux Runtime ($runtime)" >&2; return 1; }
  prefix_matches "$proton" || return 1
  # Steam's own launch goes through Pulsar when it is installed; match it so plugins load the same.
  # LIVE_PULSAR=0 starts the game bare: no plugins, a quicker boot (lt's default).
  if [ "${LIVE_PULSAR:-1}" != 0 ] && [ -f "$game/Pulsar/Legacy.exe" ]; then launcher=(../Pulsar/Legacy.exe "$bin64/SpaceEngineers.exe" -useallavailablecores)
  else launcher=("$bin64/SpaceEngineers.exe" -useallavailablecores); fi
  # Silence: a null sink, removed again by stop.
  if ! pactl list short sinks 2>/dev/null | grep -q live_game_null; then
    pactl load-module module-null-sink sink_name=live_game_null sink_properties=device.description=LiveGameNull > "$state/null-sink" 2>/dev/null || true
  fi
  rm -f "$state/display"
  newest_log > "$state/log-before"
  local width="${LIVE_WIDTH:-2560}" height="${LIVE_HEIGHT:-1440}"
  (
    cd "$bin64" || exit 1
    # Pulsar tells Proton from Windows by STEAM_COMPAT_PROTON and exits on the Windows path without it.
    export SteamAppId=$app SteamGameId=$app SteamOverlayGameId=$app STEAM_COMPAT_APP_ID=$app SteamClientLaunch=1 SteamEnv=1 STEAM_COMPAT_PROTON=1
    export STEAM_COMPAT_CLIENT_INSTALL_PATH="$steam" STEAM_COMPAT_DATA_PATH="$compat" STEAM_COMPAT_INSTALL_PATH="$game"
    export STEAM_COMPAT_LIBRARY_PATHS="$steam/steamapps:$library" STEAM_COMPAT_TOOL_PATHS="$proton:$runtime"
    export STEAM_COMPAT_MOUNTS="$steam/steamapps/common/Steamworks Shared:$runtime"
    export STEAM_COMPAT_SHADER_PATH="$library/shadercache/$app" STEAM_COMPAT_FLAGS=search-cwd
    export DISABLE_MANGOHUD=1 ENABLE_GAMESCOPE_WSI=0 PULSE_SINK=live_game_null
    exec setsid gamescope --backend headless -W "$width" -H "$height" -w "$width" -h "$height" -r 60 --force-windows-fullscreen -- \
      env -u WAYLAND_DISPLAY sh -c 'echo "$DISPLAY" > "$0"; exec "$@"' "$state/display" \
      "$runtime/_v2-entry-point" --verb=waitforexitandrun -- "$proton/proton" waitforexitandrun "${launcher[@]}"
  ) > "$state/gamescope.log" 2>&1 &
  for _ in $(seq 1 30); do [ -s "$state/display" ] && break; sleep 1; done
  [ -s "$state/display" ] || { echo "gamescope did not start; see $state/gamescope.log" >&2; return 1; }
  echo "gamescope display $(display)"
}

stop() {
  local pid; pid=$(game_pid)
  [ -n "$pid" ] && kill "$pid"
  for _ in $(seq 1 40); do gamescope_running || break; sleep 1; done
  gamescope_running && pkill -f '^gamescope '
  for _ in $(seq 1 20); do pgrep -f "$compat" >/dev/null || break; sleep 1; done
  [ -s "$state/null-sink" ] && pactl unload-module "$(cat "$state/null-sink")" 2>/dev/null
  rm -f "$state/null-sink" "$state/display"
}

# Main menu -> "Continue Game", which loads whichever world was played last.
continue_last() {
  local before log
  before="${1:-$(cat "$state/log-before" 2>/dev/null)}"
  for _ in $(seq 1 180); do
    log=$(newest_log)
    [ "$log" != "$before" ] && grep -q "MyConfig.Save() - END" "$log" 2>/dev/null && break
    sleep 1
  done
  sleep 6
  read -r id _ _ w h _ <<< "$(window)"
  [ -n "${id:-}" ] || { echo "no game window on $(display)" >&2; return 1; }
  DISPLAY=$(display) "$xse" click $((w / 10)) $((h * ${LIVE_CONTINUE_Y:-5139} / 10000))
  for _ in $(seq 1 180); do grep -q "MyGuiScreenGamePlay.LoadContent - END" "$log" && break; sleep 1; done
  grep -q "MyGuiScreenGamePlay.LoadContent - END" "$log" || { echo "world did not load; see $log" >&2; return 1; }
  sleep "${LIVE_SETTLE:-8}"
  echo "session loaded: $log"
  grep -n "Compilation of.*failed\|error CS[0-9]" "$log" | head -5
}

deploy() {
  (cd "$repo" && \
    dotnet build GrappleHook/GrappleHook.csproj --no-restore -m:1 -nr:false -v quiet)
}

snap() {
  local name="$1" scale="${2:-0.5}" id
  read -r id _ <<< "$(window)"
  DISPLAY=$(display) "$xse" shot "$id" "$state/$name.ppm" || return 1
  python3 -I -c 'import sys
from PIL import Image
im=Image.open(sys.argv[1]); s=float(sys.argv[3])
im.resize((max(1,int(im.width*s)),max(1,int(im.height*s)))).save(sys.argv[2])' "$state/$name.ppm" "$state/$name.png" "$scale"
  rm -f "$state/$name.ppm"
  echo "$state/$name.png"
}

input() { DISPLAY=$(display) "$xse" "$@"; }

case "${1:-}" in
  build) build ;;
  start) start ;;
  stop) stop ;;
  continue) continue_last ;;
  deploy) deploy ;;
  cycle)
    stop
    [ "${2:-}" = "--no-deploy" ] || deploy || exit 1
    start && continue_last ;;
  key) input key "$2" "${3:-60}" ;;
  say) input key Return; sleep .6; input type "$2"; sleep .3; input key Return; sleep 1 ;;
  look) input rel "$2" "$3" "${4:-30}" ;;
  hold) input down "$2"; sleep "$3"; input up "$2" ;;
  click) input click "$2" "$3" "${4:-1}" ;;   # button: 1 left, 3 right
  snap) snap "$2" "${3:-0.5}" ;;
  log) newest_log ;;
  pid) game_pid ;;
  *) echo "usage: live.sh build|start|stop|continue|deploy|cycle [--no-deploy]|key <chord>|say <text>|look <dx> <dy>|hold <key> <s>|click <x> <y> [button]|snap <name> [scale]|log" >&2; exit 2 ;;
esac
