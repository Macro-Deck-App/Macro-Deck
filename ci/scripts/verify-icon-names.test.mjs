import { strict as assert } from 'node:assert';
import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { findIconContractProblems, findUnknownIconNames } from './verify-icon-names.mjs';

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

const RUNTIME_TYPES = 'ui/runtime/src/ui-components/ui-component-types.ts';
const RUNTIME_ICON = 'ui/runtime/src/ui-components/ui-icon.component.ts';
const SDK_VALUES = 'ui-model/src/MacroDeck.Ui/Components/UiComponentValues.cs';
const GLYPHS = [
	'.icon-xs { width: 14px; }',
	".icon-crosshair {\n  mask-image: url('./icons/crosshair.svg');\n}",
	".icon-wifi {\n  mask-image: url('./icons/wifi-solid-full.svg');\n}",
].join('\n');

function contract({ runtime = ["'crosshair', 'wifi'"], sdk = 'Crosshair, Wifi', maximum = 'UI_ICON_VERSIONS.length' } = {}) {
	const root = fixture('', { utilities: GLYPHS });
	mkdirSync(join(root, 'ui/runtime/src/ui-components'), { recursive: true });
	mkdirSync(join(root, 'ui-model/src/MacroDeck.Ui/Components'), { recursive: true });
	writeFileSync(join(root, RUNTIME_TYPES),
		`export const UI_ICON_VERSIONS: readonly (readonly string[])[] = [\n${runtime.map(g => `  [${g}],`).join('\n')}\n];\n`);
	writeFileSync(join(root, RUNTIME_ICON), `version: { minimum: 1, maximum: ${maximum} },`);
	writeFileSync(join(root, SDK_VALUES),
		'public const string Crosshair = "crosshair";\npublic const string Wifi = "wifi";\n'
		+ `public static readonly IReadOnlyList<string> Version1 =\n[\n${sdk},\n];`);
	return root;
}

test('accepts published icon groups that both sides agree on and that all draw', () => {
	assert.deepEqual(findIconContractProblems(contract()), []);
});

test('rejects a published size modifier, which would paint a filled square', () => {
	const problems = findIconContractProblems(contract({ runtime: ["'crosshair', 'wifi', 'xs'"] }));
	assert.equal(problems.length, 2);
	assert.match(problems[0], /"xs" is a size modifier/);
});

test('rejects a published name with no glyph behind it', () => {
	const problems = findIconContractProblems(contract({ runtime: ["'crosshair', 'wifi'", "'chevron-down'"] }));
	assert.deepEqual(problems, ['group 2: "chevron-down" has no glyph class in ui/runtime/styles/icons.css']);
});

test('rejects a first group the producer and the reader disagree on', () => {
	const problems = findIconContractProblems(contract({ sdk: 'Crosshair' }));
	assert.equal(problems.length, 1);
	assert.match(problems[0], /Version1 and .* group 1 differ/);
});

test('rejects a reader whose maximum version does not follow the group count', () => {
	const problems = findIconContractProblems(contract({ maximum: '1' }));
	assert.equal(problems.length, 1);
	assert.match(problems[0], /maximum version/);
});

test('leaves an unpublished app glyph free, so a new app icon is not public by accident', () => {
	assert.deepEqual(findIconContractProblems(contract({ runtime: ["'crosshair'"], sdk: 'Crosshair' })), []);
});
