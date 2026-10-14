#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export const JOBS = ['dotnet', 'ui', 'bootstrapper', 'installer', 'notices', 'e2e'];

const DOTNET_TREES = ['dotnet', 'e2e', 'notices'];
const UI_TREES = ['ui', 'e2e', 'notices'];

// A path no rule matches runs every job, so a new folder is fully checked until it is classified.
// Rules add up: a file runs the union of every rule it matches, and an empty rule only classifies it.
const RULES = [
  [/^\.github\/workflows\/(ci|e2e-run)\.yml$/, JOBS],
  [/^\.github\//, []],
  [/^ci\//, JOBS],

  [/^(host|sdk|protocol|ui-model)\//, DOTNET_TREES],
  [/^tools\//, ['dotnet', 'notices']],
  [/^(global\.json|Directory\.(Build\.props|Build\.targets|Packages\.props)|MacroDeck\.slnx(\.DotSettings)?|\.editorconfig)$/, DOTNET_TREES],

  [/^ui\/runtime\//, [...UI_TREES, 'dotnet']],
  [/^ui\/(angular|web-client)\//, UI_TREES],
  [/^ui\/scripts\//, ['ui', 'dotnet']],
  [/^ui\/package(-lock)?\.json$/, [...UI_TREES, 'dotnet']],
  [/^ui\/\.editorconfig$/, ['ui']],
  [/^ui\/dev\.js$/, []],
  [/^ui\/bootstrapper\//, ['bootstrapper', 'notices']],
  [/^ui\/bootstrapper\/installer\//, ['installer']],
  [/^ui\/bootstrapper\/src\/.*\.(mjs|js|html|css)$/, ['ui']],

  [/^e2e\//, ['e2e']],
  [/^(third-party\/|NOTICE$|THIRD-PARTY-NOTICES$)/, ['notices']],

  [/^docs\//, []],
  [/^docs\/public\//, ['dotnet', 'e2e']],
  [/^engineering\//, []],
  [/^[^/]+\.md$/, []],
  [/^(renovate\.json|Makefile|\.gitignore)$/, []],

  // Files one area's tests read from another area's tree.
  [/^docs\/src\/content\/docs\/(policies\/deprecations|guide\/troubleshooting)\.md$/, ['dotnet']],
  [/^ui\/angular\/projects\/desktop-ui\/src\/app\/domain\/action-parameter-mapping\.util\.ts$/, ['dotnet']],
  [/^ui\/bootstrapper\/(src\/host\.rs|src\/localization\/generated\.rs|icons\/)/, ['dotnet']],
  [/^(ui-model\/src\/MacroDeck\.Ui\/Components\/UiComponentValues\.cs|ui-model\/fixtures\/|host\/src\/MacroDeckHost\/HostEndpoints\.cs)/, ['ui']],
  [/^host\/src\/MacroDeckHost\/MacroDeckHost\.csproj$/, ['bootstrapper']],
];

export function jobsForPath(path) {
  const matching = RULES.filter(([pattern]) => pattern.test(path));
  if (matching.length === 0) {
    return new Set(JOBS);
  }

  return new Set(matching.flatMap(([, jobs]) => jobs));
}

export function scope(paths) {
  const selected = new Set(paths.flatMap((path) => [...jobsForPath(path)]));
  return Object.fromEntries(JOBS.map((job) => [job, selected.has(job)]));
}

export function fullScope() {
  return Object.fromEntries(JOBS.map((job) => [job, true]));
}

function changedPaths(base, head) {
  // Without --no-renames a moved file reports only its new path, and its old area would go unchecked.
  const output = execFileSync('git', ['diff', '--name-only', '--no-renames', base, head], { encoding: 'utf8' });
  return output.split('\n').filter((line) => line.length > 0);
}

function usage() {
  console.error('Usage: ci-scope.mjs (--all | <base> <head>) [--github-output <path>]');
  process.exit(2);
}

function main(argv) {
  const args = [...argv];
  let outputPath;
  const outputIndex = args.indexOf('--github-output');
  if (outputIndex !== -1) {
    outputPath = args[outputIndex + 1];
    if (!outputPath) {
      usage();
    }
    args.splice(outputIndex, 2);
  }

  let result;
  if (args.length === 1 && args[0] === '--all') {
    result = fullScope();
  } else if (args.length === 2) {
    const paths = changedPaths(args[0], args[1]);
    for (const path of paths) {
      console.log(`${path}: ${[...jobsForPath(path)].join(', ') || 'no jobs'}`);
    }
    result = scope(paths);
  } else {
    usage();
  }

  const lines = JOBS.map((job) => `${job}=${result[job]}`);
  console.log(lines.join('\n'));
  if (outputPath) {
    appendFileSync(outputPath, `${lines.join('\n')}\n`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
