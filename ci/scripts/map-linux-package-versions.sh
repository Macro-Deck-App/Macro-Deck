#!/usr/bin/env bash
# Rewrites the DEB version metadata Tauri's bundler cannot express through its
# own config, and gives the DEB and RPM a conventional, space-free file name
# (the bundler names them after productName, i.e. "Macro Deck ...").
#
# Why DEB still needs a rewrite: tauri-codegen validates config.version with
# `semver::Version::from_str` (tauri-codegen-2.6.3, src/context.rs), so a
# Debian "~beta" suffix - or any other non-semver string - can never be set
# through a Tauri config file. The RPM does NOT have this problem: its mapped
# version/release (rpmVersion/rpmRelease from ci/scripts/release-version.mjs's
# mapNativePackageVersion) are both legal Tauri config values on their own, so
# the RPM is packaged by a separate `tauri bundle` pass whose config already
# carries them (see .github/workflows/build.yml's "Bundle RPM" step) - that
# pass repackages the binary the AppImage/DEB build compiled rather than
# compiling its own. So the RPM arrives here already correctly versioned - this
# script only renames it and asserts the invariant that it matches what was
# asked for.
#
# Usage: map-linux-package-versions.sh <bundleDir> <debVersion> <rpmVersion> <rpmRelease>
#   bundleDir:  tauri bundle output dir (ui/bootstrapper/target/release/bundle),
#               expected to contain exactly one *.deb under deb/ and one
#               *.rpm under rpm/
#   debVersion: e.g. "3.0.0" or "3.0.0~beta.42"
#   rpmVersion: e.g. "3.0.0" (RPM version syntax forbids '-', so this is
#               always the plain core version, beta or not)
#   rpmRelease: e.g. "1" or "0.beta.42"
set -euo pipefail

bundle_dir=${1:?usage: map-linux-package-versions.sh <bundleDir> <debVersion> <rpmVersion> <rpmRelease>}
deb_version=${2:?usage: map-linux-package-versions.sh <bundleDir> <debVersion> <rpmVersion> <rpmRelease>}
rpm_version=${3:?usage: map-linux-package-versions.sh <bundleDir> <debVersion> <rpmVersion> <rpmRelease>}
rpm_release=${4:?usage: map-linux-package-versions.sh <bundleDir> <debVersion> <rpmVersion> <rpmRelease>}

deb_dir="$bundle_dir/deb"
rpm_dir="$bundle_dir/rpm"

# --- DEB ---------------------------------------------------------------
# dpkg-deb -R/--build round-trips the archive without touching data.tar, so
# md5sums (computed over data.tar's contents) stays valid after only the
# control file's Version: line changes. This runs unconditionally - for a
# stable release deb_version already equals the built Version, so the
# rewrite is a harmless no-op and only the file name changes.
mapfile -t deb_in < <(find "$deb_dir" -maxdepth 1 -name '*.deb')
if [ "${#deb_in[@]}" -ne 1 ]; then
	echo "error: expected exactly one .deb in $deb_dir, found ${#deb_in[@]}" >&2
	exit 1
fi
deb_src="${deb_in[0]}"
# Read from the package rather than hardcoded, so a future productName or
# architecture change cannot silently rename the output to a lying file name.
deb_package=$(dpkg-deb -f "$deb_src" Package)
deb_arch=$(dpkg-deb -f "$deb_src" Architecture)
deb_out="$deb_dir/${deb_package}_${deb_version}_${deb_arch}.deb"

deb_work=$(mktemp -d)
trap 'rm -rf "$deb_work"' EXIT

dpkg-deb -R "$deb_src" "$deb_work/work"
control="$deb_work/work/DEBIAN/control"
if [ ! -f "$control" ]; then
	echo "error: dpkg-deb -R did not produce a control file at $control" >&2
	exit 1
fi
if ! grep -q '^Version: ' "$control"; then
	echo "error: no 'Version:' line found in $control" >&2
	exit 1
fi
sed -i "s/^Version: .*/Version: ${deb_version}/" "$control"
rm -f "$deb_src"
dpkg-deb --build --root-owner-group "$deb_work/work" "$deb_out"

# Verify the rewrite actually took: a wrongly-versioned DEB installs fine and
# only misbehaves at the next `apt upgrade`, which is exactly the silent
# failure mode this script exists to prevent.
built_deb_version=$(dpkg-deb -f "$deb_out" Version)
if [ "$built_deb_version" != "$deb_version" ]; then
	echo "error: DEB Version rewrite did not take (got ${built_deb_version}, expected ${deb_version})" >&2
	exit 1
fi
echo "Rewrote DEB Version -> ${deb_version} (${deb_out})"

# --- RPM ---------------------------------------------------------------
# Bundled in its own pass at the mapped version/release, so nothing here needs
# rewriting - only renamed, after asserting the bundler actually produced what
# was asked for (a real invariant check now, not a rewrite fallback).
mapfile -t rpm_in < <(find "$rpm_dir" -maxdepth 1 -name '*.rpm')
if [ "${#rpm_in[@]}" -ne 1 ]; then
	echo "error: expected exactly one .rpm in $rpm_dir, found ${#rpm_in[@]}" >&2
	exit 1
fi
rpm_src="${rpm_in[0]}"

built_version=$(rpm -qp --queryformat '%{VERSION}' "$rpm_src")
built_release=$(rpm -qp --queryformat '%{RELEASE}' "$rpm_src")

if [ "$built_version" != "$rpm_version" ] || [ "$built_release" != "$rpm_release" ]; then
	echo "error: RPM was not bundled at the expected version (got ${built_version}-${built_release}, expected ${rpm_version}-${rpm_release}); check the rpm-version-override.json config used for the RPM bundle step." >&2
	exit 1
fi

# Read from the package rather than hardcoded, so a future productName or
# runner architecture change cannot silently rename the output to a lying file name.
rpm_name=$(rpm -qp --queryformat '%{NAME}' "$rpm_src")
rpm_arch=$(rpm -qp --queryformat '%{ARCH}' "$rpm_src")
rpm_out="$rpm_dir/${rpm_name}-${rpm_version}-${rpm_release}.${rpm_arch}.rpm"

mv "$rpm_src" "$rpm_out"
echo "Renamed RPM -> ${rpm_out}"
