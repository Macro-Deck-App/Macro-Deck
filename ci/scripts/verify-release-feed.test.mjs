import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { FEED_BASE_URL, buildManifest } from './make-update-manifest.mjs';
import { feedFileName, isManifestFile, verifyReleaseFeed } from './verify-release-feed.mjs';

const scriptPath = fileURLToPath(new URL('./verify-release-feed.mjs', import.meta.url));
const SETUP_EXE = 'Macro Deck_3.0.0-beta.42_x64-setup.exe';
const APPIMAGE = 'Macro Deck_3.0.0-beta.42_amd64.AppImage';

function windowsManifest() {
  return buildManifest('windows', '3.0.0-beta.42', SETUP_EXE, 'dGVzdA==', '2026-07-21T00:00:00.000Z');
}

function stageRelease({ nested, channel = 'latest' }) {
  const dir = mkdtempSync(join(tmpdir(), 'md-feed-'));
  const installerDir = nested ? join(dir, 'nsis') : dir;
  if (nested) {
    mkdirSync(installerDir);
  }
  writeFileSync(join(installerDir, SETUP_EXE), '');
  writeFileSync(join(dir, `${channel}-windows.json`), JSON.stringify(windowsManifest(), null, 2));
  return dir;
}

test('resolves the file name of a feed url', () => {
  assert.equal(feedFileName(`${FEED_BASE_URL}Macro%20Deck_3.0.0_x64-setup.exe`), 'Macro Deck_3.0.0_x64-setup.exe');
  assert.equal(feedFileName(`${FEED_BASE_URL}latest-windows.json`), 'latest-windows.json');
});

test('rejects urls that are not a plain file under the feed root', () => {
  assert.equal(feedFileName(`${FEED_BASE_URL}nsis/setup.exe`), null);
  assert.equal(feedFileName('https://example.com/setup.exe'), null);
  assert.equal(feedFileName(FEED_BASE_URL), null);
  assert.equal(feedFileName(undefined), null);
});

test('recognizes channel files by name', () => {
  assert.ok(isManifestFile('latest-windows.json'));
  assert.ok(isManifestFile('nested/latest-darwin.json'));
  assert.ok(isManifestFile('beta-linux.json'));
  assert.ok(!isManifestFile('Macro Deck_3.0.0_x64-setup.exe'));
  assert.ok(!isManifestFile('latest.json'));
  assert.ok(!isManifestFile('development-windows.json'));
});

// DEB/RPM and their .sha256 checksum files are plain downloads, never
// referenced by a channel manifest (issue #271's notification-only
// contract), so their presence alongside the AppImage manifest must not
// raise any problems.
test('deb, rpm and checksum files staged next to the manifest artifact produce no problems', () => {
  assert.deepEqual(
    verifyReleaseFeed(
      [
        join('appimage', APPIMAGE),
        join('appimage', `${APPIMAGE}.sha256`),
        join('deb', 'macro-deck_3.0.0~beta.42_amd64.deb'),
        join('deb', 'macro-deck_3.0.0~beta.42_amd64.deb.sha256'),
        join('rpm', 'macro-deck-3.0.0-0.beta.42.x86_64.rpm'),
        join('rpm', 'macro-deck-3.0.0-0.beta.42.x86_64.rpm.sha256'),
        'latest-linux.json',
      ],
      [
        {
          path: 'latest-linux.json',
          manifest: buildManifest('linux', '3.0.0-beta.42', APPIMAGE, 'dGVzdA==', '2026-07-21T00:00:00.000Z'),
        },
      ]
    ),
    []
  );
});

test('accepts a manifest whose artifact is staged', () => {
  assert.deepEqual(
    verifyReleaseFeed([join('nsis', SETUP_EXE), 'latest-windows.json'], [
      { path: 'latest-windows.json', manifest: windowsManifest() },
    ]),
    []
  );
});

test('reports an artifact the manifest points at but that is not staged', () => {
  const problems = verifyReleaseFeed(['latest-windows.json'], [
    { path: 'latest-windows.json', manifest: windowsManifest() },
  ]);
  assert.equal(problems.length, 1);
  assert.match(problems[0], /not among the staged files/);
});

test('reports a manifest url that is not directly under the feed root', () => {
  const manifest = windowsManifest();
  manifest.platforms['windows-x86_64'].url = `${FEED_BASE_URL}nsis/${encodeURIComponent(SETUP_EXE)}`;
  const problems = verifyReleaseFeed([join('nsis', SETUP_EXE)], [{ path: 'latest-windows.json', manifest }]);
  assert.equal(problems.length, 1);
  assert.match(problems[0], /not a file directly under/);
});

test('reports file names that would collide when published flat', () => {
  const problems = verifyReleaseFeed(
    [join('nsis', SETUP_EXE), join('msi', SETUP_EXE), 'latest-windows.json'],
    [{ path: 'latest-windows.json', manifest: windowsManifest() }]
  );
  assert.equal(problems.length, 1);
  assert.match(problems[0], /staged twice/);
});

test('reports a manifest without platforms', () => {
  const problems = verifyReleaseFeed(['latest-linux.json'], [{ path: 'latest-linux.json', manifest: {} }]);
  assert.deepEqual(problems, ['latest-linux.json lists no platforms']);
});

// Regression guard for issue #147: the Windows job uploads the installer and
// the channel file from different directories, so the build artifact keeps the
// nsis/ prefix. Publishing that verbatim put the installer at releases/nsis/...
// while the manifest pointed at releases/... - the updater downloaded a 404.
// The check has to pass for the nested staging the publish step flattens.
test('passes for a beta channel in a nested staging directory as a CLI', () => {
  const dir = stageRelease({ nested: true, channel: 'beta' });
  try {
    const stdout = execFileSync(process.execPath, [scriptPath, dir], { encoding: 'utf8' });
    assert.match(stdout, /beta-windows\.json \(windows-x86_64\) -> Macro Deck_3\.0\.0-beta\.42_x64-setup\.exe/);
    assert.match(stdout, /Verified 1 channel file\(s\) against 2 staged file\(s\)\./);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('fails as a CLI when the referenced installer is missing', () => {
  const dir = mkdtempSync(join(tmpdir(), 'md-feed-'));
  try {
    writeFileSync(join(dir, 'latest-windows.json'), JSON.stringify(windowsManifest(), null, 2));
    assert.throws(
      () => execFileSync(process.execPath, [scriptPath, dir], { encoding: 'utf8', stdio: 'pipe' }),
      (error) => {
        assert.equal(error.status, 1);
        assert.match(error.stderr, /not among the staged files/);
        return true;
      }
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('lists the referenced file names for the post-upload check', () => {
  const dir = stageRelease({ nested: true });
  try {
    const stdout = execFileSync(process.execPath, [scriptPath, dir, '--names'], { encoding: 'utf8' });
    assert.equal(stdout, `${SETUP_EXE}\n`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('passes as a CLI without any channel file', () => {
  const dir = mkdtempSync(join(tmpdir(), 'md-feed-'));
  try {
    writeFileSync(join(dir, SETUP_EXE), '');
    const stdout = execFileSync(process.execPath, [scriptPath, dir], { encoding: 'utf8' });
    assert.match(stdout, /nothing to verify/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
