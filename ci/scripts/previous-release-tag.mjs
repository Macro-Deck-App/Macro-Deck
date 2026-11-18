#!/usr/bin/env node

import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

import { compareReleaseVersions, parseReleaseVersion } from './release-version.mjs';

function tryParse(version) {
  try {
    return parseReleaseVersion(version);
  } catch {
    return null;
  }
}

export function previousReleaseTag(version, tags) {
  const current = parseReleaseVersion(version);
  let previous = null;
  for (const tag of tags) {
    if (typeof tag !== 'string' || !tag.startsWith('v')) {
      continue;
    }

    const candidate = tryParse(tag.slice(1));
    if (
      candidate === null
      || candidate.isBeta !== current.isBeta
      || compareReleaseVersions(candidate.version, current.version) >= 0
    ) {
      continue;
    }

    if (previous === null || compareReleaseVersions(candidate.version, previous) > 0) {
      previous = candidate.version;
    }
  }

  return previous === null ? null : `v${previous}`;
}

function main(argv) {
  const [version] = argv;
  if (!version) {
    console.error('usage: previous-release-tag.mjs <version> < tags.txt');
    process.exit(1);
  }

  try {
    const tags = readFileSync(0, 'utf8').split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
    console.log(previousReleaseTag(version, tags) ?? '');
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
