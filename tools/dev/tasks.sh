#!/usr/bin/env bash
# The steps behind the root Makefile, one function per target. Usage: tasks.sh <task> [args].
# The CI suites mirror .github/workflows/ci.yml; keep them in step when a workflow changes.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"

DOTNET=${DOTNET:-dotnet}
NPM=${NPM:-npm}
CARGO=${CARGO:-cargo}
CONFIGURATION=${CONFIGURATION:-Release}
BUILD_CHANNEL=${BUILD_CHANNEL:-Production}
TEST_RESULTS=${TEST_RESULTS:-TestResults}
dotnet_flags=(-c "$CONFIGURATION" "-p:BuildChannel=$BUILD_CHANNEL")

host_project=host/src/MacroDeckHost
web_client_dist="$root/ui/web-client/dist"
conformance_project=sdk/tests/MacroDeck.Plugin.Testing.Tests.ConformanceTests/MacroDeck.Plugin.Testing.Tests.ConformanceTests.csproj

# The dev host runs from the repository root so its .data directory is the one ng serve and the
# bootstrapper read the loopback secret from.
host_run="$DOTNET run --project $host_project"
host_run_with_web_client="ASPNETCORE_WEBROOT=\"$web_client_dist\" $host_run"
desktop_ui_run="cd ui/angular && $NPM start"

step() {
	printf '\n\033[1;36m==> %s\033[0m\n' "$*"
}

ensure_ui_deps() {
	if [[ ! ui/node_modules/.package-lock.json -nt ui/package-lock.json ]]; then
		step "npm ci (ui)"
		(cd ui && "$NPM" ci)
		touch ui/node_modules/.package-lock.json
	fi
}

kill_tree() {
	local child
	for child in $(pgrep -P "$1" 2>/dev/null); do
		kill_tree "$child"
	done
	kill -TERM "$1" 2>/dev/null || true
}

# Runs name=command pairs at once with prefixed output and stops all of them when one exits.
pids=()
names=()
run_parallel() {
	local spec name command index
	# A Homebrew DYLD_LIBRARY_PATH makes the bootstrapper load the wrong libpng and crash.
	while IFS= read -r variable; do
		unset "$variable"
	done < <(compgen -e | grep '^DYLD_' || true)
	export FORCE_COLOR=1 DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1

	stop_all() {
		trap - INT TERM EXIT
		local pid
		for pid in "${pids[@]}"; do
			kill_tree "$pid"
		done
		wait 2>/dev/null || true
	}
	trap stop_all INT TERM EXIT

	for spec in "$@"; do
		name=${spec%%=*}
		command=${spec#*=}
		(bash -c "$command" </dev/null 2>&1 | awk -v prefix="[$name] " '{ print prefix $0; fflush() }') 2>/dev/null &
		pids+=("$!")
		names+=("$name")
	done

	while true; do
		for index in "${!pids[@]}"; do
			if ! kill -0 "${pids[$index]}" 2>/dev/null; then
				echo "[tasks] ${names[$index]} exited, stopping the others" >&2
				exit 0
			fi
		done
		sleep 1
	done
}

# Setup

task_install() {
	ensure_ui_deps
}

# Development

task_dev() {
	task_web_client
	echo "Desktop UI: http://localhost:4200   Web client: http://localhost:7193   (make web-client rebuilds it)"
	run_parallel "host=$host_run_with_web_client" "desktop-ui=$desktop_ui_run"
}

task_dev_ui() {
	ensure_ui_deps
	run_parallel "host=$host_run" "desktop-ui=$desktop_ui_run"
}

task_dev_app() {
	ensure_ui_deps
	run_parallel "host=$host_run" "app=cd ui && $NPM run dev"
}

task_host() {
	eval "$host_run"
}

task_host_web_client() {
	task_web_client
	eval "$host_run_with_web_client"
}

task_desktop_ui() {
	ensure_ui_deps
	eval "$desktop_ui_run"
}

task_web_client() {
	ensure_ui_deps
	step "Build runtime and web client"
	(cd ui && "$NPM" run build:web-client)
}

task_dev_reset() {
	step "Delete .data"
	rm -rf .data
}

# CI suites

task_build_host() {
	step "dotnet build (warnings as errors)"
	"$DOTNET" build MacroDeck.slnx "${dotnet_flags[@]}" -warnaserror
}

task_test_host() {
	task_build_host
	step "Host tests"
	"$DOTNET" test MacroDeck.slnx "${dotnet_flags[@]}" --no-build \
		--filter 'FullyQualifiedName!~MacroDeck.Plugin.Testing.Tests.ConformanceTests' \
		--logger trx --results-directory "$TEST_RESULTS"
}

task_test_conformance() {
	step "SDK conformance tests"
	"$DOTNET" test "$conformance_project" "${dotnet_flags[@]}" --logger trx --results-directory "$TEST_RESULTS"
}

task_test_platform() {
	local project
	case "$(uname -s)" in
	Darwin) project=host/tests/MacroDeckHost.Tests.UnitTests.MacOS ;;
	MINGW* | MSYS*) project=host/tests/MacroDeckHost.Tests.UnitTests.Windows ;;
	*)
		echo "No separate platform test project for $(uname -s); test-host already covers it."
		return 0
		;;
	esac
	step "Platform tests ($project)"
	"$DOTNET" test "$project" "${dotnet_flags[@]}" --logger trx --results-directory "$TEST_RESULTS"
}

task_test_packages() {
	local packages="$root/.cache/packages" version
	task_build_host
	step "dotnet pack"
	rm -rf "$packages"
	"$DOTNET" pack MacroDeck.slnx "${dotnet_flags[@]}" --no-build -o "$packages"
	version=$("$DOTNET" msbuild sdk/src/MacroDeck.Sdk/MacroDeck.Sdk.csproj -getProperty:PackageVersion \
		"-p:BuildChannel=$BUILD_CHANNEL" | tr -d '[:space:]')
	step "Verify packages ($version)"
	node ci/scripts/verify-nuget-packages.mjs "$packages" "$version"
}

task_test_ui_scripts() {
	ensure_ui_deps
	step "Icon checks and script tests"
	node ci/scripts/verify-icon-names.mjs
	node ci/scripts/verify-app-icons.mjs
	node --test ci/scripts/*.test.mjs
	node --test ui/bootstrapper/src/external-links.test.mjs ui/bootstrapper/src/update-window.test.mjs
}

task_test_runtime() {
	ensure_ui_deps
	step "Runtime build, ES5 gate and tests"
	(cd ui/runtime && "$NPM" run build && "$NPM" run check:es5 && "$NPM" test)
}

task_test_web_client() {
	ensure_ui_deps
	step "Web client build, tests and compatibility gate"
	(cd ui/runtime && "$NPM" run build)
	(cd ui/web-client && "$NPM" run build && "$NPM" test && "$NPM" run check:es5 && "$NPM" run test:legacy)
}

task_test_desktop_ui() {
	ensure_ui_deps
	step "desktop-ui specs and layering checks"
	(cd ui/runtime && "$NPM" run build)
	(cd ui/angular &&
		npx ng test desktop-ui --watch=false --browsers=ChromeHeadless &&
		"$NPM" run test:proxy &&
		"$NPM" run test:dialog-box &&
		"$NPM" run test:boundaries &&
		"$NPM" run test:stacking)
}

task_build_ui() {
	ensure_ui_deps
	step "Production UI builds and device targets"
	(cd ui/angular && "$NPM" run build:prod)
	(cd ui/web-client && "$NPM" run build && "$NPM" run build:carthing)
	node --test ci/scripts/verify-web-client-targets.test.mjs
	node ci/scripts/verify-worker-manifest.mjs ui/web-client/dist ui/web-client/dist-carthing
}

task_test_ui() {
	task_test_ui_scripts
	task_test_runtime
	task_test_web_client
	task_test_desktop_ui
	task_build_ui
}

task_test_notices() {
	step "Third-party notices"
	(cd ui && "$NPM" ci --ignore-scripts)
	"$DOTNET" restore "$host_project/MacroDeckHost.csproj" '-p:RuntimeIdentifiers="win-x64;linux-x64;osx-arm64"'
	"$DOTNET" run --project tools/src/MacroDeck.LicenseTool -c Release
	git diff --exit-code NOTICE THIRD-PARTY-NOTICES
}

task_test_bootstrapper() {
	step "Bootstrapper fmt, clippy and tests"
	(cd ui/bootstrapper &&
		"$CARGO" fmt --check &&
		MACRODECK_BUILD_CHANNEL=$BUILD_CHANNEL "$CARGO" clippy --all-targets -- -D warnings &&
		MACRODECK_BUILD_CHANNEL=$BUILD_CHANNEL "$CARGO" test)
}

task_test_e2e() {
	step "Core E2E (${E2E_SUITE:-all})"
	./e2e/scripts/run-local.sh "${E2E_SUITE:-all}" "${E2E_FILTER:-}"
}

task_test_e2e_smoke() {
	E2E_SUITE=smoke task_test_e2e
}

task_test() {
	task_test_host
	task_test_conformance
	task_test_platform
	task_test_ui
	task_test_bootstrapper
}

task_ci() {
	task_test
	task_test_packages
	task_test_notices
	task_test_e2e
}

# Docs

task_docs() {
	if [[ ! docs/node_modules/.package-lock.json -nt docs/package-lock.json ]]; then
		(cd docs && "$NPM" ci)
		touch docs/node_modules/.package-lock.json
	fi
	step "Build docs"
	(cd docs && "$NPM" run build)
}

task_help() {
	awk 'BEGIN { FS = ":.*## " }
		/^##@/ { printf "\n\033[1m%s\033[0m\n", substr($0, 5) }
		/^[a-zA-Z0-9_-]+:.*## / { printf "  \033[36m%-18s\033[0m %s\n", $1, $2 }' Makefile
	echo
}

task=${1:-help}
shift || true
function_name="task_${task//-/_}"
if ! declare -F "$function_name" >/dev/null; then
	echo "Unknown task: $task" >&2
	task_help
	exit 2
fi
"$function_name" "$@"
