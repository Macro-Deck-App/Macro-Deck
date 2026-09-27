import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Scratch ports from local test runs have been committed into the dev proxy twice (#340, #422),
// which silently breaks `ng serve` for everyone else. The expected ports are read from the host
// sources so this check follows a deliberate port change instead of blocking it.
const repoRoot = new URL('../../', import.meta.url);

function read(relativePath) {
	return readFileSync(fileURLToPath(new URL(relativePath, repoRoot)), 'utf8');
}

function match(relativePath, pattern) {
	const found = read(relativePath).match(pattern);
	assert.ok(found, `expected ${pattern} to match in ${relativePath}`);
	return Number(found[1]);
}

const loopbackPort = match(
	'host/src/MacroDeckHost/HostEndpoints.cs',
	/DevelopmentLoopbackPort\s*=\s*(\d+)/
);

const { default: config, loopbackSecret, proxyConfig, SECRET_HEADER } = await import('./proxy.conf.mjs');

test('the desktop proxy targets the host loopback port', () => {
	for (const [context, entry] of Object.entries(config)) {
		assert.equal(entry.target, `http://127.0.0.1:${loopbackPort}`, `unexpected target for ${context}`);
		assert.equal(entry.changeOrigin, undefined, `${context} must keep the loopback Host header`);
	}
});

test('proxied HTTP and WebSocket requests carry the loopback secret the dev host wrote', () => {
	const env = { MACRODECK_LOOPBACK_SECRET: 'ab'.repeat(32) };
	const handlers = {};
	proxyConfig('http://127.0.0.1:1', env)['/api'].configure({ on: (event, handler) => { handlers[event] = handler; } });

	for (const event of ['proxyReq', 'proxyReqWs']) {
		const headers = {};
		handlers[event]({ setHeader: (name, value) => { headers[name] = value; } });
		assert.equal(headers[SECRET_HEADER], env.MACRODECK_LOOPBACK_SECRET, event);
	}
});

test('without a secret the proxy forwards requests untouched', () => {
	assert.equal(loopbackSecret({ MACRODECK_LOOPBACK_SECRET_FILE: '/nonexistent/loopback-secret' }), null);
});
