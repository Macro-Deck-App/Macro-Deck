/**
 * The down-level pass: the client, re-emitted for the browsers on issue #824's compatibility floor.
 *
 * esbuild's own floor is ES2015, so the bundle it just wrote is re-emitted here through
 * @babel/preset-env against ./.browserslistrc and becomes the one bundle the client ships - there
 * is no second entry and no feature gate, so every browser runs the same code (issue #829). What
 * the engines lack at runtime rather than at parse time comes from ./polyfills.legacy.js, bundled
 * the same way.
 *
 * The stylesheet is the one thing that stays engine-dependent: the sheets are authored with custom
 * properties, which the floor engines drop declarations over, so a resolved copy is emitted beside
 * the authored one and the shell's probe loads whichever the engine can read.
 */
import { transformAsync } from '@babel/core';
import autoprefixer from 'autoprefixer';
import { build } from 'esbuild';
import { readFile, writeFile, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import postcss from 'postcss';

import { downlevelCss, parseRootTokens } from './css-downlevel.mjs';
import { resolveCustomProperties } from './css-custom-properties.mjs';
import { flexGapFallback } from './flex-gap-fallback.mjs';
import { buildShell } from './index-html.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));

/**
 * Which emitted files exist only for the engines below the floor.
 *
 * Only the resolved stylesheet: the shell's probe fetches it, and no engine that takes that branch
 * has a service worker, so precaching it would cost every modern client a download it never reads.
 */
export function isLegacyAsset(file) {
  return /(^|\/)styles-legacy-[a-f0-9]+\.css$/.test(file);
}

async function readTargets() {
  const content = await readFile(path.join(here, '.browserslistrc'), 'utf8');
  return content.split('\n').map(line => line.trim()).filter(line => line && !line.startsWith('#'));
}

async function toEs5(code, targets) {
  const result = await transformAsync(code, {
    babelrc: false,
    configFile: false,
    compact: false,
    sourceMaps: false,
    // The bundle ends in a sourceMappingURL comment. Its map describes the pre-Babel code, so it is
    // dropped rather than carried into an output it no longer maps.
    inputSourceMap: false,
    // `bugfixes` keeps preset-env from applying a transform to engines that only carry a narrower
    // bug than the one the plugin exists for - notably Safari 9's block-scoped function bugs.
    presets: [['@babel/preset-env', { targets, bugfixes: true, useBuiltIns: false }]],
  });
  return result.code.replace(/\n?\/\/# sourceMappingURL=\S*\s*$/, '') + '\n';
}

async function bundlePolyfills() {
  // The polyfill packages are CommonJS and ESM in a mix, so they are bundled rather than
  // concatenated. The target is the same ES2015 esbuild emits for the client bundle; the Babel
  // pass below is what actually reaches ES5, for esbuild's own IIFE wrapper as much as for the
  // polyfill sources.
  const bundled = await build({
    entryPoints: [path.join(here, 'polyfills.legacy.js')],
    bundle: true,
    format: 'iife',
    target: 'es2015',
    write: false,
    logLevel: 'silent',
  });
  return bundled.outputFiles[0].text;
}

/**
 * @param out the build's output directory
 * @param scriptName the ES2015 bundle esbuild emitted, down-levelled rather than rebuilt so the
 *   shipped code is provably the same code, and then removed - it is not what ships
 * @param styleName the assembled stylesheet, kept as-is for engines with custom properties
 * @param hash the build's content hash, so every file is named the same way
 */
export async function buildLegacy({ out, scriptName, styleName, hash }) {
  const targets = await readTargets();

  const polyfillsCode = await toEs5(await bundlePolyfills(), targets);
  const mainCode = await toEs5(await readFile(path.join(out, scriptName), 'utf8'), targets);

  const authoredCss = await readFile(path.join(out, styleName), 'utf8');
  const resolved = resolveCustomProperties(authoredCss);
  // The tokens resolve the lengths inside min()/max()/clamp(); after the pass above there is
  // nothing left for them to resolve, and an unresolvable call is still left alone.
  const downlevelled = downlevelCss(resolved.css, parseRootTokens(authoredCss));
  const withGapFallback = flexGapFallback(downlevelled);
  const prefixed = postcss([autoprefixer({ overrideBrowserslist: targets })])
    .process(withGapFallback.css, { from: undefined }).css;

  const polyfillsName = `polyfills-${hash(polyfillsCode)}.js`;
  const mainName = `main-${hash(mainCode)}.js`;
  const legacyStylesName = `styles-legacy-${hash(prefixed)}.css`;
  await writeFile(path.join(out, polyfillsName), polyfillsCode);
  await writeFile(path.join(out, mainName), mainCode);
  await writeFile(path.join(out, legacyStylesName), prefixed);

  // The ES2015 bundle was an intermediate. Leaving it behind would ship a second copy of the whole
  // client that nothing loads, and the service worker would dutifully precache it.
  await rm(path.join(out, scriptName), { force: true });
  await rm(path.join(out, `${scriptName}.map`), { force: true });

  const indexPath = path.join(out, 'index.html');
  await writeFile(indexPath, buildShell(await readFile(indexPath, 'utf8'), {
    polyfillsSrc: polyfillsName,
    mainSrc: mainName,
    legacyStylesHref: legacyStylesName,
    accentTemplate: resolved.accentTemplate,
  }));

  return {
    files: [polyfillsName, mainName, legacyStylesName],
    gapFallbacks: withGapFallback.count,
    rendererOnlyDeclarations: resolved.unresolved,
  };
}
