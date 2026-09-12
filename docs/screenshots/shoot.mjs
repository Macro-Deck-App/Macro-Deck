// Renders every scene from Scenes/ with the real Macro Deck UI runtime and saves it as a PNG under
// src/assets/ui/. Needs the .NET SDK, an installed ui/ workspace and a Chromium (set CHROME to override).
import { execFile, spawnSync } from 'node:child_process';
import { promisify } from 'node:util';
import { existsSync, mkdirSync, readdirSync, readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { homedir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const out = path.join(here, 'out');
const scenesDir = path.join(out, 'scenes');
const assets = path.resolve(here, '../src/assets/ui');
const only = process.argv.slice(2);
const execFileAsync = promisify(execFile);

function run(command, args) {
	const result = spawnSync(command, args, { cwd: here, stdio: 'inherit' });
	if (result.status !== 0) throw new Error(`${command} ${args.join(' ')} failed`);
}

function findChrome() {
	if (process.env.CHROME) return process.env.CHROME;
	// The system Chrome can hang at startup on a broken macOS trust store; Playwright's shell does not.
	const cache = path.join(homedir(), 'Library/Caches/ms-playwright');
	const shells = existsSync(cache)
		? readdirSync(cache).filter(name => name.startsWith('chromium_headless_shell-')).sort().reverse()
		: [];
	for (const shell of shells) {
		for (const platform of readdirSync(path.join(cache, shell))) {
			const binary = path.join(cache, shell, platform, 'chrome-headless-shell');
			if (existsSync(binary)) return binary;
		}
	}
	const system = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
	return existsSync(system) ? system : 'chromium';
}

run('dotnet', ['run', '--project', 'Scenes', '-c', 'Release', '--', scenesDir]);

const esbuild = await import(pathToFileURL(path.join(repo, 'ui/node_modules/esbuild/lib/main.js')).href);
await esbuild.build({
	entryPoints: [path.join(here, 'entry.ts')],
	bundle: true,
	format: 'esm',
	outfile: path.join(out, 'runtime.js'),
	logLevel: 'warning',
});

const roots = {
	'/styles/': path.join(repo, 'ui/runtime/styles'),
	'/scenes/': scenesDir,
	'/': out,
};
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml' };
const server = createServer((request, response) => {
	const url = new URL(request.url, 'http://localhost');
	const file = url.pathname === '/render.html'
		? path.join(here, 'render.html')
		: Object.entries(roots)
			.filter(([prefix]) => url.pathname.startsWith(prefix))
			.map(([prefix, dir]) => path.join(dir, url.pathname.slice(prefix.length)))[0];
	if (!file || !existsSync(file)) {
		response.writeHead(404).end();
		return;
	}
	response.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' });
	response.end(readFileSync(file));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const { port } = server.address();

mkdirSync(assets, { recursive: true });
const chrome = findChrome();
const scenes = readdirSync(scenesDir)
	.filter(file => file.endsWith('.json'))
	.map(file => path.basename(file, '.json'))
	.filter(name => only.length === 0 || only.includes(name));

try {
	for (const name of scenes) {
		const { width, height } = JSON.parse(readFileSync(path.join(scenesDir, `${name}.json`), 'utf8'));
		const target = path.join(assets, `${name}.png`);
		// Async on purpose: the page is served from this process, so a blocking spawn would starve it.
		await execFileAsync(chrome, [
			'--headless=new',
			'--hide-scrollbars',
			'--force-device-scale-factor=2',
			`--window-size=${width},${height}`,
			'--default-background-color=00000000',
			'--virtual-time-budget=3000',
			`--screenshot=${target}`,
			`http://127.0.0.1:${port}/render.html?scene=${encodeURIComponent(name)}`,
		], { timeout: 60_000 });
		if (!existsSync(target)) throw new Error(`Could not capture ${name}`);
		console.log(`${name}.png`);
	}
} finally {
	server.close();
}
