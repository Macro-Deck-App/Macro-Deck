import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { after, before, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import {
  accepts,
  bootstrapperBinary,
  freePort,
  HostApi,
  hostBinary,
  isLinux,
  isRunning,
  isWindows,
  killLeftovers,
  leftovers,
  listProcesses,
  newSecret,
  packFixturePlugin,
  pluginProcess,
  processesOf,
  SmokeApp,
  until
} from './support.mjs';

const run = promisify(execFile);
const here = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(here, '..', '..', '..');
const startTimeout = 180_000;
const keyProbe = join(here, 'x11-keys.py');
const heldKey = 'Shift_L';
const checkKeys = isLinux && Boolean(process.env.DISPLAY);

const app = new SmokeApp('boot-01');
let current = null;

function pluginBuildDirectory() {
  return process.env.SMOKE_PLUGIN_BUILD_DIR
    ?? join(repositoryRoot, 'sdk', 'tests', 'fixtures', 'MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin', 'bin',
      'Release', 'net10.0');
}

async function waitForExit(child, timeoutMs = 90_000) {
  return await Promise.race([
    child.exited,
    new Promise((_, reject) => setTimeout(() => reject(new Error('process did not exit in time')), timeoutMs))
  ]);
}

async function onlyProcess(path, except = []) {
  return await until(`exactly one process of ${path}`, async () => {
    const found = (await processesOf(path)).filter(entry => !except.includes(entry.pid));
    return found.length === 1 ? found[0] : null;
  }, startTimeout);
}

async function waitForRunningApp(cursor, { exceptBootstrapper = [], exceptHost = [] } = {}) {
  await cursor.waitFor(/\[host\] starting .* on loopback port \d+/, startTimeout);
  await cursor.waitFor(/\[window\] page loaded: http:\/\/127\.0\.0\.1:\d+\/admin/, startTimeout);
  await cursor.waitFor(/UI connection \S+ opened/, startTimeout);
  const bootstrapper = await onlyProcess(bootstrapperBinary(), exceptBootstrapper);
  const host = await onlyProcess(hostBinary(), exceptHost);
  const api = new HostApi(app.loopbackPort, app.secret);
  const plugin = await api.waitForHealthyPlugin();
  const pluginEntry = await until('the plugin process under the host', () => pluginProcess(app, host.pid));
  assert.equal(pluginEntry.pid, plugin.processId);
  const session = await api.request('GET', '/api/host/session');
  return { bootstrapperPid: bootstrapper.pid, hostPid: host.pid, pluginPid: plugin.processId, api, session };
}

async function assertGone(running, description) {
  await until(`${description}: host ${running.hostPid} to exit`, async () => !(await isRunning(running.hostPid)),
    90_000);
  const processes = await listProcesses();
  assert.ok(!processes.some(entry => entry.pid === running.pluginPid), `${description}: the plugin is still running`);
  if (!isWindows) {
    const group = processes.filter(entry => entry.pgid === running.hostPid);
    assert.deepEqual(group, [], `${description}: processes left in the host's process group`);
  }
  assert.equal(await accepts(running.loopbackPort), false, `${description}: loopback port still accepts`);
  assert.equal(await accepts(app.publicPort), false, `${description}: public port still accepts`);
}

async function keyHeld() {
  try {
    await run('python3', ['-I', keyProbe, 'held', heldKey]);
    return true;
  } catch (error) {
    if (error.code === 1) {
      return false;
    }
    throw error;
  }
}

before(async () => {
  await app.allocatePorts();
  const port = await freePort();
  const { child, api } = await app.startHost({ port, secret: newSecret() });
  await api.request('POST', '/api/auth/setup', { username: 'smoke-user', password: 'macro-deck-smoke-password' });
  await api.request('POST', '/api/settings/onboarding/complete');
  const buildDirectory = pluginBuildDirectory();
  assert.ok(existsSync(join(buildDirectory, 'manifest.json')),
    `the WellBehavedPlugin fixture is not built in ${buildDirectory}`);
  await api.installPlugin(packFixturePlugin(buildDirectory, app.runDir));
  await api.waitForHealthyPlugin();
  await api.request('POST', '/api/host/shutdown?reason=smoke-seed');
  await waitForExit(child);
});

after(async () => {
  if (checkKeys) {
    await run('python3', ['-I', keyProbe, 'release', heldKey]).catch(() => null);
  }
  await killLeftovers(app);
});

test('the app starts its host, opens the configuration UI and connects it', async () => {
  const cursor = app.cursor();
  const child = app.launch();
  current = await waitForRunningApp(cursor);
  current.loopbackPort = app.loopbackPort;
  assert.equal(current.bootstrapperPid, child.pid);
});

test('a restart requested by the host relaunches the app and connects the UI again', async () => {
  assert.ok(current, 'the app is not running');
  const before = current;
  const cursor = app.cursor();
  const result = await before.api.request('POST', '/api/host/restart', {});
  assert.deepEqual([result.supported, result.success], [true, true]);
  await cursor.waitFor(/\[host\] restart requested by the host; relaunching Macro Deck/);
  current = await waitForRunningApp(cursor, {
    exceptBootstrapper: [before.bootstrapperPid],
    exceptHost: [before.hostPid]
  });
  current.loopbackPort = app.loopbackPort;
  assert.equal(await isRunning(before.bootstrapperPid), false, 'the old app is still running');
  assert.equal(await isRunning(before.hostPid), false, 'the old host is still running');
  assert.equal(await isRunning(before.pluginPid), false, 'the old plugin is still running');
  assert.notEqual(current.session.sessionId, before.session.sessionId);
});

test('--quit quits the running app and leaves no host or plugin behind', async () => {
  assert.ok(current, 'the app is not running');
  const running = current;
  current = null;
  const cursor = app.cursor();
  await waitForExit(app.launch(['--quit']), 30_000);
  await cursor.waitFor(/\[app\] another launch asked to quit/);
  await until('the app to exit', async () => !(await isRunning(running.bootstrapperPid)), 90_000);
  await assertGone(running, 'after --quit');
  assert.deepEqual(await leftovers(app), []);
});

test('--quit with no running app exits without starting one', async () => {
  const cursor = app.cursor();
  const exit = await waitForExit(app.launch(['--quit']), 30_000);
  assert.equal(exit.code, 0);
  await cursor.waitFor(/\[app\] --quit with no running instance; exiting/);
  assert.doesNotMatch(cursor.text(), /\[host\] starting/);
  assert.deepEqual(await leftovers(app), []);
});

test('an older host still running is replaced, not adopted', async () => {
  const port = await freePort();
  const secret = newSecret();
  const older = await app.startHost({ port, secret });
  app.writeConfig('loopback-port', String(port));
  app.writeConfig('loopback-secret', secret);
  const cursor = app.cursor();
  app.launch();
  await cursor.waitFor(/\[host\] replacing the running host \S+ on port \d+ with \S+/, startTimeout);
  await cursor.waitFor(/Shutdown requested via API, reason: "?replaced/);
  await waitForExit(older.child);
  current = await waitForRunningApp(cursor, { exceptHost: [older.child.pid] });
  current.loopbackPort = app.loopbackPort;
  assert.doesNotMatch(cursor.text(), /\[host\] adopting running host/);
  const running = current;
  current = null;
  await waitForExit(app.launch(['--quit']), 30_000);
  await until('the app to exit', async () => !(await isRunning(running.bootstrapperPid)), 90_000);
  await assertGone(running, 'after replacing');
});

test('a killed app does not orphan its host, and held keys are released', { skip: isWindows }, async () => {
  const cursor = app.cursor();
  app.launch();
  const running = await waitForRunningApp(cursor);
  running.loopbackPort = app.loopbackPort;
  if (checkKeys) {
    await running.api.request('POST', '/api/actions/run', {
      integrationId: 'app.macro-deck.keyboard',
      actionId: 'key-down',
      parameters: { keys: JSON.stringify({ modifiers: ['Shift'], key: '' }) }
    });
    await until('Shift to be held', () => keyHeld(), 10_000);
  }
  const stopCursor = app.cursor();
  process.kill(running.bootstrapperPid, 'SIGKILL');
  await stopCursor.waitFor(/is no longer running; stopping the host/, 30_000);
  await assertGone(running, 'after the app was killed');
  if (checkKeys) {
    assert.match(stopCursor.text(), /Integration ['"]?app\.macro-deck\.keyboard['"]? shut down cleanly/);
    assert.equal(await keyHeld(), false, 'Shift is still held');
  }
  assert.deepEqual(await leftovers(app), []);
});
