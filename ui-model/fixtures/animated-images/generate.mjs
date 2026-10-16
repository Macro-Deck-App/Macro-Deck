import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const target = path.join(here, 'fixtures.json');

const CLEAR = [0, 0, 0, 0];
const RED = [255, 0, 0, 255];
const GREEN = [0, 255, 0, 255];
const BLUE = [0, 0, 255, 255];
const YELLOW = [255, 255, 0, 255];
const CYAN = [0, 255, 255, 255];
const MAGENTA = [255, 0, 255, 255];
const WHITE = [255, 255, 255, 255];
const GREY = [128, 128, 128, 255];
const PALETTE = [RED, GREEN, BLUE, YELLOW, CYAN, MAGENTA, WHITE, GREY];

class BitWriter {
	constructor() {
		this.bytes = [];
		this.pending = 0;
		this.count = 0;
	}

	write(value, width) {
		for (let bit = 0; bit < width; bit++) {
			this.pending |= ((value >> bit) & 1) << this.count;
			if (++this.count === 8) {
				this.bytes.push(this.pending);
				this.pending = 0;
				this.count = 0;
			}
		}
	}

	finish() {
		if (this.count > 0) this.bytes.push(this.pending);
		this.pending = 0;
		this.count = 0;
		return this.bytes;
	}
}

function u16(value) {
	return [value & 0xff, (value >> 8) & 0xff];
}

function u24(value) {
	return [value & 0xff, (value >> 8) & 0xff, (value >> 16) & 0xff];
}

function u32(value) {
	return [value & 0xff, (value >> 8) & 0xff, (value >> 16) & 0xff, (value >>> 24) & 0xff];
}

function ascii(text) {
	return [...text].map(character => character.charCodeAt(0));
}

function lzw(indices, minCodeSize, clearWhenFull) {
	const clear = 1 << minCodeSize;
	const end = clear + 1;
	const bits = new BitWriter();
	let width = minCodeSize + 1;
	let next = end + 1;
	let table = new Map();
	const emit = code => {
		bits.write(code, width);
		if (next > (1 << width) - 1 && width < 12) width++;
	};
	bits.write(clear, width);
	if (indices.length === 0) {
		bits.write(end, width);
		return bits.finish();
	}
	let prefix = indices[0];
	for (let index = 1; index < indices.length; index++) {
		const symbol = indices[index];
		const known = table.get(prefix * 256 + symbol);
		if (known !== undefined) {
			prefix = known;
			continue;
		}
		emit(prefix);
		if (next < 4096) {
			table.set(prefix * 256 + symbol, next++);
		} else if (clearWhenFull) {
			bits.write(clear, width);
			table = new Map();
			next = end + 1;
			width = minCodeSize + 1;
		}
		prefix = symbol;
	}
	emit(prefix);
	bits.write(end, width);
	return bits.finish();
}

function subBlocks(bytes) {
	const out = [];
	for (let at = 0; at < bytes.length; at += 255) {
		const block = bytes.slice(at, at + 255);
		out.push(block.length, ...block);
	}
	out.push(0);
	return out;
}

function tableBits(entries) {
	let bits = 1;
	while (1 << bits < entries) bits++;
	return bits;
}

function colourTable(palette) {
	const bits = tableBits(palette.length);
	const padded = [...palette];
	while (padded.length < 1 << bits) padded.push([0, 0, 0, 255]);
	return { bits, bytes: padded.flatMap(([red, green, blue]) => [red, green, blue]) };
}

function interlacedRows(height) {
	const rows = [];
	for (const [start, step] of [[0, 8], [4, 8], [2, 4], [1, 2]]) {
		for (let row = start; row < height; row += step) rows.push(row);
	}
	return rows;
}

function gif({ width, height, palette, background = 0, loop = null, frames, cutAfterFrameData = null }) {
	const out = [...ascii('GIF89a'), ...u16(width), ...u16(height)];
	if (palette) {
		const table = colourTable(palette);
		out.push(0x80 | 0x70 | (table.bits - 1), background, 0, ...table.bytes);
	} else {
		out.push(0x70, 0, 0);
	}
	if (loop !== null) out.push(0x21, 0xff, 0x0b, ...ascii('NETSCAPE2.0'), 0x03, 0x01, ...u16(loop), 0x00);
	for (const [index, frame] of frames.entries()) {
		const transparent = frame.transparent ?? null;
		out.push(0x21, 0xf9, 0x04, (frame.disposal ?? 0) << 2 | (transparent === null ? 0 : 1), ...u16(frame.delay), transparent ?? 0, 0x00);
		const local = frame.palette ? colourTable(frame.palette) : null;
		const packed = (local ? 0x80 | (local.bits - 1) : 0) | (frame.interlaced ? 0x40 : 0);
		out.push(0x2c, ...u16(frame.x ?? 0), ...u16(frame.y ?? 0), ...u16(frame.width), ...u16(frame.height), packed);
		if (local) out.push(...local.bytes);
		const order = frame.interlaced ? interlacedRows(frame.height) : [...Array(frame.height).keys()];
		const indices = order.flatMap(row => frame.indices.slice(row * frame.width, (row + 1) * frame.width));
		const minCodeSize = frame.minCodeSize ?? Math.max(2, tableBits((frame.palette ?? palette ?? [0, 0]).length));
		const data = subBlocks(lzw(indices, Math.min(minCodeSize, 11), frame.clearWhenFull ?? true));
		out.push(minCodeSize);
		if (cutAfterFrameData !== null && index === cutAfterFrameData.frame) {
			out.push(...data.slice(0, cutAfterFrameData.bytes));
			return out;
		}
		out.push(...data);
	}
	out.push(0x3b);
	return out;
}

function vp8l(width, height, [red, green, blue, alpha]) {
	const bits = new BitWriter();
	bits.write(0x2f, 8);
	bits.write(width - 1, 14);
	bits.write(height - 1, 14);
	bits.write(1, 1);
	bits.write(0, 3);
	bits.write(0, 1);
	bits.write(0, 1);
	bits.write(0, 1);
	for (const symbol of [green, red, blue, alpha, 0]) {
		bits.write(1, 1);
		bits.write(0, 1);
		bits.write(1, 1);
		bits.write(symbol, 8);
	}
	return bits.finish();
}

function chunk(fourCc, payload) {
	return [...ascii(fourCc), ...u32(payload.length), ...payload, ...(payload.length % 2 === 1 ? [0] : [])];
}

function webp({ width, height, loop, frames }) {
	const body = [
		...ascii('WEBP'),
		...chunk('VP8X', [0x12, 0, 0, 0, ...u24(width - 1), ...u24(height - 1)]),
		...chunk('ANIM', [0, 0, 0, 0, ...u16(loop)]),
	];
	for (const frame of frames) {
		const flags = (frame.blend ? 0 : 0x02) | (frame.dispose ? 0x01 : 0);
		body.push(...chunk('ANMF', [
			...u24((frame.x ?? 0) / 2),
			...u24((frame.y ?? 0) / 2),
			...u24(frame.width - 1),
			...u24(frame.height - 1),
			...u24(frame.duration),
			flags,
			...chunk('VP8L', vp8l(frame.width, frame.height, frame.colour)),
		]));
	}
	return [...ascii('RIFF'), ...u32(body.length), ...body];
}

function blankCanvas(width, height) {
	return Array.from({ length: width * height }, () => CLEAR);
}

function clearRect(canvas, width, height, frame) {
	for (let y = frame.y ?? 0; y < Math.min(height, (frame.y ?? 0) + frame.height); y++) {
		for (let x = frame.x ?? 0; x < Math.min(width, (frame.x ?? 0) + frame.width); x++) canvas[y * width + x] = CLEAR;
	}
}

function gifFrames({ width, height, palette, frames }) {
	let canvas = blankCanvas(width, height);
	let restore = null;
	const out = [];
	for (const [index, frame] of frames.entries()) {
		if (index > 0) {
			const previous = frames[index - 1];
			if (previous.disposal === 2) clearRect(canvas, width, height, previous);
			if (previous.disposal === 3) canvas = restore;
		}
		if (frame.disposal === 3) restore = [...canvas];
		const colours = frame.palette ?? palette;
		for (let y = 0; y < frame.height; y++) {
			for (let x = 0; x < frame.width; x++) {
				const canvasX = (frame.x ?? 0) + x;
				const canvasY = (frame.y ?? 0) + y;
				const colourIndex = frame.indices[y * frame.width + x];
				if (canvasX >= width || canvasY >= height || colourIndex === frame.transparent) continue;
				canvas[canvasY * width + canvasX] = colours[colourIndex];
			}
		}
		out.push([...canvas]);
	}
	return out;
}

function webpFrames({ width, height, frames }) {
	const canvas = blankCanvas(width, height);
	const out = [];
	for (const [index, frame] of frames.entries()) {
		if (index > 0 && frames[index - 1].dispose) clearRect(canvas, width, height, frames[index - 1]);
		for (let y = frame.y ?? 0; y < (frame.y ?? 0) + frame.height; y++) {
			for (let x = frame.x ?? 0; x < (frame.x ?? 0) + frame.width; x++) {
				if (!frame.blend || frame.colour[3] === 255) canvas[y * width + x] = frame.colour;
			}
		}
		out.push([...canvas]);
	}
	return out;
}

function shownForMs(durationMs) {
	return durationMs <= 10 ? 100 : durationMs;
}

function timeline(durationsMs, plays) {
	const effective = durationsMs.map(shownForMs);
	const loop = effective.reduce((sum, duration) => sum + duration, 0);
	const samples = [];
	let start = 0;
	for (const [frame, duration] of effective.entries()) {
		samples.push({ t: start, frame }, { t: start + duration - 1, frame });
		start += duration;
	}
	if (plays === null) {
		samples.push({ t: loop, frame: 0 }, { t: 7 * loop + effective[0], frame: Math.min(1, effective.length - 1) });
	} else {
		if (plays > 1) samples.push({ t: loop, frame: 0 });
		samples.push({ t: plays * loop, frame: effective.length - 1 }, { t: plays * loop + 10000, frame: effective.length - 1 });
	}
	return samples;
}

function base64(bytes) {
	return Buffer.from(bytes).toString('base64');
}

function rgba(canvas) {
	return base64(canvas.flat());
}

function solid(width, height, index) {
	return Array(width * height).fill(index);
}

function pseudoRandom(count, seed) {
	const out = [];
	let state = seed;
	for (let index = 0; index < count; index++) {
		state = (state * 1103515245 + 12345) & 0x7fffffff;
		out.push(state >> 16 & 0xff);
	}
	return out;
}

function gifFixture(name, description, spec, { reference = 'model', plays }) {
	const bytes = gif(spec);
	const durationsMs = spec.frames.map(frame => frame.delay * 10);
	const fixture = { name, description, format: 'gif', bytes: base64(bytes), width: spec.width, height: spec.height };
	if (reference === 'chromium') return { ...fixture, reference };
	return {
		...fixture,
		reference,
		frameCount: spec.frames.length,
		durationsMs,
		effectiveDurationsMs: durationsMs.map(shownForMs),
		plays,
		timeline: timeline(durationsMs, plays),
		frames: gifFrames(spec).map(rgba),
	};
}

function webpFixture(name, description, spec) {
	const durationsMs = spec.frames.map(frame => frame.duration);
	const plays = spec.loop === 0 ? null : spec.loop;
	return {
		name,
		description,
		format: 'webp',
		bytes: base64(webp(spec)),
		width: spec.width,
		height: spec.height,
		reference: 'model',
		frameCount: spec.frames.length,
		durationsMs,
		effectiveDurationsMs: durationsMs.map(shownForMs),
		plays,
		timeline: timeline(durationsMs, plays),
		frames: webpFrames(spec).map(rgba),
	};
}

const full = (index, delay, extra = {}) => ({ width: 4, height: 4, delay, indices: solid(4, 4, index), ...extra });
const fullWebp = (colour, duration) => ({ width: 4, height: 4, duration, colour });
const codeSizeFrame = minCodeSize => {
	const colours = Array.from({ length: 1 << minCodeSize }, (_, index) => [index * 7 % 256, 255 - index, index * 3 % 256, 255]);
	return { width: 4, height: 4, delay: 10, palette: colours, minCodeSize, indices: Array.from({ length: 16 }, (_, index) => (index * 5 + minCodeSize) % colours.length) };
};
const phaseFrame = (index, delay) => {
	const bits = [...Array(8).keys()].map(bit => (index >> bit) & 1);
	bits.push(bits.reduce((sum, bit) => sum + bit, 0) % 2);
	const indices = [];
	for (let y = 0; y < 20; y++) {
		for (let x = 0; x < 20; x++) {
			const row = Math.floor(y / 4);
			const column = Math.floor(x / 4);
			const ring = row === 0 || row === 4 || column === 0 || column === 4;
			indices.push(ring || bits[(row - 1) * 3 + column - 1] === 1 ? 1 : 0);
		}
	}
	return { width: 20, height: 20, delay, indices, transparent: 0, disposal: 2 };
};
const noise = pseudoRandom(64 * 64, 7);
const noisePalette = Array.from({ length: 256 }, (_, index) => [index, 255 - index, index * 13 % 256, 255]);

const fixtures = [
	gifFixture('gif-timing', 'Six full frames of 0, 1, 2, 5, 10 and 25 hundredths, looping for ever: 0 and 1 count 100 ms.', {
		width: 4, height: 4, palette: PALETTE, loop: 0,
		frames: [full(0, 0), full(1, 1), full(2, 2), full(3, 5), full(4, 10), full(5, 25)],
	}, { plays: null }),
	gifFixture('gif-plays-once', 'No loop extension: plays once and holds its last frame.', {
		width: 4, height: 4, palette: PALETTE, frames: [full(0, 10), full(1, 10), full(2, 10)],
	}, { plays: 1 }),
	gifFixture('gif-plays-three', 'Loop count 2: plays three times.', {
		width: 4, height: 4, palette: PALETTE, loop: 2, frames: [full(3, 20), full(4, 20)],
	}, { plays: 3 }),
	gifFixture('gif-compositing', 'Partial frames with transparency and disposal none, background, previous and unspecified.', {
		width: 4, height: 4, palette: PALETTE, loop: 0,
		frames: [
			{ ...full(0, 20), disposal: 1 },
			{ x: 1, y: 1, width: 2, height: 2, delay: 20, indices: solid(2, 2, 1), disposal: 2 },
			{ x: 2, y: 2, width: 2, height: 2, delay: 20, indices: [2, 7, 2, 2], transparent: 7, disposal: 3 },
			{ x: 0, y: 3, width: 1, height: 1, delay: 20, indices: [0], palette: [WHITE], disposal: 0 },
			{ ...full(7, 20), indices: [...solid(4, 4, 7).slice(0, 5), 3, ...solid(4, 4, 7).slice(6)], transparent: 7, disposal: 1 },
		],
	}, { plays: null }),
	gifFixture('gif-disposal-previous-first', 'A first frame disposed to previous, which is the empty canvas.', {
		width: 4, height: 4, palette: PALETTE, loop: 0,
		frames: [{ ...full(0, 20), disposal: 3 }, { x: 2, y: 0, width: 2, height: 2, delay: 20, indices: solid(2, 2, 2) }],
	}, { plays: null }),
	gifFixture('gif-interlaced', 'An interlaced 8x8 frame whose rows each have their own colour, then a plain frame.', {
		width: 8, height: 8, palette: PALETTE, loop: 0,
		frames: [
			{ width: 8, height: 8, delay: 20, interlaced: true, indices: [...Array(8).keys()].flatMap(row => Array(8).fill(row)) },
			{ width: 8, height: 8, delay: 20, indices: solid(8, 8, 6) },
		],
	}, { plays: null }),
	gifFixture('gif-code-sizes', 'Frames compressed with minimum code sizes 2 to 8, each with a local table of that size.', {
		width: 4, height: 4, palette: PALETTE, loop: 0, frames: [2, 3, 4, 5, 6, 7, 8].map(codeSizeFrame),
	}, { plays: null }),
	gifFixture('gif-table-full-clear', 'A 64x64 noise frame that fills the 4096 code table, which the encoder clears.', {
		width: 64, height: 64, palette: noisePalette, loop: 0,
		frames: [{ width: 64, height: 64, delay: 20, indices: noise, minCodeSize: 8 }, { width: 64, height: 64, delay: 20, indices: solid(64, 64, 9), minCodeSize: 8 }],
	}, { plays: null }),
	gifFixture('gif-table-full-no-clear', 'The same noise with a full table that is never cleared: later codes reuse the full table.', {
		width: 64, height: 64, palette: noisePalette, loop: 0,
		frames: [{ width: 64, height: 64, delay: 20, indices: noise, minCodeSize: 8, clearWhenFull: false }, { width: 64, height: 64, delay: 20, indices: solid(64, 64, 9), minCodeSize: 8 }],
	}, { plays: null }),
	gifFixture('gif-truncated', 'The data of the second frame stops half way and the file ends there.', {
		width: 4, height: 4, palette: PALETTE, loop: 0, cutAfterFrameData: { frame: 1, bytes: 4 },
		frames: [full(0, 20), full(1, 20)],
	}, { reference: 'chromium' }),
	gifFixture('gif-frame-beyond-screen', 'A second frame placed partly outside the logical screen.', {
		width: 4, height: 4, palette: PALETTE, loop: 0,
		frames: [full(0, 20), { x: 2, y: 2, width: 4, height: 4, delay: 20, indices: solid(4, 4, 2) }],
	}, { reference: 'chromium' }),
	gifFixture('gif-no-colour-table', 'Neither a global nor a local colour table.', {
		width: 4, height: 4, loop: 0, frames: [full(0, 20), full(1, 20)],
	}, { reference: 'chromium' }),
	gifFixture('gif-zero-size-frame', 'A frame of zero width between two full frames.', {
		width: 4, height: 4, palette: PALETTE, loop: 0,
		frames: [full(0, 20), { x: 0, y: 0, width: 0, height: 4, delay: 20, indices: [] }, full(2, 20)],
	}, { reference: 'chromium' }),
	gifFixture('gif-index-beyond-table', 'Indices up to 7 against a local colour table of two entries.', {
		width: 4, height: 4, palette: PALETTE, loop: 0,
		frames: [full(0, 20), { width: 4, height: 4, delay: 20, palette: [GREEN, BLUE], minCodeSize: 3, indices: [...Array(16).keys()].map(index => index % 8) }],
	}, { reference: 'chromium' }),
	gifFixture('gif-invalid-code-size', 'A second frame declaring a minimum code size of 12.', {
		width: 4, height: 4, palette: PALETTE, loop: 0, frames: [full(0, 20), { ...full(1, 20), minCodeSize: 12 }],
	}, { reference: 'chromium' }),
	gifFixture('gif-disposal-four', 'A square with disposal method 4, then a pixel drawn over what the disposal left.', {
		width: 4, height: 4, palette: PALETTE, loop: 0,
		frames: [full(0, 20), { x: 1, y: 1, width: 2, height: 2, delay: 20, disposal: 4, indices: solid(2, 2, 1) },
			{ x: 0, y: 0, width: 1, height: 1, delay: 20, indices: solid(1, 1, 2) }],
	}, { reference: 'chromium' }),
	gifFixture('gif-first-frame-beyond-screen', 'A first frame larger than the 2x2 logical screen.', {
		width: 2, height: 2, palette: PALETTE, loop: 0, frames: [full(0, 20), full(1, 20)],
	}, { reference: 'chromium' }),
	gifFixture('gif-zero-screen', 'A logical screen of 0x0 holding two full frames.', {
		width: 0, height: 0, palette: PALETTE, loop: 0, frames: [full(0, 20), full(1, 20)],
	}, { reference: 'chromium' }),
	gifFixture('gif-phase', 'Eight frames of 200 ms whose index is drawn as opaque and clear cells inside an opaque ring.', {
		width: 20, height: 20, palette: [CLEAR, WHITE], loop: 0, frames: [...Array(8).keys()].map(index => phaseFrame(index, 20)),
	}, { plays: null }),
	gifFixture('gif-phase-red', 'The same eight frames in red, to tell an incoming picture from the outgoing one.', {
		width: 20, height: 20, palette: [CLEAR, RED], loop: 0, frames: [...Array(8).keys()].map(index => phaseFrame(index, 20)),
	}, { plays: null }),
	webpFixture('webp-timing', 'Six full frames of 0, 10, 15, 33, 100 and 250 ms, looping for ever: 0 and 10 count 100 ms.', {
		width: 4, height: 4, loop: 0,
		frames: [fullWebp(RED, 0), fullWebp(GREEN, 10), fullWebp(BLUE, 15), fullWebp(YELLOW, 33), fullWebp(CYAN, 100), fullWebp(MAGENTA, 250)],
	}),
	webpFixture('webp-plays-two', 'Loop count 2: plays twice.', {
		width: 4, height: 4, loop: 2, frames: [fullWebp(RED, 120), fullWebp(BLUE, 120)],
	}),
	webpFixture('webp-compositing', 'Partial frames with and without blending and disposed to the background.', {
		width: 8, height: 8, loop: 0,
		frames: [
			{ width: 8, height: 8, duration: 200, colour: RED },
			{ x: 2, y: 2, width: 4, height: 4, duration: 200, colour: GREEN, blend: true, dispose: true },
			{ x: 0, y: 0, width: 4, height: 4, duration: 200, colour: CLEAR },
			{ x: 4, y: 0, width: 4, height: 4, duration: 200, colour: CLEAR, blend: true },
			{ x: 4, y: 4, width: 4, height: 4, duration: 200, colour: BLUE, blend: true },
		],
	}),
];

function withReferenceFrom(previous, fixture) {
	if (fixture.reference !== 'chromium') return fixture;
	const known = previous?.fixtures?.find(candidate => candidate.name === fixture.name && candidate.bytes === fixture.bytes);
	const { name, description, format, bytes, width, height, reference } = fixture;
	return known ? { ...known, description } : { name, description, format, bytes, width, height, reference };
}

const previous = existsSync(target) ? JSON.parse(readFileSync(target, 'utf8')) : null;
const result = { version: 1, fixtures: fixtures.map(fixture => withReferenceFrom(previous, fixture)) };
const text = `${JSON.stringify(result, null, 1)}\n`;

if (process.argv.includes('--check')) {
	if (!existsSync(target) || readFileSync(target, 'utf8') !== text) {
		console.error('fixtures.json is not what generate.mjs writes; run node ui-model/fixtures/animated-images/generate.mjs');
		process.exit(1);
	}
	const missing = result.fixtures.filter(fixture => fixture.reference === 'chromium' && !fixture.frames);
	if (missing.length > 0) {
		console.error(`no Chromium reference yet for ${missing.map(fixture => fixture.name).join(', ')}; run node ui/scripts/animated-images.mjs --update`);
		process.exit(1);
	}
	console.log(`${result.fixtures.length} animated image fixtures are current`);
} else {
	writeFileSync(target, text);
	console.log(`wrote ${result.fixtures.length} fixtures to ${path.relative(process.cwd(), target)}`);
}
