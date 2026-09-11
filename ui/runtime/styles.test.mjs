/**
 * Binds the stylesheets this package ships to the TypeScript that owns their numbers.
 *
 * The widget reference cell was written down in four independent places - a Sass variable, two TS
 * constants and the legacy build's cqmin factor - kept in step only by `// mirrors ...` comments.
 * Both copies that remain are checked here, because the way that drift surfaces is the worst kind:
 * the CSS values are `var()` fallbacks, so a mismatch is silent until the host omits the property.
 */
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const HERE = path.dirname(fileURLToPath(import.meta.url));

const css = readFileSync(path.join(HERE, 'styles', 'widget-metrics.css'), 'utf8');
const domain = readFileSync(path.join(HERE, 'src', 'domain', 'widget.interface.ts'), 'utf8');

function cssPx(name) {
  const match = css.match(new RegExp(`--${name}:\\s*(\\d+(?:\\.\\d+)?)px`));
  assert.ok(match, `widget-metrics.css declares no --${name}`);
  return Number(match[1]);
}

function tsNumber(name) {
  const match = domain.match(new RegExp(`export const ${name}\\s*=\\s*(\\d+(?:\\.\\d+)?)`));
  assert.ok(match, `widget.interface.ts declares no ${name}`);
  return Number(match[1]);
}

test('the list surface scrolls its own children rather than dividing its box between them', () => {
  const renderer = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8');
  const surface = renderer.match(/\n\.widget-list \{([^}]*)\}/);
  assert.ok(surface, 'renderer.css declares no .widget-list rule');
  assert.match(surface[1], /overflow-y:\s*(auto|scroll)\b/);

  const children = renderer.match(/\n\.widget-list > \* \{([^}]*)\}/);
  assert.ok(children, 'renderer.css declares no .widget-list > * rule');
  assert.match(children[1], /flex:\s*0 0 auto\b/);
});

test('a horizontal list lays its children out in a row that scrolls along x only', () => {
  const renderer = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8');
  const surface = renderer.match(/\n\.widget-list-horizontal \{([^}]*)\}/);
  assert.ok(surface, 'renderer.css declares no .widget-list-horizontal rule');
  assert.match(surface[1], /flex-direction:\s*row\b/);
  assert.match(surface[1], /overflow-x:\s*(auto|scroll)\b/);
  assert.match(surface[1], /overflow-y:\s*hidden\b/);
});

test('the widget reference cell is the same number in CSS and TypeScript', () => {
  assert.equal(cssPx('widget-reference-cell'), tsNumber('WIDGET_REFERENCE_CELL_SIZE'));
});

test('the default widget corner radius is the same number in CSS and TypeScript', () => {
  assert.equal(cssPx('widget-reference-radius'), tsNumber('WIDGET_REFERENCE_BORDER_RADIUS'));
});

test('every stylesheet that inlines the default widget radius uses the same number', () => {
  // The renderer's corner radius is authored as `var(--widget-radius, Npx)` at each call site, the
  // way the Sass function it replaced expanded. That puts the default in several files, so the one
  // number they all mean is checked here rather than trusted to stay in step.
  const expected = tsNumber('WIDGET_REFERENCE_BORDER_RADIUS');
  const offenders = [];
  const roots = [path.resolve(HERE, '..', 'angular', 'projects'), path.join(HERE, 'styles')];
  const stack = roots.filter(existsSync);
  while (stack.length) {
    const entry = stack.pop();
    if (statSync(entry).isDirectory()) {
      for (const child of readdirSync(entry)) {
        if (child === 'node_modules' || child === 'dist') continue;
        stack.push(path.join(entry, child));
      }
      continue;
    }
    if (!/\.(scss|css)$/.test(entry)) continue;
    for (const match of readFileSync(entry, 'utf8').matchAll(/--widget-radius,\s*(\d+)px/g)) {
      if (Number(match[1]) !== expected) offenders.push(`${path.relative(HERE, entry)}: ${match[1]}px`);
    }
  }
  assert.deepEqual(offenders, [], `every default must be ${expected}px`);
});

/**
 * The unconditional style rules in `source`, as `{ selectors, body }`.
 *
 * Walks the braces rather than splitting on them, for two reasons. An at-rule's own block makes a
 * naive split mis-parse everything after the first `@media`, so a declaration moved into one would
 * silently stop being found at all. And a rule inside an at-rule is deliberately *not* returned: the
 * guards below assert that something holds always, and a declaration that only applies under a media
 * query does not answer that.
 */
function topLevelRules(source) {
  const rules = [];
  let prelude = '';
  let depth = 0;
  let selectors = '';
  for (const char of source) {
    if (char === '{') {
      if (depth === 0) selectors = prelude.trim();
      depth++;
      prelude = '';
    } else if (char === '}') {
      depth--;
      // An at-rule's prelude is not a selector list, and its block holds rules rather than
      // declarations.
      if (depth === 0 && !selectors.startsWith('@')) {
        rules.push({ selectors: selectors.split(',').map(one => one.trim()), body: prelude });
      }
      prelude = '';
    } else {
      prelude += char;
    }
  }
  return rules;
}

function declares(source, selector, declaration) {
  return topLevelRules(source)
    .some(rule => rule.selectors.includes(selector) && declaration.test(rule.body));
}

function declaresCallout(source, selector, value) {
  return declares(source, selector, new RegExp(`-webkit-touch-callout:\\s*${value}`));
}

/** Every sheet this package ships, read from disk - a new one must not slip past the guards below. */
function shippedSheets() {
  return readdirSync(path.join(HERE, 'styles')).filter(entry => entry.endsWith('.css')).sort();
}

test('the stylesheets stay free of Sass', () => {
  // They are consumed directly by a client with no Sass toolchain, so a construct that needs one
  // would not fail at build time - it would ship verbatim and be ignored by the browser.
  const sheets = shippedSheets();
  const offenders = [];
  for (const sheet of sheets) {
    const source = readFileSync(path.join(HERE, 'styles', sheet), 'utf8');
    for (const construct of ['@use ', '@forward ', '@mixin ', '@include ', '@extend ', '@each ', '@function ']) {
      if (source.includes(construct)) offenders.push(`${sheet}: ${construct.trim()}`);
    }
    for (const match of source.matchAll(/^\s*(\$[\w-]+)\s*:/gm)) offenders.push(`${sheet}: ${match[1]}`);
  }
  assert.deepEqual(offenders, []);
});

test('the shipped stylesheets carry no construct the compatibility floor drops', () => {
  // Safari 9 drops the whole declaration for each of these, and a dropped `inset` is an overlay that
  // covers nothing. The legacy build rewrites them for the Angular client after the fact; these
  // sheets are consumed directly, so they have to be written that way to begin with.
  const sheets = shippedSheets();
  const offenders = [];
  for (const sheet of sheets) {
    const source = readFileSync(path.join(HERE, 'styles', sheet), 'utf8')
      .replace(/\/\*[\s\S]*?\*\//g, '');
    if (/(^|[^-\w])inset\s*:/.test(source)) offenders.push(`${sheet}: inset shorthand`);
    if (/#[0-9a-fA-F]{8}\b/.test(source)) offenders.push(`${sheet}: 8-digit hex`);
    if (/#[0-9a-fA-F]{4}\b(?![0-9a-fA-F])/.test(source)) offenders.push(`${sheet}: 4-digit hex`);
  }
  assert.deepEqual(offenders, []);
});

test('a ring takes the corner of the button it is drawn over', () => {
  // The button's own corner is painted inline by the renderer, because it is a fraction of that
  // button's height rather than a constant. Everything above it inherits: the overlay from the button,
  // the ring layers from the overlay. A gap anywhere along that chain resolves to zero, which is a hard
  // rectangle drawn across a rounded button.
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  const button = /(^|\n)\.widget-button\s*\{([^}]*)\}/.exec(source);
  const ring = /(^|\n)\.widget-button-ring\s*\{([^}]*)\}/.exec(source);

  assert.ok(button, '.widget-button must still exist');
  assert.doesNotMatch(button[2], /border-radius:/,
    "a nested button's corner is the renderer's to paint - a constant here would override it");
  // A button that is the whole tree fills the tile, so its corner is the reader's own - and a nested
  // backdrop that asked for `corner: "tile"` takes the same one. Matched by selector rather than by the
  // rule's exact spelling, since the two share one rule.
  for (const selector of ['.widget-button.widget-node-root', '.widget-button.widget-tile-corner']) {
    const rule = topLevelRules(source).find(one => one.selectors.includes(selector));
    assert.ok(rule, `${selector} must still exist`);
    assert.match(rule.body, /border-radius:\s*calc\(var\(--widget-radius/);
  }
  assert.ok(ring, '.widget-button-ring must still exist');
  assert.match(ring[2], /border-radius:\s*inherit/);

  const border = readFileSync(path.join(HERE, 'styles', 'widget-border.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  assert.match(border, /border-radius:\s*inherit/,
    'the ring layers must take the corner of the overlay they sit in');
});

test('a layer does not un-flex the nodes inside it', () => {
  // `.widget-layer > *` outweighs `.widget-stack` by one specificity point, so a `display` here wins
  // over the one the node type declares for itself. It silently laid every stack inside a layer out
  // as a block - a centred readout pinned to the top left, with nothing failing anywhere.
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  const rule = /\.widget-layer\s*>\s*\*\s*\{([^}]*)\}/.exec(source);

  assert.ok(rule, '.widget-layer > * must still exist');
  assert.doesNotMatch(rule[1], /(^|[^-\w])display\s*:/);
});

test('a transform does not un-flex the nodes inside it', () => {
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  const rule = /\.widget-transform\s*>\s*\*\s*\{([^}]*)\}/.exec(source);

  assert.ok(rule, '.widget-transform > * must exist');
  assert.doesNotMatch(rule[1], /(^|[^-\w])display\s*:/);
});

test('a layer can place a transform across its box', () => {
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  const own = source.search(/(^|[\s,}])\.widget-transform\s*\{/);
  const layered = source.search(/\.widget-layer\s*>\s*\*\s*\{/);

  assert.ok(own >= 0 && layered >= 0);
  assert.ok(own < layered, '.widget-transform must come before .widget-layer > *, or its position wins');
});

test('every widget type declares the display it needs', () => {
  // The corollary of the rule above: nothing else sets it for them. Read rule by rule rather than by
  // the first mention of the class - a grouped selector carrying something else entirely (the shared
  // `user-select`, say) would otherwise be mistaken for the type's own rule.
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');

  for (const selector of ['.widget-stack', '.widget-button', '.widget-image']) {
    const declaring = [...source.matchAll(/([^{}]+)\{([^}]*)\}/g)].filter(rule =>
      rule[1].split(',').some(one => one.trim() === selector) && /display:\s*flex/.test(rule[2]));

    assert.equal(declaring.length, 1, `${selector} must declare its own display, exactly once`);
  }
});

test('a text field is drawn at the height the layout budgets for it', () => {
  // The stylesheet paints the chrome and the layout adds it up. When the two disagree the field is
  // simply the wrong height - and, worse, the sibling filling the rest of the stack overflows it by
  // exactly the difference, which is how a picker's last row ended up outside its own dialog.
  const layout = readFileSync(path.join(HERE, 'src', 'ui-framework', 'layout.ts'), 'utf8');
  const constant = name => {
    const match = layout.match(new RegExp(`export const ${name}\\s*=\\s*(\\d+(?:\\.\\d+)?)`));
    assert.ok(match, `layout.ts declares no ${name}`);
    return Number(match[1]);
  };

  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  const rule = /\.widget-text-field\s*\{([^}]*)\}/.exec(source);
  assert.ok(rule, '.widget-text-field must exist');

  const lineHeight = /line-height:\s*([\d.]+)/.exec(rule[1]);
  const padding = /padding:\s*([\d.]+)em/.exec(rule[1]);
  const border = /border:\s*([\d.]+)px/.exec(rule[1]);

  assert.equal(Number(lineHeight?.[1]), constant('WIDGET_FIELD_LINE_HEIGHT'));
  assert.equal(Number(padding?.[1]), constant('WIDGET_FIELD_PADDING_EM'));
  assert.equal(Number(border?.[1]), constant('WIDGET_FIELD_BORDER_PX'));
});

test('a stack keeps content that asks for more room than it has inside its own box', () => {
  // Every length in the profile is a fraction of the widget basis and a text node's width is its own
  // text, so a stack can be handed more content than its box holds. The box is inset by the safe
  // area that clears the tile's rounded corner, so a child that spills out of it is drawn under the
  // curve and sliced by it - a slider label cut off by the corner it sits in.
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');

  for (const selector of ['.widget-stack', '.widget-button']) {
    const rule = new RegExp(`\\${selector}\\s*>\\s*\\*\\s*\\{([^}]*)\\}`).exec(source);
    assert.ok(rule, `${selector} > * must exist`);

    const shrink = /flex:\s*[\d.]+\s+([\d.]+)/.exec(rule[1]) ?? /flex-shrink:\s*([\d.]+)/.exec(rule[1]);
    assert.ok(shrink && Number(shrink[1]) > 0,
      `${selector} > * must let a child give way along the main axis`);
    assert.match(rule[1], /max-width:\s*100%/, `${selector} > * must cap a child's width`);
    assert.match(rule[1], /max-height:\s*100%/, `${selector} > * must cap a child's height`);
  }
});

test('a long press over a widget raises no platform menu of its own', () => {
  // A deck answers a long press itself - `onLongPress` on a tile, `long-press` inside a tree. iOS
  // raises its callout sheet ("Copy" / "Share" over album artwork) from a recognizer that no
  // `preventDefault` on a pointer event reaches; `-webkit-touch-callout` is the only thing that
  // stops it, and it inherits, so it is declared on the containers rather than per node.
  const renderer = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  const grid = readFileSync(path.join(HERE, 'styles', 'grid.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');

  for (const selector of ['.widget-button', '.widget-image', '.widget-stack', '.widget-text']) {
    assert.ok(declaresCallout(renderer, selector, 'none'),
      `${selector} must suppress the platform callout`);
  }

  assert.ok(declaresCallout(grid, '.deck-grid-tile-surface', 'none'),
    'a deck tile must suppress it too - the tree inside it inherits from the surface');
});

test('a text field keeps the callout the widget tree gives up', () => {
  // The one element whose text is the user's. It takes selection back from the containers, and the
  // callout has to come back with it: without it iOS offers no paste into the field.
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  assert.ok(declaresCallout(source, '.widget-text-field', 'default'),
    '.widget-text-field must take the callout back');
});

test('artwork is not draggable out of the widget it belongs to', () => {
  // A press-and-move over an image is a drag of the image by default, which is not the gesture the
  // node declared. The renderer's own `draggable="false"` is the half every engine honours.
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');

  for (const selector of ['.widget-button-artwork', '.widget-image img']) {
    assert.ok(declares(source, selector, /-webkit-user-drag:\s*none/),
      `${selector} must not be draggable`);
  }
});

/**
 * The compatibility fallbacks in `render/custom-properties.ts` name the selectors and the ring
 * styles whose declarations they stand in for on engines with no custom properties (issue #829).
 * Those lists are the stylesheet's knowledge, written a second time in TypeScript, and nothing but
 * these tests keeps them in step: the fallback code never runs in any browser a test is run in, so
 * a renamed selector would go unnoticed until a deck on a 2015 tablet lost its corners.
 */
function fallbackList(name) {
  const source = readFileSync(path.join(HERE, 'src', 'render', 'custom-properties.ts'), 'utf8');
  const match = new RegExp(`export const ${name} = \\[([^\\]]*)\\]`).exec(source);
  assert.ok(match, `custom-properties.ts declares no ${name}`);
  return match[1].split(',').map(entry => entry.trim().replace(/^'|'$/g, '')).filter(Boolean);
}

test('every selector the radius fallback targets is one that reads the radius tokens', () => {
  const source = ['grid.css', 'renderer.css']
    .map(sheet => readFileSync(path.join(HERE, 'styles', sheet), 'utf8'))
    .join('\n')
    .replace(/\/\*[\s\S]*?\*\//g, '');

  const declared = [];
  for (const rule of topLevelRules(source)) {
    if (!/border-radius:\s*calc\(var\(--widget-radius/.test(rule.body)) continue;
    for (const selector of rule.selectors) declared.push(selector);
  }

  assert.deepEqual(
    [...fallbackList('SCALED_RADIUS_SELECTORS'), ...fallbackList('REFERENCE_RADIUS_SELECTORS')].sort(),
    declared.sort(),
    'custom-properties.ts must cover exactly the rules that read --widget-radius',
  );
});

test('every ring style the fallback tints is one that paints the colour on the ring itself', () => {
  const source = readFileSync(path.join(HERE, 'styles', 'widget-border.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');

  const declared = [];
  for (const rule of topLevelRules(source)) {
    if (!/background:\s*var\(--wb-color\)/.test(rule.body)) continue;
    for (const selector of rule.selectors) declared.push(selector.replace('.wb-', ''));
  }

  assert.deepEqual(fallbackList('TINTED_RING_STYLES').sort(), declared.sort(),
    'a style that paints --wb-color on the element needs the fallback, and no other does');
});

test('no sheet that styles a widget border ring conditions it on reduced motion', () => {
  // The ring is the one decoration that keeps looping under the preference (issue 682): freezing it in
  // the runtime sheet alone left it animating in the configuration UI and dead in the web client.
  const roots = [
    path.join(HERE, 'styles'),
    path.resolve(HERE, '..', 'angular', 'projects'),
    path.resolve(HERE, '..', 'web-client', 'src'),
  ];
  // Asserted rather than skipped: a root that moved would otherwise take a whole surface out of the
  // walk and leave this green, which is the one failure a guard cannot afford.
  for (const root of roots) assert.ok(existsSync(root), `${root} must exist to be scanned`);

  const styling = [];
  const offenders = [];
  const stack = [...roots];
  while (stack.length) {
    const entry = stack.pop();
    if (statSync(entry).isDirectory()) {
      for (const child of readdirSync(entry)) {
        if (child === 'node_modules' || child === 'dist') continue;
        stack.push(path.join(entry, child));
      }
      continue;
    }
    if (!/\.(scss|css)$/.test(entry)) continue;
    const sheet = readFileSync(entry, 'utf8')
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/(^|[^:])\/\/.*$/gm, '$1');
    if (!/(^|[\s,>+~(&])\.(ring(?![\w-])|wb-)/.test(sheet)) continue;
    styling.push(path.relative(HERE, entry));
    if (/prefers-reduced-motion/.test(sheet)) offenders.push(path.relative(HERE, entry));
  }

  // Whatever the query gates, and however it nests, a sheet holding the ring's own rules has no
  // business naming the preference at all, so the check needs no parser.
  assert.deepEqual(offenders, [],
    'a widget border ring must keep animating whatever the viewer prefers, on every surface');
  // By name, not by count: a third sheet matching .wb- for its own reasons would otherwise let one of
  // these two go missing unnoticed.
  for (const sheet of ['styles/widget-border.css', 'widget-border-overlay.component.scss']) {
    assert.ok(styling.some(found => found.endsWith(sheet)),
      `${sheet} styles the ring and must be among the scanned sheets, saw ${styling.join(', ')}`);
  }
});

test('a button that fades its background stops fading when the viewer asks for less motion', () => {
  const source = readFileSync(path.join(HERE, 'styles', 'renderer.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '');
  const opened = source.search(/@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{/);
  assert.notEqual(opened, -1, 'renderer.css declares no prefers-reduced-motion block');

  let depth = 0;
  let end = source.indexOf('{', opened);
  for (let at = end; at < source.length; at++) {
    if (source[at] === '{') depth++;
    else if (source[at] === '}' && --depth === 0) { end = at; break; }
  }

  const stilled = [...source.slice(source.indexOf('{', opened) + 1, end).matchAll(/([^{}]+)\{([^}]*)\}/g)]
    .filter(rule => /transition:\s*none/.test(rule[2]))
    .flatMap(rule => rule[1].split(',').map(one => one.trim()));

  assert.ok(stilled.includes('.widget-button'),
    'a button eases its background colour, so the reduce query has to still it like every other transition');
});
