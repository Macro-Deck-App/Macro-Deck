import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Browser, findChrome } from './headless-chrome.mjs';
import { compareMeasured, fitProblems, withoutFitSizes } from './measure-conformance/compare.mjs';

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

const fitFailures = [];

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
	const browser = await Browser.launch(findChrome(), CASE_TIMEOUT_MS, async opened => {
		await opened.send('Emulation.setDeviceMetricsOverride', { width: VIEWPORT, height: VIEWPORT, deviceScaleFactor: 1, mobile: false }, true);
		await opened.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] }, true);
		await opened.send('Emulation.setTimezoneOverride', { timezoneId: TIME_ZONE }, true);
	});

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
			const measured = { tree: scene.tree, font: fontInfo, timeZone: TIME_ZONE, chrome: browser.version, cases };
			const problems = fitProblems(tree.root, measured);
			if (problems.length > 0) fitFailures.push({ file: measuredFile(scene.tree), differences: problems });
			results.set(measuredFile(scene.tree), withoutFitSizes(measured));
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

if (fitFailures.length > 0) {
	for (const { file, differences } of fitFailures) {
		console.error(`${file}:`);
		for (const difference of differences) console.error(`  ${difference}`);
	}
	console.error('A fitted text is cut off above its minimum: the runtime\'s fit is wrong, so nothing was written.');
	process.exit(1);
}

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
