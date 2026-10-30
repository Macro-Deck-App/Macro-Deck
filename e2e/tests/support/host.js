import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { appendFileSync, existsSync, mkdirSync, mkdtempSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const RESTART_EXIT_CODE = 86;

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');

// A worker that dies without running afterAll must not leave hosts behind holding ports and files.
const running = new Set();
process.on('exit', () => {
  for (const child of running) {
    child.kill('SIGKILL');
  }
});

function defaultHostBinary() {
  const publishDir = join(repositoryRoot, 'ui', 'bootstrapper', 'host-publish');
  const candidates = process.platform === 'win32'
    ? ['MacroDeckHost.exe', 'Macro Deck Host.exe']
    : ['Macro Deck Host', 'MacroDeckHost'];
  return candidates.map(name => join(publishDir, name)).find(path => existsSync(path))
    ?? join(publishDir, candidates[0]);
}

export function hostBinary() {
  return process.env.MACRO_DECK_HOST_BINARY ?? defaultHostBinary();
}

// The OS hands out the port and the socket is closed again before the host binds it, so another
// process can take it in between. start() treats a host that never answers as that race and retries.
export async function freePort() {
  return await new Promise((resolvePort, reject) => {
    const server = createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolvePort(port));
    });
  });
}

async function listenOn(server, port, host) {
  await new Promise((resolveListen, reject) => {
    server.once('error', reject);
    server.listen(port, host, resolveListen);
  });
}

// Holds a port the way another program on the machine would, for tests of a host that finds it taken.
// Dual-stack first: on Windows an IPv4-only holder still lets the host bind the IPv6 wildcard.
export async function occupyPort(port) {
  const server = createServer(socket => socket.destroy());
  try {
    await listenOn(server, port, '::');
  } catch {
    await listenOn(server, port, '0.0.0.0');
  }
  return { port, close: () => new Promise(done => server.close(() => done())) };
}

function timestamp() {
  return new Date().toISOString();
}

async function answers(url) {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(2_000) });
    return response.ok;
  } catch {
    return false;
  }
}

async function until(condition, timeoutMs, intervalMs = 250) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await condition()) {
      return true;
    }
    await new Promise(done => setTimeout(done, intervalMs));
  }
  return false;
}

// One staged production host with its own data directory, ports, temp directory and loopback secret.
// It plays the bootstrapper's supervisor role: an exit with the restart code relaunches the process.
export class MacroDeckHost {
  constructor(name, options = {}) {
    this.name = name;
    this.options = options;
    const base = process.env.E2E_RUN_DIR ?? tmpdir();
    mkdirSync(base, { recursive: true });
    this.runDir = mkdtempSync(join(base, `md-e2e-${name.replace(/[^a-z0-9]+/gi, '-').slice(0, 24)}-`));
    this.dataDir = join(this.runDir, 'data');
    this.tmpDir = join(this.runDir, 'tmp');
    mkdirSync(this.dataDir, { recursive: true });
    mkdirSync(this.tmpDir, { recursive: true });
    this.hostLog = join(this.runDir, 'macro-deck-host.log');
    this.supervisorLog = join(this.runDir, 'macro-deck-host-supervisor.log');
    this.secret = randomBytes(32).toString('hex');
    this.binary = hostBinary();
    this.child = null;
    this.starts = 0;
    this.exits = [];
    this.stopping = false;
    this.ports = null;
  }

  get loopbackUrl() {
    return `http://127.0.0.1:${this.ports.loopback}`;
  }

  get publicUrl() {
    return `http://127.0.0.1:${this.ports.public}`;
  }

  log(message) {
    appendFileSync(this.supervisorLog, `[${timestamp()}] ${message}\n`);
  }

  async allocatePorts() {
    const loopback = await freePort();
    let publicPort = await freePort();
    while (publicPort === loopback) {
      publicPort = await freePort();
    }
    let https = await freePort();
    while (https === loopback || https === publicPort) {
      https = await freePort();
    }
    this.ports = { loopback, public: publicPort, https, ...this.options.ports };
  }

  environment() {
    return {
      ...process.env,
      MACRO_DECK_DATA_DIRECTORY: this.dataDir,
      MACRODECK_HOST_PORT: String(this.ports.loopback),
      MACRO_DECK_PORT: String(this.ports.public),
      MACRO_DECK_HTTPS_PORT: String(this.ports.https),
      MACRODECK_LOOPBACK_SECRET: this.secret,
      // The host only offers a restart when it knows the shell that relaunches it.
      MACRODECK_SHELL_EXECUTABLE: this.binary,
      // The single-instance port file lives in the temp directory, so every host needs its own.
      TMPDIR: this.tmpDir,
      TMP: this.tmpDir,
      TEMP: this.tmpDir,
      ...this.options.env,
    };
  }

  launch() {
    this.starts += 1;
    this.log('Starting Macro Deck host');
    const child = spawn(this.binary, [], {
      cwd: dirname(this.binary),
      env: this.environment(),
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
    const append = chunk => appendFileSync(this.hostLog, chunk);
    child.stdout.on('data', append);
    child.stderr.on('data', append);
    running.add(child);
    child.exited = new Promise(done => {
      child.on('exit', code => {
        running.delete(child);
        this.exits.push(code);
        this.log(`Host exited with code ${code}`);
        if (this.child === child) {
          this.child = null;
        }
        if (code === RESTART_EXIT_CODE && !this.stopping) {
          this.log(`Host requested restart (exit code ${RESTART_EXIT_CODE})`);
          this.launch();
        }
        done(code);
      });
    });
    child.on('error', error => this.log(`Host failed to start: ${error.message}`));
    this.child = child;
  }

  async waitUntilReady(timeoutMs = 60_000, { requirePublic = true } = {}) {
    const ready = await until(async () => {
      const loopback = await answers(`${this.loopbackUrl}/api/auth/status`);
      return loopback && (!requirePublic || await answers(`${this.publicUrl}/api/auth/status`));
    }, timeoutMs);
    if (!ready) {
      throw new Error(`Macro Deck host ${this.name} did not become ready; see ${this.hostLog}`);
    }
  }

  async start({ requirePublic = true } = {}) {
    if (!existsSync(this.binary)) {
      throw new Error(`No staged host at ${this.binary}; stage it with ci/scripts/stage-host.sh or set MACRO_DECK_HOST_BINARY`);
    }
    const keepPorts = this.ports !== null;
    for (let attempt = 1; ; attempt += 1) {
      if (!keepPorts) {
        await this.allocatePorts();
      }
      this.stopping = false;
      this.launch();
      try {
        await this.waitUntilReady(attempt === 1 ? 60_000 : 30_000, { requirePublic });
        return this;
      } catch (error) {
        await this.stop();
        if (keepPorts || attempt >= 3) {
          throw error;
        }
        this.log(`Retrying with new ports after: ${error.message}`);
      }
    }
  }

  // The production shutdown path; a forced kill only if the host does not leave on its own.
  async stop(timeoutMs = 30_000) {
    this.stopping = true;
    const child = this.child;
    if (!child) {
      return;
    }
    try {
      await fetch(`${this.loopbackUrl}/api/host/shutdown?reason=e2e`, {
        method: 'POST',
        headers: { 'X-MacroDeck-Loopback-Secret': this.secret },
        signal: AbortSignal.timeout(5_000),
      });
    } catch {
      // A host that cannot answer is stopped by the kill below.
    }
    const exited = await Promise.race([
      child.exited.then(() => true),
      new Promise(done => setTimeout(() => done(false), timeoutMs)),
    ]);
    if (!exited) {
      this.log('Host did not stop in time; killing it');
      child.kill('SIGKILL');
      await child.exited;
    }
    this.child = null;
  }

  // Waits for the relaunch a restart request triggers, then for both listeners to answer again.
  async waitForRestart(startsBefore, timeoutMs = 60_000) {
    const relaunched = await until(() => this.starts > startsBefore, timeoutMs);
    if (!relaunched) {
      throw new Error(`Macro Deck host ${this.name} was not relaunched; see ${this.supervisorLog}`);
    }
    await this.waitUntilReady(timeoutMs);
  }
}

export async function startHost(name, options) {
  const host = new MacroDeckHost(name, options);
  await host.start(options);
  return host;
}
