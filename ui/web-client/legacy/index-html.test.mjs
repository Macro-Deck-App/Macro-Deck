import assert from 'node:assert/strict';
import test from 'node:test';
import vm from 'node:vm';

import { ACCENT_TEMPLATE_ID, buildShell, STYLE_PROBE_MARKER, styleProbeScript } from './index-html.mjs';

const INDEX =
  '<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n' +
  '<title>Web Client - Macro Deck</title>\n<base href="/">\n' +
  '<link rel="manifest" href="manifest.webmanifest">\n' +
  '<link rel="stylesheet" href="styles-ABC123.css">\n</head>\n<body>\n' +
  '<div id="app"></div>\n<script src="main-ABC123.js"></script>\n</body>\n</html>';

const OPTIONS = {
  polyfillsSrc: 'polyfills-11111111.js',
  mainSrc: 'main-22222222.js',
  legacyStylesHref: 'styles-legacy-33333333.css',
  accentTemplate: '.wc-btn-primary {\n  background-color: __MD_ACCENT__;\n}\n',
};

/**
 * Runs the probe the way a browser would, against a browser described by what it supports. The
 * probe's whole job is this decision, so it is exercised rather than pattern-matched.
 */
function runProbe({ customProperties }) {
  const source = styleProbeScript('styles-legacy-33333333.css')
    .replace(/^<script[^>]*>/, '')
    .replace(/<\/script>$/, '');
  const written = [];
  const context = {
    document: { write: markup => written.push(markup) },
    CSS: customProperties === null
      ? undefined
      : { supports: (property) => property.indexOf('--') === 0 && customProperties },
  };
  vm.createContext(context);
  vm.runInContext(source, context);
  return written;
}

test('a browser with custom properties is left with the authored stylesheet', () => {
  assert.deepEqual(runProbe({ customProperties: true }), []);
});

test('a browser without custom properties pulls in the resolved stylesheet', () => {
  assert.deepEqual(
    runProbe({ customProperties: false }),
    ['<link rel="stylesheet" href="styles-legacy-33333333.css">'],
  );
});

test('a browser with no CSS.supports at all pulls in the resolved stylesheet', () => {
  // The Android 4 stock browser: no CSS.supports to ask, and no custom properties either.
  assert.deepEqual(
    runProbe({ customProperties: null }),
    ['<link rel="stylesheet" href="styles-legacy-33333333.css">'],
  );
});

test('the shell loads the polyfills before the client', () => {
  const html = buildShell(INDEX, OPTIONS);

  assert.ok(!html.includes('src="main-ABC123.js"'), 'the ES2015 intermediate must not be loaded');
  assert.ok(html.indexOf('polyfills-11111111.js') < html.indexOf('main-22222222.js'));
});

test('the probe runs after the authored stylesheet, so the resolved one wins', () => {
  // Both sheets parse on an engine without custom properties; the later one takes ties, and the
  // resolved sheet is the one whose declarations that engine can actually read.
  const html = buildShell(INDEX, OPTIONS);

  assert.ok(html.indexOf('styles-ABC123.css') < html.indexOf(STYLE_PROBE_MARKER));
  assert.ok(html.indexOf(STYLE_PROBE_MARKER) < html.indexOf('</head>'));
});

test('the shell carries the accent rules the floor engines need', () => {
  const html = buildShell(INDEX, OPTIONS);

  assert.ok(html.includes(`<script type="text/plain" id="${ACCENT_TEMPLATE_ID}">`));
  assert.ok(html.includes('background-color: __MD_ACCENT__;'));
});

test('a build with no accent rules carries no template', () => {
  const html = buildShell(INDEX, { ...OPTIONS, accentTemplate: '' });

  assert.ok(!html.includes(ACCENT_TEMPLATE_ID));
});

test('the shell keeps the base href the assets resolve against', () => {
  assert.ok(buildShell(INDEX, OPTIONS).includes('<base href="/">'));
});

test('building twice over the same document is refused', () => {
  assert.throws(() => buildShell(buildShell(INDEX, OPTIONS), OPTIONS), /already carries/);
});

test('a document that loads no bundle is refused rather than shipped without one', () => {
  const withoutBundle = INDEX.replace('<script src="main-ABC123.js"></script>', '');

  assert.throws(() => buildShell(withoutBundle, OPTIONS), /loads no main bundle/);
});
