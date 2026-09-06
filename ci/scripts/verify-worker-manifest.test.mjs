import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';

import { verifyWorkerManifest } from './verify-worker-manifest.mjs';

function fingerprint(content) {
	return createHash('sha256').update(content).digest('hex').slice(0, 16);
}

/** A built client, with whatever the caller wants the manifest to claim about it. */
async function dist({ files = { 'index.html': 'shell' }, assets, version } = {}) {
	const directory = await mkdtemp(path.join(tmpdir(), 'worker-manifest-'));
	for (const [name, content] of Object.entries(files)) {
		await mkdir(path.dirname(path.join(directory, name)), { recursive: true });
		await writeFile(path.join(directory, name), content);
	}

	const listed = assets ?? Object.keys(files).sort();
	const recorded = version ?? fingerprint(listed
		.map(asset => asset + ':' + fingerprint(files[asset] ?? ''))
		.join('\n'));

	await writeFile(
		path.join(directory, 'macro-deck-worker.js'),
		`var MANIFEST = ${JSON.stringify({ version: recorded, assets: listed })};\n`);
	return directory;
}

test('a manifest that describes what actually shipped passes', async () => {
	const directory = await dist({ files: { 'index.html': 'shell', 'main-A.js': 'code' } });

	assert.deepEqual(await verifyWorkerManifest(directory), []);
});

test('an asset rewritten after the manifest was recorded is caught', async () => {
	const directory = await dist({ files: { 'index.html': 'shell' } });
	// Exactly what a step appended after build.mjs would do - and what leaves the worker failing
	// against its own precache, disabling the PWA with no build error at all.
	await writeFile(path.join(directory, 'index.html'), 'shell, rewritten afterwards');

	const problems = await verifyWorkerManifest(directory);
	assert.equal(problems.length, 1);
	assert.match(problems[0], /rewrote an asset after/);
});

test('a precached file that is not there is caught', async () => {
	const directory = await dist({ files: { 'index.html': 'shell' }, assets: ['index.html', 'gone.js'] });

	const problems = await verifyWorkerManifest(directory);
	assert.ok(problems.some(problem => /gone\.js is precached but missing/.test(problem)));
});

test('a worker that precaches itself is caught', async () => {
	const directory = await dist({
		files: { 'index.html': 'shell' },
		assets: ['index.html', 'macro-deck-worker.js'],
	});

	const problems = await verifyWorkerManifest(directory);
	assert.ok(problems.some(problem => /precaches itself/.test(problem)),
		'a worker inside its own cache is how a broken worker becomes permanent');
});

test('a build with no worker at all is caught', async () => {
	const directory = await mkdtemp(path.join(tmpdir(), 'worker-manifest-'));

	const problems = await verifyWorkerManifest(directory);
	assert.equal(problems.length, 1);
	assert.match(problems[0], /missing/);
});

test('a worker carrying no manifest is caught', async () => {
	const directory = await mkdtemp(path.join(tmpdir(), 'worker-manifest-'));
	await writeFile(path.join(directory, 'macro-deck-worker.js'), 'self.addEventListener("fetch", () => {});');

	const problems = await verifyWorkerManifest(directory);
	assert.match(problems[0], /carries no MANIFEST/);
});
