import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  buildManifest,
  findUpdaterArtifact,
  releaseAssetBaseUrl,
  TARGETS,
} from './make-update-manifest.mjs';

const scriptPath = fileURLToPath(new URL('./make-update-manifest.mjs', import.meta.url));

test('finds the single updater artifact per target', () => {
  assert.equal(
    findUpdaterArtifact('windows', ['Macro Deck_3.0.0-beta.42_x64-setup.exe', 'Macro Deck_3.0.0-beta.42_x64-setup.exe.sig']),
    'Macro Deck_3.0.0-beta.42_x64-setup.exe'
  );
  assert.equal(
    findUpdaterArtifact('darwin', ['Macro Deck.app.tar.gz', 'Macro Deck.app.tar.gz.sig']),
    'Macro Deck.app.tar.gz'
  );
  assert.equal(
    findUpdaterArtifact('linux', ['Macro Deck_3.0.0-beta.42_amd64.AppImage', 'Macro Deck_3.0.0-beta.42_amd64.AppImage.sig']),
    'Macro Deck_3.0.0-beta.42_amd64.AppImage'
  );
});

test('rejects unknown targets, missing and ambiguous artifacts', () => {
  assert.throws(() => findUpdaterArtifact('freebsd', []), /Unknown target/);
  assert.throws(() => findUpdaterArtifact('windows', ['readme.txt']), /No updater artifact/);
  assert.throws(
    () => findUpdaterArtifact('linux', ['a.AppImage', 'b.AppImage']),
    /Ambiguous updater artifacts/
  );
});

test('a sibling .AppImage.sha256 checksum file does not confuse the AppImage lookup', () => {
  assert.equal(
    findUpdaterArtifact('linux', [
      'Macro Deck_3.0.0-beta.42_amd64.AppImage',
      'Macro Deck_3.0.0-beta.42_amd64.AppImage.sig',
      'Macro Deck_3.0.0-beta.42_amd64.AppImage.sha256',
    ]),
    'Macro Deck_3.0.0-beta.42_amd64.AppImage'
  );
});

// Guards the notification-only contract from issue #271: DEB/RPM are plain
// downloads and must never become updater payloads, so linux's TARGETS entry
// has to keep pointing at the appimage/ subdirectory and the .AppImage suffix
// even after deb/rpm bundles are added to the same build job.
test('the linux updater target stays pinned to the AppImage bundle', () => {
  assert.equal(TARGETS.linux.subdir, 'appimage');
  assert.equal(TARGETS.linux.suffix, '.AppImage');
});

test('builds a manifest with the platform key, trimmed signature and encoded url, and no release notes', () => {
  const manifest = buildManifest(
    'windows',
    '3.0.0-beta.42',
    'Macro Deck_3.0.0-beta.42_x64-setup.exe',
    'dGVzdA==\n',
    '2026-07-21T00:00:00.000Z'
  );
  assert.deepStrictEqual(manifest, {
    version: '3.0.0-beta.42',
    pub_date: '2026-07-21T00:00:00.000Z',
    platforms: {
      'windows-x86_64': {
        signature: 'dGVzdA==',
        url: `${releaseAssetBaseUrl('3.0.0-beta.42')}Macro%20Deck_3.0.0-beta.42_x64-setup.exe`,
      },
    },
  });
});

test('uses the arm64 platform key on macOS and x64 on linux', () => {
  assert.ok('darwin-aarch64' in buildManifest('darwin', '3.0.0', 'a.app.tar.gz', 's', 'd').platforms);
  assert.ok('linux-x86_64' in buildManifest('linux', '3.0.0', 'a.AppImage', 's', 'd').platforms);
});

test('rejects a non-canonical version before writing an updater manifest', () => {
  assert.throws(
    () => buildManifest('windows', '3.0.0-beta.01', 'setup.exe', 'signature', '2026-07-21T00:00:00.000Z'),
    /Invalid release version/
  );
});

test('writes the manifest when invoked as a CLI', () => {
  const dir = mkdtempSync(join(tmpdir(), 'md-manifest-'));
  try {
    const nsisDir = join(dir, 'nsis');
    mkdirSync(nsisDir);
    const artifact = 'Macro Deck_3.0.0-beta.42_x64-setup.exe';
    writeFileSync(join(nsisDir, artifact), '');
    writeFileSync(join(nsisDir, `${artifact}.sig`), 'dGVzdA==\n');
    const outFile = join(dir, 'beta-windows.json');
    const stdout = execFileSync(
      process.execPath,
      [scriptPath, 'windows', '3.0.0-beta.42', dir, outFile],
      { encoding: 'utf8' }
    );
    assert.match(stdout, /Wrote .*beta-windows\.json/);
    const manifest = JSON.parse(readFileSync(outFile, 'utf8'));
    assert.equal(manifest.version, '3.0.0-beta.42');
    assert.equal(manifest.platforms['windows-x86_64'].signature, 'dGVzdA==');
    assert.equal(Object.hasOwn(manifest, 'notes'), false);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// The payload is served from the GitHub release rather than the R2 feed, so the
// url has to address the tag publish-release.yml creates for this version.
test('the payload url addresses the release tag of the version', () => {
  assert.equal(
    releaseAssetBaseUrl('3.0.0-beta.42', 'Macro-Deck-App/Macro-Deck'),
    'https://github.com/Macro-Deck-App/Macro-Deck/releases/download/v3.0.0-beta.42/'
  );

  const manifest = buildManifest('linux', '3.0.0', 'Macro Deck_3.0.0_amd64.AppImage', 'sig', '2026-07-21T00:00:00.000Z');
  assert.equal(
    manifest.platforms['linux-x86_64'].url,
    'https://github.com/Macro-Deck-App/Macro-Deck/releases/download/v3.0.0/Macro%20Deck_3.0.0_amd64.AppImage'
  );
});
