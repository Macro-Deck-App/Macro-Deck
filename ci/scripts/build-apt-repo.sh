#!/usr/bin/env bash
set -euo pipefail

usage='usage: build-apt-repo.sh <repoDir> <deb> <version> <indexDir with <suite>/Packages> <signingKey> [passphraseFile]'
repo=${1:?$usage}
deb=${2:?$usage}
version=${3:?$usage}
index_dir=${4:?$usage}
signing_key=${5:?$usage}
passphrase_file=${6:-}
index_tool="$(cd "$(dirname "$0")" && pwd)/make-apt-index.mjs"

package=$(dpkg-deb -f "$deb" Package)
deb_version=$(dpkg-deb -f "$deb" Version)
architecture=$(dpkg-deb -f "$deb" Architecture)
expected_version=$(node "$index_tool" deb-version "$version")
if [ "$package" != macro-deck ] || [ "$deb_version" != "$expected_version" ] || [ "$architecture" != amd64 ]; then
	echo "error: $deb is ${package} ${deb_version} ${architecture}, expected macro-deck ${expected_version} amd64" >&2
	exit 1
fi

pool=$(node "$index_tool" pool-path "$package" "$deb_version" "$architecture")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

mkdir -p "$work/$(dirname "$pool")" "$repo/$(dirname "$pool")"
cp "$deb" "$work/$pool"
cp "$deb" "$repo/$pool"
(cd "$work" && apt-ftparchive packages "$(dirname "$pool")") > "$work/stanza"

signing=(gpg --batch --yes --pinentry-mode loopback --local-user "${signing_key}!")
if [ -n "$passphrase_file" ]; then
	signing+=(--passphrase-file "$passphrase_file")
fi

receiving=" $(node "$index_tool" suites "$version") "
for suite in $(node "$index_tool" suites); do
	dist="$repo/dists/$suite"
	binary="$dist/main/binary-$architecture"
	mkdir -p "$binary"

	if [[ "$receiving" == *" $suite "* ]]; then
		node "$index_tool" merge "$index_dir/$suite/Packages" "$work/stanza" "$binary/Packages"
	else
		cp "$index_dir/$suite/Packages" "$binary/Packages"
	fi
	gzip -9nc "$binary/Packages" > "$binary/Packages.gz"

	rm -f "$dist/Release" "$dist/Release.gpg" "$dist/InRelease"
	apt-ftparchive \
		-o APT::FTPArchive::DoByHash=true \
		-o 'APT::FTPArchive::Release::Origin=Macro Deck' \
		-o 'APT::FTPArchive::Release::Label=Macro Deck' \
		-o "APT::FTPArchive::Release::Suite=$suite" \
		-o "APT::FTPArchive::Release::Codename=$suite" \
		-o "APT::FTPArchive::Release::Architectures=$architecture" \
		-o APT::FTPArchive::Release::Components=main \
		-o "APT::FTPArchive::Release::Description=Macro Deck ($suite)" \
		release "$dist" > "$work/Release"
	mv "$work/Release" "$dist/Release"

	"${signing[@]}" --clearsign --output "$dist/InRelease" "$dist/Release"
	"${signing[@]}" --armor --detach-sign --output "$dist/Release.gpg" "$dist/Release"
	echo "Signed $suite: $(grep -c '^Package: ' "$binary/Packages" || true) package version(s)"
done
