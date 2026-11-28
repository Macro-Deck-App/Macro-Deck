import { test } from 'node:test';
import assert from 'node:assert/strict';

import { releaseAnnouncement } from './discord-release-announcement.mjs';

const repository = 'Macro-Deck-App/Macro-Deck';
const releaseUrl = `https://github.com/${repository}/releases/tag/v3.1.0`;
const pull = (number) => `https://github.com/${repository}/pull/${number}`;
const notes = [
  '## What\'s Changed',
  '### 🎉 Exciting New Features',
  `* Let the Folders panel collapse by @someone in ${pull(1)}`,
  '### 🐞 Bug Fixes',
  `* Fix a crash by @someone in ${pull(2)}`,
  `* Fix another crash by @someone in ${pull(3)}`
].join('\n');
const bugFixNotes = `## What's Changed\n### 🐞 Bug Fixes\n* Fix a crash by @someone in ${pull(2)}`;

function services(reply, stopReason = 'end_turn') {
  const claudeRequests = [];
  const pullRequests = [];
  const fetchImpl = async (url, init) => {
    if (url.startsWith('https://api.github.com/')) {
      pullRequests.push(url);
      return new Response(JSON.stringify({ title: 'Collapse folders', body: 'The panel folds to a narrow rail.' }));
    }
    claudeRequests.push(JSON.parse(init.body));
    return new Response(JSON.stringify({ stop_reason: stopReason, content: [{ type: 'text', text: reply }] }));
  };
  return { claudeRequests, pullRequests, fetchImpl };
}

async function announce(version, fetchImpl, { apiKey = 'test-key', releaseNotes = notes } = {}) {
  const github = { repository, token: 'github-token' };
  const payload = await releaseAnnouncement(version, releaseUrl, releaseNotes, apiKey, github, fetchImpl);
  return payload.embeds[0];
}

test('a stable release is announced in green and a pre-release in orange', async () => {
  const { fetchImpl } = services('📁 The Folders panel can be collapsed');

  const stable = await announce('3.1.0', fetchImpl);
  const beta = await announce('3.1.0-beta.4', fetchImpl);

  assert.equal(stable.color, 0x3fb950);
  assert.equal(beta.color, 0xd29922);
  assert.equal(stable.title, 'Macro Deck 3.1.0 released');
  assert.equal(beta.title, '[Pre-Release] Macro Deck 3.1.0-beta.4 released');
  assert.equal(stable.url, releaseUrl);
});

test('highlights are followed by one line for the bug fixes and a link to the changelog', async () => {
  const { fetchImpl } = services('📁 The Folders panel can be collapsed\n🎚️ Sliders react to presses');

  const embed = await announce('3.1.0', fetchImpl);

  assert.equal(
    embed.description,
    '**Highlights**\n📁 The Folders panel can be collapsed\n🎚️ Sliders react to presses\n🐞 Plus 2 bug fixes\n\n'
    + `[Read the full changelog for v3.1.0](${releaseUrl})`
  );
});

test('Claude gets the release notes and the descriptions of the pull requests that can be a highlight', async () => {
  const { claudeRequests, pullRequests, fetchImpl } = services('📁 The Folders panel can be collapsed');

  await announce('3.1.0-beta.4', fetchImpl);

  const [request] = claudeRequests;
  assert.equal(request.model, 'claude-sonnet-5-5');
  assert.match(request.messages[0].content, /3\.1\.0-beta\.4 \(pre-release\)[\s\S]*Let the Folders panel collapse/);
  assert.match(request.messages[0].content, /The panel folds to a narrow rail\./);
  assert.deepEqual(pullRequests, [`https://api.github.com/repos/${repository}/pulls/1`]);
});

test('only plain highlight lines reach Discord', async () => {
  const { fetchImpl } = services(
    'Here are the highlights:\n📁 The Folders panel can be collapsed\n🙏 Thanks @everyone\n🔗 See https://example.invalid'
  );

  const embed = await announce('3.1.0', fetchImpl);

  assert.match(embed.description, /📁 The Folders panel can be collapsed/);
  assert.doesNotMatch(embed.description, /Here are|@everyone|example\.invalid/);
});

test('a release with nothing but bug fixes is announced without asking Claude', async () => {
  const neverCalled = async () => assert.fail('nothing to summarise');

  const embed = await announce('3.0.5', neverCalled, { releaseNotes: bugFixNotes });

  assert.equal(
    embed.description,
    `A new Macro Deck release is available.\n\n[Read the full changelog for v3.0.5](${releaseUrl})`
  );
});

test('the release is still announced when no summary is available', async () => {
  const failing = async () => new Response('overloaded', { status: 529 });
  const unreachable = async () => {
    throw new Error('network down');
  };
  const neverCalled = async () => assert.fail('no request without a key');

  for (const embed of [
    await announce('3.1.0', failing),
    await announce('3.1.0', unreachable),
    await announce('3.1.0', services('', 'refusal').fetchImpl),
    await announce('3.1.0', services('NONE').fetchImpl),
    await announce('3.1.0', neverCalled, { apiKey: '' })
  ]) {
    assert.equal(
      embed.description,
      `A new Macro Deck release is available.\n\n[Read the full changelog for v3.1.0](${releaseUrl})`
    );
    assert.equal(embed.color, 0x3fb950);
  }
});
