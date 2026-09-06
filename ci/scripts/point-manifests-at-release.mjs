#!/usr/bin/env node
// Rewrites the payload url of every staged channel manifest to the asset GitHub
// actually created for it, and fails when a manifest points at something the
// release does not carry. GitHub sanitizes an asset name on upload (a bundler
// name like "Macro Deck_3.0.0_amd64.AppImage" becomes "Macro.Deck_3.0.0_amd64.AppImage"),
// so the url make-update-manifest.mjs wrote cannot be trusted verbatim - and a
// manifest that 404s offers every installed client an update it cannot download.
//
// Usage: point-manifests-at-release.mjs <manifestDir> <assetsJson>
//   manifestDir: directory holding latest-*.json / beta-*.json
//   assetsJson:  `gh release view --json assets` output, or the bare asset array

import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { pathToFileURL } from 'node:url';

const MANIFEST_PATTERN = /^(?:latest|beta)-[a-z0-9]+\.json$/;

export function isManifestFile(path) {
  return MANIFEST_PATTERN.test(basename(path));
}

// GitHub replaces every run of characters outside its allowed set with a single
// dot, so two staged names can collapse onto one asset. Matching on the
// normalized form is what lets a manifest find its renamed asset.
export function normalizeAssetName(name) {
  return name.replace(/[^A-Za-z0-9._-]+/g, '.');
}

export function manifestPayloadName(url) {
  if (typeof url !== 'string') {
    return null;
  }
  const lastSlash = url.lastIndexOf('/');
  if (lastSlash < 0) {
    return null;
  }
  try {
    return decodeURIComponent(url.slice(lastSlash + 1)) || null;
  } catch {
    return null;
  }
}

export function parseAssets(text) {
  const parsed = JSON.parse(text);
  const assets = Array.isArray(parsed) ? parsed : parsed?.assets;
  if (!Array.isArray(assets)) {
    throw new Error('expected an asset array or an object with an "assets" array');
  }
  return assets;
}

/**
 * Returns `{ manifests, problems }` - each manifest with its urls rewritten to
 * the matching asset's download url. A non-empty `problems` means the release
 * must not be announced: some platform has no downloadable payload.
 */
export function pointManifestsAtRelease(manifests, assets) {
  const byName = new Map();
  for (const asset of assets) {
    const url = asset?.url ?? asset?.browser_download_url ?? asset?.browserDownloadUrl;
    if (typeof asset?.name === 'string' && typeof url === 'string') {
      byName.set(normalizeAssetName(asset.name), url);
    }
  }

  const problems = [];
  const rewritten = [];
  for (const { path, manifest } of manifests) {
    const platforms = manifest?.platforms;
    if (!platforms || Object.keys(platforms).length === 0) {
      problems.push(`${path} lists no platforms`);
      continue;
    }
    const next = { ...manifest, platforms: { ...platforms } };
    for (const [platform, entry] of Object.entries(platforms)) {
      const name = manifestPayloadName(entry?.url);
      if (name === null) {
        problems.push(`${path} (${platform}) has no payload url`);
        continue;
      }
      const url = byName.get(normalizeAssetName(name));
      if (url === undefined) {
        problems.push(`${path} (${platform}) points at "${name}", which the release does not carry`);
        continue;
      }
      next.platforms[platform] = { ...entry, url };
    }
    rewritten.push({ path, manifest: next });
  }
  return { manifests: rewritten, problems };
}

function main(argv) {
  const [dir, assetsFile] = argv;
  if (!dir || !assetsFile) {
    console.error('usage: point-manifests-at-release.mjs <manifestDir> <assetsJson>');
    process.exit(1);
  }
  const paths = readdirSync(dir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && isManifestFile(entry.name))
    .map((entry) => join(entry.parentPath, entry.name));
  if (paths.length === 0) {
    console.error(`No channel manifests found in ${dir}.`);
    process.exit(1);
  }

  const manifests = paths.map((path) => ({ path, manifest: JSON.parse(readFileSync(path, 'utf8')) }));
  const assets = parseAssets(readFileSync(assetsFile, 'utf8'));
  const result = pointManifestsAtRelease(manifests, assets);
  if (result.problems.length > 0) {
    console.error('The release does not back every channel manifest:');
    for (const problem of result.problems) {
      console.error(`  - ${problem}`);
    }
    process.exit(1);
  }

  for (const { path, manifest } of result.manifests) {
    writeFileSync(path, `${JSON.stringify(manifest, null, 2)}\n`);
    for (const [platform, entry] of Object.entries(manifest.platforms)) {
      console.log(`${basename(path)} (${platform}) -> ${entry.url}`);
    }
  }
  console.log(`Pointed ${result.manifests.length} channel file(s) at ${assets.length} release asset(s).`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
