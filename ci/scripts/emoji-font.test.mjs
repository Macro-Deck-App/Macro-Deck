import { strict as assert } from 'node:assert';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { FONT_PATH, assertBitmapEmoji, install } from './emoji-font.mjs';

function font(tags, copyright = 'Copyright 2022 Google Inc.') {
	const names = Buffer.from(copyright, 'utf16le').swap16();
	const name = Buffer.alloc(6 + 12 + names.length);
	name.writeUInt16BE(0, 0);
	name.writeUInt16BE(copyright ? 1 : 0, 2);
	name.writeUInt16BE(18, 4);
	if (copyright) {
		name.writeUInt16BE(3, 6);
		name.writeUInt16BE(1, 8);
		name.writeUInt16BE(1033, 10);
		name.writeUInt16BE(0, 12);
		name.writeUInt16BE(names.length, 14);
		name.writeUInt16BE(0, 16);
		names.copy(name, 18);
	}
	const all = [...tags, 'name'];
	const header = Buffer.alloc(12 + all.length * 16);
	header.writeUInt32BE(0x00010000, 0);
	header.writeUInt16BE(all.length, 4);
	all.forEach((tag, index) => {
		const entry = 12 + index * 16;
		header.write(tag, entry, 'ascii');
		header.writeUInt32BE(tag === 'name' ? header.length : header.length + name.length, entry + 8);
		header.writeUInt32BE(tag === 'name' ? name.length : 0, entry + 12);
	});
	return Buffer.concat([header, name]);
}

test('a bitmap emoji font passes and its copyright line is read from the name table', () => {
	assert.equal(assertBitmapEmoji(font(['CBDT', 'CBLC'])).copyright, 'Copyright 2022 Google Inc.');
});

test('a COLR font is refused even when it also has bitmap tables', () => {
	assert.throws(() => assertBitmapEmoji(font(['CBDT', 'CBLC', 'COLR'])), /COLR/);
});

test('a font without bitmap tables is refused', () => {
	assert.throws(() => assertBitmapEmoji(font(['COLR'])), /CBDT/);
	assert.throws(() => assertBitmapEmoji(font(['CBDT'])), /CBLC/);
});

test('a font without a copyright line is refused', () => {
	assert.throws(() => assertBitmapEmoji(font(['CBDT', 'CBLC'], '')), /copyright/);
});

test('something that is not a font is refused', () => {
	assert.throws(() => assertBitmapEmoji(Buffer.from('<!DOCTYPE html>')), /not a TrueType/);
});

test('install places the font and appends the licence once to the notices', () => {
	const root = mkdtempSync(join(tmpdir(), 'emoji-font-'));
	const appdir = join(root, 'appdir');
	mkdirSync(join(appdir, 'usr/share/doc'), { recursive: true });
	writeFileSync(join(appdir, 'usr/share/doc/NOTICES'), 'existing notices\n');
	writeFileSync(join(root, 'font.ttf'), font(['CBDT', 'CBLC']));
	writeFileSync(join(root, 'OFL.txt'), 'SIL OPEN FONT LICENSE Version 1.1\n');

	install(join(root, 'font.ttf'), join(root, 'OFL.txt'), appdir, 'usr/share/doc/NOTICES');

	assert.ok(existsSync(join(appdir, FONT_PATH)));
	const notices = readFileSync(join(appdir, 'usr/share/doc/NOTICES'), 'utf8');
	assert.ok(notices.startsWith('existing notices\n'));
	assert.match(notices, /bundled unmodified/);
	assert.match(notices, /Copyright 2022 Google Inc\./);
	assert.match(notices, /SIL OPEN FONT LICENSE Version 1\.1/);
});
