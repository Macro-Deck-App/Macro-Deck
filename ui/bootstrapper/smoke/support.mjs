import { execFile, spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, openSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createConnection, createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { crc32 } from 'node:zlib';

const run = promisify(execFile);
const bootstrapperRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

export const isWindows = process.platform === 'win32';
export const isLinux = process.platform === 'linux';

export const pluginId = 'app.macro-deck.well-behaved-test-plugin';
const pluginAssembly = 'MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin';

export function bootstrapperBinary() {
  return process.env.SMOKE_BOOTSTRAPPER
    ?? join(bootstrapperRoot, 'target', 'release', isWindows ? 'macro-deck-bootstrapper.exe' : 'macro-deck-bootstrapper');
}

// The bootstrapper looks for the host next to itself first; cargo copies host-publish there.
export function hostBinary() {
  return join(dirname(bootstrapperBinary()), 'host', isWindows ? 'MacroDeckHost.exe' : 'Macro Deck Host');
}

export function sleep(ms) {
  return new Promise(done => setTimeout(done, ms));
}

export async function until(description, condition, timeoutMs = 60_000, intervalMs = 250) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    last = await condition();
    if (last) {
      return last;
    }
    await sleep(intervalMs);
  }
  throw new Error(`timed out after ${timeoutMs} ms waiting for ${description}`);
}

export async function freePort() {
  return await new Promise((done, reject) => {
    const server = createServer();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => done(port));
    });
  });
}

export async function accepts(port) {
  return await new Promise(done => {
    const socket = createConnection({ host: '127.0.0.1', port });
    socket.setTimeout(1_000);
    socket.once('connect', () => {
      socket.destroy();
      done(true);
    });
    socket.once('timeout', () => {
      socket.destroy();
      done(false);
    });
    socket.once('error', () => done(false));
  });
}

export async function listProcesses() {
  if (isWindows) {
    const script = 'Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine'
      + ' | ConvertTo-Json -Compress';
    const { stdout } = await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script],
      { maxBuffer: 64 * 1024 * 1024 });
    return JSON.parse(stdout).map(entry => ({
      pid: entry.ProcessId,
      ppid: entry.ParentProcessId,
      pgid: null,
      executable: entry.ExecutablePath ?? '',
      command: entry.CommandLine ?? ''
    }));
  }
  const { stdout } = await run('ps', ['-A', '-ww', '-o', 'pid=,ppid=,pgid=,args='], { maxBuffer: 64 * 1024 * 1024 });
  return stdout.split('\n').filter(line => line.trim()).map(line => {
    const [, pid, ppid, pgid, command] = line.match(/^\s*(\d+)\s+(\d+)\s+(\d+)\s+(.*)$/);
    return { pid: Number(pid), ppid: Number(ppid), pgid: Number(pgid), executable: '', command };
  });
}

export async function isRunning(pid) {
  return (await listProcesses()).some(entry => entry.pid === pid);
}

function startsWithPath(entry, path) {
  if (isWindows) {
    return entry.executable.toLowerCase() === path.toLowerCase();
  }
  return entry.command === path || entry.command.startsWith(`${path} `);
}

export async function processesOf(path) {
  return (await listProcesses()).filter(entry => startsWithPath(entry, path));
}

export async function leftovers(app) {
  const processes = await listProcesses();
  return processes.filter(entry => entry.pid !== process.pid && (startsWithPath(entry, bootstrapperBinary())
    || startsWithPath(entry, hostBinary())
    || entry.command.includes(app.dataDir)));
}

export async function killLeftovers(app) {
  for (const entry of await leftovers(app)) {
    try {
      process.kill(entry.pid, 'SIGKILL');
    } catch {
      continue;
    }
  }
}

export async function pluginProcess(app, hostPid) {
  const marker = ['plugins', pluginId, 'versions'].join(isWindows ? '\\' : '/');
  return (await listProcesses()).find(entry => entry.ppid === hostPid && entry.command.includes(marker));
}

export function packFixturePlugin(buildDirectory, targetDirectory) {
  const manifest = JSON.parse(readFileSync(join(buildDirectory, 'manifest.json'), 'utf8'));
  delete manifest.$schema;
  const rid = `${isWindows ? 'win' : process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch}`;
  manifest.entrypoints = {
    [rid]: { executable: `${pluginAssembly}.dll`, runtime: { kind: 'FrameworkDependent', dotnetVersion: '10.0' } }
  };
  const entries = [{ name: 'manifest.json', data: Buffer.from(JSON.stringify(manifest)) }];
  const walk = (directory, prefix) => {
    for (const item of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, item.name);
      const name = prefix ? `${prefix}/${item.name}` : item.name;
      if (item.isDirectory()) {
        walk(path, name);
      } else if (name.toLowerCase() !== 'manifest.json') {
        entries.push({ name, data: readFileSync(path) });
      }
    }
  };
  walk(buildDirectory, '');
  const target = join(targetDirectory, `${pluginAssembly}.macroDeckPlugin`);
  writeFileSync(target, storedZip(entries));
  return target;
}

function storedZip(entries) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const { name, data } of entries) {
    const nameBytes = Buffer.from(name, 'utf8');
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    centrals.push(central, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const centralSize = centrals.reduce((size, part) => size + part.length, 0);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(centralSize, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, ...centrals, end]);
}

export class LogCursor {
  constructor(directory) {
    this.directory = directory;
    this.marks = this.sizes();
  }

  files() {
    if (!existsSync(this.directory)) {
      return [];
    }
    return readdirSync(this.directory)
      .filter(name => /^(bootstrapper|host)-.*\.log$/.test(name))
      .map(name => join(this.directory, name));
  }

  sizes() {
    return new Map(this.files().map(file => [file, readFileSync(file).length]));
  }

  text() {
    return this.files()
      .map(file => readFileSync(file).subarray(this.marks.get(file) ?? 0).toString('utf8'))
      .join('\n');
  }

  async waitFor(pattern, timeoutMs = 60_000) {
    return await until(`log line ${pattern}`, () => this.text().match(pattern), timeoutMs);
  }
}

export class SmokeApp {
  constructor(name) {
    const base = resolve(process.env.SMOKE_RUN_DIR ?? tmpdir());
    mkdirSync(base, { recursive: true });
    this.runDir = mkdtempSync(join(base, `bs-${name}-`));
    this.dataDir = join(this.runDir, 'data');
    this.tmpDir = join(this.runDir, 'tmp');
    this.homeDir = join(this.runDir, 'home');
    for (const dir of [this.dataDir, this.tmpDir, this.homeDir]) {
      mkdirSync(dir, { recursive: true });
    }
    this.identifier = process.env.SMOKE_IDENTIFIER ?? 'app.macro-deck.macrodeck.development';
    this.logsDir = join(this.dataDir, 'logs');
    this.bootstrapperOutput = join(this.runDir, 'bootstrapper-output.log');
  }

  async allocatePorts() {
    this.publicPort = await freePort();
    this.httpsPort = await freePort();
  }

  get configDir() {
    // Windows reads the roaming profile folder, not APPDATA, so only the build's identifier isolates it.
    if (isWindows) {
      return join(process.env.APPDATA, this.identifier);
    }
    if (process.platform === 'darwin') {
      return join(this.homeDir, 'Library', 'Application Support', this.identifier);
    }
    return join(this.homeDir, '.config', this.identifier);
  }

  environment() {
    const environment = {
      ...process.env,
      MACRO_DECK_DATA_DIRECTORY: this.dataDir,
      MACRO_DECK_PORT: String(this.publicPort),
      MACRO_DECK_HTTPS_PORT: String(this.httpsPort),
      TMPDIR: this.tmpDir,
      TMP: this.tmpDir,
      TEMP: this.tmpDir
    };
    if (!isWindows) {
      environment.HOME = this.homeDir;
      environment.XDG_CONFIG_HOME = join(this.homeDir, '.config');
      environment.XDG_DATA_HOME = join(this.homeDir, '.local', 'share');
      environment.XDG_CACHE_HOME = join(this.homeDir, '.cache');
    }
    return environment;
  }

  cursor() {
    return new LogCursor(this.logsDir);
  }

  readConfig(name) {
    const path = join(this.configDir, name);
    return existsSync(path) ? readFileSync(path, 'utf8').trim() : null;
  }

  writeConfig(name, value) {
    mkdirSync(this.configDir, { recursive: true });
    writeFileSync(join(this.configDir, name), value);
  }

  get loopbackPort() {
    return Number(this.readConfig('loopback-port'));
  }

  get secret() {
    return this.readConfig('loopback-secret');
  }

  launch(args = []) {
    this.writeConfig('update-mode', 'off');
    const output = openSync(this.bootstrapperOutput, 'a');
    const child = spawn(bootstrapperBinary(), args, {
      cwd: dirname(bootstrapperBinary()),
      env: this.environment(),
      stdio: ['ignore', output, output]
    });
    child.exited = new Promise(done => child.once('exit', (code, signal) => done({ code, signal })));
    return child;
  }

  async startHost({ port, secret }) {
    const output = openSync(join(this.runDir, `direct-host-${port}.log`), 'a');
    const child = spawn(hostBinary(), [], {
      cwd: dirname(hostBinary()),
      env: {
        ...this.environment(),
        MACRODECK_HOST_PORT: String(port),
        MACRODECK_LOOPBACK_SECRET: secret,
        MACRODECK_SHELL_EXECUTABLE: bootstrapperBinary()
      },
      stdio: ['ignore', output, output],
      windowsHide: true
    });
    child.exited = new Promise(done => child.once('exit', (code, signal) => done({ code, signal })));
    const api = new HostApi(port, secret);
    await until('the directly started host to answer', () => api.answers(), 90_000);
    // The port file is written only after the host already answers, and the app finds a running host by it.
    const portFile = join(this.tmpDir, 'macro-deck-host.port');
    await until('the host to publish its port file',
      () => existsSync(portFile) && readFileSync(portFile, 'utf8').trim() === String(port), 30_000);
    return { child, api };
  }
}

export class HostApi {
  constructor(port, secret) {
    this.port = port;
    this.secret = secret;
  }

  async request(method, path, body) {
    const headers = { 'X-MacroDeck-Loopback-Secret': this.secret };
    let payload = body;
    if (body !== undefined && !(body instanceof FormData)) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    const response = await fetch(`http://127.0.0.1:${this.port}${path}`, {
      method,
      headers,
      body: payload,
      signal: AbortSignal.timeout(30_000)
    });
    const text = await response.text();
    if (!response.ok) {
      throw new Error(`${method} ${path} answered ${response.status}: ${text.slice(0, 500)}`);
    }
    return text ? JSON.parse(text) : null;
  }

  async answers() {
    try {
      const response = await fetch(`http://127.0.0.1:${this.port}/api/auth/status`,
        { signal: AbortSignal.timeout(2_000) });
      return response.ok;
    } catch {
      return false;
    }
  }

  async pluginRuntime() {
    const runtime = await this.request('GET', '/api/plugin-runtime');
    return runtime.plugins.find(plugin => plugin.pluginId === pluginId);
  }

  async waitForHealthyPlugin(timeoutMs = 90_000) {
    return await until('the fixture plugin to run healthy', async () => {
      const plugin = await this.pluginRuntime().catch(() => null);
      return plugin?.state === 'running' && plugin.health === 'healthy' && plugin.processId ? plugin : null;
    }, timeoutMs, 500);
  }

  async installPlugin(artifact) {
    const form = new FormData();
    form.append('file', new Blob([readFileSync(artifact)], { type: 'application/octet-stream' }), basename(artifact));
    return await this.request('POST', '/api/plugin-installation/install?allowUnsigned=true', form);
  }
}

export function newSecret() {
  return randomBytes(32).toString('hex');
}
