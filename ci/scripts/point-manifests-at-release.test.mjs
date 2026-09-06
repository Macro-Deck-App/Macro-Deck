import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { buildManifest } from './make-update-manifest.mjs';
import {
  isManifestFile,
  manifestPayloadName,
  normalizeAssetName,
  parseAssets,
  pointManifestsAtRelease,
} from './point-manifests-at-release.mjs';

const scriptPath = fileURLToPath(new URL('./point-manifests-at-release.mjs', import.meta.url));
const SETUP_EXE = 'Macro Deck_3.0.0-beta.42_x64-setup.exe';
const UPLOADED_EXE = 'Macro.Deck_3.0.0-beta.42_x64-setup.exe';
const DOWNLOAD_URL = `https://github.com/Macro-Deck-App/Macro-Deck/releases/download/v3.0.0-beta.42/${UPLOADED_EXE}`;

function windowsManifest() {
  return buildManifest('windows', '3.0.0-beta.42', SETUP_EXE, 'dGVzdA==', '2026-07-21T00:00:00.000Z');
}

function stage(manifest, name = 'latest-windows.json') {
  const dir = mkdtempSync(join(tmpdir(), 'md-release-'));
  writeFileSync(join(dir, name), `${JSON.stringify(manifest, null, 2)}\n`);
  return dir;
}

function stageAssets(assets) {
  const file = join(mkdtempSync(join(tmpdir(), 'md-assets-')), 'assets.json');
  writeFileSync(file, JSON.stringify({ assets }));
  return file;
}

test('a manifest is repointed at the name GitHub gave the asset', () => {
  const { manifests, problems } = pointManifestsAtRelease(
    [{ path: 'latest-windows.json', manifest: windowsManifest() }],
    [{ name: UPLOADED_EXE, url: DOWNLOAD_URL }]
  );

  assert.deepEqual(problems, []);
  assert.equal(manifests[0].manifest.platforms['windows-x86_64'].url, DOWNLOAD_URL);
});

test('repointing preserves the signature and the notes', () => {
  const manifest = buildManifest('windows', '3.0.0-beta.42', SETUP_EXE, 'dGVzdA==', '2026-07-21T00:00:00.000Z', 'Fixed a bug');
  const { manifests } = pointManifestsAtRelease(
    [{ path: 'latest-windows.json', manifest }],
    [{ name: UPLOADED_EXE, url: DOWNLOAD_URL }]
  );

  assert.equal(manifests[0].manifest.platforms['windows-x86_64'].signature, 'dGVzdA==');
  assert.equal(manifests[0].manifest.notes, 'Fixed a bug');
  assert.equal(manifests[0].manifest.version, '3.0.0-beta.42');
});

test('a payload the release does not carry is a problem, not a silent pass', () => {
  const { problems } = pointManifestsAtRelease(
    [{ path: 'latest-windows.json', manifest: windowsManifest() }],
    [{ name: 'something-else.exe', url: DOWNLOAD_URL }]
  );

  assert.equal(problems.length, 1);
  assert.match(problems[0], /does not carry/);
});

test('a manifest without platforms is a problem', () => {
  const { problems } = pointManifestsAtRelease([{ path: 'latest-windows.json', manifest: { platforms: {} } }], []);

  assert.equal(problems.length, 1);
  assert.match(problems[0], /no platforms/);
});

test('asset names are matched by their sanitized form', () => {
  assert.equal(normalizeAssetName('Macro Deck_3.0.0_amd64.AppImage'), 'Macro.Deck_3.0.0_amd64.AppImage');
  assert.equal(normalizeAssetName('Macro  Deck.dmg'), 'Macro.Deck.dmg');
  assert.equal(normalizeAssetName('already-fine_1.0.exe'), 'already-fine_1.0.exe');
});

test('the payload name is read back out of an encoded url', () => {
  assert.equal(manifestPayloadName(`https://example.test/download/${encodeURIComponent(SETUP_EXE)}`), SETUP_EXE);
  assert.equal(manifestPayloadName(undefined), null);
});

test('only channel files are treated as manifests', () => {
  assert.ok(isManifestFile('latest-windows.json'));
  assert.ok(isManifestFile('beta-darwin.json'));
  assert.equal(isManifestFile('Macro Deck.dmg'), false);
});

test('assets parse from a gh release view payload or a bare array', () => {
  assert.equal(parseAssets('{"assets":[{"name":"a"}]}').length, 1);
  assert.equal(parseAssets('[{"name":"a"}]').length, 1);
  assert.throws(() => parseAssets('{}'), /asset array/);
});

test('the script rewrites the staged manifest in place', () => {
  const dir = stage(windowsManifest());
  const assets = stageAssets([{ name: UPLOADED_EXE, url: DOWNLOAD_URL }]);
  try {
    execFileSync(process.execPath, [scriptPath, dir, assets], { encoding: 'utf8' });
    const written = JSON.parse(readFileSync(join(dir, 'latest-windows.json'), 'utf8'));
    assert.equal(written.platforms['windows-x86_64'].url, DOWNLOAD_URL);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the script fails when the release is missing a payload', () => {
  const dir = stage(windowsManifest());
  const assets = stageAssets([{ name: 'unrelated.exe', url: DOWNLOAD_URL }]);
  try {
    assert.throws(() => execFileSync(process.execPath, [scriptPath, dir, assets], { stdio: 'pipe' }));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
