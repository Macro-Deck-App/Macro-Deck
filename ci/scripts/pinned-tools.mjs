#!/usr/bin/env node
// Downloads release tooling only from pinned URLs and installs a file only after its SHA-256 matches.
// See engineering/development/releasing.md.

import { createHash } from 'node:crypto';
import { chmodSync, createReadStream, createWriteStream, existsSync, mkdirSync, renameSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';

export const APPRUN_SHA256 = 'f30140a43a0a59e46db21bdefdf749b9e9f2c6946e92afabbacf98b8ae73fb4f';

// The file names are the ones tauri-bundler looks for in its tools directory before downloading
// anything itself, so a seeded directory makes the bundle use exactly these files.
export const TOOL_SETS = {
	'linuxdeploy-x86_64': [
		{
			file: 'AppRun-x86_64',
			url: 'https://github.com/tauri-apps/binary-releases/releases/download/apprun-old/AppRun-x86_64',
			sha256: APPRUN_SHA256,
			executable: true
		},
		{
			file: 'linuxdeploy-x86_64.AppImage',
			url: 'https://github.com/tauri-apps/binary-releases/releases/download/linuxdeploy/linuxdeploy-x86_64.AppImage',
			sha256: 'e762bea85c8eb0d4b3508d46e5c1f037f717d0f9303ae3b4aafc8b04991fa1ef',
			executable: true
		},
		{
			file: 'linuxdeploy-plugin-gtk.sh',
			url: 'https://raw.githubusercontent.com/tauri-apps/linuxdeploy-plugin-gtk/dda522bce37387f1b853d9095713bfaa924c8423/linuxdeploy-plugin-gtk.sh',
			sha256: '7804c9eef13e59bf2783aad9882ef9db8f3f3f9e8d631874b1d348d550a3693f',
			executable: true
		},
		{
			file: 'linuxdeploy-plugin-gstreamer.sh',
			url: 'https://raw.githubusercontent.com/tauri-apps/linuxdeploy-plugin-gstreamer/2a2e67491c32995a3f279ad0ecbe77abd512b42a/linuxdeploy-plugin-gstreamer.sh',
			sha256: 'c107b49d84edbffc6ab226ed1007e0626a4f7aa2c3a36b7782bef62351d49e94',
			executable: true
		},
		{
			file: 'linuxdeploy-plugin-appimage.AppImage',
			url: 'https://github.com/linuxdeploy/linuxdeploy-plugin-appimage/releases/download/1-alpha-20250213-1/linuxdeploy-plugin-appimage-x86_64.AppImage',
			sha256: '992d502a248e14ab185448ddf6f6e7d25558cb84d4623c354c3af350c25fccb3',
			executable: true
		}
	],
	'codesigntool-windows': [
		{
			file: 'CodeSignTool-v1.3.0-windows.zip',
			url: 'https://github.com/SSLcom/CodeSignTool/releases/download/v1.3.0/CodeSignTool-v1.3.0-windows.zip',
			sha256: 'e22094505decbe622afe5b0c27abc618ed2ba179bd94f3450490352399d5ef2a',
			executable: false
		}
	]
};

export class PinnedToolError extends Error {}

// A bare "fetch failed" names neither the URL nor the network error behind it, which sits in error.cause.
export async function fetchWithRetry(url, attempts = 4) {
	for (let attempt = 1; ; attempt++) {
		try {
			return await fetch(url, { signal: AbortSignal.timeout(120_000) });
		} catch (error) {
			const cause = error.cause ? ` (${error.cause.code ?? error.cause.name}: ${error.cause.message})` : '';
			const message = `fetching ${url} failed: ${error.message}${cause}`;
			if (attempt === attempts) {
				throw new Error(message);
			}
			console.error(`${message} - retrying`);
			await new Promise(resolve => setTimeout(resolve, 5_000 * attempt));
		}
	}
}

export async function sha256File(path) {
	const hash = createHash('sha256');
	await pipeline(createReadStream(path), hash);
	return hash.digest('hex');
}

export async function downloadVerified(url, sha256, dest, { executable = false, attempts = 4, retryDelayMs = 5_000 } = {}) {
	const partial = `${dest}.partial-${process.pid}`;
	try {
		for (let attempt = 1; ; attempt++) {
			try {
				await downloadTo(url, partial);
				break;
			} catch (error) {
				if (error instanceof PinnedToolError) {
					throw error;
				}
				const cause = error.cause ? ` (${error.cause.code ?? error.cause.name}: ${error.cause.message})` : '';
				const message = `downloading ${url} failed: ${error.message}${cause}`;
				if (attempt === attempts) {
					throw new Error(message);
				}
				console.error(`${message} - retrying`);
				await new Promise(resolve => setTimeout(resolve, retryDelayMs * attempt));
			}
		}
		const actual = await sha256File(partial);
		if (actual !== sha256) {
			throw new PinnedToolError(
				`${url} has sha256 ${actual}, expected ${sha256}. Review the new upstream file before changing the pin.`
			);
		}
		if (executable) {
			chmodSync(partial, 0o755);
		}
		renameSync(partial, dest);
	} finally {
		rmSync(partial, { force: true });
	}
}

async function downloadTo(url, path) {
	const response = await fetch(url, { signal: AbortSignal.timeout(900_000) });
	if (!response.ok || !response.body) {
		throw new PinnedToolError(`downloading ${url} failed: HTTP ${response.status}`);
	}
	await pipeline(Readable.fromWeb(response.body), createWriteStream(path));
}

export async function fetchTools(entries, dir, options = {}) {
	mkdirSync(dir, { recursive: true });
	for (const entry of entries) {
		const dest = join(dir, entry.file);
		if (existsSync(dest)) {
			const actual = await sha256File(dest);
			if (actual !== entry.sha256) {
				throw new PinnedToolError(`${dest} has sha256 ${actual}, expected ${entry.sha256}; refusing to use or replace it`);
			}
			console.log(`${entry.file}: present and verified`);
			continue;
		}
		await downloadVerified(entry.url, entry.sha256, dest, { ...options, executable: entry.executable });
		console.log(`${entry.file}: downloaded and verified`);
	}
}

export function selectTools(set, files = []) {
	const entries = TOOL_SETS[set];
	if (!entries) {
		throw new PinnedToolError(`unknown tool set ${set}; known: ${Object.keys(TOOL_SETS).join(', ')}`);
	}
	if (files.length === 0) {
		return entries;
	}
	return files.map(file => {
		const entry = entries.find(candidate => candidate.file === file);
		if (!entry) {
			throw new PinnedToolError(`${file} is not part of tool set ${set}`);
		}
		return entry;
	});
}

const USAGE = 'usage: pinned-tools.mjs fetch <set> <dir> [file...]';

async function main([command, set, dir, ...files]) {
	if (command !== 'fetch' || !set || !dir) {
		throw new PinnedToolError(USAGE);
	}
	await fetchTools(selectTools(set, files), dir);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
	main(process.argv.slice(2)).catch(error => {
		console.error(`error: ${error.message}`);
		process.exit(1);
	});
}
