#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const SKIPPED = new Set(['NotExecuted', 'Inconclusive']);
const ISSUE_REFERENCE = /#\d+/;
const ENVIRONMENT_PREFIX = 'Environment:';

function decode(text) {
  return text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, decimal) => String.fromCodePoint(Number(decimal)))
    .replace(/&amp;/g, '&');
}

function attribute(tag, name) {
  const match = tag.match(new RegExp(`\\s${name}="([^"]*)"`));
  return match ? decode(match[1]) : undefined;
}

export function readResults(trx) {
  const results = [];
  const pattern = /<UnitTestResult\b([^>]*?)(\/>|>([\s\S]*?)<\/UnitTestResult>)/g;
  for (const match of trx.matchAll(pattern)) {
    const body = match[3] ?? '';
    const message = body.match(/<Message>([\s\S]*?)<\/Message>/);
    const stdout = body.match(/<StdOut>([\s\S]*?)<\/StdOut>/);
    results.push({
      name: attribute(match[1], 'testName') ?? '(unnamed)',
      outcome: attribute(match[1], 'outcome') ?? 'Unknown',
      message: decode(message?.[1] ?? stdout?.[1] ?? '').trim()
    });
  }
  return results;
}

function acceptedSkip(message, allowEnvironmentSkips) {
  if (ISSUE_REFERENCE.test(message)) {
    return true;
  }
  return allowEnvironmentSkips && message.replace(/^OneTimeSetUp:\s*/, '').startsWith(ENVIRONMENT_PREFIX);
}

export function check(results, { allowEnvironmentSkips = false } = {}) {
  const problems = [];
  const executed = results.filter((result) => !SKIPPED.has(result.outcome));
  if (executed.length === 0) {
    problems.push('no test was executed');
  }

  for (const result of results) {
    if (SKIPPED.has(result.outcome) && !acceptedSkip(result.message, allowEnvironmentSkips)) {
      problems.push(`${result.name} was skipped without an issue reference: ${result.message || '(no reason)'}`);
    }
  }

  return { executed: executed.length, skipped: results.length - executed.length, problems };
}

function main(argv) {
  const allowEnvironmentSkips = argv.includes('--allow-environment-skips');
  const files = argv.filter((argument) => !argument.startsWith('--'));
  if (files.length === 0) {
    console.error('usage: check-trx-skips.mjs [--allow-environment-skips] <result.trx>...');
    return 2;
  }

  const results = files.flatMap((file) => readResults(readFileSync(file, 'utf8')));
  const outcome = check(results, { allowEnvironmentSkips });
  console.log(`${outcome.executed} executed, ${outcome.skipped} skipped`);
  for (const problem of outcome.problems) {
    console.error(`::error::${problem}`);
  }
  return outcome.problems.length === 0 ? 0 : 1;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
