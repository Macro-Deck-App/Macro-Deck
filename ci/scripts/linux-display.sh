#!/usr/bin/env bash
set -euo pipefail

usage='usage: linux-display.sh start [x11] | stop'

state_dir="${LINUX_DISPLAY_STATE_DIR:-${RUNNER_TEMP:-/tmp}/linux-display}"
x11_display="${LINUX_DISPLAY_X11:-:99}"

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

start_x11() {
  Xvfb "$x11_display" -screen 0 1920x1080x24 -nolisten tcp >"$state_dir/xvfb.log" 2>&1 &
  echo $! >"$state_dir/xvfb.pid"
  wait_for "Xvfb on $x11_display" xdpyinfo -display "$x11_display"

  DISPLAY="$x11_display" openbox --sm-disable >"$state_dir/openbox.log" 2>&1 &
  echo $! >"$state_dir/openbox.pid"
  wait_for "openbox on $x11_display" has_window_manager "$x11_display"

  echo "DISPLAY=$x11_display"
  if [ -n "${GITHUB_ENV:-}" ]; then
    echo "DISPLAY=$x11_display" >> "$GITHUB_ENV"
  fi
}

stop_all() {
  for name in openbox xvfb; do
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
      *) fail "unknown mode '${2}', only x11 is available" ;;
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
