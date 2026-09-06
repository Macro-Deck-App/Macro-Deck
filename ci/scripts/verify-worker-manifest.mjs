#!/usr/bin/env node

// The service worker precaches the files its own manifest names and refuses to serve one whose
// content does not match what was recorded. So anything that rewrites a file after build.mjs hashed
// it leaves a worker that fails against its own precache and falls back to the network - the PWA
// silently stops being one, with no build error anywhere.
//
// build.mjs orders the manifest last for exactly this reason, and this re-derives every fingerprint
// from what is actually on disk so a step added after it is caught in CI rather than in the field.

import { readFile, stat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const WORKER_FILE = 'macro-deck-worker.js';

/** The same digest build.mjs records, so a difference here is a real difference. */
function fingerprint(content) {
  return createHash('sha256').update(content).digest('hex').slice(0, 16);
}

/**
 * @param {string} distDir a built client directory (ui/web-client/dist, or a target's dist-<id>)
 * @returns {Promise<string[]>} one message per problem; empty means the manifest describes what ships
 */
export async function verifyWorkerManifest(distDir) {
  const problems = [];

  let worker;
  try {
    worker = await readFile(path.join(distDir, WORKER_FILE), 'utf8');
  } catch {
    return [`${WORKER_FILE} is missing from ${distDir}; the client would ship without a service worker`];
  }

  const declared = worker.match(/var MANIFEST = (\{[\s\S]*?\});/);
  if (!declared) return [`${WORKER_FILE} carries no MANIFEST, so it precaches nothing`];

  let manifest;
  try {
    manifest = JSON.parse(declared[1]);
  } catch (error) {
    return [`${WORKER_FILE}'s MANIFEST is not valid JSON: ${error.message}`];
  }

  const assets = manifest.assets ?? [];
  if (assets.length === 0) problems.push(`${WORKER_FILE} precaches nothing at all`);

  const fingerprints = [];
  for (const asset of assets) {
    try {
      const content = await readFile(path.join(distDir, asset));
      fingerprints.push(asset + ':' + fingerprint(content));
    } catch {
      // A precached URL that is not there fails the worker's install outright, so the client never
      // gets a worker at all rather than getting a partial one.
      problems.push(`${asset} is precached but missing from ${distDir}`);
    }
  }

  const recomputed = fingerprint(fingerprints.join('\n'));
  if (problems.length === 0 && recomputed !== manifest.version) {
    problems.push(
      `the manifest version is ${manifest.version} but the files on disk hash to ${recomputed}; ` +
      'something rewrote an asset after build.mjs recorded it');
  }

  if (assets.includes(WORKER_FILE)) {
    problems.push('the worker precaches itself, which is how a broken worker becomes permanent');
  }

  return problems;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const roots = process.argv.slice(2);
  const directories = roots.length > 0 ? roots : [path.join('ui', 'web-client', 'dist')];

  let failed = false;
  for (const directory of directories) {
    try {
      await stat(directory);
    } catch {
      console.error(`${directory} does not exist; build the client before verifying it`);
      failed = true;
      continue;
    }

    const problems = await verifyWorkerManifest(directory);
    if (problems.length === 0) {
      console.log(`${directory}: the worker manifest matches what ships`);
      continue;
    }
    failed = true;
    for (const problem of problems) console.error(`${directory}: ${problem}`);
  }

  if (failed) process.exit(1);
}
