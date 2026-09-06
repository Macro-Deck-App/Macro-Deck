#!/usr/bin/env node

// Gate for the published package family: run after `dotnet pack`, before anything is pushed.
// Two independent kinds of check live here.
//
// The file set (pure, unit-tested below in verify-nuget-packages.test.mjs): the thirteen public package
// ids are the contract plugin authors consume, so a project that silently stops packing - or a fourteenth
// package appearing unannounced - must fail rather than reach nuget.org. Symbol packages are checked
// in both directions: missing where one is expected, and *present* where a package deliberately ships
// none, since a package with no lib/ output produces an empty snupkg NuGet warns about (NU5017).
//
// The package layout (needs the archive contents, so it shells out to unzip): the analyzer must sit
// under analyzers/dotnet/cs/ and never under lib/ - a lib/ entry makes every consumer reference it as
// an ordinary runtime dependency instead of a build-time analyzer - and the CLI must be a dotnet tool
// that registers the macrodeck-plugin command. Both are hand-authored layouts inside the nuspec, so a
// successful pack proves nothing about them.

import { execFileSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

export const PACKAGE_IDS = [
  'MacroDeck.Sdk',
  'MacroDeck.Plugin.Protocol',
  'MacroDeck.Plugin.Packaging',
  'MacroDeck.Plugin.Hosting',
  'MacroDeck.Plugin.Serilog',
  'MacroDeck.Plugin.Testing',
  'MacroDeck.Plugin.Analyzers',
  'MacroDeck.Plugin.Cli',
  'MacroDeck.Signing',
  'MacroDeck.Ui',
  'MacroDeck.Ui.Model',
  'MacroDeck.Ui.Testing',
  'MacroDeck.Localization',
];

export const PACKAGES_WITHOUT_SYMBOLS = ['MacroDeck.Plugin.Analyzers'];

export const ANALYZER_PACKAGE_ID = 'MacroDeck.Plugin.Analyzers';
export const CLI_PACKAGE_ID = 'MacroDeck.Plugin.Cli';
export const CLI_TOOL_COMMAND = 'macrodeck-plugin';

/**
 * Checks the packed file names against the expected family.
 *
 * @param {string[]} fileNames every file name in the pack output directory
 * @param {string} version the version every package must carry
 * @returns {string[]} one message per problem; empty means the set is correct
 */
export function checkPackageSet(fileNames, version) {
  if (!version) {
    throw new Error('An expected package version is required.');
  }

  const problems = [];
  const packages = new Set(fileNames.filter((name) => name.endsWith('.nupkg')));
  const symbols = new Set(fileNames.filter((name) => name.endsWith('.snupkg')));

  for (const id of PACKAGE_IDS) {
    const expected = `${id}.${version}.nupkg`;
    if (packages.delete(expected)) {
      continue;
    }

    const wrongVersion = [...packages].filter((name) => name.startsWith(`${id}.`));
    if (wrongVersion.length > 0) {
      problems.push(`Expected ${expected}, found ${wrongVersion.join(', ')}.`);
      wrongVersion.forEach((name) => packages.delete(name));
    } else {
      problems.push(`Missing package ${expected}.`);
    }
  }

  for (const unexpected of packages) {
    problems.push(`Unexpected package ${unexpected} - the published family is fixed at ${PACKAGE_IDS.length} packages.`);
  }

  for (const id of PACKAGE_IDS) {
    const expected = `${id}.${version}.snupkg`;
    const shipsSymbols = !PACKAGES_WITHOUT_SYMBOLS.includes(id);
    if (shipsSymbols && !symbols.delete(expected)) {
      problems.push(`Missing symbol package ${expected}.`);
    } else if (!shipsSymbols && symbols.delete(expected)) {
      problems.push(`${expected} exists, but ${id} ships no symbol package by design.`);
    }
  }

  for (const unexpected of symbols) {
    problems.push(`Unexpected symbol package ${unexpected}.`);
  }

  return problems;
}

function zipEntries(archive) {
  return execFileSync('unzip', ['-Z1', archive], { encoding: 'utf8' }).split('\n').filter(Boolean);
}

function zipEntryText(archive, entry) {
  return execFileSync('unzip', ['-p', archive, entry], { encoding: 'utf8' });
}

function checkPackageLayout(directory, version) {
  const problems = [];

  const analyzer = join(directory, `${ANALYZER_PACKAGE_ID}.${version}.nupkg`);
  const analyzerEntries = zipEntries(analyzer);
  if (analyzerEntries.some((entry) => entry.startsWith('lib/'))) {
    problems.push(
      `${ANALYZER_PACKAGE_ID} carries a lib/ entry; an analyzer must ship only under analyzers/dotnet/cs/.`
    );
  }
  if (!analyzerEntries.some((entry) => /^analyzers\/dotnet\/cs\/.*\.dll$/.test(entry))) {
    problems.push(`${ANALYZER_PACKAGE_ID} has no analyzers/dotnet/cs/*.dll entry.`);
  }

  const cli = join(directory, `${CLI_PACKAGE_ID}.${version}.nupkg`);
  const cliEntries = zipEntries(cli);
  const nuspec = cliEntries.find((entry) => entry.endsWith('.nuspec'));
  if (!nuspec || !zipEntryText(cli, nuspec).includes('<packageType name="DotnetTool"')) {
    problems.push(`${CLI_PACKAGE_ID} is not declared as a DotnetTool package.`);
  }
  const toolSettings = cliEntries.filter((entry) => entry.endsWith('/DotnetToolSettings.xml'));
  if (!toolSettings.some((entry) => zipEntryText(cli, entry).includes(`Name="${CLI_TOOL_COMMAND}"`))) {
    problems.push(`${CLI_PACKAGE_ID} does not register the ${CLI_TOOL_COMMAND} tool command.`);
  }

  return problems;
}

function main(argv) {
  const [directory, version] = argv;
  if (!directory || !version) {
    console.error('usage: verify-nuget-packages.mjs <package-directory> <version>');
    process.exit(1);
  }

  const fileNames = readdirSync(directory);
  const problems = checkPackageSet(fileNames, version);

  if (problems.length === 0) {
    problems.push(...checkPackageLayout(directory, version));
  }

  if (problems.length > 0) {
    for (const problem of problems) {
      console.error(`::error::${problem}`);
    }
    process.exit(1);
  }

  console.log(`Verified ${PACKAGE_IDS.length} packages at ${version}:`);
  for (const id of PACKAGE_IDS) {
    const symbols = PACKAGES_WITHOUT_SYMBOLS.includes(id) ? '' : ' (+ symbols)';
    console.log(`  ${id}.${version}.nupkg${symbols}`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
