// Flex `gap` fallback for legacy WebKit. Safari only supports gap in flex containers from
// 14.1, so iOS 9-14 drop every `gap:` declaration and the layout collapses into touching
// elements - the visibly wrong spacing in issue #43 (widget titles, music-player rows).
//
// For each flex rule with a gap, a `sel > * + *` margin rule is emitted right after it (same
// parent, so @media context and cascade order are preserved). The fallback is scoped to the
// `no-flex-gap` class the legacy polyfills put on <html> after a runtime probe: an @supports
// gate cannot express this, because every condition that correlates with flex gap (`inset`,
// grid `gap`) mismatches on some engine that reaches the legacy bundle, and a mismatch means
// gap plus margin - double spacing.
//
// Ported from the Angular client's legacy build; nothing here was specific to it.
import postcss from 'postcss';

export const NO_FLEX_GAP_CLASS = 'no-flex-gap';


const FLEX_DISPLAY = /^(inline-)?flex$/;
const ABSOLUTE_POSITION = /^(absolute|fixed)$/;

function collectDeclarations(rule) {
  const declarations = new Map();
  rule.walkDecls((declaration) => {
    declarations.set(declaration.prop, declaration.value.trim());
  });
  return declarations;
}

function gapForDirection(declarations, isColumn) {
  const axisGap = isColumn ? declarations.get('row-gap') : declarations.get('column-gap');
  if (axisGap) {
    return axisGap;
  }
  const gap = declarations.get('gap');
  if (!gap) {
    return null;
  }
  const parts = [];
  let depth = 0;
  let current = '';
  for (const ch of gap) {
    if (ch === '(') {
      depth += 1;
    } else if (ch === ')') {
      depth -= 1;
    }
    if (depth === 0 && /\s/.test(ch)) {
      if (current) {
        parts.push(current);
        current = '';
      }
      continue;
    }
    current += ch;
  }
  if (current) {
    parts.push(current);
  }
  if (parts.length === 0) {
    return null;
  }
  return parts.length === 1 ? parts[0] : (isColumn ? parts[0] : parts[1]);
}

function scopeChildSelector(selector) {
  return selector
    .split(',')
    .map((part) => `.${NO_FLEX_GAP_CLASS} ${part.trim()} > * + *`)
    .join(',');
}

function scopeOwnSelector(selector) {
  return selector
    .split(',')
    .map((part) => `.${NO_FLEX_GAP_CLASS} ${part.trim()}`)
    .join(',');
}

// The "subject" of a compound selector - the element/id plus its classes - ignoring any
// attribute selector, which would otherwise make a subject unique and defeat the index below.
function parseSubject(selectorPart) {
  const compounds = selectorPart.trim().split(/\s*[>+~]\s*|\s+/).filter(Boolean);
  const last = compounds[compounds.length - 1] ?? '';
  const withoutAttrs = last.replace(/\[[^\]]*\]/g, '');
  const classes = new Set((withoutAttrs.match(/\.[-\w]+/g) ?? []).map((token) => token.slice(1)));
  const elementOrId = withoutAttrs.replace(/\.[-\w]+/g, '');
  return { elementOrId, classes };
}

function isSubset(smaller, larger) {
  for (const item of smaller) {
    if (!larger.has(item)) {
      return false;
    }
  }
  return true;
}

// A gap override rule (e.g. `.music-player.compact`) does not repeat `display:flex` - it relies
// on the base rule (`.music-player`) still applying. Resolve its axis/wrap from the most specific
// indexed base whose subject the override rule's subject extends, matching the cascade: same
// element/id, and the base's classes are a subset of the override's.
function resolveBase(index, selector) {
  let best = null;
  for (const part of selector.split(',')) {
    const subject = parseSubject(part);
    for (const entry of index) {
      if (entry.elementOrId !== subject.elementOrId) {
        continue;
      }
      if (!isSubset(entry.classes, subject.classes)) {
        continue;
      }
      if (!best || entry.classes.size > best.classes.size) {
        best = entry;
      }
    }
  }
  return best;
}

function compact(rule) {
  rule.raws.before = '';
  rule.raws.between = '';
  rule.raws.after = '';
  rule.raws.semicolon = false;
  rule.walkDecls((decl) => {
    decl.raws.before = '';
    decl.raws.between = ':';
  });
  return rule;
}

export function flexGapFallback(css) {
  const root = postcss.parse(css);

  const flexIndex = [];
  root.walkRules((rule) => {
    const declarations = collectDeclarations(rule);
    const display = declarations.get('display');
    if (!display || !FLEX_DISPLAY.test(display)) {
      return;
    }
    const wrap = declarations.get('flex-wrap');
    const isColumn = (declarations.get('flex-direction') ?? 'row').startsWith('column');
    for (const part of rule.selector.split(',')) {
      const subject = parseSubject(part);
      flexIndex.push({ ...subject, isColumn, wrap });
    }
  });

  const gapTargets = [];
  const autoMarginTargets = [];
  const absoluteTargets = [];

  root.walkRules((rule) => {
    const declarations = collectDeclarations(rule);

    const position = declarations.get('position');
    if (position && ABSOLUTE_POSITION.test(position)) {
      // Never reset a margin the rule sets itself - the fallback is what has to give way here,
      // not the author's own box.
      const own = ['margin-top', 'margin-left'].filter(
        (prop) => !declarations.has(prop) && !declarations.has('margin'));
      if (own.length > 0) {
        absoluteTargets.push({ rule, props: own });
      }
    }

    for (const prop of ['margin-top', 'margin-left']) {
      if (declarations.get(prop) === 'auto') {
        autoMarginTargets.push({ rule, prop });
      }
    }

    const hasGap = declarations.has('gap') || declarations.has('row-gap') || declarations.has('column-gap');
    if (!hasGap) {
      return;
    }

    const display = declarations.get('display');
    let isColumn;
    let wrap;
    let gap;

    if (display && FLEX_DISPLAY.test(display)) {
      wrap = declarations.get('flex-wrap');
      isColumn = (declarations.get('flex-direction') ?? 'row').startsWith('column');
      if (wrap && wrap.startsWith('wrap')) {
        // A wrapping container is not something the floor renders correctly whatever the spacing:
        // old WebKit measures one from its first line alone, so the wrapped line contributes no
        // height and spills out of the box (issue #829). The sheets avoid wrapping for that reason
        // - see `.wc-settings-row` - and a gap fallback for one would be spacing on a broken box.
        return;
      }
      gap = gapForDirection(declarations, isColumn);
      // A rule that declares its own zero gap never had a fallback to begin with, so there
      // is nothing to reset - unlike an override rule turning an inherited gap down to zero.
      if (!gap || gap === '0' || gap === '0px') {
        return;
      }
    } else {
      const base = resolveBase(flexIndex, rule.selector);
      if (!base) {
        return;
      }
      isColumn = base.isColumn;
      wrap = base.wrap;
      if (wrap && wrap.startsWith('wrap')) {
        return;
      }
      gap = gapForDirection(declarations, isColumn);
      if (gap === null) {
        return;
      }
    }

    gapTargets.push({ rule, gap, isColumn });
  });

  for (const { rule, gap, isColumn } of gapTargets) {
    const fallback = compact(postcss.rule({ selector: scopeChildSelector(rule.selector) }));
    fallback.append({ prop: isColumn ? 'margin-top' : 'margin-left', value: gap });
    rule.parent.insertAfter(rule, fallback);
  }

  // Appended at the very end of the enclosing container (root or @media block) rather than
  // right after their own rule, so they always sort after every `> * + *` fallback in that
  // container regardless of where the original rule sits - the tie-break that lets an author's
  // `margin-*: auto` (or an absolutely positioned child's box) win the equal-specificity clash.
  for (const { rule, prop } of autoMarginTargets) {
    const reassert = compact(postcss.rule({ selector: scopeOwnSelector(rule.selector) }));
    reassert.append({ prop, value: 'auto' });
    rule.parent.append(reassert);
  }

  for (const { rule, props } of absoluteTargets) {
    const reset = compact(postcss.rule({ selector: scopeOwnSelector(rule.selector) }));
    for (const prop of props) {
      reset.append({ prop, value: '0' });
    }
    rule.parent.append(reset);
  }

  return { css: root.toString(), count: gapTargets.length };
}
