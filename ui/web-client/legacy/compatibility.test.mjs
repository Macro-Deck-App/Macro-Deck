/**
 * The compatibility audit, run against what the build actually emitted (#824 step 7, issue #829).
 *
 * Requires `npm run build` first, the way `npm run check:es5` does - these check the artifacts, not
 * the sources, because the artifacts are what a browser on the floor is handed.
 */
import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  ALLOWED_BELOW_FLOOR,
  CSS_FEATURES,
  FLOOR_SAFE_CSS,
  JS_APIS,
  DOM_API_EXCEPTIONS,
  DOM_APIS_ABOVE_FLOOR,
  JS_APIS_ALLOWED_BELOW_FLOOR,
  PREFIXED_AT_FLOOR,
  scanDomApis,
  RENDERER_TOKENS,
  floorTargets,
  isVendorPrefixed,
  referencesApi,
  scanStylesheet,
  unsupportedAtFloor,
} from './compatibility.mjs';
import { ACCENT_SENTINELS } from './css-custom-properties.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const dist = path.join(here, '..', 'dist');

function emitted(pattern) {
  const match = readdirSync(dist).find(file => pattern.test(file));
  assert.ok(match, `no ${pattern} in dist/; run \`npm run build\` first`);
  return readFileSync(path.join(dist, match), 'utf8');
}

const targets = floorTargets();
const resolvedCss = emitted(/^styles-legacy-[a-f0-9]+\.css$/);
const bundle = emitted(/^main-[A-Za-z0-9]+\.js$/);
const polyfills = emitted(/^polyfills-[A-Za-z0-9]+\.js$/);

test('the floor covers browsers old enough to need this audit at all', () => {
  // Guards the audit itself: a floor accidentally narrowed to modern browsers would make every
  // check below pass while proving nothing.
  assert.ok(targets.includes('safari 9'));
  assert.ok(targets.some(target => target.startsWith('ios_saf 9')));
  assert.ok(targets.includes('chrome 30'));
});

test('the runtime and the client declare the same floor', () => {
  const read = (file) => readFileSync(file, 'utf8')
    .split('\n').map(line => line.trim()).filter(line => line && !line.startsWith('#')).join('\n');

  assert.equal(read(path.join(here, '.browserslistrc')), read(path.join(here, '..', '.browserslistrc')));
  assert.equal(
    read(path.join(here, '.browserslistrc')),
    read(path.join(here, '..', '..', 'runtime', '.browserslistrc')),
  );
});

test('every construct the shipped stylesheet uses is classified', () => {
  // The check that makes the rest of this file a gate rather than a snapshot: a construct nobody
  // has decided about fails here, instead of quietly reaching a browser that cannot parse it.
  const unclassified = [...scanStylesheet(resolvedCss)]
    .filter(construct => !isVendorPrefixed(construct))
    .filter(construct => !FLOOR_SAFE_CSS.has(construct))
    .filter(construct => !(construct in CSS_FEATURES))
    .sort();

  assert.deepEqual(unclassified, [],
    'add each to FLOOR_SAFE_CSS or to CSS_FEATURES in compatibility.mjs');
});

test('a construct nobody classified would fail the audit', () => {
  // Proves the check above is a gate rather than a snapshot of a sheet that happens to pass: a
  // modern property the sheet does not use today is neither floor-safe nor looked up, so adding
  // one fails rather than slipping through.
  const invented = scanStylesheet('.a{backdrop-filter:blur(2px)}');

  assert.ok(invented.has('property:backdrop-filter'));
  assert.ok(!FLOOR_SAFE_CSS.has('property:backdrop-filter'));
  assert.ok(!('property:backdrop-filter' in CSS_FEATURES));
});

test('every construct the floor cannot parse is a written-down exception', () => {
  const used = scanStylesheet(resolvedCss);
  const offenders = [];
  for (const [construct, featureId] of Object.entries(CSS_FEATURES)) {
    if (!used.has(construct)) continue;
    if (unsupportedAtFloor(featureId, targets).length === 0) continue;
    if (construct in PREFIXED_AT_FLOOR || construct in ALLOWED_BELOW_FLOOR) continue;
    offenders.push(construct);
  }

  assert.deepEqual(offenders, [],
    'down-level it in css-downlevel.mjs, add the prefixed form to PREFIXED_AT_FLOOR, '
    + 'or record why shipping it is safe in ALLOWED_BELOW_FLOOR');
});

test('every construct claimed to be prefixed really is prefixed in the shipped sheet', () => {
  // The entries above are excused because autoprefixer emits an old-syntax equivalent. That is a
  // claim about the build's output, so it is read back out of the output rather than trusted.
  const missing = Object.entries(PREFIXED_AT_FLOOR)
    .filter(([construct]) => scanStylesheet(resolvedCss).has(construct))
    .filter(([, prefixed]) => !resolvedCss.includes(prefixed))
    .map(([construct]) => construct);

  assert.deepEqual(missing, [], 'autoprefixer no longer emits the fallback this entry relies on');
});

test('the shipped stylesheet reads no custom property', () => {
  // The whole reason the resolved sheet exists: an engine without custom properties drops every
  // declaration containing var() rather than ignoring the function, so one left behind is a rule
  // the deck loses entirely.
  assert.ok(!scanStylesheet(resolvedCss).has('variable'));
});

test('nothing is excused that the stylesheet no longer uses', () => {
  // An exception outliving the construct it excused turns into permission nobody reviewed.
  const used = scanStylesheet(resolvedCss);
  const stale = Object.keys(ALLOWED_BELOW_FLOOR).filter(construct => !used.has(construct));

  assert.deepEqual(stale, [], 'remove it from ALLOWED_BELOW_FLOOR');
});

test('nothing is excused that the floor turned out to support', () => {
  // The other half of keeping the exceptions honest: raising the floor, or a browser ageing out of
  // the matrix, can make an entry describe a problem nobody has any more. Left alone it reads as
  // permission somebody reviewed, and the next person inherits a rule with no reason behind it.
  const stale = [...Object.keys(PREFIXED_AT_FLOOR), ...Object.keys(ALLOWED_BELOW_FLOOR)]
    .filter(construct => unsupportedAtFloor(CSS_FEATURES[construct], targets).length === 0);

  assert.deepEqual(stale, [], 'the floor covers this now - drop the entry');
});

test('every runtime API the client calls is either on the floor or polyfilled', () => {
  const offenders = [];
  for (const [api, featureId] of Object.entries(JS_APIS)) {
    if (!referencesApi(bundle, api)) continue;
    if (unsupportedAtFloor(featureId, targets).length === 0) continue;
    if (referencesApi(polyfills, api)) continue;
    if (api in JS_APIS_ALLOWED_BELOW_FLOOR) continue;
    offenders.push(api);
  }

  assert.deepEqual(offenders, [],
    'add a shim to polyfills.legacy.js, or record the degradation in JS_APIS_ALLOWED_BELOW_FLOOR');
});

test('every token only the renderer knows is accounted for', () => {
  // A token with no `:root` value resolves to whatever fallback the stylesheet declared, which is a
  // constant where the renderer meant a computed number. Adding one without saying how it reaches
  // the floor is how a deck ends up with every widget the same size.
  const authored = readFileSync(path.join(here, '..', 'src', 'styles.css'), 'utf8');
  const sheets = authored + readFileSync(
    path.join(here, '..', '..', 'runtime', 'styles', 'renderer.css'), 'utf8')
    + readFileSync(path.join(here, '..', '..', 'runtime', 'styles', 'grid.css'), 'utf8')
    + readFileSync(path.join(here, '..', '..', 'runtime', 'styles', 'widget-border.css'), 'utf8');
  const declared = new Set([...readFileSync(
    path.join(here, '..', '..', 'runtime', 'styles', 'tokens.css'), 'utf8')
    .matchAll(/^\s*(--[\w-]+)\s*:/gm)].map(match => match[1]));

  const rendererWritten = [...new Set([...sheets.matchAll(/var\(\s*(--[\w-]+)/g)].map(match => match[1]))]
    .filter(token => !declared.has(token))
    .sort();

  assert.deepEqual(rendererWritten, Object.keys(RENDERER_TOKENS).sort(),
    'record in RENDERER_TOKENS how the new token reaches a browser without custom properties');
});

const SHIPPED_SOURCE = [
  ['runtime/src/', path.join(here, '..', '..', 'runtime', 'src')],
  ['web-client/src/', path.join(here, '..', 'src')],
];

test('no shipped source reads a DOM member the floor does not have', () => {
  // The check the audit was missing. `es-check` sees syntax and the JS_APIS scan sees globals, but
  // a member read off a node is neither: on an engine without it the read yields `undefined` and
  // the surrounding guard silently takes the wrong branch. `paintDeck` returned early on exactly
  // that, and an iPad 2 showed a blank deck with a clean console and every request answered 200.
  const offenders = scanDomApis(SHIPPED_SOURCE).filter(hit => !(hit in DOM_API_EXCEPTIONS));

  assert.deepEqual(offenders, [],
    'use something the floor has, or record in DOM_API_EXCEPTIONS why naming it is safe');
});

test('a guard written on a DOM member above the floor would fail the audit', () => {
  // Proves the check above is a gate: the member that caused the bug is still watched for, and the
  // file it was in carries no exception, so writing it again fails rather than shipping.
  assert.ok('isConnected' in DOM_APIS_ABOVE_FLOOR);
  assert.ok(!('web-client/src/shell.ts:isConnected' in DOM_API_EXCEPTIONS));
});

test('nothing is excused that no longer names the member', () => {
  const named = scanDomApis(SHIPPED_SOURCE);
  const stale = Object.keys(DOM_API_EXCEPTIONS).filter(key => named.indexOf(key) === -1);

  assert.deepEqual(stale, [], 'the source no longer names it - drop the entry');
});

test('the client fills in exactly the accent placeholders the build emits', () => {
  // Two files, two languages, one contract: the build writes these into the shell and
  // `src/accent-styles.ts` substitutes them. A rename on either side leaves the accent unpainted.
  const source = readFileSync(path.join(here, '..', 'src', 'accent-styles.ts'), 'utf8');

  for (const sentinel of ACCENT_SENTINELS.values()) {
    assert.ok(source.includes(sentinel), `accent-styles.ts does not know ${sentinel}`);
  }
});

test('the shipped shell carries no accent placeholder into a stylesheet', () => {
  // The sentinels live in an inert <script type="text/plain">; one that reached the stylesheet
  // would be an unparseable value in a rule that paints the accent.
  for (const sentinel of ACCENT_SENTINELS.values()) {
    assert.ok(!resolvedCss.includes(sentinel));
  }
});

test('an API is found wherever code reaches it, never in data that merely spells its name', () => {
  assert.equal(referencesApi("icons:['clipboard','copy']", 'navigator.clipboard'), false);
  assert.equal(referencesApi('n.clipboard.writeText(t)', 'navigator.clipboard'), true);
  assert.equal(referencesApi("navigator['clipboard'].writeText(t)", 'navigator.clipboard'), true);
  assert.equal(referencesApi('const { clipboard } = navigator;', 'navigator.clipboard'), true);
  assert.equal(referencesApi('new Intl.DateTimeFormat()', 'Intl'), true);
});
