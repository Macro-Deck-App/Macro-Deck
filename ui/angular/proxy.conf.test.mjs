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

function proxyTargets(relativePath) {
	const config = JSON.parse(read(relativePath));
	return Object.entries(config).map(([context, entry]) => [context, entry.target]);
}

test('the desktop proxy targets the host loopback port', () => {
	for (const [context, target] of proxyTargets('ui/angular/proxy.conf.json')) {
		assert.equal(target, `http://127.0.0.1:${loopbackPort}`, `unexpected target for ${context}`);
	}
});

