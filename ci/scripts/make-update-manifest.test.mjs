import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { buildManifest, findUpdaterArtifact, FEED_BASE_URL, MAX_NOTES_LENGTH, TARGETS } from './make-update-manifest.mjs';

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

// Regression guard for issue #249: buildManifest and findUpdaterArtifact grew
// a notes parameter/argument, and the arity change must not disturb these
// pre-existing error paths.
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

test('builds a manifest with the platform key, trimmed signature, encoded url and notes', () => {
  const manifest = buildManifest(
    'windows',
    '3.0.0-beta.42',
    'Macro Deck_3.0.0-beta.42_x64-setup.exe',
    'dGVzdA==\n',
    '2026-07-21T00:00:00.000Z',
    '## What changed\n\n- Fixed a bug'
  );
  assert.deepStrictEqual(manifest, {
    version: '3.0.0-beta.42',
    pub_date: '2026-07-21T00:00:00.000Z',
    platforms: {
      'windows-x86_64': {
        signature: 'dGVzdA==',
        url: `${FEED_BASE_URL}Macro%20Deck_3.0.0-beta.42_x64-setup.exe`,
      },
    },
    notes: '## What changed\n\n- Fixed a bug',
  });
});

// issue #249: an absent, empty or whitespace-only body must produce no
// `notes` key at all - not `notes: undefined` (which JSON.stringify already
// drops) and definitely not `notes: null` (which would still serialize and
// break the UI's fallback-text behavior).
test('omits the notes field entirely when the body is absent, empty or whitespace-only', () => {
  for (const notes of [undefined, '', '   ', '\n\t \n']) {
    const manifest = buildManifest('windows', '3.0.0', 'setup.exe', 's', 'd', notes);
    assert.equal(Object.hasOwn(manifest, 'notes'), false);
    assert.equal(Object.hasOwn(JSON.parse(JSON.stringify(manifest)), 'notes'), false);
  }
});

test('caps notes at MAX_NOTES_LENGTH by truncating', () => {
  const oversized = 'x'.repeat(MAX_NOTES_LENGTH + 500);
  const manifest = buildManifest('windows', '3.0.0', 'setup.exe', 's', 'd', oversized);
  assert.equal(manifest.notes.length, MAX_NOTES_LENGTH);
  assert.equal(manifest.notes, oversized.slice(0, MAX_NOTES_LENGTH));
});

test('never splits a surrogate pair when truncating notes', () => {
  // Placing a surrogate pair (an astral character, here an emoji) exactly on
  // the truncation boundary is the case a naive `slice(0, MAX_NOTES_LENGTH)`
  // gets wrong - it would keep the pair's high surrogate and drop its low
  // surrogate, leaving a lone, invalid surrogate at the end.
  const prefix = 'x'.repeat(MAX_NOTES_LENGTH - 1);
  const oversized = `${prefix}\u{1F600}${'y'.repeat(500)}`;
  const manifest = buildManifest('windows', '3.0.0', 'setup.exe', 's', 'd', oversized);

  assert.ok(manifest.notes.length <= MAX_NOTES_LENGTH);
  const lastCode = manifest.notes.charCodeAt(manifest.notes.length - 1);
  assert.ok(
    lastCode < 0xd800 || lastCode > 0xdbff,
    'must not end on a lone high surrogate',
  );
  // A round trip through JSON must preserve the (possibly shorter) result
  // verbatim - a lone surrogate would still "work" here but is not what a
  // well-formed truncation should ever produce.
  assert.equal(JSON.parse(JSON.stringify(manifest.notes)), manifest.notes);
});

test('passes notes through verbatim, including Markdown, CRLF, quotes and backslashes', () => {
  const body = '# Heading\r\n\r\n- list item\r\nSee https://example.com/path?a=1&b=2\r\nA "quoted" \\ backslash';
  const manifest = buildManifest('windows', '3.0.0', 'setup.exe', 's', 'd', body);
  const roundTripped = JSON.parse(JSON.stringify(manifest));
  assert.equal(roundTripped.notes, body);
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

test('all three targets built with the same version and notes carry the same notes and one platform key each', () => {
  const notes = '- Fixed things\n- Improved other things';
  const manifests = {
    windows: buildManifest('windows', '3.0.0', 'setup.exe', 's', 'd', notes),
    darwin: buildManifest('darwin', '3.0.0', 'a.app.tar.gz', 's', 'd', notes),
    linux: buildManifest('linux', '3.0.0', 'a.AppImage', 's', 'd', notes),
  };
  for (const manifest of Object.values(manifests)) {
    assert.equal(manifest.notes, notes);
    assert.equal(Object.keys(manifest.platforms).length, 1);
  }
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
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the CLI reads the notes body from a file and writes it into the manifest', () => {
  const dir = mkdtempSync(join(tmpdir(), 'md-manifest-'));
  try {
    const nsisDir = join(dir, 'nsis');
    mkdirSync(nsisDir);
    const artifact = 'Macro Deck_3.0.0-beta.42_x64-setup.exe';
    writeFileSync(join(nsisDir, artifact), '');
    writeFileSync(join(nsisDir, `${artifact}.sig`), 'dGVzdA==\n');
    const notesFile = join(dir, 'release-notes.md');
    writeFileSync(notesFile, '# 3.0.0-beta.42\n\n- Something changed');
    const outFile = join(dir, 'beta-windows.json');
    const result = execFileSync(
      process.execPath,
      [scriptPath, 'windows', '3.0.0-beta.42', dir, outFile, notesFile],
      { encoding: 'utf8' }
    );
    assert.match(result, /Wrote .*beta-windows\.json/);
    const manifest = JSON.parse(readFileSync(outFile, 'utf8'));
    assert.equal(manifest.notes, '# 3.0.0-beta.42\n\n- Something changed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// Source-compatibility for the existing 4-argument call sites (issue #249):
// omitting the notes file must keep working exactly as before, with no
// `notes` key written.
test('the CLI with the notes argument omitted still succeeds and writes no notes key', () => {
  const dir = mkdtempSync(join(tmpdir(), 'md-manifest-'));
  try {
    const nsisDir = join(dir, 'nsis');
    mkdirSync(nsisDir);
    const artifact = 'Macro Deck_3.0.0-beta.42_x64-setup.exe';
    writeFileSync(join(nsisDir, artifact), '');
    writeFileSync(join(nsisDir, `${artifact}.sig`), 'dGVzdA==\n');
    const outFile = join(dir, 'beta-windows.json');
    execFileSync(process.execPath, [scriptPath, 'windows', '3.0.0-beta.42', dir, outFile], { encoding: 'utf8' });
    const manifest = JSON.parse(readFileSync(outFile, 'utf8'));
    assert.equal(Object.hasOwn(manifest, 'notes'), false);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the CLI exits non-zero with an error mentioning the path when the notes file is missing', () => {
  const dir = mkdtempSync(join(tmpdir(), 'md-manifest-'));
  try {
    const nsisDir = join(dir, 'nsis');
    mkdirSync(nsisDir);
    const artifact = 'Macro Deck_3.0.0-beta.42_x64-setup.exe';
    writeFileSync(join(nsisDir, artifact), '');
    writeFileSync(join(nsisDir, `${artifact}.sig`), 'dGVzdA==\n');
    const outFile = join(dir, 'beta-windows.json');
    const notesFile = join(dir, 'does-not-exist.md');
    assert.throws(
      () =>
        execFileSync(process.execPath, [scriptPath, 'windows', '3.0.0-beta.42', dir, outFile, notesFile], {
          encoding: 'utf8',
          stdio: 'pipe',
        }),
      (error) => {
        assert.equal(error.status, 1);
        assert.ok(error.stderr.includes(notesFile));
        return true;
      }
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
