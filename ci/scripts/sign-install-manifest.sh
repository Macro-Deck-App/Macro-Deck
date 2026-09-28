#!/usr/bin/env bash
# The bootstrapper verifies this signature with the updater pubkey, so it must be the updater key.
set -euo pipefail

host_dir=$(cd "${1:?usage: sign-install-manifest.sh <hostDir>}" && pwd)
bootstrapper_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../ui/bootstrapper" && pwd)"
: "${TAURI_SIGNING_PRIVATE_KEY:?the install manifest can only be signed with TAURI_SIGNING_PRIVATE_KEY set}"

rm -f "$host_dir/install-manifest.json.sig"
(cd "$bootstrapper_dir" && npm run --silent tauri signer sign -- \
	--private-key "$TAURI_SIGNING_PRIVATE_KEY" \
	--password "${TAURI_SIGNING_PRIVATE_KEY_PASSWORD:-}" \
	"$host_dir/install-manifest.json" >/dev/null)
test -s "$host_dir/install-manifest.json.sig"
