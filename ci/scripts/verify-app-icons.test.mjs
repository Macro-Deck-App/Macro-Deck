import { strict as assert } from 'node:assert';
import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { findBadAppleTouchIcons } from './verify-app-icons.mjs';

const ICONS_DIR = 'ui/web-client/public/icons';
const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

/** A minimal buffer that is a real PNG signature plus an IHDR chunk - enough for byte-offset reads. */
function pngWithIhdr(width, height, colourType) {
	const data = Buffer.alloc(13);
	data.writeUInt32BE(width, 0);
	data.writeUInt32BE(height, 4);
	data.writeUInt8(8, 8);
	data.writeUInt8(colourType, 9);
	const length = Buffer.alloc(4);
	length.writeUInt32BE(13, 0);
	return Buffer.concat([PNG_SIGNATURE, length, Buffer.from('IHDR'), data]);
}

function fixture(files) {
	const root = mkdtempSync(join(tmpdir(), 'app-icons-'));
	mkdirSync(join(root, ICONS_DIR), { recursive: true });
	for (const [name, buffer] of Object.entries(files)) {
		writeFileSync(join(root, ICONS_DIR, name), buffer);
	}
	return root;
}

test('accepts an opaque, correctly-sized icon', () => {
	const { problems } = findBadAppleTouchIcons(fixture({ 'apple-touch-icon-180.png': pngWithIhdr(180, 180, 2) }));
	assert.deepEqual(problems, []);
});

test('rejects an icon that still carries an alpha channel', () => {
	// This is the exact defect that shipped: a pre-rounded squircle with transparent corners, colour
	// type 6, instead of a full-bleed opaque square for iOS to mask itself.
	const { problems } = findBadAppleTouchIcons(fixture({ 'apple-touch-icon-180.png': pngWithIhdr(180, 180, 6) }));
	assert.equal(problems.length, 1);
	assert.match(problems[0].issue, /colour type 6/);
});

test('rejects an icon whose dimensions do not match its filename', () => {
	const { problems } = findBadAppleTouchIcons(fixture({ 'apple-touch-icon-180.png': pngWithIhdr(192, 192, 2) }));
	assert.equal(problems.length, 1);
	assert.match(problems[0].issue, /192x192/);
});

test('ignores files outside the apple-touch-icon-*.png naming pattern', () => {
	const { checked, problems } = findBadAppleTouchIcons(fixture({ 'icon-192.png': pngWithIhdr(192, 192, 6) }));
	assert.equal(checked, 0);
	assert.deepEqual(problems, []);
});

test('checks every apple-touch-icon file present', () => {
	const { checked, problems } = findBadAppleTouchIcons(fixture({
		'apple-touch-icon-180.png': pngWithIhdr(180, 180, 2),
		'apple-touch-icon-167.png': pngWithIhdr(167, 167, 2),
		'apple-touch-icon-152.png': pngWithIhdr(152, 152, 2)
	}));
	assert.equal(checked, 3);
	assert.deepEqual(problems, []);
});
