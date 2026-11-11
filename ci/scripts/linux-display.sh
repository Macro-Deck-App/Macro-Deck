#!/usr/bin/env bash
set -euo pipefail

usage='usage: linux-display.sh start x11|wayland | stop'

state_dir="${LINUX_DISPLAY_STATE_DIR:-${RUNNER_TEMP:-/tmp}/linux-display}"
x11_display="${LINUX_DISPLAY_X11:-:99}"
width="${LINUX_DISPLAY_WIDTH:-1920}"
height="${LINUX_DISPLAY_HEIGHT:-1080}"

fail() {
  echo "linux-display: $*" >&2
  for log in "$state_dir"/*.log; do
    [ -f "$log" ] || continue
    echo "--- $log" >&2
    tail -n 40 "$log" >&2
  done
  exit 1
}

wait_for() {
  local description="$1"
  shift
  for _ in $(seq 1 100); do
    if "$@" >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.1
  done
  fail "timed out waiting for $description"
}

has_window_manager() {
  DISPLAY="$1" xprop -root _NET_SUPPORTING_WM_CHECK | grep -q 'window id'
}

publish() {
  echo "$1=$2"
  if [ -n "${GITHUB_ENV:-}" ]; then
    echo "$1=$2" >> "$GITHUB_ENV"
  fi
}

start_x11() {
  Xvfb "$x11_display" -screen 0 "${width}x${height}x24" -nolisten tcp >"$state_dir/xvfb.log" 2>&1 &
  echo $! >"$state_dir/xvfb.pid"
  wait_for "Xvfb on $x11_display" xdpyinfo -display "$x11_display"

  DISPLAY="$x11_display" openbox --sm-disable >"$state_dir/openbox.log" 2>&1 &
  echo $! >"$state_dir/openbox.pid"
  wait_for "openbox on $x11_display" has_window_manager "$x11_display"

  publish DISPLAY "$x11_display"
}

start_wayland() {
  start_x11 >/dev/null
  local runtime_dir="$state_dir/xdg-runtime"
  mkdir -p -m 700 "$runtime_dir"
  if [ ! -d /tmp/.X11-unix ]; then
    mkdir -p /tmp/.X11-unix
    chmod 1777 /tmp/.X11-unix
  fi

  # Nested in Xvfb rather than headless: only the x11 backend gives weston a seat, without which XWayland
  # windows never keep the focus. The GL renderer needs a GPU, pixman renders in software.
  DISPLAY="$x11_display" XDG_RUNTIME_DIR="$runtime_dir" weston --backend=x11 --renderer=pixman --xwayland \
    --socket=wayland-macrodeck --width="$width" --height="$height" \
    --log="$state_dir/weston.log" >/dev/null 2>&1 &
  echo $! >"$state_dir/weston.pid"
  wait_for "the weston socket" test -S "$runtime_dir/wayland-macrodeck"
  wait_for "XWayland" grep -q 'xserver listening on display' "$state_dir/weston.log"

  local xwayland_display
  xwayland_display=$(grep -o 'xserver listening on display :[0-9]*' "$state_dir/weston.log" | tail -n 1 | awk '{print $NF}')
  wait_for "XWayland on $xwayland_display" xdpyinfo -display "$xwayland_display"

  publish XDG_RUNTIME_DIR "$runtime_dir"
  publish WAYLAND_DISPLAY wayland-macrodeck
  publish XDG_SESSION_TYPE wayland
  publish LINUX_DISPLAY_X11 "$x11_display"
  publish DISPLAY "$xwayland_display"
}

stop_all() {
  for name in openbox xvfb weston; do
    local pid_file="$state_dir/$name.pid"
    if [ -f "$pid_file" ]; then
      kill "$(cat "$pid_file")" 2>/dev/null || true
      rm -f "$pid_file"
    fi
  done
}

case "${1:-}" in
  start)
    mkdir -p "$state_dir"
    case "${2:-x11}" in
      x11) start_x11 ;;
      wayland) start_wayland ;;
      *) fail "unknown mode '${2}', expected x11 or wayland" ;;
    esac
    ;;
  stop)
    stop_all
    ;;
  *)
    echo "$usage" >&2
    exit 2
    ;;
esac
