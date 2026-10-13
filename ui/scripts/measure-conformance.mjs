import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { compareMeasured } from './measure-conformance/compare.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const ui = path.resolve(here, '..');
const repo = path.resolve(ui, '..');
const fixtures = path.join(repo, 'ui-model/fixtures/component-profile');
const VIEWPORT = 1000;
const CASE_TIMEOUT_MS = 30000;
const TIME_ZONE = 'UTC';

function parseArgs(argv) {
	const args = { check: false, out: null };
	for (let index = 0; index < argv.length; index++) {
		if (argv[index] === '--check') args.check = true;
		else if (argv[index] === '--out') args.out = path.resolve(argv[++index]);
		else throw new Error(`unknown argument ${argv[index]}`);
	}
	return args;
}

function findChrome() {
	const configured = process.env.CHROME_BIN || process.env.CHROME;
	if (configured) return configured;
	// The system Chrome can hang at startup on a broken macOS trust store; Playwright's shell does not.
	const cache = path.join(homedir(), process.platform === 'darwin' ? 'Library/Caches/ms-playwright' : '.cache/ms-playwright');
	const shells = existsSync(cache)
		? readdirSync(cache).filter(name => name.startsWith('chromium_headless_shell-')).sort().reverse()
		: [];
	for (const shell of shells) {
		for (const platform of readdirSync(path.join(cache, shell))) {
			const binary = path.join(cache, shell, platform, 'chrome-headless-shell');
			if (existsSync(binary)) return binary;
		}
	}
	throw new Error('no Chrome found; set CHROME_BIN or CHROME');
}

function measuredFile(tree) {
	return tree.replace(/-tree\.json$/, '-measured.json');
}

function coverageProblems(manifest) {
	const listed = new Set(manifest.scenes.map(scene => scene.tree));
	const produced = new Set(manifest.scenes.map(scene => measuredFile(scene.tree)));
	const files = readdirSync(fixtures);
	return [
		...files.filter(file => /^conformance(-.+)?-tree\.json$/.test(file) && !listed.has(file))
			.map(file => `${file} is not in measured-scenes.json`),
		...files.filter(file => file.endsWith('-measured.json') && !produced.has(file))
			.map(file => `${file} belongs to no scene in measured-scenes.json`),
	];
}

function readJson(file) {
	return JSON.parse(readFileSync(file, 'utf8'));
}

function measurableCase(testCase) {
	const tile = testCase.tile;
	return typeof testCase.basis === 'number'
		&& tile !== null && typeof tile === 'object'
		&& Number.isFinite(tile.width) && Number.isFinite(tile.height)
		&& tile.width > 0 && tile.height > 0;
}

function sceneCases(scene) {
	const cases = scene.cases ?? readJson(path.join(fixtures, scene.layout)).cases.filter(measurableCase);
	const seen = new Set();
	return cases
		.map(testCase => ({
			tile: { width: Math.round(testCase.tile.width * 100) / 100, height: Math.round(testCase.tile.height * 100) / 100 },
			basis: testCase.basis,
		}))
		.filter(testCase => {
			const key = `${testCase.tile.width}x${testCase.tile.height}@${testCase.basis}`;
			if (seen.has(key)) return false;
			seen.add(key);
			return true;
		});
}

async function bundle() {
	const esbuild = createRequire(import.meta.url)('esbuild');
	const result = await esbuild.build({
		entryPoints: { measure: path.join(here, 'measure-conformance/entry.ts'), styles: path.join(ui, 'preview-renderer/styles.css') },
		bundle: true,
		format: 'iife',
		target: 'chrome110',
		loader: { '.svg': 'dataurl' },
		legalComments: 'none',
		write: false,
		outdir: path.join(tmpdir(), 'macro-deck-measure'),
		logLevel: 'warning',
	});
	const file = suffix => result.outputFiles.find(output => output.path.endsWith(suffix)).text;
	return { script: file('measure.js'), style: file('styles.css') };
}

function page({ script, style }) {
	const frozen = '*,*::before,*::after{transition:none!important;animation:none!important}';
	return `<!doctype html><meta charset="utf-8"><style>${style}${frozen}</style><div id="tile"></div><script>${script}</script>`;
}

class Browser {
	static async launch(binary) {
		const profile = mkdtempSync(path.join(tmpdir(), 'macro-deck-measure-'));
		const process_ = spawn(binary, [
			'--headless=new',
			'--remote-debugging-port=0',
			`--user-data-dir=${profile}`,
			'--font-render-hinting=none',
			'--enable-font-subpixel-positioning',
			'--hide-scrollbars',
			'--no-first-run',
			'--no-default-browser-check',
			'about:blank',
		], { stdio: 'ignore', detached: process.platform !== 'win32' });
		const portFile = path.join(profile, 'DevToolsActivePort');
		const started = Date.now();
		while (!existsSync(portFile) || !readFileSync(portFile, 'utf8').includes('\n')) {
			if (Date.now() - started > CASE_TIMEOUT_MS) throw new Error(`Chrome did not start: ${binary}`);
			await new Promise(resolve => setTimeout(resolve, 50));
		}
		const [port, socketPath] = readFileSync(portFile, 'utf8').trim().split('\n');
		const socket = new WebSocket(`ws://127.0.0.1:${port}${socketPath}`);
		await new Promise((resolve, reject) => {
			socket.addEventListener('open', resolve, { once: true });
			socket.addEventListener('error', reject, { once: true });
		});
		const browser = new Browser(process_, profile, socket);
		const version = await browser.send('Browser.getVersion');
		browser.version = version.product;
		const { targetId } = await browser.send('Target.createTarget', { url: 'about:blank' });
		browser.session = (await browser.send('Target.attachToTarget', { targetId, flatten: true })).sessionId;
		await browser.send('Emulation.setDeviceMetricsOverride', { width: VIEWPORT, height: VIEWPORT, deviceScaleFactor: 1, mobile: false }, true);
		await browser.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] }, true);
		await browser.send('Emulation.setTimezoneOverride', { timezoneId: TIME_ZONE }, true);
		return browser;
	}

	constructor(process_, profile, socket) {
		this.process = process_;
		this.profile = profile;
		this.socket = socket;
		this.pending = new Map();
		this.next = 0;
		socket.addEventListener('message', event => {
			const message = JSON.parse(event.data);
			const waiting = this.pending.get(message.id);
			if (!waiting) return;
			this.pending.delete(message.id);
			if (message.error) waiting.reject(new Error(`${message.error.message} (${message.error.code})`));
			else waiting.resolve(message.result);
		});
	}

	send(method, params = {}, inSession = false) {
		const id = ++this.next;
		const message = { id, method, params };
		if (inSession) message.sessionId = this.session;
		return new Promise((resolve, reject) => {
			this.pending.set(id, { resolve, reject });
			this.socket.send(JSON.stringify(message));
		});
	}

	async close() {
		this.socket.close();
		const exited = new Promise(resolve => this.process.once('exit', resolve));
		// Chrome's helpers outlive the main process and keep writing the profile, so stop the whole group.
		if (process.platform === 'win32') this.process.kill();
		else process.kill(-this.process.pid, 'SIGTERM');
		await exited;
		try {
			rmSync(this.profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
		} catch (error) {
			console.warn(`could not remove the Chrome profile ${this.profile}: ${error.message}`);
		}
	}
}

async function measureAll(manifest) {
	const fontPath = path.join(repo, manifest.font);
	const font = readFileSync(fontPath);
	const fontInfo = { file: manifest.font, sha256: createHash('sha256').update(font).digest('hex') };
	const html = page(await bundle());

	let current = null;
	let settle = null;
	const server = createServer((request, response) => {
		if (request.url.startsWith('/render')) return response.end(html);
		if (request.url === '/case.json') return response.end(JSON.stringify(current));
		if (request.url === '/font.ttf') return response.end(font);
		if (request.url === '/result' && request.method === 'POST') {
			let body = '';
			request.on('data', chunk => (body += chunk));
			request.on('end', () => {
				response.end();
				settle?.(JSON.parse(body));
			});
			return;
		}
		response.statusCode = 404;
		response.end();
	});
	await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
	const origin = `http://127.0.0.1:${server.address().port}`;
	const browser = await Browser.launch(findChrome());

	try {
		const results = new Map();
		for (const scene of manifest.scenes) {
			const tree = readJson(path.join(fixtures, scene.tree));
			const cases = [];
			for (const testCase of sceneCases(scene)) {
				current = { tile: testCase.tile, basis: testCase.basis, locale: manifest.locale, root: tree.root };
				let timer;
				const answer = new Promise((resolve, reject) => {
					settle = resolve;
					timer = setTimeout(() => reject(new Error(`${scene.tree} at ${JSON.stringify(testCase)} timed out`)), CASE_TIMEOUT_MS);
				});
				await browser.send('Page.navigate', { url: `${origin}/render?case=${cases.length}` }, true);
				const result = await answer.finally(() => clearTimeout(timer));
				if (result.error) throw new Error(`${scene.tree} at ${JSON.stringify(testCase)}: ${result.error}`);
				cases.push({ ...testCase, nodes: result.nodes });
			}
			results.set(measuredFile(scene.tree), { tree: scene.tree, font: fontInfo, timeZone: TIME_ZONE, chrome: browser.version, cases });
		}
		return results;
	} finally {
		await browser.close();
		server.close();
	}
}

const args = parseArgs(process.argv.slice(2));
const manifest = readJson(path.join(fixtures, 'measured-scenes.json'));
const results = await measureAll(manifest);
const serialize = value => `${JSON.stringify(value, null, 2)}\n`;

if (!args.check) {
	for (const [file, measured] of results) writeFileSync(path.join(fixtures, file), serialize(measured));
	console.log(`wrote ${results.size} measured files with ${[...results.values()][0]?.chrome}`);
} else {
	const failures = [];
	const coverage = coverageProblems(manifest);
	if (coverage.length > 0) failures.push({ file: 'measured-scenes.json', measured: null, differences: coverage });
	for (const [file, measured] of results) {
		const committed = path.join(fixtures, file);
		const differences = existsSync(committed) ? compareMeasured(readJson(committed), measured) : ['the file is missing'];
		if (differences.length > 0) failures.push({ file, measured, differences });
	}
	if (failures.length === 0) {
		console.log(`${results.size} measured files match what the browser lays out`);
	} else {
		for (const { file, differences } of failures) {
			console.error(`${file}:`);
			for (const difference of differences.slice(0, 20)) console.error(`  ${difference}`);
			if (differences.length > 20) console.error(`  ... ${differences.length - 20} more`);
		}
		const regenerated = failures.filter(failure => failure.measured !== null);
		if (args.out && regenerated.length > 0) {
			mkdirSync(args.out, { recursive: true });
			for (const { file, measured } of regenerated) writeFileSync(path.join(args.out, file), serialize(measured));
			console.error(`\nregenerated files are in ${args.out}; copy them to ui-model/fixtures/component-profile/`);
		}
		console.error('Regenerate with: npm --prefix ui run measure:conformance');
		process.exitCode = 1;
	}
}
