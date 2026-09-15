import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { resolveDotnetRuntimes } from './resolve-dotnet-runtime.mjs';

const hashA = 'a'.repeat(128);
const hashB = 'b'.repeat(128);

function file(version, rid, extension, hash = hashA, host = 'https://builds.dotnet.microsoft.com') {
  const name = `aspnetcore-runtime-${rid}.${extension}`;
  return { name, rid, url: `${host}/dotnet/aspnetcore/Runtime/${version}/${name}`, hash };
}

function release(version, files) {
  return { runtime: { version }, 'aspnetcore-runtime': { version, files } };
}

function channelDocument(latest, releases) {
  return { 'latest-runtime': latest, releases };
}

const ten = channelDocument('10.0.14', [
  release('10.0.14', [file('10.0.14', 'osx-arm64', 'tar.gz', hashB), file('10.0.14', 'win-x64', 'zip', hashB)]),
  release('10.0.12', [
    file('10.0.12', 'osx-arm64', 'tar.gz'),
    file('10.0.12', 'linux-x64', 'tar.gz'),
    file('10.0.12', 'win-x64', 'zip'),
    file('10.0.12', 'win-x64', 'exe'),
  ]),
]);

const eleven = channelDocument('11.0.2', [
  release('11.0.2', [file('11.0.2', 'osx-arm64', 'tar.gz', hashB)]),
  release('11.0.1', [file('11.0.1', 'osx-arm64', 'tar.gz')]),
]);

test('the host channel resolves to the runtime the SDK compiles against, not the newest patch', () => {
  const [archive] = resolveDotnetRuntimes({ channels: ['10.0'] }, { '10.0': ten }, '10.0.12', 'osx-arm64');
  assert.equal(archive.channel, '10.0');
  assert.equal(archive.version, '10.0.12');
  assert.equal(archive.sha512, hashA);
  assert.match(archive.url, /\/10\.0\.12\/aspnetcore-runtime-osx-arm64\.tar\.gz$/);
});

test('an additional channel resolves to its latest runtime', () => {
  const archives = resolveDotnetRuntimes(
    { channels: ['10.0', '11.0'] },
    { '10.0': ten, '11.0': eleven },
    '10.0.12',
    'osx-arm64',
  );
  assert.deepEqual(
    archives.map((archive) => [archive.channel, archive.version]),
    [
      ['10.0', '10.0.12'],
      ['11.0', '11.0.2'],
    ],
  );
});

test('windows gets the zip archive and other platforms the tarball', () => {
  const [windows] = resolveDotnetRuntimes({ channels: ['10.0'] }, { '10.0': ten }, '10.0.12', 'win-x64');
  const [linux] = resolveDotnetRuntimes({ channels: ['10.0'] }, { '10.0': ten }, '10.0.12', 'linux-x64');
  assert.match(windows.url, /aspnetcore-runtime-win-x64\.zip$/);
  assert.match(linux.url, /aspnetcore-runtime-linux-x64\.tar\.gz$/);
});

test('refuses to stage when the SDK runtime channel is not bundled', () => {
  assert.throws(
    () => resolveDotnetRuntimes({ channels: ['11.0'] }, { '11.0': eleven }, '10.0.12', 'osx-arm64'),
    /10\.0/,
  );
});

test('refuses a runtime version the channel never released', () => {
  assert.throws(() => resolveDotnetRuntimes({ channels: ['10.0'] }, { '10.0': ten }, '10.0.13', 'osx-arm64'), /10\.0\.13/);
});

test('refuses a platform the release has no archive for', () => {
  assert.throws(
    () => resolveDotnetRuntimes({ channels: ['10.0'] }, { '10.0': ten }, '10.0.12', 'linux-arm64'),
    /linux-arm64/,
  );
});

test('refuses an archive hosted anywhere but builds.dotnet.microsoft.com', () => {
  const document = channelDocument('10.0.12', [
    release('10.0.12', [file('10.0.12', 'osx-arm64', 'tar.gz', hashA, 'https://example.com')]),
  ]);
  assert.throws(() => resolveDotnetRuntimes({ channels: ['10.0'] }, { '10.0': document }, '10.0.12', 'osx-arm64'), /url/);
});

test('refuses an archive whose sha512 is malformed', () => {
  const document = channelDocument('10.0.12', [release('10.0.12', [file('10.0.12', 'osx-arm64', 'tar.gz', 'abc123')])]);
  assert.throws(() => resolveDotnetRuntimes({ channels: ['10.0'] }, { '10.0': document }, '10.0.12', 'osx-arm64'), /sha512/);
});

test('the bundled runtime channels include the target framework the host builds for', () => {
  const config = JSON.parse(
    readFileSync(fileURLToPath(new URL('../dotnet-runtime/bundled-runtimes.json', import.meta.url)), 'utf8'),
  );
  const props = readFileSync(fileURLToPath(new URL('../../Directory.Build.props', import.meta.url)), 'utf8');
  const match = /<TargetFramework>net(\d+\.\d+)<\/TargetFramework>/.exec(props);
  assert.ok(match, 'Directory.Build.props declares a netX.Y TargetFramework');
  assert.ok(config.channels.includes(match[1]), `bundled-runtimes.json lists channel ${match[1]}`);
});
