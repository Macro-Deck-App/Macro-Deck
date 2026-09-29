import { strict as assert } from 'node:assert';
import { createHash } from 'node:crypto';
import { existsSync, mkdtempSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, test } from 'node:test';

import { TOOL_SETS, fetchTools, selectTools } from './pinned-tools.mjs';

const TOOL = Buffer.from('#!/bin/sh\necho pinned tool\n');
const TOOL_SHA256 = createHash('sha256').update(TOOL).digest('hex');
const OTHER_SHA256 = createHash('sha256').update('something else').digest('hex');

let server;
let baseUrl;
let requests = 0;
let flakyRequests = 0;

before(async () => {
	server = createServer((request, response) => {
		requests++;
		if (request.url === '/tool') {
			response.end(TOOL);
		} else if (request.url === '/flaky' && flakyRequests++ === 0) {
			response.writeHead(200, { 'content-length': TOOL.length });
			response.write(TOOL.subarray(0, 5));
			response.socket.destroy();
		} else if (request.url === '/flaky') {
			response.end(TOOL);
		} else {
			response.statusCode = 404;
			response.end();
		}
	});
	await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
	baseUrl = `http://127.0.0.1:${server.address().port}`;
});

after(() => server.close());

function entry(overrides = {}) {
	return { file: 'tool', url: `${baseUrl}/tool`, sha256: TOOL_SHA256, executable: true, ...overrides };
}

test('a download matching its pin is installed and executable', async () => {
	const dir = mkdtempSync(join(tmpdir(), 'pinned-'));
	await fetchTools([entry()], dir);
	assert.deepEqual(readFileSync(join(dir, 'tool')), TOOL);
	assert.ok(process.platform === 'win32' || (statSync(join(dir, 'tool')).mode & 0o111) !== 0);
});

test('a download that does not match its pin fails and leaves nothing behind', async () => {
	const dir = mkdtempSync(join(tmpdir(), 'pinned-'));
	await assert.rejects(fetchTools([entry({ sha256: OTHER_SHA256 })], dir), /expected/);
	assert.deepEqual(readdirSync(dir), []);
});

test('a transfer that breaks midway is retried and still verified', async () => {
	const dir = mkdtempSync(join(tmpdir(), 'pinned-'));
	await fetchTools([entry({ url: `${baseUrl}/flaky` })], dir, { retryDelayMs: 1 });
	assert.deepEqual(readFileSync(join(dir, 'tool')), TOOL);
	assert.deepEqual(readdirSync(dir), ['tool']);
});

test('a failed download fails and leaves nothing behind', async () => {
	const dir = mkdtempSync(join(tmpdir(), 'pinned-'));
	await assert.rejects(fetchTools([entry({ url: `${baseUrl}/missing` })], dir), /HTTP 404/);
	assert.deepEqual(readdirSync(dir), []);
});

test('an existing file with the wrong hash is refused and left untouched', async () => {
	const dir = mkdtempSync(join(tmpdir(), 'pinned-'));
	writeFileSync(join(dir, 'tool'), 'tampered');
	const before = requests;
	await assert.rejects(fetchTools([entry()], dir), /refusing/);
	assert.equal(readFileSync(join(dir, 'tool'), 'utf8'), 'tampered');
	assert.equal(requests, before);
});

test('an existing file with the right hash is used without a download', async () => {
	const dir = mkdtempSync(join(tmpdir(), 'pinned-'));
	writeFileSync(join(dir, 'tool'), TOOL);
	const before = requests;
	await fetchTools([entry({ url: `${baseUrl}/missing` })], dir);
	assert.equal(requests, before);
	assert.ok(existsSync(join(dir, 'tool')));
});

test('every pin names an https source outside a moving branch or channel, and a sha256', () => {
	for (const [set, entries] of Object.entries(TOOL_SETS)) {
		for (const { file, url, sha256 } of entries) {
			const where = `${set}/${file}`;
			assert.match(url, /^https:\/\//, where);
			assert.match(sha256, /^[0-9a-f]{64}$/, where);
			assert.doesNotMatch(url, /\/(master|main|continuous|latest)\//, where);
		}
	}
});

test('selecting single files keeps their pins and rejects unknown names', () => {
	const [plugin] = selectTools('linuxdeploy-x86_64', ['linuxdeploy-plugin-appimage.AppImage']);
	assert.equal(plugin.file, 'linuxdeploy-plugin-appimage.AppImage');
	assert.throws(() => selectTools('linuxdeploy-x86_64', ['unknown']), /not part of/);
	assert.throws(() => selectTools('unknown'), /unknown tool set/);
});
