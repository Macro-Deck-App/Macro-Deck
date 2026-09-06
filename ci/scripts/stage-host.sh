#!/usr/bin/env bash
# Stages the .NET host publish output into ui/bootstrapper/host-publish so the
# Tauri bundler can pack it as bundle resources. Mirrors what the
# CI pipeline does. Usage: ci/scripts/stage-host.sh <rid> [version] [build-channel]
set -euo pipefail

rid=${1:?usage: stage-host.sh <rid> [version] [build-channel]}
version=${2:-3.0.0-dev.0}
build_channel=${3:-Development}

root=$(cd "$(dirname "$0")/../.." && pwd)
publish_dir="$root/ui/bootstrapper/host-publish"

rm -rf "$publish_dir"
dotnet publish "$root/host/src/MacroDeckHost" \
	-c Release \
	-r "$rid" \
	--self-contained true \
	-p:Version="$version" \
	-p:BuildChannel="$build_channel" \
	-p:DebugType=None \
	-p:DebugSymbols=false \
	-o "$publish_dir"

# wwwroot layout: web-client (public deck) at /, desktop-ui (configuration UI) at /admin, and one
# device-specific web-client build per target under /targets/<id> (issue #727).
desktop_ui_dist="$root/ui/angular/dist/desktop-ui/browser"
web_client_dist="$root/ui/web-client/dist"
carthing_dist="$root/ui/web-client/dist-carthing"
rm -rf "$publish_dir/wwwroot"
if [[ -d "$web_client_dist" ]]; then
	cp -R "$web_client_dist" "$publish_dir/wwwroot"
else
	echo "warning: web-client dist not found ($web_client_dist), web client will be missing from wwwroot" >&2
	mkdir -p "$publish_dir/wwwroot"
fi
if [[ -d "$desktop_ui_dist" ]]; then
	cp -R "$desktop_ui_dist" "$publish_dir/wwwroot/admin"
else
	echo "warning: desktop-ui dist not found ($desktop_ui_dist), /admin will be missing from wwwroot" >&2
fi
if [[ -d "$carthing_dist" ]]; then
	mkdir -p "$publish_dir/wwwroot/targets"
	cp -R "$carthing_dist" "$publish_dir/wwwroot/targets/carthing"
else
	echo "warning: carthing dist not found ($carthing_dist), /targets/carthing will be missing from wwwroot" >&2
fi

# The client is copied, never built, so a stage after a commit pairs a fresh host with whatever dist
# happens to be lying around - and the client then greets you with "this page is out of date"
# instead of loading. Cheap to check, and the message beats working that out from the symptom.
staged_shell="$publish_dir/wwwroot/index.html"
if [[ -f "$staged_shell" ]]; then
	ui_commit=$(sed -n 's/.*macro-deck-ui-commit" content="\([^"]*\)".*/\1/p' "$staged_shell" | head -1)
	host_commit=$(git -C "$root" rev-parse --short=7 HEAD 2>/dev/null || echo "")
	if [[ -n "$ui_commit" && -n "$host_commit" && "$ui_commit" != "$host_commit" ]]; then
		echo "warning: the staged client is from $ui_commit but this host is $host_commit;" >&2
		echo "         run 'npm run build' in ui/web-client and stage again, or the client will" >&2
		echo "         report itself out of date" >&2
	fi
fi

touch "$publish_dir/.macro-deck-packaged"
# Restore the tracked placeholder the rm -rf above removed (see .gitignore).
touch "$publish_dir/.gitkeep"
echo "Host staged in $publish_dir"
