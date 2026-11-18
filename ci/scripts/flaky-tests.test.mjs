import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { failedTestsFromLog, flakyTestsFromReport, renderIssue } from './flaky-tests.mjs';

const scriptPath = fileURLToPath(new URL('./flaky-tests.mjs', import.meta.url));

const report = {
  suites: [
    {
      title: 'startup.spec.js',
      file: 'startup.spec.js',
      specs: [
        { title: 'a restart', file: 'startup.spec.js', line: 14, tests: [{ status: 'flaky' }] },
        { title: 'a real failure', file: 'startup.spec.js', line: 27, tests: [{ status: 'unexpected' }] },
        { title: 'a pass', file: 'startup.spec.js', line: 40, tests: [{ status: 'expected' }] }
      ],
      suites: [
        {
          title: 'Macro Deck core E2E',
          specs: [{ title: '@smoke persists state', file: 'startup.spec.js', line: 60, tests: [{ status: 'flaky' }] }]
        }
      ]
    }
  ]
};

function runReport(content) {
  const directory = mkdtempSync(join(tmpdir(), 'flaky-tests-'));
  const summary = join(directory, 'summary.md');
  writeFileSync(summary, '');
  const file = join(directory, 'results.json');
  if (content !== undefined) {
    writeFileSync(file, JSON.stringify(content));
  }
  try {
    const stdout = execFileSync(process.execPath, [scriptPath, 'report', file], {
      encoding: 'utf8',
      env: { ...process.env, GITHUB_STEP_SUMMARY: summary }
    });
    return { stdout, summary: readFileSync(summary, 'utf8') };
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('a job log names every failed NUnit and Playwright test and nothing that passed or was flaky', () => {
  const log = [
    'Host build & tests\tUNKNOWN STEP\t2026-11-11T19:18:22.6770204Z   Failed A_change_closes_sockets("change") [1 s]',
    '2026-11-13T07:37:43.3223602Z   Failed Repeated_failures_fault_the_plugin [30 s]',
    '2026-11-13T07:37:43.4000000Z   Passed Something_else [2 ms]',
    '2026-11-13T07:45:41.2371944Z   1 failed',
    '2026-11-13T07:45:41.2372316Z     tests/editor.spec.js:190:1 › typing an icon zoom keeps the number ─────',
    '2026-11-13T07:45:41.2372400Z   1 flaky',
    '2026-11-13T07:45:41.2372500Z     tests/core.spec.js:199:3 › Macro Deck core E2E › @smoke persists state ───',
    '2026-11-13T07:45:41.2372616Z   2 skipped',
    '2026-11-13T07:45:41.2372824Z   69 passed (13.4m)'
  ].join('\n');

  assert.deepEqual(failedTestsFromLog(log).sort(), [
    'A_change_closes_sockets("change")',
    'Repeated_failures_fault_the_plugin',
    'editor.spec.js › typing an icon zoom keeps the number'
  ]);
});

test('the report names a test as flaky only when it passed on its retry, with its describe path', () => {
  assert.deepEqual(flakyTestsFromReport(report).map((entry) => entry.key), [
    'startup.spec.js › a restart',
    'startup.spec.js › Macro Deck core E2E › @smoke persists state'
  ]);
});

test('report annotates each flaky test and lists it in the job summary', () => {
  const { stdout, summary } = runReport(report);

  assert.match(stdout, /^::warning file=e2e\/tests\/startup\.spec\.js,line=14,title=Flaky E2E test::startup\.spec\.js › a restart$/m);
  assert.match(summary, /- startup\.spec\.js › Macro Deck core E2E › @smoke persists state/);
});

test('report stays quiet and succeeds when the suite never produced a report or nothing was flaky', () => {
  assert.equal(runReport().summary, '');
  assert.equal(runReport({ suites: [] }).stdout, '');
});

test('the issue ranks repeated tests first, counts a failure that passed on rerun as flaky, and lists jobs without a test', () => {
  const occurrence = (testName, date, flaky) => ({
    test: testName,
    job: 'E2E / Core E2E',
    date,
    url: `https://example.test/${date}`,
    flaky
  });
  const body = renderIssue({
    branch: 'main',
    from: '2026-11-04',
    to: '2026-11-18',
    runs: 33,
    attempts: 35,
    occurrences: [
      occurrence('editor.spec.js › once', '2026-11-13T07:45:41Z', false),
      occurrence('startup.spec.js › a restart', '2026-11-16T10:00:00Z', true),
      occurrence('startup.spec.js › a restart', '2026-11-18T20:47:20Z', false)
    ],
    unattributed: [{ job: 'UI tests & build (Linux)', date: '2026-11-16T20:32:57Z', url: 'https://example.test/ui', flaky: false }]
  });

  const rows = body.split('\n').filter((line) => line.startsWith('| ') && !line.startsWith('| Test') && !line.startsWith('| ---'));
  assert.deepEqual(rows, [
    '| startup.spec.js › a restart | E2E / Core E2E | 1 | 1 | [2026-11-18](https://example.test/2026-11-18T20:47:20Z) |',
    '| editor.spec.js › once | E2E / Core E2E | 1 | 0 | [2026-11-13](https://example.test/2026-11-13T07:45:41Z) |'
  ]);
  assert.match(body, /- \[UI tests & build \(Linux\)\]\(https:\/\/example\.test\/ui\) on 2026-11-16$/m);
});

test('the issue says so when nothing failed', () => {
  const body = renderIssue({ branch: 'main', from: 'a', to: 'b', runs: 3, attempts: 3, occurrences: [], unattributed: [] });

  assert.match(body, /No test failed in this window\./);
});
