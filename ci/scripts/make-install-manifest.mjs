#!/usr/bin/env node
// The format must match what ui/bootstrapper/src/install_integrity.rs reads.

import { createHash, createPublicKey, verify as verifySignature } from 'node:crypto';
import { existsSync, lstatSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

export const MANIFEST_FILE = 'install-manifest.json';
export const SIGNATURE_FILE = `${MANIFEST_FILE}.sig`;
export const FORMAT_VERSION = 1;

const EXCLUDED = new Set([MANIFEST_FILE, SIGNATURE_FILE]);

export function listFiles(hostDir) {
  const files = [];
  const walk = (dir, prefix) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const relative = prefix ? `${prefix}/${entry.name}` : entry.name;
      const absolute = join(dir, entry.name);
      if (entry.isDirectory()) {
        walk(absolute, relative);
      } else if (entry.isFile() && !EXCLUDED.has(relative)) {
        files.push(relative);
      }
    }
  };
  walk(hostDir, '');
  return files.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

export function sha256File(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex');
}

export function buildManifest(hostDir, version, commit) {
  if (!version || !commit) {
    throw new Error('version and commit are required');
  }
  return {
    formatVersion: FORMAT_VERSION,
    version,
    commit,
    files: listFiles(hostDir).map((path) => {
      const absolute = join(hostDir, ...path.split('/'));
      return { path, size: statSync(absolute).size, sha256: sha256File(absolute) };
    }),
  };
}

export function serializeManifest(manifest) {
  return `${JSON.stringify(manifest, null, 2)}\n`;
}

export function readManifest(hostDir) {
  const manifest = JSON.parse(readFileSync(join(hostDir, MANIFEST_FILE), 'utf8'));
  if (manifest.formatVersion !== FORMAT_VERSION || !Array.isArray(manifest.files)) {
    throw new Error(`${MANIFEST_FILE} has an unsupported format`);
  }
  return manifest;
}

export function writeManifest(hostDir, version, commit) {
  const manifest = buildManifest(hostDir, version, commit);
  writeFileSync(join(hostDir, MANIFEST_FILE), serializeManifest(manifest));
  return manifest;
}

export function rewriteManifest(hostDir) {
  const { version, commit } = readManifest(hostDir);
  return writeManifest(hostDir, version, commit);
}

export function findProblems(hostDir, manifest) {
  const problems = [];
  for (const file of manifest.files) {
    const absolute = join(hostDir, ...file.path.split('/'));
    if (!existsSync(absolute) || !lstatSync(absolute).isFile()) {
      problems.push({ path: file.path, problem: 'missing' });
    } else if (statSync(absolute).size !== file.size || sha256File(absolute) !== file.sha256) {
      problems.push({ path: file.path, problem: 'modified' });
    }
  }
  return problems;
}

function decodeBase64Text(value) {
  return Buffer.from(value.trim(), 'base64').toString('utf8');
}

function lines(text) {
  return text.split(/\r?\n/).filter((line) => line.length > 0);
}

export function parsePublicKey(base64Key) {
  const keyLine = lines(decodeBase64Text(base64Key)).find((line) => !line.startsWith('untrusted comment:'));
  const raw = Buffer.from(keyLine ?? '', 'base64');
  if (raw.length !== 42 || raw.subarray(0, 2).toString('latin1') !== 'Ed') {
    throw new Error('not a minisign public key');
  }
  return { keyId: raw.subarray(2, 10), key: raw.subarray(10) };
}

function ed25519Key(rawKey) {
  return createPublicKey({
    key: { kty: 'OKP', crv: 'Ed25519', x: rawKey.toString('base64url') },
    format: 'jwk',
  });
}

// Accepts the base64 text that tauri signer writes, which wraps a regular minisign signature file.
export function verifyMinisign(data, base64Signature, base64PublicKey) {
  const publicKey = parsePublicKey(base64PublicKey);
  const [, signatureLine, trustedLine, globalLine] = lines(decodeBase64Text(base64Signature));
  const signature = Buffer.from(signatureLine ?? '', 'base64');
  if (signature.length !== 74 || !trustedLine?.startsWith('trusted comment: ')) {
    throw new Error('not a minisign signature');
  }
  const algorithm = signature.subarray(0, 2).toString('latin1');
  if (!signature.subarray(2, 10).equals(publicKey.keyId)) {
    throw new Error('the signature was made with a different key');
  }
  if (algorithm !== 'ED' && algorithm !== 'Ed') {
    throw new Error(`unsupported signature algorithm ${algorithm}`);
  }
  const key = ed25519Key(publicKey.key);
  const signed = algorithm === 'ED' ? createHash('blake2b512').update(data).digest() : data;
  if (!verifySignature(null, signed, key, signature.subarray(10))) {
    throw new Error('the signature does not match the manifest');
  }
  const trustedComment = Buffer.from(trustedLine.slice('trusted comment: '.length), 'utf8');
  const globalSignature = Buffer.from(globalLine ?? '', 'base64');
  if (!verifySignature(null, Buffer.concat([signature.subarray(10), trustedComment]), key, globalSignature)) {
    throw new Error('the trusted comment signature does not match');
  }
}

export function resolvePublicKey(value) {
  if (value.endsWith('.json')) {
    return JSON.parse(readFileSync(value, 'utf8')).plugins.updater.pubkey;
  }
  return value;
}

export function verifyInstallation(hostDir, base64PublicKey) {
  const manifest = readManifest(hostDir);
  if (base64PublicKey) {
    verifyMinisign(
      readFileSync(join(hostDir, MANIFEST_FILE)),
      readFileSync(join(hostDir, SIGNATURE_FILE), 'utf8'),
      base64PublicKey
    );
  }
  return { manifest, problems: findProblems(hostDir, manifest) };
}

function main(argv) {
  const [command, hostDir, ...rest] = argv;
  if (command === 'write' && hostDir && rest.length === 2) {
    const manifest = writeManifest(hostDir, rest[0], rest[1]);
    console.log(`Wrote ${join(hostDir, MANIFEST_FILE)} (${manifest.files.length} files, ${manifest.version})`);
    return;
  }
  if (command === 'rewrite' && hostDir && rest.length === 0) {
    const manifest = rewriteManifest(hostDir);
    console.log(`Rewrote ${join(hostDir, MANIFEST_FILE)} (${manifest.files.length} files, ${manifest.version})`);
    return;
  }
  if (command === 'verify' && hostDir && (rest.length === 0 || (rest.length === 2 && rest[0] === '--pubkey'))) {
    const publicKey = rest.length === 2 ? resolvePublicKey(rest[1]) : null;
    const { manifest, problems } = verifyInstallation(hostDir, publicKey);
    if (manifest.files.length === 0) {
      console.error(`error: ${MANIFEST_FILE} in ${hostDir} lists no files`);
      process.exit(1);
    }
    for (const { path, problem } of problems) {
      console.error(`error: ${problem}: ${path}`);
    }
    if (problems.length > 0) {
      process.exit(1);
    }
    console.log(`Verified ${manifest.files.length} files in ${hostDir}${publicKey ? ' and the manifest signature' : ''}`);
    return;
  }
  console.error(
    'usage: make-install-manifest.mjs write <hostDir> <version> <commit> | rewrite <hostDir> | verify <hostDir> [--pubkey <key|tauri.conf.json>]'
  );
  process.exit(1);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(`error: ${error.message}`);
    process.exit(1);
  }
}
