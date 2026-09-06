import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { META_NAME, injectCommitMeta, resolveCommit, stampDirectory } from './stamp-ui-commit.mjs';

const shell = ['<!doctype html>', '<html lang="en">', '<head>', '  <meta charset="utf-8">',
  '  <title>UI</title>', '</head>', '<body></body>', '</html>'].join('\n');

test('prefers the explicit override over the CI sha and the working copy', () => {
  const env = { MACRO_DECK_UI_COMMIT: 'aaaaaaaaaaaa', GITHUB_SHA: 'bbbbbbbbbbbb' };

  assert.equal(resolveCommit(env, () => 'cccccccccccc'), 'aaaaaaa');
});

test('falls back to the CI sha, then to the working copy', () => {
  assert.equal(resolveCommit({ GITHUB_SHA: 'bbbbbbbbbbbb' }, () => 'cccccccccccc'), 'bbbbbbb');
  assert.equal(resolveCommit({}, () => 'cccccccccccc\n'), 'ccccccc');
});

test('reports no commit when nothing resolves one', () => {
  assert.equal(resolveCommit({}, () => ''), null);
  assert.equal(resolveCommit({ MACRO_DECK_UI_COMMIT: '  ' }, () => ''), null);
});

test('inserts the meta tag once, behind the charset so it stays in the first bytes', () => {
  const stamped = injectCommitMeta(shell, 'abc1234');

  assert.match(
    stamped,
    /<meta charset="utf-8">\n {2}<meta name="macro-deck-ui-commit" content="abc1234">\n {2}<title>/
  );
  assert.equal(stamped.match(new RegExp(META_NAME, 'g')).length, 1);
  assert.match(stamped, /<title>UI<\/title>/);
});

test('falls back to the head tag when there is no charset meta', () => {
  const stamped = injectCommitMeta('<head>\n  <title>UI</title>\n</head>', 'abc1234');

  assert.match(stamped, /<head>\n {2}<meta name="macro-deck-ui-commit" content="abc1234">\n {2}<title>/);
});

test('replaces an existing tag instead of adding a second one', () => {
  const stamped = injectCommitMeta(injectCommitMeta(shell, 'abc1234'), 'def5678');

  assert.equal(stamped.match(new RegExp(META_NAME, 'g')).length, 1);
  assert.match(stamped, /content="def5678"/);
});

test('leaves a shell without a head tag untouched', () => {
  assert.equal(injectCommitMeta('<body></body>', 'abc1234'), '<body></body>');
});

test('stamps every index shell in the dist tree', async () => {
  const dist = mkdtempSync(join(tmpdir(), 'stamp-ui-commit-'));
  try {
    mkdirSync(join(dist, 'web-client', 'browser', 'targets', 'carthing'), { recursive: true });
    writeFileSync(join(dist, 'web-client', 'browser', 'index.html'), shell);
    // A device target ships its own shell (issue #727), which is the second one this must reach.
    writeFileSync(join(dist, 'web-client', 'browser', 'targets', 'carthing', 'index.html'), shell);
    writeFileSync(join(dist, 'web-client', 'browser', 'main.js'), 'console.log(1);');

    const stamped = await stampDirectory(dist, 'abc1234');

    assert.equal(stamped.length, 2);
    for (const file of stamped) {
      assert.match(readFileSync(file, 'utf8'), /content="abc1234"/);
    }
    assert.equal(readFileSync(join(dist, 'web-client', 'browser', 'main.js'), 'utf8'), 'console.log(1);');
  } finally {
    rmSync(dist, { recursive: true, force: true });
  }
});
