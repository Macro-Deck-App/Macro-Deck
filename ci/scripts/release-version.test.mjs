import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  compareReleaseVersions,
  mapNativePackageVersion,
  parsePackageVersion,
  parseReleaseVersion,
  releaseMetadata,
} from './release-version.mjs';

const scriptPath = fileURLToPath(new URL('./release-version.mjs', import.meta.url));

test('parses stable and canonical beta release versions', () => {
  assert.deepEqual(parseReleaseVersion('3.0.0'), {
    version: '3.0.0',
    coreVersion: '3.0.0',
    betaNumber: null,
    isBeta: false,
  });
  assert.deepEqual(parseReleaseVersion('3.0.0-beta.42'), {
    version: '3.0.0-beta.42',
    coreVersion: '3.0.0',
    betaNumber: '42',
    isBeta: true,
  });
});

test('rejects legacy, malformed, and non-canonical release versions', () => {
  for (const version of [
    '3.0.0-b42',
    '3.0.0-beta',
    '3.0.0-beta.0',
    '3.0.0-beta.01',
    '3.0.0-beta.1+abc1234',
    '03.0.0',
    '3.00.0',
    '3.0.00',
  ]) {
    assert.throws(() => parseReleaseVersion(version), /Invalid release version/);
  }
});

// A preview is a package-only shape: the NuGet workflows accept it, the application release does not,
// because nothing maps it to an updater channel or a DEB/RPM version.
test('accepts preview alongside stable and beta as a package version', () => {
  assert.deepEqual(parsePackageVersion('3.0.0-preview.4'), {
    version: '3.0.0-preview.4',
    coreVersion: '3.0.0',
    prereleaseLabel: 'preview',
    prereleaseNumber: '4',
    isPrerelease: true,
  });
  assert.equal(parsePackageVersion('3.0.0-beta.42').prereleaseLabel, 'beta');
  assert.equal(parsePackageVersion('3.0.0').isPrerelease, false);
});

test('rejects a preview as a release version', () => {
  assert.throws(() => parseReleaseVersion('3.0.0-preview.4'), /Invalid release version/);
  assert.throws(() => releaseMetadata('3.0.0-preview.4'), /Invalid release version/);
});

test('holds package versions to the same canonical shape as release versions', () => {
  for (const version of [
    '3.0.0-preview',
    '3.0.0-preview.0',
    '3.0.0-preview.01',
    '3.0.0-pre.4',
    '3.0.0-alpha.4',
    '3.0.0-preview.4+abc1234',
    '3.0.0-beta.4-preview.4',
    '03.0.0-preview.4',
  ]) {
    assert.throws(() => parsePackageVersion(version), /Invalid package version/);
  }
});

test('validates a package version through the command-line interface', () => {
  assert.equal(
    execFileSync(process.execPath, [scriptPath, '3.0.0-preview.4', '--package'], {
      encoding: 'utf8',
    }),
    '3.0.0-preview.4\n'
  );
  assert.throws(() =>
    execFileSync(process.execPath, [scriptPath, '3.0.0-alpha.4', '--package'], { stdio: 'pipe' })
  );
});

test('uses numeric SemVer ordering for beta prerelease identifiers', () => {
  assert.ok(compareReleaseVersions('3.0.0-beta.9', '3.0.0-beta.10') < 0);
  assert.ok(compareReleaseVersions('3.0.0-beta.10', '3.0.0-beta.11') < 0);
  assert.ok(compareReleaseVersions('3.0.0-beta.11', '3.0.0') < 0);
});

test('derives the update channels from the parsed prerelease', () => {
  assert.deepEqual(releaseMetadata('3.0.0-beta.42'), {
    version: '3.0.0-beta.42',
    is_beta: 'true',
    updater_channel: 'beta',
    manifest_channels: 'beta',
    deb_version: '3.0.0~beta.42',
    rpm_version: '3.0.0',
    rpm_release: '0.beta.42',
  });
  assert.deepEqual(releaseMetadata('3.0.0'), {
    version: '3.0.0',
    is_beta: 'false',
    updater_channel: 'latest',
    manifest_channels: 'latest beta',
    deb_version: '3.0.0',
    rpm_version: '3.0.0',
    rpm_release: '1',
  });
});

test('maps a beta-to-stable series for Debian, RPM and AUR package ordering', () => {
  assert.deepEqual(
    ['3.0.0-beta.9', '3.0.0-beta.10', '3.0.0-beta.11', '3.0.0'].map(mapNativePackageVersion),
    [
      {
        debVersion: '3.0.0~beta.9',
        rpmVersion: '3.0.0',
        rpmRelease: '0.beta.9',
        aurVersion: '3.0.0beta.9',
      },
      {
        debVersion: '3.0.0~beta.10',
        rpmVersion: '3.0.0',
        rpmRelease: '0.beta.10',
        aurVersion: '3.0.0beta.10',
      },
      {
        debVersion: '3.0.0~beta.11',
        rpmVersion: '3.0.0',
        rpmRelease: '0.beta.11',
        aurVersion: '3.0.0beta.11',
      },
      {
        debVersion: '3.0.0',
        rpmVersion: '3.0.0',
        rpmRelease: '1',
        aurVersion: '3.0.0',
      },
    ]
  );
});

test('writes workflow metadata through the command-line interface', () => {
  const dir = mkdtempSync(join(tmpdir(), 'md-release-version-'));
  const outputPath = join(dir, 'github-output');
  try {
    const stdout = execFileSync(
      process.execPath,
      [scriptPath, '3.0.0-beta.42', '--github-output', outputPath],
      { encoding: 'utf8' }
    );
    assert.equal(stdout, '3.0.0-beta.42\n');
    assert.equal(
      readFileSync(outputPath, 'utf8'),
      'version=3.0.0-beta.42\nis_beta=true\nupdater_channel=beta\nmanifest_channels=beta\n' +
        'deb_version=3.0.0~beta.42\nrpm_version=3.0.0\nrpm_release=0.beta.42\n'
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('exposes the mapped DEB/RPM package versions for a beta and a stable release', () => {
  const beta = releaseMetadata('3.0.0-beta.42');
  assert.equal(beta.deb_version, '3.0.0~beta.42');
  assert.equal(beta.rpm_version, '3.0.0');
  assert.equal(beta.rpm_release, '0.beta.42');

  const stable = releaseMetadata('3.0.0');
  assert.equal(stable.deb_version, '3.0.0');
  assert.equal(stable.rpm_version, '3.0.0');
  assert.equal(stable.rpm_release, '1');
});
