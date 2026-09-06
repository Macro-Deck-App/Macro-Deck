import assert from 'node:assert/strict';
import test from 'node:test';

import {
  downlevelClamp,
  downlevelColorMix,
  downlevelCss,
  downlevelCssColors,
  downlevelEnv,
  downlevelInset,
  downlevelMinMax,
  downlevelObjectFit,
  parseRootTokens,
} from './css-downlevel.mjs';
import { flexGapFallback, NO_FLEX_GAP_CLASS } from './flex-gap-fallback.mjs';

const TOKENS = parseRootTokens(':root{--space-2: .5rem;--space-3: .75rem;--text-lg: 1rem}');

test('an 8-digit hex color becomes rgba', () => {
  assert.equal(downlevelCssColors('color:#ff000080;'), 'color:rgba(255,0,0,0.502);');
});

test('a 4-digit hex color becomes rgba', () => {
  assert.equal(downlevelCssColors('color:#f008;'), 'color:rgba(255,0,0,0.533);');
});

test('clamp() resolves static px/rem/cqmin arguments against the 16px root', () => {
  assert.equal(downlevelClamp('font-size:clamp(0.55rem, 5cqmin, 0.8rem)'), 'font-size:8.8px');
});

test('clamp() clamps its preferred value into its bounds', () => {
  assert.equal(downlevelClamp('width:clamp(10px, 100px, 40px)'), 'width:40px');
});

test('clamp() with an unresolvable bound is left alone rather than guessed at', () => {
  const css = 'width:clamp(var(--min), 5vw, var(--max))';
  assert.equal(downlevelClamp(css), css);
});

test('clamp() bounds given as design tokens resolve to px', () => {
  assert.equal(downlevelClamp('font-size:clamp(0.65rem, 7cqmin, var(--text-lg))', TOKENS), 'font-size:10.4px');
});

test('min() keeps the smallest fixed length and max() the largest', () => {
  assert.equal(downlevelMinMax('height:min(60vh, 480px)'), 'height:480px');
  assert.equal(downlevelMinMax('height:max(2rem, 5vh)'), 'height:32px');
});

test('minmax() and calls with nothing static are left unchanged', () => {
  const grid = 'grid-template-columns:repeat(auto-fill, minmax(4.5rem, 1fr))';
  assert.equal(downlevelMinMax(grid), grid);
  const dynamic = 'width:min(var(--a), 5vw)';
  assert.equal(downlevelMinMax(dynamic), dynamic);
});

test('color-mix() of two literal colors is mixed for real', () => {
  assert.equal(downlevelColorMix('color:color-mix(in srgb, #000, #fff)'), 'color:rgb(128,128,128)');
  assert.equal(
    downlevelColorMix('background:color-mix(in srgb, #ff0000 12%, transparent)'),
    'background:rgba(255,0,0,0.12)',
  );
});

test('color-mix() with a var() component falls back to the dominant one', () => {
  // The token catalogue's accent border: unresolvable statically, and a dropped declaration would
  // leave the widget border unset entirely.
  assert.equal(
    downlevelColorMix('border-color:color-mix(in srgb, var(--color-accent) 60%, var(--color-border))'),
    'border-color:var(--color-accent)',
  );
});

test('color-mix() in a space other than srgb is left unchanged', () => {
  const css = 'color:color-mix(in oklab, red, blue)';
  assert.equal(downlevelColorMix(css), css);
});

test('the inset shorthand expands to the four sides', () => {
  assert.equal(downlevelInset('{inset:0;}'), '{top:0;right:0;bottom:0;left:0;}');
  assert.equal(downlevelInset('{inset:1px 2px;}'), '{top:1px;right:2px;bottom:1px;left:2px;}');
});

test('box-shadow: inset and inset-inline are not the shorthand and stay untouched', () => {
  assert.equal(downlevelInset('box-shadow: inset 0 0 2px red'), 'box-shadow: inset 0 0 2px red');
  assert.equal(downlevelInset('inset-inline: 0'), 'inset-inline: 0');
});

test('object-fit keeps its declaration and gains the marker the shim reads back', () => {
  const cover = downlevelObjectFit('.a{object-fit:cover;display:block}');
  assert.ok(cover.includes('object-fit:cover'));
  assert.ok(cover.includes("font-family:'-md-object-fit-cover'"));

  // This client frames artwork and the logo with contain, which the same engines also ignore.
  const contain = downlevelObjectFit('.a{object-fit:contain}');
  assert.ok(contain.includes('object-fit:contain'));
  assert.ok(contain.includes("font-family:'-md-object-fit-contain'"));
});

test('object-fit values the shim cannot reproduce are not marked', () => {
  const css = '.a{object-fit:fill}';
  assert.equal(downlevelObjectFit(css), css);
});

test('a safe-area inset resolves rather than invalidating the declaration around it', () => {
  assert.equal(
    downlevelEnv('top:calc(var(--space-3, 12px) + env(safe-area-inset-top));'),
    'top:calc(var(--space-3, 12px) + 0px);',
  );
  assert.equal(downlevelEnv('padding-left:env(safe-area-inset-left, 8px);'), 'padding-left:8px;');
});

test('cqmin resolves against the widget reference cell', () => {
  assert.equal(downlevelCss('width:10cqmin'), 'width:12px');
});

test('a flex gap becomes a margin on every child but the first', () => {
  const { css, count } = flexGapFallback('.row{display:flex;gap:8px}');
  assert.equal(count, 1);
  assert.ok(css.includes(`.${NO_FLEX_GAP_CLASS} .row > * + *{margin-left:8px}`));
});

test('a column gap becomes a top margin', () => {
  const { css } = flexGapFallback('.col{display:flex;flex-direction:column;gap:4px}');
  assert.ok(css.includes(`.${NO_FLEX_GAP_CLASS} .col > * + *{margin-top:4px}`));
});

test('a wrapping flex container gets no margin fallback', () => {
  // Not a limitation to work around: old WebKit measures a wrapping container from its first line
  // alone, so the wrapped line contributes no height and spills out of the box whatever its
  // spacing (issue #829). The sheets avoid wrapping for that reason - see `.wc-settings-row` -
  // and spacing a box the floor cannot lay out would only look like the problem was handled.
  const { count } = flexGapFallback('.wrap{display:flex;flex-wrap:wrap;gap:8px}');

  assert.equal(count, 0);
});

test('a gap on something that is not a flex container is left alone', () => {
  const { count } = flexGapFallback('.grid{display:grid;gap:8px}');
  assert.equal(count, 0);
});

test('the fallback keeps its @media context', () => {
  const { css } = flexGapFallback('@media (min-width:600px){.row{display:flex;gap:8px}}');
  const media = css.indexOf('@media');
  const fallback = css.indexOf(NO_FLEX_GAP_CLASS);
  assert.ok(fallback > media);
  assert.ok(css.trimEnd().endsWith('}}'), 'the fallback must stay inside the block that scopes it');
});

test('the client stylesheet survives the whole pass with no construct the floor drops', () => {
  const source = [
    ':root{--space-2:.5rem;--overlay:#00000080}',
    '.face{position:absolute;inset:0;object-fit:contain}',
    '.row{display:flex;gap:var(--space-2)}',
    '.pad{padding-top:env(safe-area-inset-top)}',
    '.tint{background:color-mix(in srgb, #ff0000 50%, #0000ff)}',
  ].join('');
  const { css } = flexGapFallback(downlevelCss(source, parseRootTokens(source)));

  assert.ok(!/#[0-9a-fA-F]{8}\b/.test(css));
  assert.ok(!/(^|[^-\w])inset\s*:/.test(css));
  assert.ok(!css.includes('color-mix('));
  assert.ok(!css.includes('env('));
});

