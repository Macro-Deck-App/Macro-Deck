import assert from 'node:assert/strict';
import test from 'node:test';

import {
  ACCENT_SENTINELS,
  accentDependentTokens,
  parseStylesheet,
  parseThemeTokens,
  resolveCustomProperties,
  scopeToLight,
  substitute,
} from './css-custom-properties.mjs';
import { assembleStylesheet } from '../stylesheet.mjs';

/** The declarations of one rule in `css`, as `property: value` strings. */
function declarationsOf(css, selector) {
  const pattern = new RegExp(`(^|\\n)${selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')} \\{([^}]*)\\}`);
  const match = pattern.exec(css);
  return match === null ? null : match[2].split(';').map(part => part.trim()).filter(Boolean);
}

test('a token block that follows a comment is still read', () => {
  // The exact shape of the assembled stylesheet, and the exact shape the regex this replaced could
  // not match: it required `:root` to follow `}`, `,` or the start of the file, so in the real
  // sheet it found no tokens at all and every value below silently stayed unresolved.
  const { dark } = parseThemeTokens('/* tokens.css */\n/* a comment */\n:root {\n  --gap: 4px;\n}');

  assert.equal(dark.get('--gap'), '4px');
});

test('a token block inside an at-rule is read too', () => {
  const { dark } = parseThemeTokens('@media print{:root{--gap:2px}}');

  assert.equal(dark.get('--gap'), '2px');
});

test('the theme override is read apart from the base', () => {
  const { dark, light } = parseThemeTokens(':root{--fg:#fff}\n.light{--fg:#000}');

  assert.equal(dark.get('--fg'), '#fff');
  assert.equal(light.get('--fg'), '#000');
});

test('a declaration reading a token is emitted with the token resolved', () => {
  const { css } = resolveCustomProperties(':root{--fg:#fff}\n.a{color:var(--fg)}');

  assert.deepEqual(declarationsOf(css, '.a'), ['color: #fff']);
  assert.ok(!css.includes('var('));
});

test('a token defined in terms of another resolves all the way down', () => {
  const { css } = resolveCustomProperties(':root{--base:4px;--gap:calc(var(--base) * 2)}\n.a{margin:var(--gap)}');

  assert.deepEqual(declarationsOf(css, '.a'), ['margin: calc(4px * 2)']);
});

test('the token declarations themselves are gone, being unreadable and now unread', () => {
  const { css } = resolveCustomProperties(':root{--fg:#fff}\n.light{--fg:#000}\n.a{color:var(--fg)}');

  assert.ok(!css.includes('--fg'));
});

test('a token the light theme overrides is emitted a second time, scoped to the theme', () => {
  const { css } = resolveCustomProperties(':root{--fg:#fff}\n.light{--fg:#000}\n.a{color:var(--fg)}');

  assert.deepEqual(declarationsOf(css, '.a'), ['color: #fff']);
  assert.deepEqual(declarationsOf(css, '.light .a'), ['color: #000']);
});

test('only the declarations that actually differ are repeated under the theme', () => {
  const source = ':root{--fg:#fff;--pad:4px}\n.light{--fg:#000}\n.a{color:var(--fg);padding:var(--pad)}';

  assert.deepEqual(declarationsOf(resolveCustomProperties(source).css, '.light .a'), ['color: #000']);
});

test('a rule that reads no theme-dependent token is not repeated at all', () => {
  const { css } = resolveCustomProperties(':root{--pad:4px}\n.light{--fg:#000}\n.a{padding:var(--pad)}');

  assert.equal(declarationsOf(css, '.light .a'), null);
});

test('the theme duplicate keeps the at-rule that scoped the original', () => {
  // A duplicate hoisted out of its @media would apply at every width, which is worse than the
  // missing theme colour it exists to supply.
  const source = ':root{--fg:#fff}\n.light{--fg:#000}\n@media (min-width:600px){.a{color:var(--fg)}}';
  const { css } = resolveCustomProperties(source);

  const media = css.indexOf('@media');
  assert.ok(css.indexOf('.light .a') > media);
  assert.ok(css.trimEnd().endsWith('}'));
  assert.equal(css.split('@media').length - 1, 1, 'the duplicate belongs in the block, not beside it');
});

test('the theme class lands on the html element rather than beside it', () => {
  // `appearance.ts` toggles the class on <html> itself, so a descendant selector would never match.
  assert.equal(scopeToLight('html'), 'html.light');
  assert.equal(scopeToLight(':root'), ':root.light');
  assert.equal(scopeToLight('html body'), 'html.light body');
  assert.equal(scopeToLight('.a, .b'), '.light .a, .light .b');
});

test('a declaration reading a renderer-written token falls back to what it declared', () => {
  const { css } = resolveCustomProperties('.a{border-radius:var(--widget-radius, 22px)}');

  assert.deepEqual(declarationsOf(css, '.a'), ['border-radius: 22px']);
});

test('a declaration with no fallback is dropped and reported for the renderer to supply', () => {
  // Kept, it would be dropped by the engine anyway; dropped here, `var(` left in the output can
  // only mean a mistake, and the report is what the runtime bridge is checked against.
  const { css, unresolved } = resolveCustomProperties('.ring{background:var(--wb-color)}');

  assert.ok(!css.includes('var('));
  assert.deepEqual(unresolved.map(entry => entry.property), ['background']);
});

test('the accent and everything derived from it are known to depend on it', () => {
  const { dark } = parseThemeTokens(
    ':root{--color-accent:#2196f3;--ring:0 0 2px var(--color-accent);--edge:var(--ring)}');

  assert.deepEqual(
    [...accentDependentTokens(dark)].sort(),
    ['--color-accent', '--color-accent-hover', '--color-accent-muted', '--edge', '--ring'],
  );
});

test('an accent-dependent declaration is resolved to the default and templated for the runtime', () => {
  const source = ':root{--color-accent:#2196f3}\n.btn{background:var(--color-accent)}';
  const { css, accentTemplate } = resolveCustomProperties(source);

  assert.deepEqual(declarationsOf(css, '.btn'), ['background: #2196f3']);
  assert.ok(accentTemplate.includes(ACCENT_SENTINELS.get('--color-accent')));
});

test('the accent template carries the theme variant of the rest of the declaration', () => {
  // A focus ring mixes the accent with the page background, so a template built from the dark
  // values alone would repaint the light theme dark the moment the accent changed.
  const source = ':root{--color-accent:#2196f3;--bg:#121212}\n.light{--bg:#ffffff}\n'
    + '.btn{box-shadow:0 0 0 2px var(--bg), 0 0 0 4px var(--color-accent)}';
  const { accentTemplate } = resolveCustomProperties(source);

  assert.ok(accentTemplate.includes('#121212'));
  assert.ok(accentTemplate.includes('.light .btn'));
  assert.ok(accentTemplate.includes('#ffffff'));
});

test('a declaration that never reads the accent stays out of the template', () => {
  const { accentTemplate } = resolveCustomProperties(':root{--color-accent:#2196f3;--fg:#fff}\n.a{color:var(--fg)}');

  assert.equal(accentTemplate, '');
});

test('an at-rule and its block survive the pass', () => {
  const source = '@keyframes fade{from{opacity:0}to{opacity:1}}\n@supports (display:grid){.a{display:grid}}';
  const { css } = resolveCustomProperties(source);

  assert.ok(css.includes('@keyframes fade'));
  assert.ok(css.includes('@supports (display:grid)'));
  assert.ok(css.includes('display: grid'));
});

test('substitution stops rather than looping on a self-referential token', () => {
  const tokens = new Map([['--a', 'var(--b)'], ['--b', 'var(--a)']]);

  assert.ok(substitute('color:var(--a)', tokens).includes('var('));
});

test('the stylesheet the client actually ships resolves completely', async () => {
  // The regression test the unit cases above cannot be: every one of them hands the pass a source
  // it wrote, and the production bug was precisely that the real sheet did not look like those.
  const { css, unresolved } = resolveCustomProperties(await assembleStylesheet());

  assert.equal(css.match(/var\(/g), null, 'the compatibility floor drops every declaration with var()');
  assert.deepEqual(
    unresolved.map(entry => entry.value.match(/var\(\s*(--[\w-]+)/)[1]).filter((v, i, a) => a.indexOf(v) === i),
    ['--wb-color'],
    'a renderer-written token with no fallback needs a runtime fallback; add one before adding it here',
  );
});

test('no themed declaration is overridden later by a rule of its own specificity', async () => {
  // The one way this pass can change what the browser paints. `.light .a` is more specific than
  // `.a`, where the token it replaced was not - so a themed declaration that some later rule of
  // equal specificity was meant to beat would start winning under the light theme instead. No such
  // pair exists in the sheets today; this fails if one is written, which is when the pass would
  // need to scope by something that adds no specificity.
  const css = await assembleStylesheet();
  const { dark, light } = parseThemeTokens(css);
  const lightTokens = new Map([...dark, ...light]);

  const themed = new Set();
  const declared = [];
  const walk = (nodes) => {
    for (const node of nodes) {
      if (node.type === 'atrule') { walk(node.children); continue; }
      if (node.type !== 'rule') continue;
      for (const child of node.children) {
        if (child.type !== 'declaration' || child.property.startsWith('--')) continue;
        const key = `${node.selector.replace(/\s+/g, ' ')} { ${child.property} }`;
        declared.push(key);
        if (substitute(child.value, dark) !== substitute(child.value, lightTokens)) themed.add(key);
      }
    }
  };
  walk(parseStylesheet(css));

  const overridden = [...themed].filter(key => declared.indexOf(key) !== declared.lastIndexOf(key));
  assert.deepEqual(overridden, []);
});
