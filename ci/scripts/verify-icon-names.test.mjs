import { strict as assert } from 'node:assert';
import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { findUnknownIconNames } from './verify-icon-names.mjs';

const ICON_DIR = 'ui/runtime/styles/icons';
const UTILITIES = 'ui/runtime/styles/icons.css';
const SOURCE_DIR = 'ui/angular/projects/desktop-ui/src';

/** A miniature checkout carrying two real icons, one hand-declared alias, and one source file. */
function fixture(source, { utilities = '.icon-wifi { mask-image: url("./icons/wifi-solid-full.svg"); }' } = {}) {
	const root = mkdtempSync(join(tmpdir(), 'icon-names-'));
	mkdirSync(join(root, ICON_DIR), { recursive: true });
	mkdirSync(join(root, SOURCE_DIR), { recursive: true });
	writeFileSync(join(root, ICON_DIR, 'chevron-right.svg'), '<svg/>');
	writeFileSync(join(root, ICON_DIR, 'crosshair.svg'), '<svg/>');
	writeFileSync(join(root, ICON_DIR, 'wifi-solid-full.svg'), '<svg/>');
	writeFileSync(join(root, UTILITIES), utilities);
	writeFileSync(join(root, SOURCE_DIR, 'sample.html'), source);
	return root;
}

test('accepts a class that names a real icon', () => {
	const { unknown } = findUnknownIconNames(fixture('<span class="icon icon-chevron-right icon-xs"></span>'));
	assert.deepEqual(unknown.map(u => u.name), []);
});

test('rejects a class that names an icon with no SVG behind it', () => {
	// This is the exact reference that shipped: there is no chevron-down, the house convention is to
	// rotate chevron-right, and it rendered as a blank box.
	const { unknown } = findUnknownIconNames(fixture('<span class="icon icon-chevron-down"></span>'));
	assert.deepEqual(unknown.map(u => u.name), ['chevron-down']);
});

test('rejects an empty state asking for an icon that does not exist', () => {
	const { unknown } = findUnknownIconNames(fixture('<shared-empty-state compact icon="search" />'));
	assert.deepEqual(unknown.map(u => u.name), ['search']);
});

test('accepts an empty state asking for one that does', () => {
	const { unknown } = findUnknownIconNames(fixture('<shared-empty-state compact icon="crosshair" />'));
	assert.deepEqual(unknown.map(u => u.name), []);
});

test('accepts a name declared by hand in the stylesheet rather than by filename', () => {
	const { unknown } = findUnknownIconNames(fixture('<span class="icon icon-wifi"></span>'));
	assert.deepEqual(unknown.map(u => u.name), []);
});

test('ignores a component class that merely starts with the same word', () => {
	// `icon-packs-page` is an ordinary component class, and an `icon` input elsewhere names a
	// user-supplied icon pack entry - neither has anything to do with these SVGs.
	const source = '<div class="icon-packs-page"></div><app-widget-icon-control icon="a" />';
	const { unknown } = findUnknownIconNames(fixture(source));
	assert.deepEqual(unknown.map(u => u.name), []);
});

test('ignores the size modifiers, which share the prefix without naming an icon', () => {
	const { unknown } = findUnknownIconNames(fixture('<span class="icon icon-lg icon-2xl"></span>'));
	assert.deepEqual(unknown.map(u => u.name), []);
});

test('reports where the reference is, so it can be found without a search', () => {
	const { unknown } = findUnknownIconNames(fixture('<p>one</p>\n<span class="icon icon-nope"></span>'));
	assert.equal(unknown.length, 1);
	assert.equal(unknown[0].line, 2);
	assert.match(unknown[0].file, /sample\.html$/);
});
