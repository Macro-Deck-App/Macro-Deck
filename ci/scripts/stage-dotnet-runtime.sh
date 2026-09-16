#!/usr/bin/env bash
# Usage: stage-dotnet-runtime.sh <rid> <dest>; the host channel is pinned to the building SDK's patch.
# Env: MACRODECK_SDK_RUNTIME_VERSION overrides the SDK query, MACRODECK_RUNTIME_CACHE the download cache.
set -euo pipefail

rid=${1:?usage: stage-dotnet-runtime.sh <rid> <dest>}
dest=${2:?usage: stage-dotnet-runtime.sh <rid> <dest>}

root=$(cd "$(dirname "$0")/../.." && pwd)
cache=${MACRODECK_RUNTIME_CACHE:-$root/.cache/dotnet-runtime}

sdk_runtime_version=${MACRODECK_SDK_RUNTIME_VERSION:-}
if [[ -z "$sdk_runtime_version" ]]; then
	sdk_runtime_version=$(dotnet msbuild "$root/host/src/MacroDeckHost/MacroDeckHost.csproj" \
		-getProperty:BundledNETCoreAppPackageVersion | tr -d '[:space:]')
fi
if [[ -z "$sdk_runtime_version" ]]; then
	echo "error: could not determine the SDK's runtime version" >&2
	exit 1
fi

sha512_of() {
	if command -v sha512sum >/dev/null 2>&1; then
		sha512sum "$1" | cut -d' ' -f1
	else
		shasum -a 512 "$1" | cut -d' ' -f1
	fi
}

archives=$(node "$root/ci/scripts/resolve-dotnet-runtime.mjs" \
	"$root/ci/dotnet-runtime/bundled-runtimes.json" "$sdk_runtime_version" "$rid")

mkdir -p "$cache" "$dest"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

while IFS=$'\t' read -r version url sha512; do
	[[ -n "$version" ]] || continue
	case "$url" in
	*.zip) extension=zip ;;
	*.tar.gz) extension=tar.gz ;;
	*)
		echo "error: unexpected runtime archive type: $url" >&2
		exit 1
		;;
	esac
	archive="$cache/$sha512.$extension"

	if [[ ! -f "$archive" ]]; then
		echo "Downloading .NET runtime $version for $rid"
		curl --fail --location --silent --show-error --retry 3 --output "$archive.partial" "$url"
		mv "$archive.partial" "$archive"
	fi
	actual=$(sha512_of "$archive")
	if [[ "$actual" != "$sha512" ]]; then
		rm -f "$archive"
		echo "error: sha512 mismatch for $url" >&2
		echo "       expected $sha512" >&2
		echo "       actual   $actual" >&2
		exit 1
	fi

	extracted="$work/$version"
	mkdir -p "$extracted"
	if [[ "$extension" == zip ]]; then
		unzip -q -o "$archive" -d "$extracted"
	else
		tar -xzf "$archive" -C "$extracted"
	fi
	cp -R "$extracted/." "$dest/"
	echo "Staged .NET runtime $version"
done <<<"$archives"

# The LTTng trace provider links liblttng-ust.so.0, which Ubuntu 22.04 lacks, and breaks linuxdeploy.
rm -f "$dest"/shared/Microsoft.NETCore.App/*/libcoreclrtraceptprovider.so

if [[ ! -f "$dest/dotnet" && ! -f "$dest/dotnet.exe" ]]; then
	echo "error: no dotnet muxer in $dest" >&2
	exit 1
fi
for framework in Microsoft.NETCore.App Microsoft.AspNetCore.App; do
	if [[ ! -d "$dest/shared/$framework/$sdk_runtime_version" ]]; then
		echo "error: $dest is missing $framework $sdk_runtime_version" >&2
		exit 1
	fi
done
echo "Runtime staged in $dest"
