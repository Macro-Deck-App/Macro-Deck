#!/usr/bin/env node
// Adds the bitmap Noto Color Emoji font to an extracted AppImage and its licence to the notices.
// The bundled WebKit aborts on COLRv1 fonts, so only a CBDT build may ever ship here.

import { copyFileSync, appendFileSync, mkdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const FONT_PATH = 'usr/share/macro-deck/fonts/NotoColorEmoji.ttf';

export function inspectFont(bytes) {
	const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
	if (bytes.length < 12 || view.getUint32(0) !== 0x00010000) {
		throw new Error('not a TrueType font');
	}
	const tables = new Map();
	for (let index = 0; index < view.getUint16(4); index++) {
		const entry = 12 + index * 16;
		const tag = String.fromCharCode(...bytes.subarray(entry, entry + 4));
		tables.set(tag, { offset: view.getUint32(entry + 8), length: view.getUint32(entry + 12) });
	}
	return { tables, copyright: readCopyright(bytes, view, tables.get('name')) };
}

function readCopyright(bytes, view, name) {
	if (!name) {
		return '';
	}
	const count = view.getUint16(name.offset + 2);
	const strings = name.offset + view.getUint16(name.offset + 4);
	for (let index = 0; index < count; index++) {
		const record = name.offset + 6 + index * 12;
		const [platform, , , id, length, offset] = [0, 2, 4, 6, 8, 10].map(at => view.getUint16(record + at));
		if (id !== 0 || (platform !== 3 && platform !== 0)) {
			continue;
		}
		const raw = Buffer.from(bytes.subarray(strings + offset, strings + offset + length));
		return raw.swap16().toString('utf16le').trim();
	}
	return '';
}

export function assertBitmapEmoji(bytes) {
	const { tables, copyright } = inspectFont(bytes);
	for (const required of ['CBDT', 'CBLC']) {
		if (!tables.has(required)) {
			throw new Error(`emoji font has no ${required} table; only the bitmap build may be bundled`);
		}
	}
	if (tables.has('COLR')) {
		throw new Error('emoji font has a COLR table; the bundled WebKit aborts on COLRv1 fonts');
	}
	if (!copyright) {
		throw new Error('emoji font carries no copyright line in its name table');
	}
	return { copyright };
}

export function noticeText(copyright, licence) {
	return [
		'',
		'Noto Color Emoji (bitmap build), bundled unmodified',
		'https://github.com/googlefonts/noto-emoji',
		copyright,
		'',
		licence.trim(),
		''
	].join('\n');
}

export function install(fontFile, licenceFile, appdir, noticesPath) {
	const { copyright } = assertBitmapEmoji(readFileSync(fontFile));
	const target = join(appdir, FONT_PATH);
	mkdirSync(dirname(target), { recursive: true });
	copyFileSync(fontFile, target);
	appendFileSync(join(appdir, noticesPath), noticeText(copyright, readFileSync(licenceFile, 'utf8')));
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
	const [command, font, licence, appdir, noticesPath] = process.argv.slice(2);
	if (command !== 'install' || !noticesPath) {
		console.error('usage: emoji-font.mjs install <font> <licence> <appdir> <noticesPath>');
		process.exit(1);
	}
	try {
		install(font, licence, appdir, noticesPath);
	} catch (error) {
		console.error(`error: ${error.message}`);
		process.exit(1);
	}
}
