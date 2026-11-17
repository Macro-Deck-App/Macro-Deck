import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { check, readResults } from './check-trx-skips.mjs';

const scriptPath = fileURLToPath(new URL('./check-trx-skips.mjs', import.meta.url));

function result(name, outcome, message) {
  const output = message === undefined
    ? ''
    : `<Output><StdOut>${message}</StdOut><ErrorInfo><Message>${message}</Message></ErrorInfo></Output>`;
  return `<UnitTestResult executionId="x" testName="${name}" outcome="${outcome}">${output}</UnitTestResult>`;
}

function trx(...results) {
  return `<?xml version="1.0" encoding="utf-8"?><TestRun><Results>${results.join('')}</Results></TestRun>`;
}

function run(content, ...flags) {
  const directory = mkdtempSync(join(tmpdir(), 'check-trx-'));
  const file = join(directory, 'results.trx');
  writeFileSync(file, content);
  try {
    execFileSync(process.execPath, [scriptPath, ...flags, file], { stdio: 'pipe' });
    return 0;
  } catch (error) {
    return error.status;
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('a run where every test passed is accepted', () => {
  assert.equal(run(trx(result('A', 'Passed'), result('B', 'Passed'))), 0);
});

test('a run that executed nothing fails', () => {
  assert.equal(run(trx()), 1);
  assert.equal(run(trx(result('A', 'NotExecuted', 'Red today: #1700 Caps Lock'))), 1);
});

test('a skip without an issue reference fails', () => {
  assert.equal(run(trx(result('A', 'Passed'), result('B', 'NotExecuted', 'No X display available.'))), 1);
});

test('an inconclusive result without an issue reference fails', () => {
  assert.equal(run(trx(result('A', 'Passed'), result('B', 'Inconclusive', 'Keyboard layout cannot be loaded'))), 1);
});

test('a skip that names its issue is accepted', () => {
  assert.equal(run(trx(result('A', 'Passed'), result('B', 'NotExecuted', 'Red today: #1700 Caps Lock'))), 0);
});

test('an environment skip is accepted only when allowed', () => {
  const content = trx(result('A', 'Passed'), result('B', 'NotExecuted', 'OneTimeSetUp: Environment: Accessibility granted'));

  assert.equal(run(content), 1);
  assert.equal(run(content, '--allow-environment-skips'), 0);
});

test('a failed test is not a skip and leaves the pass/fail verdict to dotnet test', () => {
  assert.deepEqual(check(readResults(trx(result('A', 'Failed', 'boom')))).problems, []);
});

test('escaped messages are decoded before they are checked', () => {
  const [parsed] = readResults(trx(result('A', 'NotExecuted', 'see &lt;issue&gt; &#35;1700')));

  assert.equal(parsed.message, 'see <issue> #1700');
});
