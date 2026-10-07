/**
 * Filesystem access for `component-profile-conformance.spec.ts`, which checks the renderer against the
 * shared fixture in `ui-model/fixtures/component-profile/`.
 *
 * That spec file cannot `import 'fs'`/`import 'path'` itself: `boundaries.test.mjs`'s "the specs need
 * no framework either" check keeps every `.spec.ts` free of bare specifiers, Node builtins included,
 * so a spec stays runnable under whatever harness a future consumer of this package points it at. A
 * `.cjs` helper is exempt - `dom.cjs` already does the same thing for jsdom - so the filesystem walk
 * lives here instead, behind one global the spec calls without importing anything.
 */
const fs = require('fs');
const path = require('path');

function fixturesRoot() {
  let dir = __dirname;
  for (;;) {
    const candidate = path.join(dir, 'ui-model', 'fixtures');
    if (fs.existsSync(candidate)) return candidate;
    const parent = path.dirname(dir);
    if (parent === dir) throw new Error(`ui-model/fixtures was not found above ${__dirname}`);
    dir = parent;
  }
}

globalThis.__loadWidgetProfileFixture = function loadWidgetProfileFixture(name) {
  return JSON.parse(fs.readFileSync(path.join(fixturesRoot(), 'component-profile', name), 'utf8'));
};

// The thresholds spec checks readThresholds against the same cases the C# UiThresholds tests use.
globalThis.__loadThresholdFixture = function loadThresholdFixture() {
  return JSON.parse(fs.readFileSync(path.join(fixturesRoot(), 'thresholds', 'threshold-values.json'), 'utf8'));
};

globalThis.__loadColorFixture = function loadColorFixture() {
  return JSON.parse(fs.readFileSync(path.join(fixturesRoot(), 'colors', 'color-references.json'), 'utf8'));
};
