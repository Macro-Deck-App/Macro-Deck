#!/usr/bin/env bash
set -euo pipefail

usage='usage: check-apt-repo.sh <baseUri> <keyring.asc> <suite> [debVersion]'
base=${1:?$usage}
keyring=${2:?$usage}
suite=${3:?$usage}
version=${4:-}

root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT
mkdir -p "$root/lists/partial" "$root/archives/partial" "$root/download"
cp "$keyring" "$root/macro-deck.asc"
echo "deb [arch=amd64 signed-by=$root/macro-deck.asc] $base $suite main" > "$root/sources.list"

apt_options=(
	-o "Dir::Etc::SourceList=$root/sources.list"
	-o Dir::Etc::SourceParts=/dev/null
	-o "Dir::State::Lists=$root/lists"
	-o "Dir::Cache::Archives=$root/archives"
	-o Dir::Cache::pkgcache=
	-o Dir::Cache::srcpkgcache=
	-o APT::Sandbox::User=root
	-o Debug::NoLocking=1
)

# Without --error-on=any a source that fails verification is only a warning.
apt-get "${apt_options[@]}" --error-on=any update
apt-cache "${apt_options[@]}" policy macro-deck

if [ -n "$version" ]; then
	(cd "$root/download" && apt-get "${apt_options[@]}" download "macro-deck=$version")
	downloaded=$(find "$root/download" -name '*.deb' | head -n 1)
	if [ -z "$downloaded" ] || [ "$(dpkg-deb -f "$downloaded" Version)" != "$version" ]; then
		echo "error: $suite did not serve macro-deck $version" >&2
		exit 1
	fi
	echo "$suite serves macro-deck $version"
fi
