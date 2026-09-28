import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { appendFileSync, cpSync, mkdtempSync, readFileSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  buildManifest,
  MANIFEST_FILE,
  rewriteManifest,
  serializeManifest,
  SIGNATURE_FILE,
  verifyInstallation,
  verifyMinisign,
} from './make-install-manifest.mjs';

const scriptPath = fileURLToPath(new URL('./make-install-manifest.mjs', import.meta.url));
const fixture = fileURLToPath(new URL('../../ui/bootstrapper/tests/fixtures/install-integrity/', import.meta.url));
const fixtureHost = join(fixture, 'host');
const fixturePublicKey = readFileSync(join(fixture, 'public-key'), 'utf8').trim();
const productionConfig = fileURLToPath(new URL('../../ui/bootstrapper/tauri.conf.json', import.meta.url));

function copyOfFixture(t) {
  const dir = mkdtempSync(join(tmpdir(), 'install-manifest-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  cpSync(fixtureHost, dir, { recursive: true });
  return dir;
}

function run(...args) {
  try {
    return { status: 0, output: execFileSync('node', [scriptPath, ...args], { encoding: 'utf8', stdio: 'pipe' }) };
  } catch (error) {
    return { status: error.status, output: `${error.stdout}${error.stderr}` };
  }
}

test('the writer reproduces the signed fixture manifest byte for byte', () => {
  const manifest = buildManifest(fixtureHost, '3.1.0-beta.2', 'abc1234');
  assert.equal(serializeManifest(manifest), readFileSync(join(fixtureHost, MANIFEST_FILE), 'utf8'));
});

test('the manifest lists every installed file with posix paths, including dotfiles, but not itself', () => {
  const paths = buildManifest(fixtureHost, '1.0.0', 'abc1234').files.map((file) => file.path);
  assert.deepEqual(paths, [
    '.macro-deck-packaged',
    'Macro Deck Host',
    'runtime/shared/libhost.txt',
    'wwwroot/admin/main.js',
    'wwwroot/index.html',
  ]);
});

test('a signature made by tauri signer verifies against its public key', () => {
  const { problems } = verifyInstallation(fixtureHost, fixturePublicKey);
  assert.deepEqual(problems, []);
});

test('the signature is rejected by a different key, such as the release key', () => {
  const releaseKey = JSON.parse(readFileSync(productionConfig, 'utf8')).plugins.updater.pubkey;
  assert.throws(() => verifyInstallation(fixtureHost, releaseKey), /different key/);
});

test('an edited manifest no longer matches its signature', () => {
  const edited = readFileSync(join(fixtureHost, MANIFEST_FILE), 'utf8').replace('3.1.0-beta.2', '3.1.0-beta.3');
  const signature = readFileSync(join(fixtureHost, SIGNATURE_FILE), 'utf8');
  assert.throws(() => verifyMinisign(Buffer.from(edited), signature, fixturePublicKey), /does not match/);
});

test('verify reports missing and modified files and ignores files the manifest does not list', (t) => {
  const dir = copyOfFixture(t);
  unlinkSync(join(dir, 'wwwroot', 'admin', 'main.js'));
  appendFileSync(join(dir, 'runtime', 'shared', 'libhost.txt'), 'x');
  writeFileSync(join(dir, 'wwwroot', 'index.html'), '<!doctype html><title>Macro Deck</title>?');
  writeFileSync(join(dir, 'leftover-from-an-older-build.txt'), 'old');

  const { problems } = verifyInstallation(dir, fixturePublicKey);
  assert.deepEqual(problems, [
    { path: 'runtime/shared/libhost.txt', problem: 'modified' },
    { path: 'wwwroot/admin/main.js', problem: 'missing' },
    { path: 'wwwroot/index.html', problem: 'modified' },
  ]);
});

test('rewrite keeps version and commit and records what is actually shipped', (t) => {
  const dir = copyOfFixture(t);
  writeFileSync(join(dir, 'runtime', 'shared', 'libhost.txt'), 'patched by the bundler\n');
  const manifest = rewriteManifest(dir);
  assert.equal(manifest.version, '3.1.0-beta.2');
  assert.equal(manifest.commit, 'abc1234');
  assert.deepEqual(verifyInstallation(dir, null).problems, []);
});

test('the command line fails on a damaged tree and on a foreign key, and passes an intact one', (t) => {
  assert.equal(run('verify', fixtureHost, '--pubkey', fixturePublicKey).status, 0);
  assert.equal(run('verify', fixtureHost, '--pubkey', productionConfig).status, 1);

  const dir = copyOfFixture(t);
  unlinkSync(join(dir, 'Macro Deck Host'));
  const damaged = run('verify', dir, '--pubkey', fixturePublicKey);
  assert.equal(damaged.status, 1);
  assert.match(damaged.output, /missing: Macro Deck Host/);
});

test('write refuses to run without a version or commit', () => {
  assert.throws(() => buildManifest(fixtureHost, '', 'abc1234'), /required/);
  assert.equal(run('write', fixtureHost, '1.0.0').status, 1);
});
