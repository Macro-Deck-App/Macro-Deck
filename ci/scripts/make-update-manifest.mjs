#!/usr/bin/env node
// Generates a Tauri updater manifest (<channel>-<target>.json) for one platform
// from a tauri build bundle directory. Only the manifest is served from the R2
// release feed; the payload it points at is the asset attached to the GitHub
// release. The updater plugin resolves a channel-specific {{target}} manifest per
// platform, so partially built releases never clobber another platform's channel file.
//
// The url written here is the name the bundler produced. GitHub sanitizes asset
// names on upload, so point-manifests-at-release.mjs rewrites these urls to the
// real ones once the release exists.
//
// Usage: make-update-manifest.mjs <target> <version> <bundleDir> <outFile> [notesFile]
//   target:    windows | darwin | linux
//   version:   full app version (e.g. 3.0.0-beta.42)
//   bundleDir: tauri bundle output (ui/bootstrapper/target/release/bundle)
//   outFile:   where to write the manifest JSON
//   notesFile: optional path to a Markdown file whose contents become the
//              manifest's `notes` field (the release changelog)
//
// GITHUB_REPOSITORY selects the repository the payload urls address.

import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

import { parseReleaseVersion } from './release-version.mjs';

export const DEFAULT_REPOSITORY = 'Macro-Deck-App/Macro-Deck';

// Where the release feed serves the channel manifests from. Payloads are not
// published here any more - only these manifests are.
export const FEED_BASE_URL = 'https://updater.macro-deck.app/releases/';

// The tag publish-release.yml creates for a version, and the asset base url under it.
export function releaseAssetBaseUrl(version, repository = DEFAULT_REPOSITORY) {
  return `https://github.com/${repository}/releases/download/v${version}/`;
}

// Per-target updater artifact: subdirectory of the bundle dir and the file
// suffix of the signed updater payload, plus the platform key used by the
// updater plugin (the packaged targets are x64 Windows/Linux and arm64 macOS).
export const TARGETS = {
  windows: { subdir: 'nsis', suffix: '-setup.exe', platformKey: 'windows-x86_64' },
  darwin: { subdir: 'macos', suffix: '.app.tar.gz', platformKey: 'darwin-aarch64' },
  linux: { subdir: 'appimage', suffix: '.AppImage', platformKey: 'linux-x86_64' },
};

export function findUpdaterArtifact(target, fileNames) {
  const spec = TARGETS[target];
  if (!spec) {
    throw new Error(`Unknown target "${target}" (expected one of: ${Object.keys(TARGETS).join(', ')})`);
  }
  const candidates = fileNames.filter((name) => name.endsWith(spec.suffix));
  if (candidates.length === 0) {
    throw new Error(`No updater artifact (*${spec.suffix}) found for target "${target}"`);
  }
  if (candidates.length > 1) {
    throw new Error(`Ambiguous updater artifacts for target "${target}": ${candidates.join(', ')}`);
  }
  return candidates[0];
}

// Every client polls its channel manifest on a six-hour timer, so the notes
// embedded in it must stay small; anything longer is truncated.
export const MAX_NOTES_LENGTH = 16 * 1024;

// A plain `slice(0, n)` can land the cut in the middle of a UTF-16 surrogate
// pair (an emoji or other astral character in the notes), leaving a lone
// surrogate at the end - invalid text that can round-trip strangely once
// re-encoded downstream. Back off by one code unit when that would happen.
function truncateAtCodePointBoundary(text, maxLength) {
  if (text.length <= maxLength) {
    return text;
  }
  const lastCode = text.charCodeAt(maxLength - 1);
  const isHighSurrogate = lastCode >= 0xd800 && lastCode <= 0xdbff;
  return text.slice(0, isHighSurrogate ? maxLength - 1 : maxLength);
}

export function buildManifest(target, version, artifactFileName, signature, pubDate, notes, repository) {
  const spec = TARGETS[target];
  const parsedVersion = parseReleaseVersion(version);
  const baseUrl = releaseAssetBaseUrl(parsedVersion.version, repository);
  const manifest = {
    version: parsedVersion.version,
    pub_date: pubDate,
    platforms: {
      [spec.platformKey]: {
        signature: signature.trim(),
        url: `${baseUrl}${encodeURIComponent(artifactFileName)}`,
      },
    },
  };
  // An absent, empty or whitespace-only body must leave `notes` out of the
  // manifest entirely - `null` would still ship a "notes" key over the wire,
  // and the desktop UI's own fallback text only kicks in when the field is
  // missing.
  if (typeof notes === 'string' && notes.trim().length > 0) {
    manifest.notes = truncateAtCodePointBoundary(notes, MAX_NOTES_LENGTH);
  }
  return manifest;
}

function main(argv) {
  const [target, version, bundleDir, outFile, notesFile] = argv;
  if (!target || !version || !bundleDir || !outFile) {
    console.error('usage: make-update-manifest.mjs <target> <version> <bundleDir> <outFile> [notesFile]');
    process.exit(1);
  }
  const spec = TARGETS[target];
  if (!spec) {
    console.error(`unknown target "${target}"`);
    process.exit(1);
  }
  let notes;
  if (notesFile) {
    try {
      notes = readFileSync(notesFile, 'utf8');
    } catch (error) {
      console.error(`could not read notes file "${notesFile}": ${error.message}`);
      process.exit(1);
    }
  }
  const dir = join(bundleDir, spec.subdir);
  const artifact = findUpdaterArtifact(target, readdirSync(dir));
  const signature = readFileSync(join(dir, `${artifact}.sig`), 'utf8');
  const manifest = buildManifest(
    target,
    version,
    artifact,
    signature,
    new Date().toISOString(),
    notes,
    process.env.GITHUB_REPOSITORY || DEFAULT_REPOSITORY
  );
  writeFileSync(outFile, `${JSON.stringify(manifest, null, 2)}\n`);
  console.log(`Wrote ${outFile} (${spec.platformKey} -> ${artifact})`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
