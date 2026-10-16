import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import path from 'node:path';

export function findChrome() {
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

export class Browser {
	static async launch(binary, timeoutMs, configure) {
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
			if (Date.now() - started > timeoutMs) throw new Error(`Chrome did not start: ${binary}`);
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
		await configure(browser);
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
