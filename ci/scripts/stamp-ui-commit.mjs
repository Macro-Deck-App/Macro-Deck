#!/usr/bin/env node


import { readdir, readFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const COMMIT_LENGTH = 7;
export const META_NAME = 'macro-deck-ui-commit';

const META_PATTERN = new RegExp(`[ \\t]*<meta\\s+name="${META_NAME}"[^>]*>\\r?\\n?`, 'i');

/**
 * Resolves the commit to stamp: an explicit override first, then the CI-provided SHA, then the
 * working copy. Returns null when none of them yields a commit, so the build stays unstamped
 * instead of claiming a wrong one.
 */
export function resolveCommit(env = process.env, readGitCommit = gitHeadCommit) {
  const candidate = env.MACRO_DECK_UI_COMMIT || env.GITHUB_SHA || readGitCommit();
  const commit = typeof candidate === 'string' ? candidate.trim() : '';
  return commit ? commit.slice(0, COMMIT_LENGTH) : null;
}

/**
 * Inserts the meta tag after the charset meta when present (the charset has to stay within the
 * first bytes of the document), otherwise right after <head>. An earlier tag is replaced so
 * re-runs stay idempotent.
 */
export function injectCommitMeta(html, commit) {
  const stripped = html.replace(META_PATTERN, '');
  const tag = `<meta name="${META_NAME}" content="${commit}">`;
  const charsetMatch = /<meta charset[^>]*>/i.exec(stripped);
  const headMatch = /<head[^>]*>/i.exec(stripped);
  if (!headMatch) {
    return stripped;
  }

  const anchor = charsetMatch ?? headMatch;
  const insertAt = anchor.index + anchor[0].length;
  return `${stripped.slice(0, insertAt)}\n  ${tag}${stripped.slice(insertAt)}`;
}

function gitHeadCommit() {
  try {
    return execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' });
  } catch {
    return '';
  }
}

async function findShells(directory) {
  let entries;
  try {
    entries = await readdir(directory, { withFileTypes: true });
  } catch {
    return [];
  }

  const shells = [];
  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      shells.push(...(await findShells(entryPath)));
    } else if (/^index.*\.html$/i.test(entry.name)) {
      shells.push(entryPath);
    }
  }

  return shells;
}

export async function stampDirectory(directory, commit) {
  const shells = await findShells(directory);
  for (const shell of shells) {
    const html = await readFile(shell, 'utf8');
    await writeFile(shell, injectCommitMeta(html, commit));
  }

  return shells;
}

async function main() {
  const commit = resolveCommit();
  if (!commit) {
    console.warn('stamp-ui-commit: no commit available, leaving the build unstamped.');
    return;
  }

  const distDir = path.resolve(fileURLToPath(new URL('../../ui/angular/dist', import.meta.url)));
  const stamped = await stampDirectory(distDir, commit);
  if (stamped.length === 0) {
    console.error(`stamp-ui-commit: no HTML shell found under ${distDir}.`);
    process.exit(1);
  }

  console.log(`stamp-ui-commit: stamped ${commit} into ${stamped.length} shell(s).`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
