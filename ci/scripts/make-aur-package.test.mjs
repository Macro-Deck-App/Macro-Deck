import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  ARCH_DEPENDS,
  BETA_PACKAGE,
  STABLE_PACKAGE,
  aurPackagesFor,
  findReleaseAsset,
  maintainerFrom,
  nextPkgrel,
  parseChecksumSidecar,
  renderPkgbuild,
  shouldPush,
} from './make-aur-package.mjs';
import { mapNativePackageVersion } from './release-version.mjs';

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const FIXTURES = join(REPO_ROOT, 'ci', 'aur', 'fixtures');

const DEBIAN_TO_ARCH = new Map([
  ['libc6', 'glibc'],
  ['libgtk-3-0', 'gtk3'],
  ['libgtk-3-0t64', 'gtk3'],
  ['libwebkit2gtk-4.1-0', 'webkit2gtk-4.1'],
  ['libayatana-appindicator3-1', 'libayatana-appindicator'],
  ['libxdo3', 'xdotool'],
  ['libstdc++6', 'gcc-libs'],
  ['zlib1g', 'zlib'],
  ['libssl3', 'openssl'],
  ['libssl3t64', 'openssl'],
  ['libssl1.1', 'openssl'],
  ['libicu76', 'icu'],
  ['libicu74', 'icu'],
  ['libicu72', 'icu'],
  ['libicu71', 'icu'],
  ['libicu70', 'icu'],
  ['shared-mime-info', 'shared-mime-info'],
  ['desktop-file-utils', 'desktop-file-utils'],
]);

const IMPLIED_BY_BASE = ['glibc'];

const debianToArchDependency = (entry) => {
  for (const alternative of entry.split('|')) {
    const arch = DEBIAN_TO_ARCH.get(alternative.replace(/\(.*\)/, '').trim());
    if (arch !== undefined) {
      return arch;
    }
  }
  return null;
};

const spec = (overrides = {}) => ({
  pkgname: BETA_PACKAGE,
  pkgver: '3.0.0beta.2',
  pkgrel: 1,
  pkgdesc: 'Turn a phone, tablet or browser into a control surface for your PC',
  url: 'https://macro-deck.app',
  license: 'Apache-2.0',
  maintainer: 'Manuel Mayer <info@manuel-mayer.dev>',
  assetName: 'macro-deck_3.0.0.beta.2_amd64.deb',
  assetUrl: 'https://example.invalid/macro-deck_3.0.0.beta.2_amd64.deb',
  sha256: '83732468f0380c6808ec498c0a38d99bf4738d5ba1f5aa2f475bbefd54b20a30',
  ...overrides,
});

test('a beta release feeds only the beta package, a stable release feeds both', () => {
  assert.deepEqual(aurPackagesFor('3.0.0-beta.3'), [BETA_PACKAGE]);
  assert.deepEqual(aurPackagesFor('3.0.1'), [STABLE_PACKAGE, BETA_PACKAGE]);
});

test('a pkgver never carries the hyphen pacman reserves for pkgrel', () => {
  assert.equal(mapNativePackageVersion('3.0.0-beta.3').aurVersion, '3.0.0beta.3');
  assert.equal(mapNativePackageVersion('3.0.1').aurVersion, '3.0.1');
  for (const version of ['3.0.0-beta.1', '3.0.0-beta.42', '3.0.0', '10.20.30']) {
    assert.ok(!mapNativePackageVersion(version).aurVersion.includes('-'), version);
  }
});

test('no release replaces a published package with an older version', () => {
  for (const pkgname of [STABLE_PACKAGE, BETA_PACKAGE]) {
    assert.equal(shouldPush({ pkgname, version: '3.0.2', vercmp: -1 }), false);
    assert.equal(shouldPush({ pkgname, version: '3.0.2', vercmp: 1 }), true);
    assert.equal(shouldPush({ pkgname, version: '3.0.2', vercmp: 0 }), true);
  }
});

test('a prerelease never reaches the stable package', () => {
  assert.equal(shouldPush({ pkgname: STABLE_PACKAGE, version: '3.0.0-beta.3', vercmp: 1 }), false);
  assert.equal(shouldPush({ pkgname: BETA_PACKAGE, version: '3.0.0-beta.3', vercmp: 1 }), true);
});

test('pkgrel restarts on a new version, bumps for a repackage, and holds when nothing changed', () => {
  const published = 'pkgname=macro-deck-bin\npkgver=3.0.1\npkgrel=2\ndepends=(gtk3)\n';
  assert.equal(
    nextPkgrel({ currentPkgbuild: published, renderedBody: 'a', currentBody: 'b', newPkgver: '3.0.2' }),
    1,
  );
  assert.equal(
    nextPkgrel({ currentPkgbuild: published, renderedBody: 'a', currentBody: 'b', newPkgver: '3.0.1' }),
    3,
  );
  assert.equal(
    nextPkgrel({ currentPkgbuild: published, renderedBody: 'a', currentBody: 'a', newPkgver: '3.0.1' }),
    2,
  );
  assert.equal(
    nextPkgrel({ currentPkgbuild: null, renderedBody: 'a', currentBody: null, newPkgver: '3.0.1' }),
    1,
  );
});

test('the rendered PKGBUILD carries what an AUR helper needs to build and replace', () => {
  const pkgbuild = renderPkgbuild(spec());
  assert.match(pkgbuild, /^pkgname=macro-deck-beta-bin$/m);
  assert.match(pkgbuild, /^pkgver=3\.0\.0beta\.2$/m);
  assert.match(pkgbuild, /^arch=\('x86_64'\)$/m);
  assert.match(pkgbuild, /^url="https:\/\/macro-deck\.app"$/m);
  assert.match(pkgbuild, /^license=\('Apache-2\.0'\)$/m);
  assert.match(pkgbuild, /^pkgdesc="Turn a phone, tablet or browser into a control surface for your PC \(prerelease\)"$/m);
  assert.match(pkgbuild, /^# Maintainer: Manuel Mayer <info@manuel-mayer\.dev>$/m);
  assert.match(pkgbuild, /^provides=\("macro-deck=\$pkgver"\)$/m);
  assert.match(pkgbuild, /^conflicts=\('macro-deck'\)$/m);
  assert.match(pkgbuild, /^sha256sums=\('83732468f0380c6808ec498c0a38d99bf4738d5ba1f5aa2f475bbefd54b20a30'\)$/m);
  assert.match(pkgbuild, /^source=\("macro-deck_3\.0\.0\.beta\.2_amd64\.deb::https:\/\/example\.invalid\//m);
  assert.match(pkgbuild, /^noextract=\("macro-deck_3\.0\.0\.beta\.2_amd64\.deb"\)$/m);
});

test('the extraction cannot report success after packaging nothing', () => {
  const pkgbuild = renderPkgbuild(spec());
  assert.match(pkgbuild, /set -o pipefail/);
  assert.match(pkgbuild, /\[\[ -f "\$pkgdir\/usr\/bin\/MacroDeck" \]\]/);
});

test('the prerelease package says so, and is otherwise the stable one', () => {
  const beta = renderPkgbuild(spec());
  const stable = renderPkgbuild(spec({ pkgname: STABLE_PACKAGE }));
  assert.match(stable, /^pkgdesc="Turn a phone, tablet or browser into a control surface for your PC"$/m);
  const strip = (text) => text.replace(/^pkgname=.*$/m, '').replace(/ \(prerelease\)/, '');
  assert.equal(strip(beta), strip(stable));
});

test('the source name is the asset GitHub serves, not the one the checksum sidecar records', () => {
  const { sha256, stagedName } = parseChecksumSidecar(
    readFileSync(join(FIXTURES, 'macro-deck.deb.sha256'), 'utf8'),
  );
  assert.equal(stagedName, 'macro-deck_3.0.0~beta.2_amd64.deb');
  assert.equal(sha256, '83732468f0380c6808ec498c0a38d99bf4738d5ba1f5aa2f475bbefd54b20a30');

  const { assets } = JSON.parse(readFileSync(join(FIXTURES, 'assets.json'), 'utf8'));
  const asset = findReleaseAsset(assets, stagedName);
  assert.equal(asset.name, 'macro-deck_3.0.0.beta.2_amd64.deb');
  assert.match(asset.url, /^https:\/\/github\.com\/Macro-Deck-App\/Macro-Deck\/releases\/download\//);
});

test('an asset the release does not carry is an error, never a rendered guess', () => {
  assert.throws(
    () => findReleaseAsset([{ name: 'other.deb', url: 'https://example.invalid/other.deb' }], 'macro-deck.deb'),
    /carries no asset/,
  );
});

test('every dependency the DEB declares reaches the Arch package', () => {
  const config = JSON.parse(
    readFileSync(join(REPO_ROOT, 'ui', 'bootstrapper', 'tauri.linux.conf.json'), 'utf8'),
  );
  const mapped = new Set();
  for (const entry of config.bundle.linux.deb.depends) {
    const arch = debianToArchDependency(entry);
    assert.notEqual(arch, null, `no Arch package known for "${entry}"`);
    mapped.add(arch);
  }
  for (const arch of mapped) {
    assert.ok(
      ARCH_DEPENDS.includes(arch) || IMPLIED_BY_BASE.includes(arch),
      `"${arch}" is mapped but missing from the rendered depends`,
    );
  }
});

test('the maintainer is the bootstrapper crate author', () => {
  const cargoToml = readFileSync(join(REPO_ROOT, 'ui', 'bootstrapper', 'Cargo.toml'), 'utf8');
  assert.equal(maintainerFrom(cargoToml), 'Manuel Mayer <info@manuel-mayer.dev>');
});
