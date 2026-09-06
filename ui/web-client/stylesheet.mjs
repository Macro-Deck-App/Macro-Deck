// The client's stylesheet, assembled from the runtime package's sheets plus the client's own.
//
// The order is written out rather than globbed: `tokens` has to precede everything that reads a
// token, and a glob would order them alphabetically. Shared with the tests so the compatibility
// checks run against the sheet the build actually ships rather than a copy of the list.
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const runtimeStyles = path.resolve(here, '..', 'runtime', 'styles');

const RUNTIME_SHEETS = [
  'tokens.css', 'reset.css', 'animations.css', 'icons.css',
  'widget-metrics.css', 'renderer.css', 'widget-border.css', 'grid.css',
];

const CLIENT_SHEETS = [
  ['client', path.join(here, 'src', 'styles.css')],
  ['setup', path.join(here, 'src', 'setup', 'setup.css')],
  ['settings', path.join(here, 'src', 'settings', 'settings.css')],
];

/** The client's chrome is meant to override the shared defaults, so it is concatenated last. */
export async function assembleStylesheet() {
  const parts = [];
  for (const sheet of RUNTIME_SHEETS) {
    parts.push(`/* ${sheet} */`, await readFile(path.join(runtimeStyles, sheet), 'utf8'));
  }
  for (const [label, file] of CLIENT_SHEETS) {
    parts.push(`/* ${label} */`, await readFile(file, 'utf8'));
  }
  return parts.join('\n');
}
