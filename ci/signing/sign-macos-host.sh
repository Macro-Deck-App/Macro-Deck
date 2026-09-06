#!/usr/bin/env bash
# Signs the single-file host launcher (the only Mach-O binary in the staged
# host-publish directory - it carries the .NET runtime and the third-party
# natives inside it) before the Tauri bundler copies it into the .app. The
# bundler only signs binaries it knows about, so nested host binaries must
# already carry a hardened-runtime signature for notarization to pass.
#
# Usage: sign-macos-host.sh <host-publish-dir> <entitlements.plist>
# Env:   APPLE_SIGNING_IDENTITY - codesign identity; skips signing when unset.
set -euo pipefail

directory=${1:?usage: sign-macos-host.sh <host-publish-dir> <entitlements.plist>}
entitlements=${2:?usage: sign-macos-host.sh <host-publish-dir> <entitlements.plist>}

if [[ -z "${APPLE_SIGNING_IDENTITY:-}" ]]; then
	echo "[sign] APPLE_SIGNING_IDENTITY not set, skipping host signing"
	exit 0
fi

signed=0
while IFS= read -r -d '' candidate; do
	if file -b "$candidate" | grep -q 'Mach-O'; then
		codesign --force --options runtime --timestamp \
			--entitlements "$entitlements" \
			--sign "$APPLE_SIGNING_IDENTITY" \
			"$candidate"
		signed=$((signed + 1))
	fi
done < <(find "$directory" -type f \( -perm -u+x -o -name '*.dylib' \) -print0)

echo "[sign] signed $signed Mach-O binaries in $directory"
