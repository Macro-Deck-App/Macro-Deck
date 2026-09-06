#!/usr/bin/env node
// Verifies the staged release artifacts before they are published to the R2
// release feed. Two invariants have to hold or auto-update breaks silently
// (issue #147): the feed is flat - every object sits directly under releases/,
// because a manifest url is FEED_BASE_URL + the plain file name - and every
// file a channel-<target>.json points at must really be part of the upload set.
// The packaged installers arrive from the build artifacts in their bundler
// subdirectories (nsis/, appimage/, macos/, dmg/), so the publish step
// flattens them by file name; this check keeps that flattening honest.
//
// Usage: verify-release-feed.mjs <stagedDir> [--names]
//   --names prints only the referenced file names, one per line, so the
//   publish step can assert each of them really landed in the bucket.

import { readdirSync, readFileSync } from 'node:fs';
import { basename, join, relative } from 'node:path';
import { pathToFileURL } from 'node:url';

import { FEED_BASE_URL } from './make-update-manifest.mjs';

const MANIFEST_PATTERN = /^(?:latest|beta)-[a-z0-9]+\.json$/;

/**
 * The file name a feed url addresses, or null when the url does not point at a
 * plain file directly under the feed root (a nested path would never be
 * reachable, since the publish step uploads everything flat).
 */
export function feedFileName(url) {
  if (typeof url !== 'string' || !url.startsWith(FEED_BASE_URL)) {
    return null;
  }
  const rest = url.slice(FEED_BASE_URL.length);
  if (rest.length === 0 || rest.includes('/')) {
    return null;
  }
  try {
    return decodeURIComponent(rest);
  } catch {
    return null;
  }
}

export function isManifestFile(path) {
  return MANIFEST_PATTERN.test(basename(path));
}

export function manifestReferences(manifest) {
  const platforms = manifest?.platforms;
  if (!platforms || typeof platforms !== 'object') {
    return [];
  }
  return Object.entries(platforms).map(([platform, entry]) => ({ platform, url: entry?.url }));
}

/**
 * `files` are the staged paths relative to the upload directory, `manifests`
 * the parsed channel files as `{ path, manifest }`. Returns one message per
 * problem; an empty array means the set can be published as is.
 */
export function verifyReleaseFeed(files, manifests) {
  const problems = [];
  const byName = new Map();
  for (const file of files) {
    const name = basename(file);
    const existing = byName.get(name);
    if (existing === undefined) {
      byName.set(name, file);
    } else {
      problems.push(
        `"${name}" is staged twice (${existing}, ${file}); publishing flat would overwrite one with the other`
      );
    }
  }

  for (const { path, manifest } of manifests) {
    const references = manifestReferences(manifest);
    if (references.length === 0) {
      problems.push(`${path} lists no platforms`);
      continue;
    }
    for (const { platform, url } of references) {
      const name = feedFileName(url);
      if (name === null) {
        problems.push(
          `${path} (${platform}) points at "${url}", which is not a file directly under ${FEED_BASE_URL}`
        );
        continue;
      }
      if (!byName.has(name)) {
        problems.push(`${path} (${platform}) points at "${name}", which is not among the staged files`);
      }
    }
  }
  return problems;
}

function listFiles(dir) {
  return readdirSync(dir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => relative(dir, join(entry.parentPath, entry.name)));
}

function main(argv) {
  const [dir, ...flags] = argv;
  if (!dir) {
    console.error('usage: verify-release-feed.mjs <stagedDir> [--names]');
    process.exit(1);
  }
  const namesOnly = flags.includes('--names');
  const files = listFiles(dir);
  const manifests = files.filter(isManifestFile).map((path) => ({
    path,
    manifest: JSON.parse(readFileSync(join(dir, path), 'utf8')),
  }));
  if (manifests.length === 0) {
    if (!namesOnly) {
      console.log('No channel files staged - nothing to verify.');
    }
    return;
  }

  const problems = verifyReleaseFeed(files, manifests);
  if (problems.length > 0) {
    console.error('The staged release feed is inconsistent:');
    for (const problem of problems) {
      console.error(`  - ${problem}`);
    }
    process.exit(1);
  }

  for (const { path, manifest } of manifests) {
    for (const { platform, url } of manifestReferences(manifest)) {
      console.log(namesOnly ? feedFileName(url) : `${path} (${platform}) -> ${feedFileName(url)}`);
    }
  }
  if (!namesOnly) {
    console.log(`Verified ${manifests.length} channel file(s) against ${files.length} staged file(s).`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
