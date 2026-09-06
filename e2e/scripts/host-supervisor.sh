#!/usr/bin/env bash
set -euo pipefail

host_binary=${MACRO_DECK_HOST_BINARY:?MACRO_DECK_HOST_BINARY must point to the staged host executable}
host_dir=$(dirname "$host_binary")
host_name=$(basename "$host_binary")
host_log=${MACRO_DECK_HOST_LOG:-host.log}
supervisor_log=${MACRO_DECK_SUPERVISOR_LOG:-host-supervisor.log}
restart_exit_code=86
child_pid=''

# The host only offers restarting when it knows the shell executable that relaunches it,
# which the Tauri bootstrapper normally provides. This supervisor plays that role here, so
# it has to advertise the same variable or /api/host/restart reports "not supported".
export MACRODECK_SHELL_EXECUTABLE=${MACRODECK_SHELL_EXECUTABLE:-$host_binary}

log() {
  printf '[%s] %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*" | tee -a "$supervisor_log"
}

stop_child() {
  if [[ -n "$child_pid" ]] && kill -0 "$child_pid" 2>/dev/null; then
    log "Stopping host process $child_pid"
    kill "$child_pid" 2>/dev/null || true
    wait "$child_pid" 2>/dev/null || true
  fi
  child_pid=''
}

cleanup() {
  stop_child
  exit 0
}

trap cleanup INT TERM

while true; do
  log "Starting Macro Deck host"
  (
    cd "$host_dir"
    exec "./$host_name"
  ) >>"$host_log" 2>&1 &
  child_pid=$!

  set +e
  wait "$child_pid"
  exit_code=$?
  set -e
  child_pid=''

  if [[ "$exit_code" -eq "$restart_exit_code" ]]; then
    log "Host requested restart (exit code $restart_exit_code)"
    continue
  fi

  log "Host exited with code $exit_code"
  exit "$exit_code"
done
