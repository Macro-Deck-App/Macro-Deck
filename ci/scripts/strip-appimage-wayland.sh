#!/usr/bin/env bash
# Removes the bundled libwayland* libraries from the built AppImage and repacks
# it (issue #448).
#
# Why they must go: Tauri's bundler runs a pinned linuxdeploy build from
# tauri-apps/binary-releases whose embedded AppImage excludelist predates
# AppImageCommunity/pkg2appimage#559 - the entry that added libwayland-client.so.0
# precisely because a bundled copy breaks newer host Mesa. So linuxdeploy deploys
# the runner's (Ubuntu 22.04) libwayland* into the AppImage, the AppRun puts them
# ahead of the system ones, and on distributions with a newer graphics stack the
# host's Mesa loads them and the app never starts.
#
# Why removing them is safe: Tauri's GTK plugin exports GDK_BACKEND=x11 in the
# AppRun hook, so the app always runs on X11/XWayland and never uses GDK's Wayland
# backend. The bundled libraries are only ever pulled in transitively by the host's
# graphics stack - which is the failure itself.
#
# Why post-build: linuxdeploy does support --exclude-library, but the bundler
# neither passes it nor offers an environment hook, so the exclusion cannot be
# requested through `tauri build`.
#
# It also writes the bundled Ubuntu libraries' notices (appimage-library-notices.mjs) and
# repacks with the pinned runtime, so it always repacks.
#
# Repacking invalidates the updater signature `tauri build` wrote, so the caller
# must re-sign the AppImage afterwards (see .github/workflows/build.yml).
#
# Usage: strip-appimage-wayland.sh <appimageDir>
#   appimageDir: tauri's AppImage bundle output
#                (ui/bootstrapper/target/release/bundle/appimage), expected to
#                contain exactly one *.AppImage
set -euo pipefail

appimage_dir=${1:?usage: strip-appimage-wayland.sh <appimageDir>}
arch=${ARCH:-x86_64}
notices_tool="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/appimage-library-notices.mjs"
notices_path=usr/share/doc/macro-deck/THIRD-PARTY-NOTICES-LINUX-LIBRARIES

mapfile -t images < <(find "$appimage_dir" -maxdepth 1 -name '*.AppImage')
if [ "${#images[@]}" -ne 1 ]; then
	echo "error: expected exactly one .AppImage in $appimage_dir, found ${#images[@]}" >&2
	exit 1
fi
appimage=$(cd "$(dirname "${images[0]}")" && pwd)/$(basename "${images[0]}")

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# --appimage-extract is handled by the AppImage runtime itself and needs no FUSE.
(cd "$work" && "$appimage" --appimage-extract >/dev/null)
appdir="$work/squashfs-root"

mapfile -t libs < <(find "$appdir" -name 'libwayland*')
# Not an error: a future linuxdeploy with an up-to-date excludelist stops
# bundling them, and the notices below still need the repack.
if [ "${#libs[@]}" -eq 0 ]; then
	echo "No bundled libwayland* found in $(basename "$appimage") - nothing to strip"
fi
for lib in "${libs[@]}"; do
	echo "Removing ${lib#"$appdir"/}"
done
find "$appdir" -name 'libwayland*' -delete

runtime="$work/runtime-$arch"
node "$notices_tool" fetch-runtime "$runtime"
# Generated after the strip so the removed libraries are not attributed.
node "$notices_tool" generate "$appdir"

# Repack with the very tool Tauri used so the squashfs settings match; only the runtime is the
# pinned one the notices name. Tauri caches the tool; fall back to the URL it fetches from.
tools_dir="${XDG_CACHE_HOME:-$HOME/.cache}/tauri"
# The unsuffixed name is what the bundler caches the plugin under; downloading
# into the same slot when it is missing also primes it for the next build.
plugin="$tools_dir/linuxdeploy-plugin-appimage.AppImage"
if [ ! -f "$plugin" ]; then
	mkdir -p "$tools_dir"
	curl -fsSL -o "$plugin" \
		"https://github.com/linuxdeploy/linuxdeploy-plugin-appimage/releases/download/continuous/linuxdeploy-plugin-appimage-$arch.AppImage"
fi
chmod +x "$plugin"

out="$work/$(basename "$appimage")"
env APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$arch" OUTPUT="$out" LDAI_RUNTIME_FILE="$runtime" "$plugin" --appdir "$appdir"
if [ ! -f "$out" ]; then
	echo "error: linuxdeploy-plugin-appimage did not produce $out" >&2
	exit 1
fi
mv -f "$out" "$appimage"
chmod +x "$appimage"

# Verify against the repacked file rather than the directory that went into it:
# shipping an AppImage that still carries the libraries, or one that lost its
# entry point, would only surface on a user's machine.
verify="$work/verify"
mkdir "$verify"
(cd "$verify" && "$appimage" --appimage-extract >/dev/null)
verify_appdir="$verify/squashfs-root"

mapfile -t remaining < <(find "$verify_appdir" -name 'libwayland*')
if [ "${#remaining[@]}" -ne 0 ]; then
	echo "error: repacked AppImage still contains ${#remaining[@]} libwayland* file(s)" >&2
	exit 1
fi
if [ ! -x "$verify_appdir/AppRun" ]; then
	echo "error: repacked AppImage has no executable AppRun" >&2
	exit 1
fi
if [ -z "$(find "$verify_appdir/usr/bin" -maxdepth 1 -type f 2>/dev/null)" ]; then
	echo "error: repacked AppImage has no binaries in usr/bin" >&2
	exit 1
fi
if [ -z "$(find "$verify_appdir" -maxdepth 1 -name '*.desktop')" ]; then
	echo "error: repacked AppImage has no desktop entry" >&2
	exit 1
fi
if ! cmp -s "$appdir/$notices_path" "$verify_appdir/$notices_path"; then
	echo "error: repacked AppImage does not carry the generated $notices_path" >&2
	exit 1
fi
node "$notices_tool" check-runtime "$appimage"

echo "Stripped ${#libs[@]} libwayland* file(s), added $notices_path and repacked $(basename "$appimage")"
