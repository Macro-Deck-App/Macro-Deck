#!/usr/bin/env bash
# Runs the Core E2E suite locally the way .github/workflows/e2e-run.yml does.
# Usage: run-local.sh [all|smoke] [playwright file filter]. E2E_SKIP_BUILD=1 reuses the staged host.
set -euo pipefail

suite=${1:-all}
filter=${2:-}
root=$(cd "$(dirname "$0")/../.." && pwd)

case "$(uname -s)-$(uname -m)" in
Linux-x86_64) rid=linux-x64 ;;
Linux-aarch64 | Linux-arm64) rid=linux-arm64 ;;
Darwin-arm64) rid=osx-arm64 ;;
Darwin-x86_64) rid=osx-x64 ;;
MINGW*-x86_64 | MSYS*-x86_64) rid=win-x64 ;;
*)
	echo "error: no host runtime identifier for $(uname -s) $(uname -m)" >&2
	exit 1
	;;
esac

if [[ "${E2E_SKIP_BUILD:-}" != "1" ]]; then
	(cd "$root/ui" && npm ci && npm run build && npm run build:web-client)
	"$root/ci/scripts/stage-host.sh" "$rid" "3.0.0-e2e.0" Production
fi

(cd "$root/e2e" && npm install --no-audit --no-fund && npx playwright install chromium)

# Every spec file starts its own host with its own data directory, ports and temp directory under
# this run directory (e2e/tests/support/host.js), so nothing here collides with a running Macro Deck.
work=$(mktemp -d "${TMPDIR:-/tmp}/macro-deck-e2e.XXXXXX")
echo "E2E run directory (host data and logs): $work"

args=()
if [[ "$suite" == "smoke" ]]; then
	args+=(--grep '@smoke')
fi
if [[ -n "$filter" ]]; then
	args+=("$filter")
fi

cd "$root/e2e"
E2E_RUN_DIR="$work" npx playwright test ${args[@]+"${args[@]}"}
