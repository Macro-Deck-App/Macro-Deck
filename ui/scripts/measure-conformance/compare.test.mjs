import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compareMeasured, fitProblems, withoutFitSizes } from './compare.mjs';

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

function fitted(fontSize, truncatedX) {
	return {
		cases: [{
			tile: { width: 120, height: 120 },
			basis: 120,
			nodes: {
				clock: {
					type: 'macrodeck.dynamic-text',
					box: { x: 0, y: 0, width: 96, height: 30 },
					texts: [{ text: '14:05:30', naturalWidth: 97, givenWidth: 96, truncated: { x: truncatedX, y: false }, fontSize }],
				},
			},
		}],
	};
}

const clockRoot = { id: 'root', type: 'ui.stack', children: [{ id: 'clock', type: 'macrodeck.dynamic-text', properties: { minSize: { basis: 0.13 } } }] };

test('a fitted text cut off above its minimum is a problem, whatever the reference says', () => {
	const problems = fitProblems(clockRoot, fitted(28.8, true));
	assert.equal(problems.length, 1);
	assert.match(problems[0], /120x120@120 clock: cut off at 28\.8 px, above its minimum 15\.6/);
});

test('a fitted text cut off at its minimum, or fitting above it, is not', () => {
	assert.deepEqual(fitProblems(clockRoot, fitted(15.6, true)), []);
	assert.deepEqual(fitProblems(clockRoot, fitted(28.8, false)), []);
});

test('a minimum the check cannot resolve is refused, not skipped', () => {
	const root = { id: 'root', children: [{ id: 'clock', properties: { minSize: { basis: 0.13, ofParent: 0.2 } } }] };
	const problems = fitProblems(root, fitted(28.8, false));
	assert.equal(problems.length, 1);
	assert.match(problems[0], /clock: its minSize needs the layout around it/);
});

test('a drawn fallback is checked against its own minimum', () => {
	const root = { id: 'root', children: [{ id: 'dial', fallback: { id: 'clock', properties: { minSize: { basis: 0.13 } } } }] };
	assert.equal(fitProblems(root, fitted(28.8, true)).length, 1);
});

test('the size checked against the minimum is not written', () => {
	assert.equal('fontSize' in withoutFitSizes(fitted(28.8, false)).cases[0].nodes.clock.texts[0], false);
});
