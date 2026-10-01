// Bundles the framework-free UI runtime and its styles into the two files the plugin CLI embeds to render
// [UiPreview] scenarios. Deterministic: the output only changes when ui/runtime or preview-renderer/ does.
import { createRequire } from 'node:module';
import { existsSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { gunzipSync, gzipSync } from 'node:zlib';
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

const check = process.argv.includes('--check');
let stale = false;
for (const [name, source] of [['preview-renderer.js', 'preview-renderer.js'], ['preview-renderer.css', 'preview-renderer-css.css']]) {
	const fresh = readFileSync(path.join(scratch, source));
	const file = path.join(target, `${name}.gz`);
	// The bundle is compared decompressed: the compressed bytes depend on the zlib build.
	if (check) stale ||= !existsSync(file) || !gunzipSync(readFileSync(file)).equals(fresh);
	else writeFileSync(file, gzipSync(fresh, { level: 9 }));
}
rmSync(scratch, { recursive: true, force: true });
if (stale) {
	console.error('The embedded preview renderer is stale: run npm run build:preview-renderer in ui and commit the result.');
	process.exit(1);
}
