#!/usr/bin/env node

import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const RELEASE_PRERELEASE_LABELS = ['beta'];
const PACKAGE_PRERELEASE_LABELS = ['beta', 'preview'];

function versionPattern(labels) {
  return new RegExp(
    `^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-(${labels.join('|')})\\.([1-9]\\d*))?$`
  );
}

const RELEASE_VERSION_PATTERN = versionPattern(RELEASE_PRERELEASE_LABELS);
const PACKAGE_VERSION_PATTERN = versionPattern(PACKAGE_PRERELEASE_LABELS);

function compareNumericIdentifiers(left, right) {
  if (left.length !== right.length) {
    return left.length < right.length ? -1 : 1;
  }

  return left === right ? 0 : left < right ? -1 : 1;
}

export function parseReleaseVersion(input) {
  const version = typeof input === 'string' ? input : '';
  const match = RELEASE_VERSION_PATTERN.exec(version);
  if (!match) {
    throw new Error(
      `Invalid release version "${version}". Expected X.Y.Z or X.Y.Z-beta.N, where N is a positive integer without leading zeroes.`
    );
  }

  const [, major, minor, patch, , betaNumber] = match;
  const coreVersion = `${major}.${minor}.${patch}`;
  return {
    version,
    coreVersion,
    betaNumber: betaNumber ?? null,
    isBeta: betaNumber !== undefined,
  };
}

/**
 * Parses a version the public NuGet packages may be published at. A superset of a release version:
 * every release version is a valid package version, plus X.Y.Z-preview.N for a package-only preview.
 *
 * @param {string} input the version to validate
 * @returns {{version: string, coreVersion: string, prereleaseLabel: string | null, prereleaseNumber: string | null, isPrerelease: boolean}}
 */
export function parsePackageVersion(input) {
  const version = typeof input === 'string' ? input : '';
  const match = PACKAGE_VERSION_PATTERN.exec(version);
  if (!match) {
    const shapes = PACKAGE_PRERELEASE_LABELS.map((label) => `X.Y.Z-${label}.N`).join(' or ');
    throw new Error(
      `Invalid package version "${version}". Expected X.Y.Z, ${shapes}, where N is a positive integer without leading zeroes.`
    );
  }

  const [, major, minor, patch, label, prereleaseNumber] = match;
  return {
    version,
    coreVersion: `${major}.${minor}.${patch}`,
    prereleaseLabel: label ?? null,
    prereleaseNumber: prereleaseNumber ?? null,
    isPrerelease: label !== undefined,
  };
}

export function compareReleaseVersions(left, right) {
  const parsedLeft = parseReleaseVersion(left);
  const parsedRight = parseReleaseVersion(right);
  const leftCore = parsedLeft.coreVersion.split('.');
  const rightCore = parsedRight.coreVersion.split('.');

  for (let index = 0; index < leftCore.length; index += 1) {
    const comparison = compareNumericIdentifiers(leftCore[index], rightCore[index]);
    if (comparison !== 0) {
      return comparison;
    }
  }

  if (parsedLeft.isBeta === parsedRight.isBeta) {
    return parsedLeft.isBeta
      ? compareNumericIdentifiers(parsedLeft.betaNumber, parsedRight.betaNumber)
      : 0;
  }

  return parsedLeft.isBeta ? -1 : 1;
}

export function mapNativePackageVersion(version) {
  const parsed = parseReleaseVersion(version);
  if (!parsed.isBeta) {
    return {
      debVersion: parsed.coreVersion,
      rpmVersion: parsed.coreVersion,
      rpmRelease: '1',
    };
  }

  return {
    debVersion: `${parsed.coreVersion}~beta.${parsed.betaNumber}`,
    rpmVersion: parsed.coreVersion,
    rpmRelease: `0.beta.${parsed.betaNumber}`,
  };
}

export function releaseMetadata(version) {
  const parsed = parseReleaseVersion(version);
  const native = mapNativePackageVersion(version);
  return {
    version: parsed.version,
    is_beta: String(parsed.isBeta),
    updater_channel: parsed.isBeta ? 'beta' : 'latest',
    manifest_channels: parsed.isBeta ? 'beta' : 'latest beta',
    deb_version: native.debVersion,
    rpm_version: native.rpmVersion,
    rpm_release: native.rpmRelease,
  };
}

function main(argv) {
  const [version, option, outputPath] = argv;
  const usage = () => {
    console.error('usage: release-version.mjs <version> [--package | --github-output <path>]');
    process.exit(1);
  };

  if (!version) {
    usage();
  }
  if (option !== undefined && option !== '--package' && (option !== '--github-output' || !outputPath)) {
    usage();
  }

  try {
    if (option === '--package') {
      console.log(parsePackageVersion(version).version);
      return;
    }

    const metadata = releaseMetadata(version);
    if (option === '--github-output') {
      appendFileSync(outputPath, `${Object.entries(metadata).map(([key, value]) => `${key}=${value}`).join('\n')}\n`);
      console.log(metadata.version);
    } else {
      console.log(JSON.stringify(metadata));
    }
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
