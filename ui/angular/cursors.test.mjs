import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const UI = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DESKTOP = 'angular/projects/desktop-ui/src/app';

const SOURCE_ROOTS = [
  'angular/projects/desktop-ui/src',
  'runtime/src',
  'runtime/styles',
  'web-client/src',
  'bootstrapper/src',
];

const ALLOWED = new Set([
  `${DESKTOP}/components/action-builder/event-trigger/event-trigger-editor.component.scss :: .event-trigger-link`,
  `${DESKTOP}/components/backup/backup-restore-modal/backup-restore-modal.component.scss :: .brm-link`,
  `${DESKTOP}/components/pages/developer-page/plugin-development-tab/sections/compatibility-section/compatibility-section.component.scss :: .finding-migration-link`,
  `${DESKTOP}/components/pages/integrations-page/integrations-page.component.scss :: .integration-card`,
  `${DESKTOP}/components/pages/integrations-page/integrations-page.component.scss :: button.meta-item-link`,
  `${DESKTOP}/components/pages/library-page/library-overview.component.scss :: .library-card`,
  `${DESKTOP}/components/pages/store-detail-page/store-dependencies-panel.component.scss :: .dependencies-transitive-summary`,
  `${DESKTOP}/components/pages/store-detail-page/store-detail-header.component.scss :: .header-rating`,
  `${DESKTOP}/components/pages/store-detail-page/store-screenshot-strip.component.scss :: .thumb`,
  `${DESKTOP}/components/pages/store-detail-page/store-version-list.component.scss :: .history-entry-toggle`,
  `${DESKTOP}/components/pages/store-page/store-page.component.scss :: .link-button`,
  `${DESKTOP}/components/shell/settings-modal/sections/about-settings.component.scss :: .about__link`,
  `${DESKTOP}/components/shell/settings-modal/sections/adb-settings.component.scss :: .adb__device-manufacturer, .adb__device-serial`,
  `${DESKTOP}/components/shell/settings-modal/sections/third-party-notices-modal.component.scss :: .notices__link`,
  `${DESKTOP}/components/shell/settings-modal/sections/usb-settings.component.scss :: .usb__row-serial`,
  `${DESKTOP}/components/store/store-community-notice.component.scss :: .notice-link`,
  `${DESKTOP}/components/store/store-footer.component.scss :: .guidelines-link`,
  `${DESKTOP}/components/store/store-reviews-section.component.scss :: .review-more`,
  `${DESKTOP}/components/ui-render/ui-render.component.scss :: .config-choice-card`,
  `${DESKTOP}/components/variables/_variable-list.scss :: @mixin link-row`,
  `${DESKTOP}/shared/components/widget-grid/widget-grid.component.scss :: .empty-cell.edit-mode`,
  `${DESKTOP}/shared/components/widget-types/ui-tree-widget/ui-tree-widget.component.scss :: .widget-tile`,
  'runtime/styles/grid.css :: .deck-grid-tile-surface',
  'runtime/styles/renderer.css :: .widget-pressable',
]);

const DIALOG_HOSTS = [
  `${DESKTOP}/shared/components/ui-modal/ui-modal-host.component.ts`,
  `${DESKTOP}/shared/components/feedback/notification-dialog-host/notification-dialog-host.component.ts`,
  'web-client/src/modal.ts',
  'web-client/src/notification-dialogs.ts',
];

const SKIPPED_DIRECTORIES = new Set(['node_modules', 'dist', '.angular']);
const isSource = file => /\.(scss|css|ts|html)$/.test(file) && !file.endsWith('.spec.ts');

function walk(dir) {
  const out = [];
  for (const entry of readdirSync(dir)) {
    if (SKIPPED_DIRECTORIES.has(entry)) continue;
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) out.push(...walk(full));
    else if (isSource(full)) out.push(full);
  }
  return out;
}

// Same length as the input, so offsets stay valid. A comment can hold braces and the word cursor.
function blankComments(source) {
  const blank = text => text.replace(/[^\n]/g, ' ');
  return source
    .replace(/\/\*[\s\S]*?\*\//g, blank)
    .replace(/<!--[\s\S]*?-->/g, blank)
    .replace(/(^|\s)\/\/.*$/gm, (all, lead) => lead + blank(all.slice(lead.length)));
}

function ruleHeader(source, at) {
  let depth = 0;
  let open = at - 1;
  for (; open >= 0; open -= 1) {
    if (source[open] === '}') depth += 1;
    else if (source[open] === '{') {
      if (depth === 0) break;
      depth -= 1;
    }
  }
  let start = open - 1;
  while (start >= 0 && !'{};'.includes(source[start])) start -= 1;
  return source.slice(start + 1, open).replace(/\s+/g, ' ').trim();
}

function pointerRules() {
  const found = [];
  for (const root of SOURCE_ROOTS) {
    for (const file of walk(path.join(UI, root))) {
      const source = blankComments(readFileSync(file, 'utf8'));
      for (const match of source.matchAll(/cursor\s*:\s*pointer\b/g)) {
        const key = `${path.relative(UI, file).split(path.sep).join('/')} :: ${ruleHeader(source, match.index)}`;
        found.push(key);
      }
    }
  }
  return found;
}

test('only links, cards and clickable content show the hand cursor', () => {
  const offenders = pointerRules().filter(key => !ALLOWED.has(key));
  assert.deepEqual(offenders, [],
    'a control shows the arrow: delete the cursor, or write cursor: default on an element that is not a button');
});

test('every allowed pointer rule still exists', () => {
  const present = new Set(pointerRules());
  const stale = [...ALLOWED].filter(key => !present.has(key));
  assert.deepEqual(stale, [], 'remove the entry, the rule it allowed is gone');
});

test('every host that draws a tree as dialog content marks it, so its buttons show the arrow', () => {
  const unmarked = DIALOG_HOSTS.filter(
    file => !readFileSync(path.join(UI, file), 'utf8').includes('widget-dialog-content'));
  assert.deepEqual(unmarked, [], 'a dialog host has to carry the widget-dialog-content class');
});
