/**
 * Guards what makes this a framework-independent package (issue #824).
 *
 * Most of it the build already enforces: `tsconfig.json` compiles against `target: ES5` with no
 * Angular types in scope, and `check:es5` gates the emitted bundle. What the build cannot see is the
 * dependency graph - npm hoists the Angular workspace's `node_modules` to `ui/`, so an `@angular/core`
 * or `rxjs` import from here would resolve and compile perfectly well. That is what these check.
 *
 * Run by `npm test -w @macro-deck/runtime`'s sibling script, and in CI.
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.join(HERE, 'src');

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
const isImpl = file => isTs(file) && !file.endsWith('.spec.ts');

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

test('the shipped runtime depends on nothing at all', () => {
  // Not a denylist of frameworks: the package declares no dependencies, so *any* bare specifier in
  // shipped code is a dependency nobody wrote down. It resolves today only because npm hoisted a
  // sibling workspace's tree, and would fail the moment this package is consumed on its own.
  const offenders = [];
  for (const file of walk(SRC, isImpl)) {
    for (const spec of specifiersOf(file)) {
      if (!spec.startsWith('.')) offenders.push(`${rel(file)} -> ${spec}`);
    }
  }
  assert.deepEqual(offenders, [], 'add it to package.json dependencies on purpose, or inline it');
});

test('nothing here reaches into a client', () => {
  const offenders = [];
  for (const file of walk(SRC, isTs)) {
    for (const spec of specifiersOf(file)) {
      if (!spec.startsWith('.')) continue;
      if (path.resolve(path.dirname(file), spec).startsWith(HERE + path.sep)) continue;
      offenders.push(`${rel(file)} -> ${spec}`);
    }
  }
  assert.deepEqual(offenders, [], 'a package that imports its consumers is not a package');
});

test('the specs need no framework either', () => {
  // The 23 spec files came across from karma unchanged. Keeping them free of bare imports is what
  // lets them run under a plain jasmine process instead of a browser and an Angular TestBed.
  const offenders = [];
  for (const file of walk(SRC, f => f.endsWith('.spec.ts'))) {
    for (const spec of specifiersOf(file)) {
      if (!spec.startsWith('.')) offenders.push(`${rel(file)} -> ${spec}`);
    }
  }
  assert.deepEqual(offenders, []);
});

test('the public API names every export instead of re-exporting whole modules', () => {
  const source = readFileSync(path.join(SRC, 'public-api.ts'), 'utf8');
  const blanket = [...source.matchAll(/^[ \t]*export\s*\*.*$/gm)].map(m => m[0].trim());
  assert.deepEqual(blanket, [], 'a blanket re-export puts internals back on the consumers\' graphs');
});

test('the public API points only at modules that exist', () => {
  const modules = new Set();
  for (const match of readFileSync(path.join(SRC, 'public-api.ts'), 'utf8')
    .matchAll(/from\s*['"]\.\/([^'"]+)['"]/g)) {
    modules.add(path.resolve(SRC, match[1]));
  }
  assert.ok(modules.size > 0, 'public-api.ts should re-export something');
  for (const module of modules) {
    const found = [module + '.ts', path.join(module, 'index.ts')].some(candidate => {
      try {
        return statSync(candidate).isFile();
      } catch {
        return false;
      }
    });
    assert.ok(found, `public-api.ts points at a module that does not exist: ${rel(module)}`);
  }
});

test('no implementation file is stranded', () => {
  const seen = new Set();
  const queue = [path.join(SRC, 'public-api.ts'), ...walk(SRC, f => f.endsWith('.spec.ts'))];
  while (queue.length) {
    const file = queue.pop();
    if (seen.has(file)) continue;
    seen.add(file);
    for (const spec of specifiersOf(file)) {
      if (!spec.startsWith('.')) continue;
      const base = path.resolve(path.dirname(file), spec);
      for (const candidate of [base + '.ts', path.join(base, 'index.ts')]) {
        try {
          if (statSync(candidate).isFile()) queue.push(candidate);
        } catch { /* not this one */ }
      }
    }
  }
  const stranded = walk(SRC, isImpl).filter(file => !seen.has(file)).map(rel).sort();
  assert.deepEqual(stranded, [], 'nothing reaches these files; delete them or export them on purpose');
});

test('the package is actually here', () => {
  // A sanity check on the checks: renaming the directory away would make every test above pass by
  // walking nothing.
  assert.ok(walk(SRC, isImpl).length > 50, 'src should hold the framework-free runtime');
});
