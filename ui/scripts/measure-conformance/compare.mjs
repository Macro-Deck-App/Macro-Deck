export const TOLERANCE_PX = 0.1;

const REFERENCE_CELL = 120;

const FIT_EPSILON_PX = 0.01;

// Null for a minimum that needs the layout around it, a cross or parent term: the check refuses it rather
// than skip it.
function minimumSizes(node, basis, sizes = new Map()) {
	const length = node.properties?.minSize;
	if (length && typeof length.basis === 'number' && length.maxOfCross === undefined && length.ofParent === undefined) {
		const own = length.basis * basis;
		sizes.set(node.id, typeof length.maxOfCell === 'number' ? Math.min(own, length.maxOfCell * REFERENCE_CELL) : own);
	} else if (length) {
		sizes.set(node.id, null);
	}
	for (const child of [...(node.children ?? []), ...(node.fallback ? [node.fallback] : [])]) minimumSizes(child, basis, sizes);
	return sizes;
}

// The contract decides, not the implementation the files are regenerated from (see the README).
export function fitProblems(root, measured) {
	const problems = [];
	for (const testCase of measured.cases) {
		const minimums = minimumSizes(root, testCase.basis);
		for (const [id, node] of Object.entries(testCase.nodes)) {
			const minimum = minimums.get(id);
			if (minimum === undefined) continue;
			if (minimum === null) {
				problems.push(`${caseKey(testCase)} ${id}: its minSize needs the layout around it, which this check cannot resolve`);
				continue;
			}
			for (const text of node.texts ?? []) {
				if (text.truncated.x && text.fontSize > minimum + FIT_EPSILON_PX) {
					problems.push(`${caseKey(testCase)} ${id}: cut off at ${text.fontSize} px, above its minimum ${Math.round(minimum * 100) / 100} px`);
				}
			}
		}
	}
	return problems;
}

export function withoutFitSizes(measured) {
	for (const testCase of measured.cases) {
		for (const node of Object.values(testCase.nodes)) {
			for (const text of node.texts ?? []) delete text.fontSize;
		}
	}
	return measured;
}

function near(expected, actual) {
	return typeof expected === 'number' && typeof actual === 'number' && Math.abs(expected - actual) <= TOLERANCE_PX;
}

function caseKey(testCase) {
	return `${testCase.tile.width}x${testCase.tile.height}@${testCase.basis}`;
}

function compareNode(where, expected, actual, differences) {
	if (expected.type !== actual.type) differences.push(`${where}: type ${expected.type} became ${actual.type}`);
	for (const side of ['x', 'y', 'width', 'height']) {
		if (!near(expected.box[side], actual.box[side])) {
			differences.push(`${where}: box.${side} ${expected.box[side]} became ${actual.box[side]}`);
		}
	}
	const expectedTexts = expected.texts ?? [];
	const actualTexts = actual.texts ?? [];
	if (expectedTexts.length !== actualTexts.length) {
		differences.push(`${where}: ${expectedTexts.length} texts became ${actualTexts.length}`);
		return;
	}
	expectedTexts.forEach((text, index) => {
		const other = actualTexts[index];
		const at = `${where} text ${index}`;
		if (text.text !== other.text) differences.push(`${at}: "${text.text}" became "${other.text}"`);
		for (const width of ['naturalWidth', 'givenWidth']) {
			if (!near(text[width], other[width])) differences.push(`${at}: ${width} ${text[width]} became ${other[width]}`);
		}
		for (const axis of ['x', 'y']) {
			if (text.truncated[axis] !== other.truncated[axis]) {
				differences.push(`${at}: truncated.${axis} ${text.truncated[axis]} became ${other.truncated[axis]}`);
			}
		}
	});
}

export function compareMeasured(expected, actual) {
	const differences = [];
	if (expected.tree !== actual.tree) differences.push(`tree ${expected.tree} became ${actual.tree}`);
	if (expected.font?.sha256 !== actual.font?.sha256) differences.push(`font ${expected.font?.file} is a different face`);
	if (expected.timeZone !== actual.timeZone) differences.push(`time zone ${expected.timeZone} became ${actual.timeZone}`);

	const actualCases = new Map(actual.cases.map(testCase => [caseKey(testCase), testCase]));
	const expectedKeys = new Set(expected.cases.map(caseKey));
	for (const key of actualCases.keys()) {
		if (!expectedKeys.has(key)) differences.push(`case ${key} is new`);
	}
	for (const testCase of expected.cases) {
		const key = caseKey(testCase);
		const other = actualCases.get(key);
		if (!other) {
			differences.push(`case ${key} is gone`);
			continue;
		}
		for (const id of Object.keys(other.nodes)) {
			if (!(id in testCase.nodes)) differences.push(`${key} ${id}: node is new`);
		}
		for (const [id, node] of Object.entries(testCase.nodes)) {
			if (!(id in other.nodes)) differences.push(`${key} ${id}: node is gone`);
			else compareNode(`${key} ${id}`, node, other.nodes[id], differences);
		}
	}
	return differences;
}
