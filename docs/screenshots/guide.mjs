import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import path from 'node:path';

const [shotsFile, outDir] = process.argv.slice(2);
if (!shotsFile || !outDir) {
	console.error('usage: node screenshots/guide.mjs <shots.json> <outDir>');
	process.exit(2);
}
const shots = JSON.parse(readFileSync(shotsFile, 'utf8'));
mkdirSync(outDir, { recursive: true });

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
	throw new Error('no chrome-headless-shell found; set CHROME');
}

const port = 9333;
const chrome = spawn(findChrome(), [
	`--remote-debugging-port=${port}`,
	`--user-data-dir=${path.join(tmpdir(), 'macro-deck-guide-shots')}`,
	'--hide-scrollbars',
	'--no-first-run',
	'about:blank',
], { stdio: 'ignore' });

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let targets;
for (let attempt = 0; attempt < 50 && !targets; attempt++) {
	try {
		targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
	} catch {
		await sleep(200);
	}
}
const page = targets.find(target => target.type === 'page');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(resolve => socket.addEventListener('open', resolve, { once: true }));

let nextId = 1;
const pending = new Map();
socket.addEventListener('message', event => {
	const message = JSON.parse(event.data);
	pending.get(message.id)?.(message);
	pending.delete(message.id);
});
const send = (method, params = {}) => new Promise(resolve => {
	const id = nextId++;
	pending.set(id, resolve);
	socket.send(JSON.stringify({ id, method, params }));
});

await send('Page.enable');
await send('Runtime.enable');
for (const shot of shots) {
	await send('Emulation.setDeviceMetricsOverride', {
		width: shot.width ?? 1280,
		height: shot.height ?? 800,
		deviceScaleFactor: 2,
		mobile: false,
	});
	if (shot.url) {
		await send('Page.navigate', { url: shot.url });
		await sleep(shot.wait ?? 4000);
	}
	for (const step of shot.steps ?? []) {
		const response = await send('Runtime.evaluate', { expression: step, awaitPromise: true, returnByValue: true });
		const value = response.result?.result?.value ?? response.result?.exceptionDetails?.exception?.description;
		if (value !== undefined) console.log(`  [${shot.name}] ${String(value).slice(0, 200)}`);
		await sleep(shot.stepWait ?? 1500);
	}
	const params = { format: 'png' };
	if (shot.clip) params.clip = { ...shot.clip, scale: 1 };
	const response = await send('Page.captureScreenshot', params);
	const file = path.join(outDir, `${shot.name}.png`);
	writeFileSync(file, Buffer.from(response.result.data, 'base64'));
	console.log('wrote', file);
}
socket.close();
chrome.kill();
