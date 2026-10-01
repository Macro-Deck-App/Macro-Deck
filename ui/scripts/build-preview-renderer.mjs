// Bundles the framework-free UI runtime and its styles into the two files the plugin CLI embeds to render
// [UiPreview] scenarios. Deterministic: the output only changes when ui/runtime or preview-renderer/ does.
import { createRequire } from 'node:module';
import { mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const ui = path.resolve(here, '..');
const target = path.resolve(ui, '../sdk/src/MacroDeck.Plugin.Cli/Rendering/Assets');
const esbuild = createRequire(import.meta.url)('esbuild');
const scratch = path.join(ui, 'preview-renderer/.out');

mkdirSync(target, { recursive: true });
await esbuild.build({
	entryPoints: { 'preview-renderer': path.join(ui, 'preview-renderer/entry.ts'), 'preview-renderer-css': path.join(ui, 'preview-renderer/styles.css') },
	bundle: true,
	minify: true,
	format: 'iife',
	target: 'chrome110',
	loader: { '.svg': 'dataurl' },
	legalComments: 'none',
	outdir: scratch,
	logLevel: 'warning',
});

for (const [name, source] of [['preview-renderer.js', 'preview-renderer.js'], ['preview-renderer.css', 'preview-renderer-css.css']]) {
	writeFileSync(path.join(target, `${name}.gz`), gzipSync(readFileSync(path.join(scratch, source)), { level: 9 }));
}
rmSync(scratch, { recursive: true, force: true });
