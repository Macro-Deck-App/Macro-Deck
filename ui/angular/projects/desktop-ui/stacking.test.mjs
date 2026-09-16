/**
 * Guards the layering the routed pages depend on.
 *
 * The shell side panels and their scrim sit at z-index 30, so a page element that outranks them can
 * cover an open panel's close button and scrim and leave the app stuck (issue 694).
 *
 * Only components/pages is walked. Chrome elsewhere is not covered: the layers that legitimately sit
 * above the panels are spread across a dozen files, and exempting them by name would be a list that
 * has to be edited to stay true.
 *
 * A Node test rather than a karma spec because this is a question about the source tree. Run by
 * npm run test:stacking.
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.join(HERE, 'src', 'app');
const PAGES = path.join(SRC, 'components', 'pages');
const SIDE_PANEL = path.join(SRC, 'shared', 'components', 'overlay', 'side-panel', 'side-panel.component.scss');

function walk(dir, predicate) {
  const out = [];
  for (const entry of readdirSync(dir)) {
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) out.push(...walk(full, predicate));
    else if (predicate(full)) out.push(full);
  }
  return out;
}

const isStyleOrTemplate = file => file.endsWith('.scss') || file.endsWith('.html');
const rel = file => path.relative(HERE, file);

// Trailing `//` needs the leading boundary so a `https://` inside a url() survives.
function uncommented(source) {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/(^|\s)\/\/.*$/gm, '$1');
}

function declarationsOf(file) {
  const source = uncommented(readFileSync(file, 'utf8'));
  const css = [...source.matchAll(/z-index\s*:\s*([^;}]+)/g)];
  const bound = [...source.matchAll(/\[style\.z-index[^\]]*\]\s*=\s*"([^"]*)"/g)];
  return [...css, ...bound].map(match => match[1].trim());
}

// Read from the shared side panel rather than copied, so moving its rung cannot leave this enforcing
// an old number in silence.
function shellPanelZIndex() {
  const match = /:host\s*\{[^}]*?z-index\s*:\s*(\d+)/s.exec(uncommented(readFileSync(SIDE_PANEL, 'utf8')));
  assert.ok(match, `no :host z-index found in ${rel(SIDE_PANEL)}`);
  return Number(match[1]);
}

// auto, unset, initial and revert all resolve to the auto layer, which cannot outrank the panels.
// inherit can, so it is not on this list.
const SAFE_KEYWORDS = new Set(['auto', 'unset', 'initial', 'revert']);

function offends(value, limit) {
  if (SAFE_KEYWORDS.has(value)) return false;
  return !/^-?\d+$/.test(value) || Number(value) >= limit;
}

test('no page layers itself into the shell', () => {
  const limit = shellPanelZIndex();
  const offenders = [];
  for (const file of walk(PAGES, isStyleOrTemplate)) {
    for (const value of declarationsOf(file)) {
      if (offends(value, limit)) offenders.push(`${rel(file)} -> z-index: ${value}`);
    }
  }
  assert.deepEqual(offenders, [],
    `a page needs a plain z-index below ${limit}, or it can cover an open panel and trap it`);
});

test('there are pages to check', () => {
  // A sanity check on the check: if the directory moved, the test above would pass by walking nothing.
  assert.ok(walk(PAGES, isStyleOrTemplate).length > 10, 'components/pages should hold the page sources');
});
