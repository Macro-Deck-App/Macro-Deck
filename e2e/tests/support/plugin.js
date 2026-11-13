import { execFile } from 'node:child_process';
import { mkdirSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { expect } from '@playwright/test';

const run = promisify(execFile);
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');

export const FIXTURE_PLUGIN = {
  id: 'app.macro-deck.e2e-fixture-plugin',
  name: 'E2E Fixture Plugin',
  setTextAction: 'set-text',
  textVariable: 'e2e_fixture_text',
  tileType: 'app.macro-deck.e2e-fixture-plugin::fixture-tile',
  tileText: 'E2E fixture tile',
};

async function dotnet(...args) {
  await run('dotnet', args, { cwd: repositoryRoot, maxBuffer: 64 * 1024 * 1024 });
}

export async function buildFixturePlugin() {
  const base = process.env.E2E_RUN_DIR ?? tmpdir();
  mkdirSync(base, { recursive: true });
  const workDir = mkdtempSync(join(base, 'md-e2e-fixture-plugin-'));
  const publishDir = join(workDir, 'publish');
  const packagePath = join(workDir, 'e2e-fixture-plugin.macroDeckPlugin');
  await dotnet('publish',
    join(repositoryRoot, 'host', 'tests', 'fixtures', 'MacroDeckHost.Tests.E2EPlugin', 'MacroDeckHost.Tests.E2EPlugin.csproj'),
    '-c', 'Release', '-p:UseAppHost=false', '-o', publishDir);
  await dotnet('run', '--project', join(repositoryRoot, 'sdk', 'src', 'MacroDeck.Plugin.Cli'), '-c', 'Release', '--',
    'pack', '--source', publishDir, '--output', packagePath, '--force');
  return packagePath;
}

export async function pluginRuntime(api, pluginId = FIXTURE_PLUGIN.id) {
  const { plugins } = await api.get('/api/plugin-runtime');
  return plugins.find(plugin => plugin.pluginId === pluginId) ?? null;
}

// A started plugin offers its actions only once its session attached, so wait for both.
export async function waitUntilPluginServes(api) {
  await expect.poll(async () => {
    const runtime = await pluginRuntime(api);
    return runtime ? `${runtime.state}/${runtime.health}` : 'missing';
  }, { timeout: 120_000, message: 'the fixture plugin runs and is healthy' }).toBe('running/healthy');
  await expect.poll(async () => {
    const { actions } = await api.get('/api/actions');
    return actions.some(action => action.integrationId === FIXTURE_PLUGIN.id
      && action.id === FIXTURE_PLUGIN.setTextAction);
  }, { timeout: 60_000, message: 'the fixture plugin offers its action' }).toBe(true);
}
