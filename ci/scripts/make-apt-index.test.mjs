import assert from 'node:assert/strict';
import test from 'node:test';

import { aptSuitesFor, mergePackages, parseStanzas, poolPath } from './make-apt-index.mjs';

const stanza = (version, sha256 = 'a'.repeat(64)) => `Package: macro-deck
Version: ${version}
Architecture: amd64
Maintainer: Manuel Mayer <info@manuel-mayer.dev>
Filename: ${poolPath('macro-deck', version, 'amd64')}
Size: 1234
SHA256: ${sha256}
Description: Turn a phone, tablet or browser into a control surface for your PC
 Macro Deck is the open-source macro pad.
 .
 It runs on Windows, macOS and Linux.
`;

const versionsIn = (index) => parseStanzas(index).map((entry) => entry.fields.get('version'));

test('a prerelease is published to the beta suite only', () => {
  assert.deepEqual(aptSuitesFor('3.0.0-beta.3'), ['beta']);
});

test('a stable release is published to both suites so beta installations graduate', () => {
  assert.deepEqual(aptSuitesFor('3.0.0'), ['stable', 'beta']);
});

test('pool paths follow the Debian layout and never contain a tilde', () => {
  assert.equal(
    poolPath('macro-deck', '3.0.0~beta.3', 'amd64'),
    'pool/main/m/macro-deck/macro-deck_3.0.0.beta.3_amd64.deb',
  );
});

test('the first version starts an empty index', () => {
  const index = mergePackages('', stanza('3.0.0~beta.3'));
  assert.deepEqual(versionsIn(index), ['3.0.0~beta.3']);
  assert.equal(index, stanza('3.0.0~beta.3'));
});

test('a new version keeps every published version and its full stanza', () => {
  const published = mergePackages(mergePackages('', stanza('3.0.0~beta.2')), stanza('3.0.0~beta.3', 'b'.repeat(64)));
  const index = mergePackages(published, stanza('3.0.0', 'c'.repeat(64)));

  assert.deepEqual(versionsIn(index), ['3.0.0~beta.2', '3.0.0~beta.3', '3.0.0']);
  assert.ok(index.startsWith(published.trimEnd()));
  assert.ok(index.includes(' Macro Deck is the open-source macro pad.\n .\n It runs on Windows, macOS and Linux.'));
});

test('publishing the same file again leaves the index unchanged', () => {
  const index = mergePackages(mergePackages('', stanza('3.0.0~beta.2')), stanza('3.0.0~beta.3'));
  assert.equal(mergePackages(index, stanza('3.0.0~beta.3')), index);
});

test('a published version cannot be replaced with a different file', () => {
  const index = mergePackages('', stanza('3.0.0~beta.3'));
  assert.throws(() => mergePackages(index, stanza('3.0.0~beta.3', 'f'.repeat(64))), /different SHA256/);
});

test('a stanza without a hash is never published', () => {
  const withoutHash = stanza('3.0.0').replace(/^SHA256: .*\n/m, '');
  assert.throws(() => mergePackages('', withoutHash), /no sha256 field/);
});
