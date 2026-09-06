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

globalThis.__loadWidgetProfileFixture = function loadWidgetProfileFixture(name) {
  let dir = __dirname;
  for (;;) {
    const candidate = path.join(dir, 'ui-model', 'fixtures');
    if (fs.existsSync(candidate)) {
      return JSON.parse(fs.readFileSync(path.join(candidate, 'component-profile', name), 'utf8'));
    }
    const parent = path.dirname(dir);
    if (parent === dir) throw new Error(`ui-model/fixtures was not found above ${__dirname}`);
    dir = parent;
  }
};
