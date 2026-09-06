#!/usr/bin/env node
// Gate for icon names that resolve to nothing. Icons are referenced as a CSS class
// (`<span class="icon icon-copy">`) or as a component input (`icon="crosshair"`), and both are
// plain strings - a name with no SVG behind it renders as a blank box with no error anywhere. Two
// such names shipped in one review round, and only a human looking at a screenshot caught them.

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const ICON_DIR = 'ui/runtime/styles/icons';
const UTILITIES = 'ui/runtime/styles/icons.css';
const SOURCE_ROOTS = ['ui/angular/projects'];
const SOURCE_EXTENSIONS = ['.ts', '.html'];

// Size and weight modifiers share the `icon-` prefix without naming an icon.
const MODIFIERS = new Set(['xs', 'sm', 'md', 'lg', 'xl', '2xl', '3xl']);

// Only a class list that also carries the bare `icon` base class is the icon font; `icon-packs-page`
// and friends are ordinary component classes that merely start with the same word.
const CLASS_LIST = /class="([^"]*)"|\[class\]="'([^']*)'"/g;
const ICON_TOKEN = /\bicon-([a-z0-9][a-z0-9-]*)/g;

// `icon` as an input means an icon-font name on shared-empty-state only. Everywhere else it names a
// user-supplied icon-pack entry, which has nothing to do with these SVGs.
const EMPTY_STATE_ICON = /<shared-empty-state\b[^>]*?\bicon="([a-z0-9][a-z0-9-]*)"/gs;

function walk(dir, found = []) {
	for (const entry of readdirSync(dir)) {
		if (entry === 'node_modules' || entry === 'dist') continue;
		const full = join(dir, entry);
		if (statSync(full).isDirectory()) {
			walk(full, found);
		} else if (SOURCE_EXTENSIONS.some(ext => entry.endsWith(ext))) {
			found.push(full);
		}
	}
	return found;
}

export function findUnknownIconNames(root = process.cwd()) {
	const available = new Set(
		readdirSync(join(root, ICON_DIR))
			.filter(f => f.endsWith('.svg'))
			.map(f => f.slice(0, -'.svg'.length)));

	// A few names are declared by hand rather than derived from a filename - `icon-wifi` points at
	// wifi-solid-full.svg - so the stylesheet is as much a source of valid names as the directory is.
	const utilities = readFileSync(join(root, UTILITIES), 'utf8');
	for (const m of utilities.matchAll(/^\s*\.icon-([a-z0-9][a-z0-9-]*)\s*\{/gm)) {
		available.add(m[1]);
	}

	const unknown = [];
	for (const sourceRoot of SOURCE_ROOTS) {
		for (const file of walk(join(root, sourceRoot))) {
			const text = readFileSync(file, 'utf8');
			const report = (name, index) => {
				if (MODIFIERS.has(name) || available.has(name)) return;
				unknown.push({ file: relative(root, file), line: text.slice(0, index).split('\n').length, name });
			};

			CLASS_LIST.lastIndex = 0;
			for (let m = CLASS_LIST.exec(text); m !== null; m = CLASS_LIST.exec(text)) {
				const classes = m[1] ?? m[2] ?? '';
				if (!/(^|\s)icon(\s|$)/.test(classes)) continue;
				ICON_TOKEN.lastIndex = 0;
				for (let t = ICON_TOKEN.exec(classes); t !== null; t = ICON_TOKEN.exec(classes)) {
					report(t[1], m.index);
				}
			}

			EMPTY_STATE_ICON.lastIndex = 0;
			for (let m = EMPTY_STATE_ICON.exec(text); m !== null; m = EMPTY_STATE_ICON.exec(text)) {
				report(m[1], m.index);
			}
		}
	}
	return { available: available.size, unknown };
}

if (import.meta.url === `file://${process.argv[1]}`) {
	const { available, unknown } = findUnknownIconNames();
	if (unknown.length === 0) {
		console.log(`Icon names OK - every reference resolves to one of ${available} icons.`);
		process.exit(0);
	}

	console.error(`Found ${unknown.length} icon reference(s) with no matching SVG in ${ICON_DIR}:`);
	for (const { file, line, name } of unknown) {
		console.error(`  ${file}:${line}  icon "${name}"`);
	}
	process.exit(1);
}
