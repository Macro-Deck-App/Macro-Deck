/**
 * Bundles the client.
 *
 * One stylesheet is assembled by ./stylesheet.mjs from the runtime package's sheets plus the
 * client's own.
 *
 * What esbuild emits here is an intermediate: ./legacy down-levels it to the ES5 the compatibility
 * floor needs, and that is the one bundle every browser loads. The stylesheet is the exception -
 * it is shipped both as authored and resolved, because only the floor engines need the latter.
 *
 * The bundle and the stylesheet ship under content-hashed names, which is what earns them the
 * host's `immutable` cache header (see StaticAssetCachePolicy) and what makes the service worker's
 * precache manifest meaningful: a new build is a new set of URLs, so nothing can be served stale.
 */
import { build } from 'esbuild';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { createHash } from 'node:crypto';
import { mkdir, copyFile, readFile, writeFile, readdir, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildLegacy, isLegacyAsset } from './legacy/build-legacy.mjs';
import { assembleStylesheet } from './stylesheet.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const runtimeStyles = path.resolve(here, '..', 'runtime', 'styles');

/**
 * The device this artifact is for, as `--target=<id>`.
 *
 * A target is data (see `src/targets/`), and each artifact compiles in exactly one of them: the
 * active-target module is swapped at build time rather than branched on at runtime, so no build
 * carries the wiring of a device it is not for. Everything else about the build is the same, which
 * is the point - a new device is a manifest and a build flag, not a fork.
 */
const targetId = readTargetId();
const out = path.join(here, targetId === null ? 'dist' : `dist-${targetId}`);

/** A target is served from its own directory under the host's wwwroot (issue #727). */
const baseHref = targetId === null ? '/' : `/targets/${targetId}/`;

function readTargetId() {
  for (const argument of process.argv.slice(2)) {
    if (argument.indexOf('--target=') === 0) {
      const id = argument.slice('--target='.length);
      if (!/^[a-z0-9][a-z0-9-]{0,31}$/.test(id)) {
        throw new Error(`--target must match the host's own id rule, got '${id}'`);
      }
      return id;
    }
  }
  return null;
}

/** Never precached and never given a hashed name: the host refuses to cache it, and so must we. */
const WORKER_FILE = 'macro-deck-worker.js';

function hash(content) {
  return createHash('sha256').update(content).digest('hex').slice(0, 16);
}

async function copyTree(from, to) {
  await mkdir(to, { recursive: true });
  for (const entry of await readdir(from)) {
    const source = path.join(from, entry);
    if ((await stat(source)).isDirectory()) await copyTree(source, path.join(to, entry));
    else await copyFile(source, path.join(to, entry));
  }
}

async function listFiles(directory, prefix = '') {
  const found = [];
  for (const entry of await readdir(directory)) {
    const full = path.join(directory, entry);
    if ((await stat(full)).isDirectory()) found.push(...await listFiles(full, prefix + entry + '/'));
    else found.push(prefix + entry);
  }
  return found;
}

// A stale hashed bundle left behind from an earlier build would be precached by the worker and
// served forever, so the output directory starts empty.
await rm(out, { recursive: true, force: true });
await mkdir(path.join(out, 'icons'), { recursive: true });

const bundle = await build({
  entryPoints: [path.join(here, 'src', 'main.ts')],
  // The one module a target replaces, swapped at resolve time rather than branched on at runtime, so
  // a build carries only the target it is for. A plugin rather than `alias`, which esbuild applies
  // to bare specifiers alone.
  plugins: targetId === null ? [] : [{
    name: 'active-target',
    setup(pluginBuild) {
      const replacement = path.join(here, 'src', 'targets', targetId, 'active-target.ts');
      pluginBuild.onResolve({ filter: /^\.\/targets\/active-target$/ }, () => ({ path: replacement }));
    },
  }],
  bundle: true,
  format: 'iife',
  // The floor is ES5, but esbuild cannot emit it; ./legacy owns that. What this target guarantees
  // is that nothing newer than the down-level pass can lower reaches the bundle.
  target: 'es2015',
  outdir: out,
  entryNames: 'main-[hash]',
  sourcemap: true,
  metafile: true,
  logLevel: 'info',
});

// The whole application catalogue is half a megabyte of strings a deck never paints (#833), and it
// reaches the bundle through any single reference - a key constant is enough. Nothing here fails when
// it comes back: the client simply gets heavy again, on the hardware least able to pay for it.
for (const [output, emitted] of Object.entries(bundle.metafile.outputs)) {
  for (const [input, { bytesInOutput }] of Object.entries(emitted.inputs ?? {})) {
    if (/localization\/generated\/app-strings/.test(input) && bytesInOutput > 0) {
      throw new Error(`${output} carries ${bytesInOutput} bytes of ${input}. `
        + 'A deck client renders the slice of the catalogue it can paint: import ClientAppStrings, '
        + 'not AppStrings.');
    }
  }
}

const scriptName = path.basename(
  Object.keys(bundle.metafile.outputs).find(file => file.endsWith('.js')),
);

const styles = await assembleStylesheet();
const styleName = `styles-${hash(styles)}.css`;
// The sheets reference their masks as ./icons/<name>.svg, which holds once they are copied beside it.
await writeFile(path.join(out, styleName), styles);

for (const icon of await readdir(path.join(runtimeStyles, 'icons'))) {
  await copyFile(path.join(runtimeStyles, 'icons', icon), path.join(out, 'icons', icon));
}

await copyTree(path.join(here, 'public'), out);

/**
 * The commit the shell was built from. `ci/scripts/stage-host.sh` reads it back out of the staged
 * `index.html` to warn when a client is staged against a host it was not built with, and the
 * client's own version check compares it against what the host reports. Absent, both go quiet and
 * pretend everything agrees.
 */
const uiCommit = await readUiCommit();

const html = (await readFile(path.join(here, 'index.html'), 'utf8'))
  .replace('href="styles.css"', `href="${styleName}"`)
  .replace('src="main.js"', `src="${scriptName}"`)
  .replace('</head>', `  <meta name="macro-deck-ui-commit" content="${uiCommit}">\n</head>`)
  .replace('<base href="/">', `<base href="${baseHref}">`);
await writeFile(path.join(out, 'index.html'), html);

// Down-levels the bundle to ES5 in place - what esbuild wrote above is an intermediate, not what
// ships - and emits the polyfills, the resolved stylesheet and the shell that ties them together.
const legacy = await buildLegacy({ out, scriptName, styleName, hash });

// Everything the client needs to paint a deck offline. Source maps are a debugging aid nobody
// offline asks for, and the worker itself must never end up inside its own cache. Nor does the
// resolved stylesheet: only engines without custom properties load it, and none of those has a
// service worker, so precaching it would cost every other client a download it never reads.
const assets = (await listFiles(out))
  .filter(file => !file.endsWith('.map') && file !== WORKER_FILE && !isLegacyAsset(file))
  .sort();

const fingerprints = [];
for (const asset of assets) fingerprints.push(asset + ':' + hash(await readFile(path.join(out, asset))));

const manifest = { version: hash(fingerprints.join('\n')), assets: assets };
const worker = (await readFile(path.join(here, 'src', 'pwa', 'service-worker.js'), 'utf8'))
  .replace(/^var MANIFEST = .*$/m, `var MANIFEST = ${JSON.stringify(manifest)};`);
await writeFile(path.join(out, WORKER_FILE), worker);

console.log(`web client bundled into ${path.basename(out)}/ (${assets.length} precached assets, version ${manifest.version})`);
console.log(`down-levelled to ES5: ${legacy.files.join(', ')} (${legacy.gapFallbacks} flex gap fallbacks, `
  + `${legacy.rendererOnlyDeclarations.length} renderer-only declarations)`);

async function readUiCommit() {
  try {
    const { stdout } = await promisify(execFile)('git', ['rev-parse', '--short=7', 'HEAD'], { cwd: here });
    return stdout.trim();
  } catch {
    // A tarball with no git history still builds; the check simply has nothing to compare.
    return '';
  }
}
