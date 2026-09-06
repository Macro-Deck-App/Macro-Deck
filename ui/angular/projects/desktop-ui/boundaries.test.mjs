/**
 * Guards the layering issue #319 established and #824 finished.
 *
 * The framework-free half is `@macro-deck/runtime`, a workspace package with no dependencies, and
 * its own rules live with it in `ui/runtime`. What is left in this project is the Angular adapter
 * that consumes it, folded into the application it serves (issue #828). So what needs guarding here
 * is that the application consumes the package rather than reaching around it, and that the
 * adapter's barrel stays something a person chose rather than a blanket re-export.
 *
 * A Node test rather than a karma spec because these are questions about the source tree, and karma
 * specs run in a browser with no filesystem. Run by `npm run test:boundaries`.
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.join(HERE, 'src');
const ADAPTER = path.join(SRC, 'app', 'shared');
const BARREL = path.join(ADAPTER, 'index.ts');
const RUNTIME_PACKAGE = '@macro-deck/runtime';

function walk(dir, predicate) {
  const out = [];
  for (const entry of readdirSync(dir)) {
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) out.push(...walk(full, predicate));
    else if (predicate(full)) out.push(full);
  }
  return out;
}

const isTs = file => file.endsWith('.ts');

/** Every module specifier a file imports or re-exports from, including multi-line statements. */
function specifiersOf(file) {
  const source = readFileSync(file, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/^\s*\/\/.*$/gm, '');
  const specs = [];
  const statement = /(?:^|\n)[ \t]*(?:import|export)\b[^;'"]*?from\s*['"]([^'"]+)['"]/g;
  const dynamic = /\bimport\(\s*['"]([^'"]+)['"]\s*\)/g;
  for (const pattern of [statement, dynamic]) {
    let match;
    while ((match = pattern.exec(source)) !== null) specs.push(match[1]);
  }
  return specs;
}

const rel = file => path.relative(HERE, file);

test('the application reaches the runtime only through the package', () => {
  // A relative path out of this project, or a deep import past the package's `exports` map, both
  // couple the application to the runtime's file layout instead of its published surface.
  const offenders = [];
  for (const file of walk(SRC, isTs)) {
    for (const spec of specifiersOf(file)) {
      if (spec.startsWith(RUNTIME_PACKAGE + '/')) offenders.push(`${rel(file)} -> ${spec}`);
      if (!spec.startsWith('.')) continue;
      const target = path.resolve(path.dirname(file), spec);
      if (!target.startsWith(HERE + path.sep)) offenders.push(`${rel(file)} -> ${spec}`);
    }
  }
  assert.deepEqual(offenders, [], `import runtime symbols from '${RUNTIME_PACKAGE}'`);
});

test('the adapter barrel names every export instead of re-exporting whole modules', () => {
  const blanket = [...readFileSync(BARREL, 'utf8').matchAll(/^[ \t]*export\s*\*.*$/gm)]
    .map(match => match[0].trim());
  assert.deepEqual(blanket, [],
    'a blanket re-export makes the barrel an unbounded surface nothing can review');
});

test('the adapter barrel exports nothing the application imports', () => {
  const exported = new Set();
  for (const match of readFileSync(BARREL, 'utf8').matchAll(/export\s*\{([^}]*)\}\s*from/g)) {
    for (const part of match[1].split(',')) {
      const name = part.trim().replace(/^type\s+/, '').split(/\s+as\s+/)[0].trim();
      if (name) exported.add(name);
    }
  }

  const consumed = new Set();
  for (const file of walk(SRC, isTs)) {
    if (file === BARREL) continue;
    const source = readFileSync(file, 'utf8');
    // `export { X } from '@shared'` is a use too - a test-support module re-exporting an adapter
    // symbol consumes it just as much as an import does.
    for (const match of source.matchAll(/(?:import|export)\s*(?:type\s*)?\{([^}]*)\}\s*from\s*['"]@shared['"]/g)) {
      for (const part of match[1].split(',')) {
        const name = part.trim().replace(/^type\s+/, '').split(/\s+as\s+/)[0].trim();
        if (name) consumed.add(name);
      }
    }
  }

  const unused = [...exported].filter(name => !consumed.has(name)).sort();
  assert.deepEqual(unused, [],
    'the barrel is meant to be what the application consumes; drop what nothing imports');
});

test('the adapter is actually here', () => {
  // A sanity check on the checks: if the directory were renamed away, every test above would pass
  // vacuously by walking nothing.
  assert.ok(walk(ADAPTER, isTs).length > 50, 'app/shared should hold the adapter');
});
