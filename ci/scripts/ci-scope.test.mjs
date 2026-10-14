import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { JOBS, scope } from './ci-scope.mjs';

const scriptPath = fileURLToPath(new URL('./ci-scope.mjs', import.meta.url));

function jobs(...paths) {
  return JOBS.filter((job) => scope(paths)[job]);
}

test('a documentation-only change runs nothing', () => {
  assert.deepEqual(
    jobs(
      'docs/src/content/docs/guide/settings.md',
      'docs/src/assets/guide/deck.png',
      'engineering/decisions/0107-built-in-integrations-are-built-as-extractable-plugins.md',
      'README.md',
      '.github/ISSUE_TEMPLATE/bug_report.yml'
    ),
    []
  );
});

test('a desktop UI change skips the .NET, bootstrapper and installer jobs', () => {
  assert.deepEqual(jobs('ui/angular/projects/desktop-ui/src/app/app.component.ts'), ['ui', 'notices', 'e2e']);
  assert.deepEqual(jobs('ui/web-client/src/main.ts'), ['ui', 'notices', 'e2e']);
});

test('a host change skips the UI, bootstrapper and installer jobs', () => {
  assert.deepEqual(jobs('host/src/MacroDeckHost.Application/Devices/DeviceService.cs'), ['dotnet', 'notices', 'e2e']);
  assert.deepEqual(jobs('sdk/src/MacroDeck.Sdk/IIntegrationContext.cs'), ['dotnet', 'notices', 'e2e']);
});

test('a bootstrapper change runs only the bootstrapper and its license check', () => {
  assert.deepEqual(jobs('ui/bootstrapper/src/tray.rs'), ['bootstrapper', 'notices']);
  assert.deepEqual(jobs('ui/bootstrapper/installer/hooks.nsh'), ['bootstrapper', 'installer', 'notices']);
});

test('the shared runtime runs both the UI and the .NET jobs', () => {
  assert.deepEqual(jobs('ui/runtime/src/ui-framework/responsive.ts'), ['dotnet', 'ui', 'notices', 'e2e']);
  assert.deepEqual(jobs('ui/package-lock.json'), ['dotnet', 'ui', 'notices', 'e2e']);
});

test('a file another area reads runs that area too', () => {
  assert.ok(scope(['ui/angular/projects/desktop-ui/src/app/domain/action-parameter-mapping.util.ts']).dotnet);
  assert.ok(scope(['docs/public/schemas/plugin-manifest-v1.schema.json']).dotnet);
  assert.ok(scope(['docs/src/content/docs/policies/deprecations.md']).dotnet);
  assert.ok(scope(['ui/bootstrapper/src/localization/generated.rs']).dotnet);
  assert.ok(scope(['host/src/MacroDeckHost/HostEndpoints.cs']).ui);
  assert.ok(scope(['ui-model/src/MacroDeck.Ui/Components/UiComponentValues.cs']).ui);
  assert.ok(scope(['host/src/MacroDeckHost/MacroDeckHost.csproj']).bootstrapper);
  assert.ok(scope(['ui/bootstrapper/src/update-window.test.mjs']).ui);
});

test('a change to CI itself or to an unclassified path runs every job', () => {
  for (const path of ['.github/workflows/ci.yml', 'ci/scripts/stage-host.sh', 'LICENSE', 'some-new-folder/file.txt']) {
    assert.deepEqual(jobs(path), JOBS, path);
  }
});

test('a change spanning areas runs the union of their jobs', () => {
  assert.deepEqual(jobs('ui/angular/src/main.ts', 'ui/bootstrapper/src/tray.rs'), [
    'ui',
    'bootstrapper',
    'notices',
    'e2e',
  ]);
});

function git(cwd, ...args) {
  return execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
}

function commit(cwd, files) {
  for (const [path, content] of Object.entries(files)) {
    mkdirSync(dirname(join(cwd, path)), { recursive: true });
    writeFileSync(join(cwd, path), content);
  }
  git(cwd, 'add', '-A');
  git(cwd, '-c', 'user.name=ci', '-c', 'user.email=ci@example.invalid', '-c', 'commit.gpgsign=false', 'commit', '-qm', 'change');
  return git(cwd, 'rev-parse', 'HEAD');
}

test('writes the scope of a commit range as GitHub outputs, counting both sides of a move', () => {
  const repo = mkdtempSync(join(tmpdir(), 'ci-scope-'));
  try {
    git(repo, 'init', '-q');
    const base = commit(repo, { 'ui/bootstrapper/src/tray.rs': 'fn main() {}\n' });
    mkdirSync(join(repo, 'docs'));
    git(repo, 'mv', 'ui/bootstrapper/src/tray.rs', 'docs/tray.rs');
    const head = commit(repo, {});
    const output = join(repo, 'github-output');

    execFileSync('node', [scriptPath, base, head, '--github-output', output], { cwd: repo });

    assert.equal(
      readFileSync(output, 'utf8'),
      'dotnet=false\nui=false\nbootstrapper=true\ninstaller=false\nnotices=true\ne2e=false\n'
    );
  } finally {
    rmSync(repo, { recursive: true, force: true });
  }
});

test('outside a pull request every job runs', () => {
  const directory = mkdtempSync(join(tmpdir(), 'ci-scope-'));
  try {
    const output = join(directory, 'github-output');
    execFileSync('node', [scriptPath, '--all', '--github-output', output]);
    assert.equal(readFileSync(output, 'utf8'), `${JOBS.map((job) => `${job}=true`).join('\n')}\n`);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
