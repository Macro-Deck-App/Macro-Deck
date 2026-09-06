#!/usr/bin/env node
// Gate for opaque apple-touch-icon assets. iOS masks apple-touch-icon-*.png with its own superellipse
// and expects a full-bleed opaque square; a source PNG that still carries an alpha channel (colour
// type 6) leaves whatever is behind the mask showing through the rounded corners on the home screen.
// Reads the IHDR chunk directly so the check needs no image-decoding dependency.

import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const ICONS_DIR = 'ui/web-client/public/icons';
const NAME_PATTERN = /^apple-touch-icon-(\d+)\.png$/;
const TRUECOLOUR_NO_ALPHA = 2;

function readIhdr(path) {
	const buffer = readFileSync(path);
	return {
		width: buffer.readUInt32BE(16),
		height: buffer.readUInt32BE(20),
		colourType: buffer.readUInt8(25)
	};
}

export function findBadAppleTouchIcons(root = process.cwd()) {
	const dir = join(root, ICONS_DIR);
	const files = readdirSync(dir).filter(f => NAME_PATTERN.test(f));

	const problems = [];
	for (const file of files) {
		const size = Number(NAME_PATTERN.exec(file)[1]);
		const { width, height, colourType } = readIhdr(join(dir, file));
		if (colourType !== TRUECOLOUR_NO_ALPHA) {
			problems.push({ file, issue: `colour type ${colourType} (expected ${TRUECOLOUR_NO_ALPHA}, truecolour with no alpha)` });
		}
		if (width !== size || height !== size) {
			problems.push({ file, issue: `${width}x${height} (expected ${size}x${size})` });
		}
	}
	return { checked: files.length, problems };
}

if (import.meta.url === `file://${process.argv[1]}`) {
	const { checked, problems } = findBadAppleTouchIcons();
	if (problems.length === 0) {
		console.log(`App icons OK - ${checked} apple-touch-icon file(s) are opaque and correctly sized.`);
		process.exit(0);
	}

	console.error(`Found ${problems.length} problem(s) in ${ICONS_DIR}:`);
	for (const { file, issue } of problems) {
		console.error(`  ${file}: ${issue}`);
	}
	process.exit(1);
}
