#!/usr/bin/env node
// A feed never moves backwards: a hotfix on an older line must not replace a newer beta or stable
// that clients already get. Usage: drop-superseded-manifests.mjs <stagedDir> <publishedDir>

import { existsSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { basename, join } from 'node:path';
import { pathToFileURL } from 'node:url';

import { isManifestFile } from './point-manifests-at-release.mjs';
import { compareReleaseVersions, parseReleaseVersion } from './release-version.mjs';

function isReleaseVersion(version) {
  try {
    parseReleaseVersion(version);
    return true;
  } catch {
    return false;
  }
}

export function supersededManifests(staged, published) {
  const decisions = [];
  for (const { name, version } of staged) {
    const current = published.get(name);
    if (current === undefined || !isReleaseVersion(current)) {
      decisions.push({ name, version, current: current ?? null, keep: true });
      continue;
    }
    decisions.push({ name, version, current, keep: compareReleaseVersions(version, current) >= 0 });
  }
  return decisions;
}

function readVersion(path) {
  return JSON.parse(readFileSync(path, 'utf8'))?.version;
}

function main(argv) {
  const [stagedDir, publishedDir] = argv;
  if (!stagedDir || !publishedDir) {
    console.error('usage: drop-superseded-manifests.mjs <stagedDir> <publishedDir>');
    process.exit(1);
  }

  const stagedPaths = readdirSync(stagedDir)
    .filter(isManifestFile)
    .map((name) => join(stagedDir, name));
  const staged = stagedPaths.map((path) => {
    const version = readVersion(path);
    parseReleaseVersion(version);
    return { name: basename(path), version };
  });

  const published = new Map();
  for (const { name } of staged) {
    const path = join(publishedDir, name);
    if (existsSync(path)) {
      published.set(name, readVersion(path));
    }
  }

  for (const decision of supersededManifests(staged, published)) {
    if (decision.keep) {
      console.log(`${decision.name}: publishing ${decision.version} (feed has ${decision.current ?? 'nothing'})`);
      continue;
    }
    rmSync(join(stagedDir, decision.name));
    console.log(`${decision.name}: keeping ${decision.current}, which is newer than ${decision.version}`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
