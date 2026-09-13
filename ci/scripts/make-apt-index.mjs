#!/usr/bin/env node

import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

import { mapNativePackageVersion, parseReleaseVersion } from './release-version.mjs';

export const APT_SUITES = ['stable', 'beta'];

export function aptSuitesFor(version) {
  return parseReleaseVersion(version).isBeta ? ['beta'] : ['stable', 'beta'];
}

// apt percent-encodes a tilde in the request path, and the CDN in front of the
// bucket is not guaranteed to decode it back, so no pool URL may contain one.
export function poolPath(pkg, debVersion, architecture) {
  return `pool/main/${pkg[0]}/${pkg}/${pkg}_${debVersion.replaceAll('~', '.')}_${architecture}.deb`;
}

export function parseStanzas(text) {
  return (text ?? '')
    .split(/\n\s*\n/)
    .map((block) => block.trim())
    .filter((block) => block !== '')
    .map((block) => ({ text: block, fields: parseFields(block) }));
}

function parseFields(block) {
  const fields = new Map();
  for (const line of block.split('\n')) {
    const match = /^([^\s:]+):\s*(.*)$/.exec(line);
    if (match !== null) {
      fields.set(match[1].toLowerCase(), match[2].trim());
    }
  }
  return fields;
}

const identity = (stanza) =>
  ['package', 'version', 'architecture'].map((field) => stanza.fields.get(field)).join(' ');

const render = (stanzas) => (stanzas.length === 0 ? '' : `${stanzas.map((stanza) => stanza.text).join('\n\n')}\n`);

export function mergePackages(existingText, newStanzaText) {
  const existing = parseStanzas(existingText);
  const added = parseStanzas(newStanzaText);
  if (added.length !== 1) {
    throw new Error(`expected exactly one new stanza, got ${added.length}`);
  }

  const [stanza] = added;
  for (const field of ['package', 'version', 'architecture', 'filename', 'sha256']) {
    if (!stanza.fields.get(field)) {
      throw new Error(`the new stanza has no ${field} field`);
    }
  }

  const published = existing.find((candidate) => identity(candidate) === identity(stanza));
  if (published === undefined) {
    return render([...existing, stanza]);
  }
  if (published.fields.get('sha256') !== stanza.fields.get('sha256')) {
    throw new Error(`${identity(stanza)} is already published with a different SHA256`);
  }
  return render(existing);
}

function main([command, ...args]) {
  const usage = () => {
    console.error(
      'usage: make-apt-index.mjs suites [version] | deb-version <version> | pool-path <package> <debVersion> <architecture> | merge <existingPackages> <newStanza> <outFile>',
    );
    process.exit(1);
  };

  try {
    if (command === 'suites' && args.length <= 1) {
      console.log((args.length === 0 ? APT_SUITES : aptSuitesFor(args[0])).join(' '));
    } else if (command === 'deb-version' && args.length === 1) {
      console.log(mapNativePackageVersion(args[0]).debVersion);
    } else if (command === 'pool-path' && args.length === 3) {
      console.log(poolPath(...args));
    } else if (command === 'merge' && args.length === 3) {
      const [existingFile, stanzaFile, outFile] = args;
      writeFileSync(outFile, mergePackages(readFileSync(existingFile, 'utf8'), readFileSync(stanzaFile, 'utf8')));
    } else {
      usage();
    }
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2));
}
