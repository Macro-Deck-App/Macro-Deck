// Custom properties, resolved away for the engines on the compatibility floor (issue #829).
//
// Safari 9, iOS 9, Chrome 30-48, Firefox 30 and the Android 4 stock browser have no CSS custom
// properties at all, and an old parser does not ignore `var()` - it drops the whole declaration.
// A sheet that reaches those engines with `var(--color-text-primary)` in it therefore loses the
// colour, the spacing and the radius rather than falling back to something, which is why the
// down-levelled client rendered structurally intact and visually blank.
//
// The sheets are authored once, with custom properties, and this pass rewrites them for the
// legacy stylesheet only. Three kinds of token are treated differently, because only one of them
// can be answered at build time:
//
//   - design tokens declared in `:root`: substituted for their literal value. Where `.light`
//     overrides one, the rule is emitted a second time scoped to `.light`, which is the class
//     `appearance.ts` toggles on <html> - so live theme switching keeps working with no custom
//     properties involved.
//   - the accent, which the user picks at runtime: substituted for the default, and additionally
//     collected into a template with sentinels the client fills in (see `ACCENT_SENTINELS`).
//   - the tokens the renderer writes with `style.setProperty` (`--widget-scale` and friends):
//     unresolvable here by definition. The declared `var()` fallback is emitted, and the runtime's
//     `render/custom-properties.ts` supplies the real value - as an inline style where the element
//     that reads the token is the one it is set on, and as a scoped rule where it is not.
//
// Pure string transforms over a small CSS parser, no dependencies, kept apart from
// css-downlevel.mjs so both stay unit-testable on their own.

/** The `<html>` class `appearance.ts` toggles. A rule that differs by theme is emitted under it. */
export const LIGHT_THEME_CLASS = 'light';

/**
 * The accent tokens the user chooses at runtime, and the placeholder each one leaves in the
 * accent template. `appearance.ts` substitutes these; they are deliberately not valid CSS, so a
 * template that reached a stylesheet by mistake paints nothing rather than painting wrong.
 */
export const ACCENT_SENTINELS = new Map([
  ['--color-accent', '__MD_ACCENT__'],
  ['--color-accent-hover', '__MD_ACCENT_HOVER__'],
  ['--color-accent-muted', '__MD_ACCENT_MUTED__'],
]);

/**
 * Parses a stylesheet into rules, at-rules and declarations.
 *
 * Brace-walking rather than splitting on `{`/`}`: an at-rule carries its own block, so a naive
 * split mis-parses everything after the first `@media` - and the token blocks this pass exists to
 * read sit above several of them.
 */
export function parseStylesheet(css) {
  let index = 0;

  function parseBlock(nested) {
    const children = [];
    let buffer = '';
    let depth = 0;
    let quote = '';

    const flush = () => {
      if (buffer.trim() !== '') children.push(...parseDeclarations(buffer));
      buffer = '';
    };

    while (index < css.length) {
      const character = css[index];

      if (quote !== '') {
        buffer += character;
        if (character === quote && css[index - 1] !== '\\') quote = '';
        index += 1;
        continue;
      }
      if (character === '"' || character === "'") {
        quote = character;
        buffer += character;
        index += 1;
        continue;
      }
      if (character === '/' && css[index + 1] === '*') {
        const close = css.indexOf('*/', index + 2);
        index = close === -1 ? css.length : close + 2;
        continue;
      }
      // A `;` or a brace inside url(...) or a selector's parentheses is not a delimiter.
      if (character === '(') depth += 1;
      else if (character === ')') depth = Math.max(0, depth - 1);

      if (depth === 0 && character === '{') {
        const prelude = buffer.trim();
        buffer = '';
        index += 1;
        const body = parseBlock(true);
        children.push(prelude.charAt(0) === '@'
          ? { type: 'atrule', prelude, children: body }
          : { type: 'rule', selector: prelude, children: body });
        continue;
      }
      if (depth === 0 && character === '}') {
        index += 1;
        flush();
        if (nested) return children;
        continue;
      }
      if (depth === 0 && character === ';') {
        buffer += character;
        flush();
        index += 1;
        continue;
      }
      buffer += character;
      index += 1;
    }
    flush();
    return children;
  }

  return parseBlock(false);
}

function parseDeclarations(text) {
  const out = [];
  let depth = 0;
  let current = '';
  const flush = () => {
    const trimmed = current.trim();
    current = '';
    if (trimmed === '') return;
    const stripped = trimmed.replace(/\/\*[\s\S]*?\*\//g, '').trim();
    if (stripped === '') {
      out.push({ type: 'comment', text: trimmed });
      return;
    }
    const separator = splitProperty(stripped);
    if (separator === -1) {
      out.push({ type: 'comment', text: trimmed });
      return;
    }
    out.push({
      type: 'declaration',
      property: stripped.slice(0, separator).trim(),
      value: stripped.slice(separator + 1).trim(),
    });
  };
  for (const character of text) {
    if (character === '(') depth += 1;
    else if (character === ')') depth -= 1;
    if (character === ';' && depth === 0) {
      flush();
      continue;
    }
    current += character;
  }
  flush();
  return out;
}

/** The colon that separates property from value, skipping the one inside a `url(data:...)`. */
function splitProperty(declaration) {
  let depth = 0;
  for (let i = 0; i < declaration.length; i += 1) {
    const character = declaration[i];
    if (character === '(') depth += 1;
    else if (character === ')') depth -= 1;
    else if (character === ':' && depth === 0) return i;
  }
  return -1;
}

export function stringifyStylesheet(nodes, indent = '') {
  const lines = [];
  for (const node of nodes) {
    if (node.type === 'comment') lines.push(indent + node.text);
    else if (node.type === 'declaration') lines.push(`${indent}${node.property}: ${node.value};`);
    else {
      const head = node.type === 'atrule' ? node.prelude : node.selector;
      const body = stringifyStylesheet(node.children, indent + '  ');
      lines.push(`${indent}${head} {`, body, `${indent}}`);
    }
  }
  return lines.filter(line => line !== '').join('\n');
}

const isThemeRule = (node, selector) =>
  node.type === 'rule' && node.selector.split(',').some(part => part.trim() === selector);

/**
 * The declared design tokens: `:root` as the base and `.light` as the theme override.
 *
 * Replaces a regex that required `:root` to follow `}`, `,` or the start of the file. In the
 * assembled stylesheet it follows a comment, so that regex matched nothing at all and every
 * caller silently received an empty map (issue #829).
 */
export function parseThemeTokens(css) {
  const dark = new Map();
  const light = new Map();
  const visit = (nodes) => {
    for (const node of nodes) {
      if (node.type === 'atrule') {
        visit(node.children);
        continue;
      }
      if (node.type !== 'rule') continue;
      for (const [selector, target] of [[':root', dark], [`.${LIGHT_THEME_CLASS}`, light]]) {
        if (!isThemeRule(node, selector)) continue;
        for (const child of node.children) {
          if (child.type === 'declaration' && child.property.startsWith('--')) {
            target.set(child.property, child.value);
          }
        }
      }
    }
  };
  visit(parseStylesheet(css));
  return { dark, light };
}

/**
 * Substitutes `var(--token[, fallback])` from `tokens`, repeatedly, so a token defined in terms of
 * another resolves too. Depth-limited against a self-referential token. A token that is in neither
 * the map nor a fallback is left in place, which is how the caller detects that a declaration
 * depends on something only the runtime can supply.
 */
export function substitute(value, tokens, depth = 0) {
  if (depth > 16 || !value.includes('var(')) return value;
  const call = findCall(value, 'var');
  if (call === null) return value;
  const [name, ...fallback] = splitTopLevel(value.slice(call.argsStart, call.end));
  const replacement = tokens.has(name)
    ? tokens.get(name)
    : (fallback.length > 0 ? fallback.join(',').trim() : null);
  if (replacement === null) {
    // Leave this call alone but keep going: a value can hold a resolvable call after it.
    const rest = substitute(value.slice(call.end + 1), tokens, depth + 1);
    return value.slice(0, call.end + 1) + rest;
  }
  return substitute(value.slice(0, call.start) + replacement + value.slice(call.end + 1), tokens, depth + 1);
}

function findCall(value, name) {
  const needle = `${name}(`;
  for (let i = value.indexOf(needle); i !== -1; i = value.indexOf(needle, i + 1)) {
    if (i > 0 && /[\w-]/.test(value[i - 1])) continue;
    let depth = 0;
    for (let j = i + needle.length - 1; j < value.length; j += 1) {
      if (value[j] === '(') depth += 1;
      else if (value[j] === ')') {
        depth -= 1;
        if (depth === 0) return { start: i, argsStart: i + needle.length, end: j };
      }
    }
    return null;
  }
  return null;
}

function splitTopLevel(args) {
  const parts = [];
  let depth = 0;
  let current = '';
  for (const character of args) {
    if (character === '(') depth += 1;
    else if (character === ')') depth -= 1;
    if (character === ',' && depth === 0) {
      parts.push(current.trim());
      current = '';
      continue;
    }
    current += character;
  }
  parts.push(current.trim());
  return parts;
}

/** Every token whose value depends, however indirectly, on one the user picks at runtime. */
export function accentDependentTokens(dark) {
  const dependent = new Set(ACCENT_SENTINELS.keys());
  for (let changed = true; changed;) {
    changed = false;
    for (const [name, value] of dark) {
      if (dependent.has(name)) continue;
      if ([...dependent].some(token => value.includes(`var(${token})`) || value.includes(`var(${token},`))) {
        dependent.add(name);
        changed = true;
      }
    }
  }
  return dependent;
}

/** `.light`-scoping for a selector, given that the class lives on the <html> element itself. */
export function scopeToLight(selector) {
  return selector.split(',').map((part) => {
    const trimmed = part.trim();
    if (/^(html|:root)\b/.test(trimmed)) {
      return trimmed.replace(/^(html|:root)/, `$1.${LIGHT_THEME_CLASS}`);
    }
    return `.${LIGHT_THEME_CLASS} ${trimmed}`;
  }).join(', ');
}

/**
 * Rewrites a stylesheet so no declaration reads a custom property.
 *
 * Returns the rewritten sheet, the accent template the client fills in at runtime, and the
 * declarations that were dropped because they depend on a token only the renderer knows - the
 * runtime bridge has to cover exactly those, and `compatibility.test.mjs` holds it to that.
 */
export function resolveCustomProperties(css) {
  const { dark, light } = parseThemeTokens(css);
  const accentTokens = accentDependentTokens(dark);
  const lightTokens = new Map([...dark, ...light]);
  const accentTemplateTokens = new Map([...dark]);
  for (const [name, sentinel] of ACCENT_SENTINELS) accentTemplateTokens.set(name, sentinel);
  const lightAccentTemplateTokens = new Map([...lightTokens]);
  for (const [name, sentinel] of ACCENT_SENTINELS) lightAccentTemplateTokens.set(name, sentinel);

  const unresolved = [];
  const accentRules = [];

  const rewrite = (nodes) => {
    const out = [];
    for (const node of nodes) {
      if (node.type === 'comment') continue;
      if (node.type === 'declaration') {
        // A declaration outside any rule; nothing in these sheets, but pass it through resolved.
        out.push({ ...node, value: substitute(node.value, dark) });
        continue;
      }
      if (node.type === 'atrule') {
        const children = rewrite(node.children);
        if (children.length > 0) out.push({ ...node, children });
        continue;
      }

      const isTokenBlock = isThemeRule(node, ':root') || isThemeRule(node, `.${LIGHT_THEME_CLASS}`);
      const kept = [];
      const lightOverrides = [];
      const accentOverrides = [];
      const lightAccentOverrides = [];

      for (const child of node.children) {
        if (child.type !== 'declaration') {
          if (child.type === 'rule' || child.type === 'atrule') kept.push(...rewrite([child]));
          continue;
        }
        // The token declarations themselves are inert once every reader has been substituted.
        if (child.property.startsWith('--')) {
          if (isTokenBlock) continue;
          kept.push({ ...child, value: substitute(child.value, dark) });
          continue;
        }

        const resolved = substitute(child.value, dark);
        if (resolved.includes('var(')) {
          // Only the renderer can answer this one, and it does - as an inline style, which beats
          // any rule here anyway. Dropping it keeps the sheet free of declarations the floor
          // engines would drop themselves, so `var(` in the output means a real mistake.
          unresolved.push({ selector: node.selector, property: child.property, value: child.value });
          continue;
        }
        kept.push({ ...child, value: resolved });

        const underLight = substitute(child.value, lightTokens);
        if (underLight !== resolved && !underLight.includes('var(')) {
          lightOverrides.push({ ...child, value: underLight });
        }
        if (dependsOnAccent(child.value, accentTokens)) {
          accentOverrides.push({ ...child, value: substitute(child.value, accentTemplateTokens) });
          // The same declaration under the light theme: an accent rule usually mixes the accent
          // with a themed colour (a focus ring sits on the page background), so a template built
          // from the dark values alone would repaint the light theme dark when it is applied.
          const underLightWithAccent = substitute(child.value, lightAccentTemplateTokens);
          if (underLightWithAccent !== substitute(child.value, accentTemplateTokens)) {
            lightAccentOverrides.push({ ...child, value: underLightWithAccent });
          }
        }
      }

      if (kept.length > 0) out.push({ ...node, children: kept });
      if (lightOverrides.length > 0) {
        out.push({ type: 'rule', selector: scopeToLight(node.selector), children: lightOverrides });
      }
      if (accentOverrides.length > 0) {
        accentRules.push({ type: 'rule', selector: node.selector, children: accentOverrides });
      }
      if (lightAccentOverrides.length > 0) {
        accentRules.push({ type: 'rule', selector: scopeToLight(node.selector), children: lightAccentOverrides });
      }
    }
    return out;
  };

  const rewritten = rewrite(parseStylesheet(css));
  return {
    css: stringifyStylesheet(rewritten) + '\n',
    accentTemplate: accentRules.length > 0 ? stringifyStylesheet(accentRules) + '\n' : '',
    unresolved,
  };
}

function dependsOnAccent(value, accentTokens) {
  for (const token of accentTokens) {
    if (value.includes(`var(${token})`) || value.includes(`var(${token},`) || value.includes(`var(${token} `)) {
      return true;
    }
  }
  return false;
}
