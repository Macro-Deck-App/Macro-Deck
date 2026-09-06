import { test } from 'node:test';
import assert from 'node:assert/strict';

import { PACKAGES_WITHOUT_SYMBOLS, PACKAGE_IDS, checkPackageSet } from './verify-nuget-packages.mjs';

const EXPECTED_IDS = [
  'MacroDeck.Sdk',
  'MacroDeck.Plugin.Protocol',
  'MacroDeck.Plugin.Packaging',
  'MacroDeck.Plugin.Hosting',
  'MacroDeck.Plugin.Serilog',
  'MacroDeck.Plugin.Testing',
  'MacroDeck.Plugin.Analyzers',
  'MacroDeck.Plugin.Cli',
  'MacroDeck.Signing',
  'MacroDeck.Ui',
  'MacroDeck.Ui.Model',
  'MacroDeck.Ui.Testing',
  'MacroDeck.Localization',
];

const EXPECTED_IDS_WITHOUT_SYMBOLS = ['MacroDeck.Plugin.Analyzers'];

function packOutput(version, { without = [], extra = [] } = {}) {
  const files = [];
  for (const id of EXPECTED_IDS) {
    files.push(`${id}.${version}.nupkg`);
    if (!EXPECTED_IDS_WITHOUT_SYMBOLS.includes(id)) {
      files.push(`${id}.${version}.snupkg`);
    }
  }
  return [...files.filter((name) => !without.includes(name)), ...extra];
}

test('the published family is exactly the thirteen documented package ids', () => {
  assert.deepEqual([...PACKAGE_IDS].sort(), [...EXPECTED_IDS].sort());
});

test('only the analyzer package is excused from shipping symbols', () => {
  assert.deepEqual([...PACKAGES_WITHOUT_SYMBOLS].sort(), [...EXPECTED_IDS_WITHOUT_SYMBOLS].sort());
});

test('accepts a complete pack output, for stable and prerelease versions alike', () => {
  for (const version of ['3.0.0', '3.0.0-beta.4']) {
    assert.deepEqual(checkPackageSet(packOutput(version), version), []);
  }
});

test('reports a package that was not packed', () => {
  const version = '3.0.0';
  const problems = checkPackageSet(packOutput(version, { without: [`MacroDeck.Sdk.${version}.nupkg`] }), version);

  assert.equal(problems.length, 1);
  assert.match(problems[0], /MacroDeck\.Sdk\.3\.0\.0\.nupkg/);
});

test('reports a package packed at the wrong version', () => {
  const version = '3.0.0-beta.4';
  const problems = checkPackageSet(
    packOutput(version, {
      without: [`MacroDeck.Plugin.Hosting.${version}.nupkg`],
      extra: ['MacroDeck.Plugin.Hosting.3.0.0.nupkg'],
    }),
    version
  );

  // Only the version mismatch is reported: the stale file must not also count as an unexpected extra.
  assert.equal(problems.length, 1);
  assert.match(problems[0], /MacroDeck\.Plugin\.Hosting\.3\.0\.0\.nupkg/);
});

test('reports a package outside the published family', () => {
  const version = '3.0.0';
  const problems = checkPackageSet(packOutput(version, { extra: [`MacroDeckHost.Domain.${version}.nupkg`] }), version);

  assert.equal(problems.length, 1);
  assert.match(problems[0], /MacroDeckHost\.Domain\.3\.0\.0\.nupkg/);
});

test('reports a missing symbol package', () => {
  const version = '3.0.0';
  const problems = checkPackageSet(
    packOutput(version, { without: [`MacroDeck.Plugin.Testing.${version}.snupkg`] }),
    version
  );

  assert.equal(problems.length, 1);
  assert.match(problems[0], /MacroDeck\.Plugin\.Testing\.3\.0\.0\.snupkg/);
});

test('reports a symbol package for a package that ships none by design', () => {
  const version = '3.0.0';
  const problems = checkPackageSet(
    packOutput(version, { extra: [`MacroDeck.Plugin.Analyzers.${version}.snupkg`] }),
    version
  );

  assert.equal(problems.length, 1);
  assert.match(problems[0], /MacroDeck\.Plugin\.Analyzers\.3\.0\.0\.snupkg/);
});

test('ignores files that are neither packages nor symbol packages', () => {
  const version = '3.0.0';
  assert.deepEqual(checkPackageSet(packOutput(version, { extra: ['README.md', 'packages.lock.json'] }), version), []);
});

test('requires an expected version rather than accepting whatever was packed', () => {
  assert.throws(() => checkPackageSet(packOutput('3.0.0'), ''), /version/i);
});
