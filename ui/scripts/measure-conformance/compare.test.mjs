import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compareMeasured } from './compare.mjs';

function measured(change = () => {}) {
	const value = {
		tree: 'conformance-todo-header-tree.json',
		font: { file: 'docs/public/fonts/Anta-Regular.ttf', sha256: 'abc' },
		timeZone: 'UTC',
		chrome: 'HeadlessChrome/151.0.0.0',
		cases: [{
			tile: { width: 295, height: 295 },
			basis: 295,
			nodes: {
				'todo.header.count': {
					type: 'ui.text',
					box: { x: 237.13, y: 14.73, width: 40.16, height: 41.28 },
					texts: [{ text: '128', naturalWidth: 47.81, givenWidth: 40.16, truncated: { x: true, y: false } }],
				},
			},
		}],
	};
	change(value);
	return value;
}

const count = value => value.cases[0].nodes['todo.header.count'];

test('a different Chrome build that lays out the same matches', () => {
	assert.deepEqual(compareMeasured(measured(), measured(value => { value.chrome = 'HeadlessChrome/153.0.0.0'; })), []);
});

test('drift within a tenth of a pixel matches', () => {
	assert.deepEqual(compareMeasured(measured(), measured(value => { count(value).box.width += 0.09; })), []);
});

test('a box that moves further fails', () => {
	const differences = compareMeasured(measured(), measured(value => { count(value).box.x += 0.5; }));
	assert.equal(differences.length, 1);
	assert.match(differences[0], /todo\.header\.count: box\.x/);
});

test('a text that stops being cut off fails even when every width matches', () => {
	const differences = compareMeasured(measured(), measured(value => { count(value).texts[0].truncated.x = false; }));
	assert.equal(differences.length, 1);
	assert.match(differences[0], /truncated\.x true became false/);
});

test('a node that appears or disappears fails', () => {
	const added = measured(value => { value.cases[0].nodes['todo.header.extra'] = count(value); });
	assert.match(compareMeasured(measured(), added)[0], /todo\.header\.extra: node is new/);
	assert.match(compareMeasured(added, measured())[0], /todo\.header\.extra: node is gone/);
});

test('measuring with another face fails', () => {
	assert.match(compareMeasured(measured(), measured(value => { value.font.sha256 = 'def'; }))[0], /different face/);
});
