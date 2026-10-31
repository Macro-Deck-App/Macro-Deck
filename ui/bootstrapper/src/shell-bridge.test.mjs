import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { JSDOM, VirtualConsole } from 'jsdom';

const script = readFileSync(new URL('./shell-bridge.js', import.meta.url), 'utf8');
const IPC_URL = 'http://ipc.localhost/install_update';

function load(answer) {
  const dom = new JSDOM('<!doctype html><body></body>', {
    url: 'http://127.0.0.1:5291/',
    runScripts: 'outside-only',
    virtualConsole: new VirtualConsole(),
  });
  const { window } = dom;
  const calls = [];
  window.fetch = function (input, init) {
    calls.push({ self: this, input, init });
    return Promise.resolve(answer);
  };
  window.__TAURI_INTERNALS__ = {
    convertFileSrc: (path, protocol) => `http://${protocol}.localhost/${path}`,
  };
  window.eval(script);
  return { window, calls };
}

function refused() {
  return new Response('missing Origin header', { status: 500, headers: { 'Content-Type': 'text/plain' } });
}

test('a request Tauri refused before running a command rejects so its postMessage fallback runs', async () => {
  const { window } = load(refused());
  await assert.rejects(window.fetch(IPC_URL, { method: 'POST' }));
});

test('a command error stays a response for the caller', async () => {
  const answer = new Response('"no update"', { status: 200, headers: { 'Tauri-Response': 'error' } });
  const { window } = load(answer);
  const response = await window.fetch(IPC_URL, { method: 'POST' });
  assert.equal(response, answer);
  assert.equal(await response.text(), '"no update"');
});

test('a successful command stays a response for the caller', async () => {
  const answer = new Response('{}', { status: 200, headers: { 'Tauri-Response': 'ok' } });
  const { window } = load(answer);
  assert.equal(await window.fetch(IPC_URL, { method: 'POST' }), answer);
});

test('page requests are left alone, failed or not', async () => {
  const answer = refused();
  const { window, calls } = load(answer);
  const init = { method: 'POST' };
  const request = new Request('http://127.0.0.1:5291/api/thing');

  assert.equal(await window.fetch('/api/thing', init), answer);
  assert.equal(await window.fetch(new URL(IPC_URL)), answer);
  assert.equal(await window.fetch(request), answer);
  assert.equal(calls[0].input, '/api/thing');
  assert.equal(calls[0].init, init);
  assert.equal(calls[0].self, window);
  assert.equal(calls[2].input, request);
  assert.equal(answer.bodyUsed, false);
});
