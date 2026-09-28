#!/usr/bin/env bash
# Runs the Core E2E suite locally the way .github/workflows/e2e-run.yml does.
# Usage: run-local.sh [all|smoke] [playwright file filter]. E2E_SKIP_BUILD=1 reuses the staged host.
set -euo pipefail

suite=${1:-all}
filter=${2:-}
root=$(cd "$(dirname "$0")/../.." && pwd)
loopback_port=${E2E_LOOPBACK_PORT:-5391}
public_port=${E2E_PUBLIC_PORT:-8392}

case "$(uname -s)-$(uname -m)" in
Linux-x86_64) rid=linux-x64 ;;
Linux-aarch64 | Linux-arm64) rid=linux-arm64 ;;
Darwin-arm64) rid=osx-arm64 ;;
Darwin-x86_64) rid=osx-x64 ;;
*)
	echo "error: no host runtime identifier for $(uname -s) $(uname -m)" >&2
	exit 1
	;;
esac

for port in "$loopback_port" "$public_port"; do
	if curl --silent --max-time 2 "http://127.0.0.1:$port/" >/dev/null 2>&1; then
		echo "error: port $port is already in use; set E2E_LOOPBACK_PORT / E2E_PUBLIC_PORT" >&2
		exit 1
	fi
done

if [[ "${E2E_SKIP_BUILD:-}" != "1" ]]; then
	(cd "$root/ui" && npm ci && npm run build && npm run build:web-client)
	"$root/ci/scripts/stage-host.sh" "$rid" "3.0.0-e2e.0" Production
fi

(cd "$root/e2e" && npm install --no-audit --no-fund && npx playwright install chromium)

# A short private TMPDIR: the host's single-instance lock lives there, so a dev host or an
# installed Macro Deck would otherwise stop this one from starting.
work=$(mktemp -d /tmp/macro-deck-e2e.XXXXXX)
mkdir -p "$work/data" "$work/tmp"
echo "E2E run directory (data and logs): $work"

export MACRO_DECK_HOST_BINARY="$root/ui/bootstrapper/host-publish/Macro Deck Host"
export MACRO_DECK_HOST_LOG="$work/macro-deck-host.log"
export MACRO_DECK_SUPERVISOR_LOG="$work/macro-deck-host-supervisor.log"
export MACRO_DECK_DATA_DIRECTORY="$work/data"
export MACRODECK_HOST_PORT="$loopback_port"
export MACRO_DECK_PORT="$public_port"
MACRODECK_LOOPBACK_SECRET=$(openssl rand -hex 32)
export MACRODECK_LOOPBACK_SECRET

TMPDIR="$work/tmp" bash "$root/e2e/scripts/host-supervisor.sh" &
supervisor_pid=$!
trap 'kill "$supervisor_pid" 2>/dev/null || true; wait "$supervisor_pid" 2>/dev/null || true' EXIT

for port in "$loopback_port" "$public_port"; do
	deadline=$((SECONDS + 60))
	until curl --fail --silent "http://127.0.0.1:$port/api/auth/status" >/dev/null; do
		if ((SECONDS > deadline)); then
			echo "error: host did not answer on port $port; see $MACRO_DECK_HOST_LOG" >&2
			exit 1
		fi
		sleep 1
	done
done

args=()
if [[ "$suite" == "smoke" ]]; then
	args+=(--grep '@smoke')
fi
if [[ -n "$filter" ]]; then
	args+=("$filter")
fi

cd "$root/e2e"
MACRO_DECK_LOOPBACK_URL="http://127.0.0.1:$loopback_port" \
	MACRO_DECK_PUBLIC_URL="http://127.0.0.1:$public_port" \
	SUITE="$suite" FILTER="$filter" \
	npx playwright test ${args[@]+"${args[@]}"}
