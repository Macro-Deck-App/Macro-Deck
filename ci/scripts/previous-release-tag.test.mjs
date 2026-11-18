import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

import { previousReleaseTag } from './previous-release-tag.mjs';

const scriptPath = fileURLToPath(new URL('./previous-release-tag.mjs', import.meta.url));

// The tag list as it stood when 3.0.3 was released: a beta tagged after 3.0.2 made GitHub compare against 3.0.1.
const tags = [
  'v3.1.0-beta.2',
  'v3.0.2',
  'v3.1.0-beta.1',
  'v3.0.1',
  'v3.0.0',
  'v3.0.0-beta.20',
  'v3.0.0-beta.9'
];

test('a stable release is compared with the newest earlier stable release, never a beta', () => {
  assert.equal(previousReleaseTag('3.0.3', tags), 'v3.0.2');
  assert.equal(previousReleaseTag('3.1.0', tags), 'v3.0.2');
});

test('a beta is compared with the newest earlier beta', () => {
  assert.equal(previousReleaseTag('3.1.0-beta.3', tags), 'v3.1.0-beta.2');
  assert.equal(previousReleaseTag('3.0.0-beta.21', tags), 'v3.0.0-beta.20');
});

test('a rebuild of an already tagged version still compares with the release before it', () => {
  assert.equal(previousReleaseTag('3.0.2', tags), 'v3.0.1');
});

test('no earlier release of the channel yields none, and foreign tags are ignored', () => {
  assert.equal(previousReleaseTag('3.0.0', ['v3.0.0-beta.20', 'nightly', 'v2.14.1-b3', 'v3.0.0']), null);
});

test('reads tags from stdin and prints an empty line when there is no previous release', () => {
  assert.equal(execFileSync(process.execPath, [scriptPath, '3.0.3'], { input: tags.join('\n') }).toString(), 'v3.0.2\n');
  assert.equal(execFileSync(process.execPath, [scriptPath, '3.0.0'], { input: '' }).toString(), '\n');
});
