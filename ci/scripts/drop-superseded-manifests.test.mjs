import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptPath = fileURLToPath(new URL('./drop-superseded-manifests.mjs', import.meta.url));

function directory(manifests) {
  const dir = mkdtempSync(join(tmpdir(), 'md-feed-'));
  for (const [name, version] of Object.entries(manifests)) {
    writeFileSync(join(dir, name), JSON.stringify({ version, platforms: {} }));
  }
  return dir;
}

function publish(staged, published) {
  const stagedDir = directory(staged);
  const publishedDir = directory(published);
  try {
    execFileSync(process.execPath, [scriptPath, stagedDir, publishedDir], { stdio: 'pipe' });
    return Object.keys(staged).filter((name) => existsSync(join(stagedDir, name)));
  } finally {
    rmSync(stagedDir, { recursive: true, force: true });
    rmSync(publishedDir, { recursive: true, force: true });
  }
}

test('a stable hotfix leaves a newer beta in the beta feed and still updates stable', () => {
  const kept = publish(
    { 'latest-windows.json': '3.0.5', 'beta-windows.json': '3.0.5' },
    { 'latest-windows.json': '3.0.4', 'beta-windows.json': '3.1.0-beta.3' }
  );

  assert.deepEqual(kept, ['latest-windows.json']);
});

test('a stable release that is newer than the beta reaches beta clients', () => {
  const kept = publish(
    { 'latest-darwin.json': '3.1.0', 'beta-darwin.json': '3.1.0' },
    { 'latest-darwin.json': '3.0.5', 'beta-darwin.json': '3.1.0-beta.3' }
  );

  assert.deepEqual(kept, ['latest-darwin.json', 'beta-darwin.json']);
});

test('a hotfix on an older line never replaces a newer stable release', () => {
  const kept = publish({ 'latest-linux.json': '3.0.6' }, { 'latest-linux.json': '3.1.0' });

  assert.deepEqual(kept, []);
});

test('the next beta replaces the previous one', () => {
  const kept = publish({ 'beta-linux.json': '3.1.0-beta.10' }, { 'beta-linux.json': '3.1.0-beta.9' });

  assert.deepEqual(kept, ['beta-linux.json']);
});

test('republishing the same version is allowed so a failed upload can be retried', () => {
  const kept = publish({ 'beta-linux.json': '3.1.0-beta.3' }, { 'beta-linux.json': '3.1.0-beta.3' });

  assert.deepEqual(kept, ['beta-linux.json']);
});

test('a feed that is missing or unreadable is replaced', () => {
  const kept = publish(
    { 'latest-windows.json': '3.0.5', 'beta-windows.json': '3.0.5' },
    { 'beta-windows.json': 'garbage' }
  );

  assert.deepEqual(kept, ['latest-windows.json', 'beta-windows.json']);
});
