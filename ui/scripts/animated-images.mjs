import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Browser, findChrome } from './headless-chrome.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const fixturesFile = path.join(repo, 'ui-model/fixtures/animated-images/fixtures.json');
const TIMEOUT_MS = 30000;
const PHASE_FRAME_MS = 200;
const PHASE_FRAMES = 8;
const INCOMING_FADE_MS = 220;
const BUTTON_BACKGROUND = [0, 0, 0];

const decoderPage = `<!doctype html><meta charset="utf-8"><script>
async function decodeFixture(fixture) {
	const data = Uint8Array.from(atob(fixture.bytes), character => character.charCodeAt(0));
	let decoder;
	try {
		decoder = new ImageDecoder({ data, type: fixture.format === 'gif' ? 'image/gif' : 'image/webp' });
		await decoder.tracks.ready;
	} catch (error) {
		return { decodeError: error.name };
	}
	await decoder.completed.catch(() => undefined);
	const track = decoder.tracks.selectedTrack;
	const result = { frameCount: track.frameCount, plays: track.repetitionCount === Infinity ? null : track.repetitionCount + 1, durationsMs: [], frames: [] };
	for (let index = 0; index < track.frameCount; index++) {
		let decoded;
		try {
			decoded = (await decoder.decode({ frameIndex: index, completeFramesOnly: false })).image;
		} catch (error) {
			result.durationsMs.push(null);
			result.frames.push(null);
			continue;
		}
		result.width = decoded.displayWidth;
		result.height = decoded.displayHeight;
		result.durationsMs.push(decoded.duration === null ? null : Math.round(decoded.duration / 1000));
		const canvas = new OffscreenCanvas(decoded.displayWidth, decoded.displayHeight);
		const context = canvas.getContext('2d');
		context.drawImage(decoded, 0, 0);
		const pixels = context.getImageData(0, 0, decoded.displayWidth, decoded.displayHeight).data;
		let binary = '';
		for (const byte of pixels) binary += String.fromCharCode(byte);
		result.frames.push(btoa(binary));
		decoded.close();
	}
	decoder.close();
	return result;
}
</script>`;

function shownForMs(durationMs) {
	return durationMs <= 10 ? 100 : durationMs;
}

function pixelDifferences(expected, actual) {
	const a = Buffer.from(expected, 'base64');
	const b = Buffer.from(actual, 'base64');
	if (a.length !== b.length) return [`${a.length / 4} pixels expected, ${b.length / 4} decoded`];
	const out = [];
	for (let at = 0; at < a.length; at += 4) {
		const transparent = a[at + 3] === 0 && b[at + 3] === 0;
		if (!transparent && a.compare(b, at, at + 4, at, at + 4) !== 0) {
			out.push(`pixel ${at / 4}: expected ${[...a.subarray(at, at + 4)]} decoded ${[...b.subarray(at, at + 4)]}`);
		}
	}
	return out;
}

function decoderDifferences(fixture, decoded) {
	if (decoded.decodeError) return fixture.decodeError === decoded.decodeError ? [] : [`decode failed: ${decoded.decodeError}`];
	const out = [];
	for (const key of ['frameCount', 'plays', 'width', 'height']) {
		if (fixture[key] !== decoded[key]) out.push(`${key}: expected ${fixture[key]} decoded ${decoded[key]}`);
	}
	if (JSON.stringify(fixture.durationsMs) !== JSON.stringify(decoded.durationsMs)) {
		out.push(`durations: expected ${fixture.durationsMs} decoded ${decoded.durationsMs}`);
	}
	(fixture.frames ?? []).forEach((frame, index) => {
		const actual = decoded.frames[index];
		if ((frame === null) !== (actual === null || actual === undefined)) out.push(`frame ${index}: decoded ${actual === null ? 'nothing' : 'a frame'}`);
		else if (frame !== null) out.push(...pixelDifferences(frame, actual).slice(0, 4).map(line => `frame ${index} ${line}`));
	});
	return out;
}

function withChromiumReference(fixture, decoded) {
	const { name, description, format, bytes } = fixture;
	if (decoded.decodeError) return { name, description, format, bytes, width: fixture.width, height: fixture.height, reference: 'chromium', decodeError: decoded.decodeError };
	return {
		name,
		description,
		format,
		bytes,
		width: decoded.width,
		height: decoded.height,
		reference: 'chromium',
		frameCount: decoded.frameCount,
		durationsMs: decoded.durationsMs,
		effectiveDurationsMs: decoded.durationsMs.map(duration => (duration === null ? null : shownForMs(duration))),
		plays: decoded.plays,
		frames: decoded.frames,
	};
}

async function withPage(html, use) {
	const server = createServer((request, response) => {
		if (request.url === '/') return response.end(html);
		response.statusCode = 404;
		response.end();
	});
	await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
	const browser = await Browser.launch(findChrome(), TIMEOUT_MS, async () => undefined);
	try {
		await browser.send('Page.enable', {}, true);
		const loaded = new Promise(resolve => browser.socket.addEventListener('message', event => {
			if (JSON.parse(event.data).method === 'Page.loadEventFired') resolve();
		}));
		await browser.send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` }, true);
		await loaded;
		return await use(async expression => {
			const answer = await browser.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, true);
			if (answer.exceptionDetails) throw new Error(answer.exceptionDetails.exception?.description ?? answer.exceptionDetails.text);
			return answer.result.value;
		}, browser);
	} finally {
		await browser.close();
		server.close();
	}
}

async function rendererPage() {
	const esbuild = createRequire(import.meta.url)('esbuild');
	const result = await esbuild.build({
		entryPoints: { artwork: path.join(here, 'animated-images/entry.ts'), styles: path.join(here, '../preview-renderer/styles.css') },
		bundle: true,
		format: 'iife',
		target: 'chrome110',
		loader: { '.svg': 'dataurl' },
		legalComments: 'none',
		write: false,
		outdir: path.join(tmpdir(), 'macro-deck-animated-images'),
		logLevel: 'warning',
	});
	const file = suffix => result.outputFiles.find(output => output.path.endsWith(suffix)).text;
	return `<!doctype html><meta charset="utf-8"><style>${file('styles.css')}</style>`
		+ '<body style="margin:0;background:#000"><div id="tile" style="position:absolute;left:20px;top:20px;width:200px;height:200px"></div>'
		+ `<script>${file('artwork.js')}</script>`;
}

const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function phaseSpread(samples) {
	let previous = null;
	let position = 0;
	const offsets = [];
	for (const sample of samples.filter(candidate => candidate.index !== null)) {
		if (previous !== null) {
			const expected = (sample.t - previous.t) / PHASE_FRAME_MS;
			let advance = ((sample.index - previous.index) % PHASE_FRAMES + PHASE_FRAMES) % PHASE_FRAMES;
			while (advance + PHASE_FRAMES <= expected + 1) advance += PHASE_FRAMES;
			position += advance;
		} else {
			position = sample.index;
		}
		offsets.push(position * PHASE_FRAME_MS - sample.t);
		previous = sample;
	}
	const latency = Math.max(...samples.map(sample => sample.latency));
	const spread = Math.max(...offsets) - Math.min(...offsets);
	const middle = (Math.max(...offsets) + Math.min(...offsets)) / 2;
	return { spread, middle, allowed: PHASE_FRAME_MS + latency + 50, readable: offsets.length };
}

function filtered(tint, brightness, saturation, opacity) {
	const bright = tint.map(channel => Math.min(255, channel * brightness));
	const s = saturation;
	const matrix = [
		[0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s],
		[0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s],
		[0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s],
	];
	return matrix.map((row, at) => {
		const value = Math.max(0, Math.min(255, row[0] * bright[0] + row[1] * bright[1] + row[2] * bright[2]));
		return value * opacity + BUTTON_BACKGROUND[at] * (1 - opacity);
	});
}

async function runtimeChecks(evaluate, browser, fixtures) {
	const failures = [];
	const phase = fixtures.find(fixture => fixture.name === 'gif-phase');
	const red = fixtures.find(fixture => fixture.name === 'gif-phase-red');
	await evaluate(`animatedArtwork.register('phase', ${JSON.stringify(phase.bytes)})`);
	await evaluate(`animatedArtwork.register('phase-red', ${JSON.stringify(red.bytes)})`);
	const source = resourceId => ({ resourceId, contentHash: resourceId });
	const shoot = async () => {
		const before = Date.now();
		const shot = await browser.send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: 260, height: 260, scale: 1 } }, true);
		return { data: shot.data, at: (before + Date.now()) / 2, latency: Date.now() - before };
	};
	const render = (type, properties) => evaluate(`animatedArtwork.render(${JSON.stringify(type)}, ${JSON.stringify(properties)})`);
	const settled = () => evaluate('animatedArtwork.settled()');

	const looks = [{ tint: '#4f8cff' }, { tint: '#ff4040' }, {}, { opacity: 0.5, zoom: 1.2, brightness: 0.8, saturation: 0.5 }, {}, { tint: '#4f8cff' }, {}];
	for (const [type, base] of [
		['ui.button', { source: source('phase'), background: '#000000' }],
		['ui.image', { source: source('phase'), size: { basis: 0.8 } }],
	]) {
		const name = `${type} appearance changes`;
		await render(type, base);
		await sleep(500);
		const samples = [];
		const started = Date.now();
		for (const [step, look] of looks.entries()) {
			await render(type, { ...base, ...look });
			const state = await settled();
			if (state.images !== 1 || state.masked !== 0) failures.push({ name, differences: [`after ${JSON.stringify(look)}: ${state.images} images, ${state.masked} masked layers`] });
			while (Date.now() - started < (step + 1) * 800) {
				const shot = await shoot();
				const read = await evaluate(`animatedArtwork.read(${JSON.stringify(shot.data)}, ${JSON.stringify(state.box)})`);
				samples.push({ t: shot.at - started, latency: shot.latency, index: read.index });
			}
		}
		const { spread, allowed, readable } = phaseSpread(samples);
		if (readable < samples.length / 2 || spread > allowed) {
			failures.push({ name, differences: [`play moved by ${Math.round(spread)} ms (allowed ${Math.round(allowed)}), ${readable} of ${samples.length} samples readable`] });
		}
		await render(type, { ...base, source: null });
	}

	const tinted = { source: source('phase'), background: '#000000', tint: '#4f8cff', brightness: 0.8, saturation: 0.5, opacity: 0.6 };
	await render('ui.button', tinted);
	await sleep(400);
	const tintState = await settled();
	const tintRead = await evaluate(`animatedArtwork.read(${JSON.stringify((await shoot()).data)}, ${JSON.stringify(tintState.box)})`);
	const expected = filtered([0x4f, 0x8c, 0xff], 0.8, 0.5, 0.6);
	if (tintRead.ring.some((channel, at) => Math.abs(channel - expected[at]) > 4)) {
		failures.push({ name: 'tint colour', differences: [`drawn ${tintRead.ring}, documented ${expected.map(Math.round)}`] });
	}
	await render('ui.button', { source: null });

	const fading = { size: { basis: 0.8 }, transition: 'crossfade', opacity: 0.8 };
	await render('ui.image', { ...fading, source: source('phase') });
	await sleep(800);
	const before = await settled();
	const started = Date.now();
	await render('ui.image', { ...fading, source: source('phase-red') });
	const samples = [];
	while (Date.now() - started < 2400) {
		const shot = await shoot();
		const read = await evaluate(`animatedArtwork.read(${JSON.stringify(shot.data)}, ${JSON.stringify(before.box)})`);
		const isRed = read.ring[0] > 150 && read.ring[1] < 60 && read.ring[2] < 60;
		samples.push({ t: shot.at - started, latency: shot.latency, index: isRed ? read.index : null });
	}
	const after = await settled();
	const shownAt = await evaluate("animatedArtwork.loadedAt('phase-red')") - started;
	// Read during its fade the incoming picture is mixed with the outgoing one, so its cells give no index.
	const promoted = phaseSpread(samples.filter(sample => sample.t >= shownAt + INCOMING_FADE_MS));
	const differences = [];
	if (promoted.readable < 4 || promoted.spread > promoted.allowed) differences.push(`the incoming play moved by ${Math.round(promoted.spread)} ms (allowed ${Math.round(promoted.allowed)})`);
	const startedOff = Math.abs(promoted.middle + shownAt + PHASE_FRAME_MS / 2);
	if (startedOff > promoted.allowed / 2) differences.push(`the incoming play is ${Math.round(startedOff)} ms off the moment it appeared, as if started over when it was promoted`);
	if (after.images !== 1) differences.push(`${after.images} images after the fade`);
	if (after.opacity !== '0.8') differences.push(`opacity ${after.opacity} after the fade`);
	for (const key of ['x', 'y', 'width', 'height']) {
		if (Math.abs(after.box[key] - before.box[key]) > 0.5) differences.push(`${key} ${before.box[key]} became ${after.box[key]}`);
	}
	if (differences.length > 0) failures.push({ name: 'crossfade promotion', differences });
	return failures;
}

const update = process.argv.includes('--update');
const document = JSON.parse(readFileSync(fixturesFile, 'utf8'));
const failures = [];

await withPage(decoderPage, async (evaluate, browser) => {
	console.log(`decoding with ${browser.version}`);
	for (const [index, fixture] of document.fixtures.entries()) {
		const decoded = await evaluate(`decodeFixture(${JSON.stringify({ bytes: fixture.bytes, format: fixture.format })})`);
		if (fixture.reference === 'chromium' && update) {
			document.fixtures[index] = withChromiumReference(fixture, decoded);
			continue;
		}
		const differences = decoderDifferences(fixture, decoded);
		if (differences.length > 0) failures.push({ name: fixture.name, differences });
	}
});

if (!update) {
	await withPage(await rendererPage(), async (evaluate, browser) => {
		failures.push(...await runtimeChecks(evaluate, browser, document.fixtures));
	});
}

if (update) {
	writeFileSync(fixturesFile, `${JSON.stringify(document, null, 1)}\n`);
	console.log('updated the Chromium references in fixtures.json');
}

if (failures.length > 0) {
	for (const { name, differences } of failures) {
		console.error(`${name}:`);
		for (const line of differences.slice(0, 12)) console.error(`  ${line}`);
	}
	process.exitCode = 1;
} else {
	console.log(`${document.fixtures.length} animated image fixtures decode in Chrome as expected, and the runtime keeps their play`);
}
