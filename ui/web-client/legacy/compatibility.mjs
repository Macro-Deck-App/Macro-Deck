/**
 * The compatibility audit: what the client ships, checked against the floor it declares (issue #829,
 * following #824's step 7).
 *
 * The floor lives in `.browserslistrc` and the answers live in `caniuse-lite`, which the build
 * already depends on through autoprefixer. Nothing here is a judgement about a browser - the data
 * decides, and this module only says which constructs to look the answer up for.
 *
 * The point is the classification, not the lookup. Every at-rule, property, function, unit and
 * selector the shipped stylesheet uses has to be either known to predate the floor, checked against
 * caniuse, or written down as a deliberate exception with a reason. A construct nobody classified
 * fails the audit, which is what keeps a new modern feature from reaching a 2015 browser unnoticed.
 */
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import browserslist from 'browserslist';
import { feature, features } from 'caniuse-lite';

const here = path.dirname(fileURLToPath(import.meta.url));

/** The declared floor, expanded to every browser version it covers. */
export function floorTargets(browserslistrc = path.join(here, '.browserslistrc')) {
  const queries = readFileSync(browserslistrc, 'utf8')
    .split('\n').map(line => line.trim()).filter(line => line && !line.startsWith('#'));
  return browserslist(queries);
}

/**
 * The browsers in `targets` that do not have `featureId` in the form the stylesheet writes it.
 *
 * Both a partial implementation ('a') and one that needs a vendor prefix ('y x') count as missing.
 * The audit's job is to force a decision, and "some of it works somewhere" and "it works if you
 * spell it differently" are exactly the answers that need one - the second especially, because the
 * prefixed spelling is autoprefixer's output rather than anything the sheets say.
 */
export function unsupportedAtFloor(featureId, targets) {
  if (featureId === NEWER_THAN_THE_DATA) return targets;
  const data = features[featureId];
  if (data === undefined) throw new Error(`no such caniuse feature: ${featureId}`);
  const stats = feature(data).stats;
  return targets.filter((target) => {
    const separator = target.lastIndexOf(' ');
    const support = (stats[target.slice(0, separator)] || {})[target.slice(separator + 1)];
    return support === undefined || support[0] !== 'y' || support.indexOf('x') !== -1;
  });
}

/**
 * Stands in for a construct `caniuse-lite` carries no data on.
 *
 * The trimmed dataset the build ships drops features nothing in its browser matrix disagrees about
 * any more. Absent data is treated as unsupported rather than skipped: everything that lands here
 * postdates the floor by years, and the alternative is an audit that goes quiet exactly where it
 * knows least.
 */
export const NEWER_THAN_THE_DATA = null;

/** Constructs whose availability has to be looked up, and the caniuse feature that answers it. */
export const CSS_FEATURES = {
  'at-rule:supports': 'css-featurequeries',
  'function:linear-gradient': 'Partial support is about double-position colour stops, and the one '
    + 'gradient in the sheets is exactly that - `linear-gradient(#000 0 0)`, inside the ring mask. '
    + 'It travels with property:mask below, which the @supports guard already hides on the floor.',
  'function:conic-gradient': 'css-conic-gradients',
  'function:calc': 'calc',
  'function:linear-gradient': 'css-gradients',
  'property:accent-color': NEWER_THAN_THE_DATA,
  'property:animation': 'css-animation',
  'property:filter': 'css-filters',
  'property:flex': 'flexbox',
  'property:gap': 'flexbox-gap',
  'property:mask': 'css-masks',
  'property:mask-composite': 'css-masks',
  'property:object-fit': 'object-fit',
  'property:scale': NEWER_THAN_THE_DATA,
  'property:scrollbar-gutter': NEWER_THAN_THE_DATA,
  'property:touch-action': 'css-touch-action',
  'property:transform': 'transforms2d',
  'property:transition': 'css-transitions',
  'property:user-select': 'user-select-none',
  'pseudo:focus-visible': 'css-focus-visible',
  'unit:rem': 'rem',
  'unit:vw': 'viewport-units',
  'variable': 'css-variables',
};

/**
 * Constructs the floor has only behind a vendor prefix, and the prefixed form that carries them.
 *
 * autoprefixer emits these from the same `.browserslistrc`, so the entry is not a claim - the audit
 * checks the prefixed form is really in the shipped sheet. Without that check an autoprefixer
 * setting could stop emitting one and nothing would notice.
 */
export const PREFIXED_AT_FLOOR = {
  'property:animation': '-webkit-animation:',
  'property:filter': '-webkit-filter:',
  'property:transform': '-webkit-transform:',
  'property:user-select': '-webkit-user-select:',
};

/**
 * Deliberate exceptions: a construct the floor does not have, shipped anyway, and why that is safe.
 *
 * Every entry is something an old parser drops on its own, leaving the declaration it was in
 * without that one effect rather than breaking the page - so the deck still paints. Anything that
 * would *lose* the deck belongs in the down-level pass instead of here.
 */
export const ALLOWED_BELOW_FLOOR = {
  'function:linear-gradient': 'Partial support is about double-position colour stops, and the one '
    + 'gradient in the sheets is exactly that - `linear-gradient(#000 0 0)`, inside the ring mask. '
    + 'It travels with property:mask below, which the @supports guard already hides on the floor.',
  'function:conic-gradient': 'The comet and marching-ants ring styles. An engine without it paints '
    + 'no ring; the tinted styles it does understand are handled by the runtime fallback.',
  'property:accent-color': 'Tints a native checkbox. Without it the checkbox is the UA default.',
  'property:gap': 'flex-gap-fallback.mjs emits the equivalent margins under .no-flex-gap.',
  'property:mask': 'Trims the ring to its border. Guarded by the @supports above, so an engine '
    + 'without it never shows the ring at all rather than showing a filled rectangle.',
  'property:mask-composite': 'As property:mask.',
  'property:object-fit': 'object-fit.legacy.js reads the marker the down-level pass leaves and '
    + 'frames the image from script.',
  'property:scale': 'The artwork fade-in nudges scale by 2%. Without it the image simply fades.',
  'property:scrollbar-gutter': 'Reserves scrollbar space. Without it the layout shifts by the '
    + 'scrollbar width when a list grows, which no floor engine has anyway (overlay scrollbars).',
  'property:touch-action': 'Suppresses double-tap zoom. The Pointer Events shim calls '
    + 'preventDefault on the source touch, which is what actually does this on the floor.',
  'pseudo:focus-visible': 'A focus ring for keyboard users. An engine without it drops the rule '
    + 'and shows no ring; none of the floor devices has a keyboard.',
  'function:calc': 'Partial on the Android 4.4 stock browser, which carries documented calc() bugs '
    + 'rather than no support. Used for lengths that are already close to their fallback, so a '
    + 'mis-evaluated one shifts a box rather than losing it.',
};

/**
 * The custom properties the renderer writes per element, and how each one reaches the floor.
 *
 * These have no `:root` value, so the down-level pass can only emit the fallback the stylesheet
 * declared - which is a constant, not the number the renderer computed. Whatever supplies the real
 * one is named here, and a token that supplies nothing says so, so that a degradation is something
 * somebody wrote down rather than something nobody noticed.
 */
export const RENDERER_TOKENS = {
  '--widget-radius': 'render/custom-properties.ts: a rule scoped to the deck surface.',
  '--widget-scale': 'render/custom-properties.ts: folded into the same radius rule.',
  '--wb-width': 'render/widget-border.ts: written on the ring as `padding`.',
  '--wb-color': 'render/widget-border.ts: written on the ring as `background`.',
  '--wb-phase': 'render/widget-border.ts: written on the ring as `animation-delay`.',
  '--widget-artwork-opacity': 'Nothing. It is read inside a @keyframes `to` frame, which no inline '
    + 'style can reach, so artwork with a reduced opacity fades in to full and settles to its own '
    + 'opacity when the animation ends. One frame of the wrong brightness, on a browser from 2015.',
};

/**
 * Runtime APIs the client calls that the floor lacks and no polyfill supplies, and why that is not
 * a broken client - except where it is, which is recorded here rather than hidden.
 */
export const JS_APIS_ALLOWED_BELOW_FLOOR = {
  Intl: 'Guarded by `hasIntlParts()` in the runtime, which also covers the engines that have Intl '
    + 'but not `formatToParts` (Safari 10-12). Without it a clock reads the device zone and dates '
    + 'and times are formatted in digits. A polyfill would mean shipping locale data for seven '
    + 'languages to browsers that are already the slowest ones the deck runs on.',
  'navigator.wakeLock': 'Feature-detected before use; without it the screen sleeps on its own, '
    + 'which is a comfort the deck loses rather than a function.',
};

/** Constructs that predate every browser on the floor. Listed rather than assumed. */
export const FLOOR_SAFE_CSS = new Set([
  'at-rule:media', 'at-rule:keyframes', 'at-rule:-webkit-keyframes',
  'function:not', 'function:rgba', 'function:url',
  'function:rotate', 'function:scale', 'function:translate', 'function:translateX', 'function:translateY',
  'function:hue-rotate', 'function:webkit-linear-gradient',
  'unit:px', 'unit:em', 'unit:s', 'unit:ms', 'unit:deg', 'unit:turn', 'unit:%',
  'pseudo:after', 'pseudo:before', 'pseudo:first-child', 'pseudo:hover',
  'pseudo:focus', 'pseudo:disabled', 'pseudo:not', 'pseudo:root',
  ...[
    'background', 'background-color', 'border', 'border-bottom', 'border-color', 'border-left',
    'border-left-color', 'border-radius', 'border-top', 'border-top-color', 'bottom', 'box-shadow',
    'box-sizing', 'color', 'color-scheme', 'content', 'cursor', 'display', 'fill', 'fill-opacity',
    'font', 'font-family', 'font-feature-settings', 'font-size', 'font-variant-numeric',
    'font-weight', 'height', 'isolation', 'left', 'line-height', 'margin', 'margin-bottom',
    'margin-left', 'margin-top', 'max-height', 'max-width', 'min-height', 'min-width', 'opacity',
    'outline', 'overflow', 'overflow-wrap', 'overflow-x', 'overflow-y', 'padding', 'padding-bottom',
    'padding-left', 'padding-right', 'padding-top', 'pointer-events', 'position', 'right', 'stroke', 'stroke-linecap',
    'stroke-linejoin', 'text-align', 'text-decoration', 'text-overflow', 'text-shadow', 'top',
    'vertical-align', 'white-space', 'width', 'word-break', 'z-index',
    // Flexbox longhands travel with `flex`, which is checked; autoprefixer emits the old syntax.
    'align-items', 'flex-direction', 'flex-shrink', 'flex-wrap', 'justify-content',
    // Longhands of a checked shorthand.
    'animation-delay', 'animation-duration', 'transform-origin',
    'mask-image', 'mask-position', 'mask-repeat', 'mask-size',
  ].map(property => `property:${property}`),
]);

/** A vendor-prefixed property is autoprefixer's output for the unprefixed one it was asked about. */
export function isVendorPrefixed(construct) {
  return /^(at-rule|property):-(webkit|moz|ms|o)-/.test(construct);
}

/** Every construct the stylesheet uses, as `kind:name`. */
export function scanStylesheet(css) {
  const found = new Set();
  const add = (kind, name) => found.add(`${kind}:${name}`);
  for (const match of css.matchAll(/@([a-zA-Z-]+)/g)) add('at-rule', match[1]);
  for (const match of css.matchAll(/(?:^|[;{])\s*(-?[a-zA-Z][a-zA-Z0-9-]*)\s*:/gm)) add('property', match[1]);
  for (const match of css.matchAll(/([a-zA-Z][a-zA-Z0-9-]*)\(/g)) add('function', match[1]);
  // A number that starts its own token: without the lookbehind the digits inside `#0000ff` and the
  // `2` in `arrow-2.svg` both read as numbers with a unit stuck to them.
  for (const match of css.matchAll(/(?<![#\w-])\d*\.?\d+([a-z%]+)(?![\w-])/g)) add('unit', match[1]);
  for (const match of css.matchAll(/:{1,2}([a-zA-Z-]+)/g)) add('pseudo', match[1]);
  if (css.includes('var(--')) found.add('variable');
  return found;
}

/**
 * The runtime APIs the client calls that the floor may not have, and the caniuse feature for each.
 *
 * Syntax is not here: `es-check` gates the emitted bundle, and Babel lowers what it can. These are
 * the things no transform can produce - they either exist in the engine or arrive as a polyfill.
 */
export const JS_APIS = {
  AbortController: 'abortcontroller',
  Intl: 'internationalization',
  IntersectionObserver: 'intersectionobserver',
  MutationObserver: 'mutationobserver',
  Promise: 'promises',
  ResizeObserver: 'resizeobserver',
  URLSearchParams: 'urlsearchparams',
  WebSocket: 'websockets',
  fetch: 'fetch',
  matchMedia: 'matchmedia',
  requestAnimationFrame: 'requestanimationframe',
  structuredClone: NEWER_THAN_THE_DATA,
  'navigator.clipboard': 'clipboard',
  'navigator.wakeLock': 'wake-lock',
  'window.PointerEvent': 'pointer',
};

/**
 * DOM members that postdate the floor, and the engine that first carries each.
 *
 * These are the ones no polyfill in `polyfills.legacy.js` supplies and no transform can produce, and
 * the reason they get their own check is how they fail: reading a property an engine does not have
 * yields `undefined` rather than throwing, so a guard written as `if (!node.isConnected) return;`
 * quietly takes the wrong branch. That is what left the deck blank on an iPad 2 with nothing in the
 * console and every request answered 200 (issue #829).
 */
export const DOM_APIS_ABOVE_FLOOR = {
  isConnected: 'Node.isConnected - Safari 10. Use `document.contains(node)`.',
  getRootNode: 'Node.getRootNode - Safari 10.',
  toggleAttribute: 'Element.toggleAttribute - Safari 12.',
  replaceChildren: 'Element.replaceChildren - Safari 14.',
  requestIdleCallback: 'requestIdleCallback - no Safari before 16.4.',
  IntersectionObserver: 'IntersectionObserver - Safari 12.1.',
  ResizeObserver: 'ResizeObserver - Safari 13.1.',
  structuredClone: 'structuredClone - Safari 15.4.',
};

/** Where one of those may be named anyway, keyed `<path under ui/>:<member>`, and why it is safe. */
export const DOM_API_EXCEPTIONS = {
  'runtime/src/protocol/messages/music-player.ts:isConnected':
    'A field on the wire, not the DOM property: whether the music integration has a session.',
  'web-client/src/icon-prefetch.ts:requestIdleCallback':
    'Feature-detected before it is called, falling back to setTimeout.',
  'runtime/src/render/text-fit.ts:ResizeObserver':
    'Guarded on `typeof ResizeObserver`, and supplied by polyfills.legacy.js on the floor.',
  'web-client/src/modal.ts:ResizeObserver': 'As the renderer above.',
  'web-client/src/folder-view.ts:ResizeObserver': 'As the renderer above.',
  'runtime/src/domain/action-flow.util.ts:structuredClone':
    'Supplied by polyfills.legacy.js, which installs it when the engine has none.',
};

/**
 * Every `<path>:<member>` where shipped source names one of the members above.
 *
 * Comments are stripped first: the codebase explains these choices where it makes them, and a
 * sentence saying why `scrollIntoView` is *not* used must not read as a use of it.
 */
export function scanDomApis(roots) {
  const found = [];
  const walk = (directory, prefix) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        walk(full, `${prefix}${entry.name}/`);
        continue;
      }
      if (!entry.name.endsWith('.ts') || entry.name.endsWith('.spec.ts')) continue;
      const source = readFileSync(full, 'utf8')
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/^\s*\/\/.*$/gm, '');
      for (const member of Object.keys(DOM_APIS_ABOVE_FLOOR)) {
        if (new RegExp(`\\b${member}\\b`).test(source)) found.push(`${prefix}${entry.name}:${member}`);
      }
    }
  };
  for (const [prefix, directory] of roots) walk(directory, prefix);
  return found.sort();
}

/** Whether `bundle` references `api` at all. A dotted name is matched on its last segment. */
export function referencesApi(bundle, api) {
  const name = api.slice(api.lastIndexOf('.') + 1);
  return new RegExp(`\\b${name}\\b`).test(bundle);
}
