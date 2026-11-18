#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import { appendFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export const FLAKY_ANNOTATION_TITLE = 'Flaky E2E test';
export const ISSUE_TITLE = 'Flaky tests on main';

const LOG_PREFIX = /^(?:[^\t]*\t[^\t]*\t)?\d{4}-\d{2}-\d{2}T[\d:.]+Z ?/;
const NUNIT_FAILED = /^\s+Failed (\S.*?) \[[^\]]*\]\s*$/;
const PLAYWRIGHT_COUNT = /^\s+(\d+) (failed|flaky|interrupted|skipped|passed|did not run)\b/;
const PLAYWRIGHT_ENTRY = /^\s+(?:\[[^\]]+\] › )?(\S+?\.spec\.[cm]?[jt]s):\d+:\d+ › (.+?)[\s─]*$/;

// The report names files relative to the test directory, the console relative to e2e/.
export function playwrightKey(file, titlePath) {
  return `${file.replace(/^tests\//, '')} › ${titlePath.join(' › ')}`;
}

// Playwright's JSON report: a suite per spec file, nested describe suites, specs with one test per project.
export function flakyTestsFromReport(report) {
  const flaky = [];
  const visit = (suite, titles) => {
    for (const spec of suite.specs ?? []) {
      if ((spec.tests ?? []).some((entry) => entry.status === 'flaky')) {
        flaky.push({ file: spec.file, line: spec.line, key: playwrightKey(spec.file, [...titles, spec.title]) });
      }
    }
    for (const child of suite.suites ?? []) {
      visit(child, [...titles, child.title]);
    }
  };
  for (const fileSuite of report.suites ?? []) {
    visit(fileSuite, []);
  }
  return flaky;
}

function escapeData(text) {
  return text.replace(/%/g, '%25').replace(/\r/g, '%0D').replace(/\n/g, '%0A');
}

function escapeProperty(text) {
  return escapeData(text).replace(/:/g, '%3A').replace(/,/g, '%2C');
}

export function reportLines(flaky) {
  const annotations = flaky.map((test) => `::warning file=${escapeProperty(`e2e/tests/${test.file}`)},line=${test.line},`
    + `title=${escapeProperty(FLAKY_ANNOTATION_TITLE)}::${escapeData(test.key)}`);
  const summary = flaky.length === 0
    ? []
    : [
        '## Flaky E2E tests',
        '',
        'These failed first and passed on their retry. They did not fail the job; the trace of the failed '
          + 'attempt is in the E2E diagnostics artifact.',
        '',
        ...flaky.map((test) => `- ${test.key}`),
        ''
      ];
  return { annotations, summary };
}

// A job log line is "<time> <text>", or "<job>\t<step>\t<time> <text>" when it comes from gh run view.
export function failedTestsFromLog(log) {
  const failed = new Set();
  let inFailedList = false;
  for (const raw of log.split(/\r?\n/)) {
    const line = raw.replace(LOG_PREFIX, '');
    const nunit = line.match(NUNIT_FAILED);
    if (nunit) {
      failed.add(nunit[1]);
      continue;
    }

    const count = line.match(PLAYWRIGHT_COUNT);
    if (count) {
      inFailedList = count[2] === 'failed';
      continue;
    }

    const entry = inFailedList ? line.match(PLAYWRIGHT_ENTRY) : null;
    if (entry) {
      failed.add(playwrightKey(entry[1], [entry[2]]));
    } else if (inFailedList && line.trim() !== '') {
      inFailedList = false;
    }
  }
  return [...failed];
}

// A failure is flaky when the same job passed on a later attempt of the same run, or on its own retry.
export function summarize(occurrences) {
  const tests = new Map();
  for (const occurrence of occurrences) {
    const entry = tests.get(occurrence.test) ?? { test: occurrence.test, job: occurrence.job, failed: 0, flaky: 0, last: null };
    entry[occurrence.flaky ? 'flaky' : 'failed'] += 1;
    if (entry.last === null || occurrence.date > entry.last.date) {
      entry.last = occurrence;
    }
    tests.set(occurrence.test, entry);
  }
  return [...tests.values()].sort((a, b) => b.failed + b.flaky - (a.failed + a.flaky)
    || (b.last.date > a.last.date ? 1 : -1));
}

function cell(text) {
  return text.replace(/\|/g, '\\|');
}

export function renderIssue({ branch, from, to, runs, attempts, occurrences, unattributed }) {
  const lines = [
    `Tests that failed in CI runs on ${branch} between ${from} and ${to} (${runs} runs, ${attempts} attempts).`,
    'Flaky counts a failure that passed on a Playwright retry or on a rerun of the same run; Failed counts '
      + 'the rest, which a real regression shows up as too.',
    '',
    'Updated daily by the Flaky tests workflow from ci/scripts/flaky-tests.mjs. Edits to this text are overwritten.',
    ''
  ];

  const tests = summarize(occurrences);
  if (tests.length === 0) {
    lines.push('No test failed in this window.', '');
  } else {
    lines.push('| Test | Job | Failed | Flaky | Last seen |', '| --- | --- | ---: | ---: | --- |');
    for (const entry of tests) {
      lines.push(`| ${cell(entry.test)} | ${cell(entry.job)} | ${entry.failed} | ${entry.flaky} | `
        + `[${entry.last.date.slice(0, 10)}](${entry.last.url}) |`);
    }
    lines.push('');
  }

  if (unattributed.length > 0) {
    lines.push('### Failed jobs without a test name', '',
      'Usually a build, lint or infrastructure failure rather than a test.', '');
    for (const job of unattributed) {
      lines.push(`- [${job.job}](${job.url}) on ${job.date.slice(0, 10)}${job.flaky ? ', passed on rerun' : ''}`);
    }
    lines.push('');
  }

  return lines.join('\n');
}

function gh(args, { json = true } = {}) {
  const output = execFileSync('gh', args, { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 });
  return json ? JSON.parse(output) : output;
}

function pages(path, key) {
  const items = [];
  for (let page = 1; ; page += 1) {
    const separator = path.includes('?') ? '&' : '?';
    const batch = gh(['api', `${path}${separator}per_page=100&page=${page}`])[key];
    items.push(...batch);
    if (batch.length < 100) {
      return items;
    }
  }
}

export function collect({ repo, workflow, branch, days, now = new Date() }) {
  const since = new Date(now.getTime() - days * 24 * 60 * 60 * 1000);
  const from = since.toISOString().slice(0, 10);
  const runs = pages(`repos/${repo}/actions/workflows/${workflow}/runs?branch=${branch}&created=%3E%3D${from}`,
    'workflow_runs')
    .filter((run) => run.status === 'completed');

  const occurrences = [];
  const unattributed = [];
  let attempts = 0;
  for (const run of runs) {
    const jobsByAttempt = [];
    for (let attempt = 1; attempt <= run.run_attempt; attempt += 1) {
      jobsByAttempt.push(pages(`repos/${repo}/actions/runs/${run.id}/attempts/${attempt}/jobs`, 'jobs'));
    }
    attempts += jobsByAttempt.length;

    jobsByAttempt.forEach((jobs, index) => {
      const later = jobsByAttempt.slice(index + 1).flat();
      for (const job of jobs) {
        const date = job.completed_at ?? run.created_at;
        if (job.conclusion === 'failure') {
          const passedLater = later.some((other) => other.name === job.name && other.conclusion === 'success');
          const tests = failedTestsFromLog(gh(['api', `repos/${repo}/actions/jobs/${job.id}/logs`], { json: false }));
          for (const test of tests) {
            occurrences.push({ test, job: job.name, date, url: job.html_url, flaky: passedLater });
          }
          if (tests.length === 0) {
            unattributed.push({ job: job.name, date, url: job.html_url, flaky: passedLater });
          }
        }
        if (job.name.includes('E2E') && (job.conclusion === 'success' || job.conclusion === 'failure')) {
          const annotations = gh(['api', `repos/${repo}/check-runs/${job.id}/annotations?per_page=100`]);
          for (const annotation of annotations.filter((item) => item.title === FLAKY_ANNOTATION_TITLE)) {
            occurrences.push({ test: annotation.message, job: job.name, date, url: job.html_url, flaky: true });
          }
        }
      }
    });
  }

  return { branch, from, to: now.toISOString().slice(0, 10), runs: runs.length, attempts, occurrences, unattributed };
}

function publish(repo, body) {
  const open = gh(['issue', 'list', '--repo', repo, '--state', 'open', '--search', `in:title "${ISSUE_TITLE}"`,
    '--json', 'number,title'])
    .find((issue) => issue.title === ISSUE_TITLE);
  const args = open
    ? ['issue', 'edit', String(open.number), '--repo', repo, '--body-file', '-']
    : ['issue', 'create', '--repo', repo, '--title', ISSUE_TITLE, '--body-file', '-'];
  execFileSync('gh', args, { input: body, stdio: ['pipe', 'inherit', 'inherit'] });
}

function option(argv, name, fallback) {
  const index = argv.indexOf(`--${name}`);
  return index === -1 ? fallback : argv[index + 1];
}

function main(argv) {
  const [command, ...rest] = argv;
  if (command === 'report' && rest.length === 1) {
    let report;
    try {
      report = JSON.parse(readFileSync(rest[0], 'utf8'));
    } catch {
      console.log(`No Playwright report at ${rest[0]}; nothing to report`);
      return 0;
    }
    const { annotations, summary } = reportLines(flakyTestsFromReport(report));
    annotations.forEach((line) => console.log(line));
    if (summary.length > 0 && process.env.GITHUB_STEP_SUMMARY) {
      appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary.join('\n')}\n`);
    }
    return 0;
  }

  if (command === 'track') {
    const repo = option(rest, 'repo', process.env.GITHUB_REPOSITORY);
    const result = collect({
      repo,
      workflow: option(rest, 'workflow', 'ci.yml'),
      branch: option(rest, 'branch', 'main'),
      days: Number(option(rest, 'days', '14'))
    });
    const body = renderIssue(result);
    if (rest.includes('--dry-run')) {
      console.log(body);
    } else {
      publish(repo, body);
    }
    return 0;
  }

  console.error('usage: flaky-tests.mjs report <results.json>\n'
    + '       flaky-tests.mjs track [--repo owner/name] [--workflow ci.yml] [--branch main] [--days 14] [--dry-run]');
  return 2;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
