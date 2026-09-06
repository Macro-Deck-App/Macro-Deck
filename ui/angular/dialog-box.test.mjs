/**
 * Binds the box a profile-rendered dialog is given to the one the framework-free client gives it.
 *
 * Both clients draw a modal's tree with the same renderer, and every length in the widget profile is
 * a fraction of the box the tree is handed - so two clients that state different boxes draw the same
 * dialog at different sizes. That is what happened: the desktop host stated no height at all, the
 * dialog collapsed to whatever the tree had already resolved to, and that height came back in as the
 * basis, so the same Weather card came out about a third of the size it has in the other client.
 *
 * The numbers live in two files that cannot import from one another, so they are checked here rather
 * than kept in step by a comment - the same reason `ui/runtime/styles.test.mjs` exists.
 */
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const read = relative => readFileSync(fileURLToPath(new URL(relative, import.meta.url)), 'utf8');

const webClient = read('../web-client/src/styles.css');
const desktopHost = read(
  'projects/desktop-ui/src/app/shared/components/ui-modal/ui-modal-host.component.ts');
const designSystemModal = read(
  'projects/desktop-ui/src/app/shared/components/overlay/modal/modal.component.scss');

/**
 * Removes every conditionally applied block, whose rules describe a different box than the default
 * one: both clients take a modal full-screen on a small display, and the box this file is about is
 * the one the dialog has when they do not.
 */
function stripConditional(source) {
  let css = source;
  for (;;) {
    const at = css.search(/@(?:media|include)\b/);
    if (at === -1) return css;
    const open = css.indexOf('{', at);
    if (open === -1) return css;
    let depth = 1;
    let end = open + 1;
    while (end < css.length && depth > 0) {
      if (css[end] === '{') depth += 1;
      else if (css[end] === '}') depth -= 1;
      end += 1;
    }
    css = css.slice(0, at) + css.slice(end);
  }
}

/**
 * A declaration the rules naming `selector` unconditionally leave it with.
 *
 * `selector` has to be one whole entry of a rule's selector list rather than a substring of a
 * compound one - `.modal-overlay.closing .modal-dialog` holds the close animation and none of the
 * surface, and matching that one instead reads as "the property is missing". A grouped rule counts,
 * because the client states one surface for every dialog it opens; the last declaration to reach the
 * selector wins, as it does in the browser.
 */
function declaration(source, selector, property) {
  const css = stripConditional(source.replace(/\/\*[\s\S]*?\*\//g, ''));
  const entry = new RegExp(`^[ \\t]*\\${selector}[ \\t]*(?:,|\\{)`, 'gm');
  let block = false;
  let value = null;
  for (const match of css.matchAll(entry)) {
    const open = css.indexOf('{', match.index);
    if (open === -1) continue;
    block = true;
    const body = css.slice(open + 1, css.indexOf('}', open));
    const found = body.match(new RegExp(`(?:^|[;\\s])${property}:\\s*([^;]+);`));
    if (found) value = found[1].trim();
  }
  assert.ok(block, `no ${selector} block found`);
  assert.ok(value !== null, `${selector} declares no ${property}`);
  return value;
}

test('a profile-rendered dialog is the same width in both clients', () => {
  const stated = desktopHost.match(/maxWidth="([^"]+)"/);
  assert.ok(stated, 'the desktop modal host states no maxWidth');
  assert.equal(stated[1], declaration(webClient, '.wc-modal', 'max-width'));
});

test('a profile-rendered dialog is the same height in both clients', () => {
  const stated = desktopHost.match(/--modal-height\]="'([^']+)'"/);
  assert.ok(stated, 'the desktop modal host states no dialog height');
  assert.equal(stated[1], declaration(webClient, '.wc-modal', 'height'));
});

/**
 * The height belongs to the dialog, not to the body inside it. Stated on the body instead, the
 * dialog came out one header taller than the other client's and the tree resolved against a box
 * that much bigger.
 */
test('the height is the dialog\'s and the body takes what is left', () => {
  assert.equal(declaration(desktopHost, '.ui-modal-content', 'height'), '100%');
  assert.equal(declaration(webClient, '.wc-modal-content', 'flex'),
    declaration(desktopHost, '.ui-modal-content', 'flex'));
});

/**
 * A dialog that scrolls is a dialog whose box is a claim rather than a fact: a tree with more than
 * fits says so with a `ui.list`, which scrolls itself and asks its producer for more. A second
 * scroll container around it puts two bars on one dialog.
 */
test('neither client wraps the tree in a scroll container of its own', () => {
  assert.equal(declaration(webClient, '.wc-modal-content', 'overflow'), 'hidden');
  assert.equal(declaration(desktopHost, '.ui-modal-content', 'overflow'), 'hidden');
  assert.match(desktopHost, /\bflush\b/,
    'the desktop dialog body must be flush, or it pads and scrolls around the tree');
});

/**
 * The same dialog opens in both clients, so its surface is the design system's in both - token for
 * token, not a hard-coded colour that stops following the theme the moment the tokens move.
 */
for (const [property, webSelector, desktopSelector] of [
  ['background', '.wc-modal', '.modal-dialog'],
  ['border-radius', '.wc-modal', '.modal-dialog'],
  ['box-shadow', '.wc-modal', '.modal-dialog'],
  ['background', '.wc-modal-backdrop', '.modal-overlay'],
  ['padding', '.wc-modal-header', '.modal-header'],
]) {
  test(`${webSelector} and ${desktopSelector} agree on ${property}`, () => {
    const web = declaration(webClient, webSelector, property);
    const desktop = declaration(designSystemModal, desktopSelector,
      property === 'background' && desktopSelector === '.modal-dialog' ? 'background-color' : property);
    assert.equal(web, desktop);
  });
}
