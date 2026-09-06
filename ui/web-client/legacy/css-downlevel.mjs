// CSS down-levelling for legacy WebKit (iOS 9 / Android 4). Ported from the Angular client's
// legacy build, which meets the same floor: the constructs are properties of the engines, not of
// either client. The stylesheets the modern build assembles carry syntax those engines cannot
// parse: 8-digit hex colors (Safari 10), the inset shorthand (14.1), clamp()/min()/max()
// (13.1/11.1), color-mix() (16.2) and container-query cqmin units (16). An unsupported
// declaration is dropped wholesale by an old parser, which loses backgrounds, overlay stretching
// and all fluid sizing. Pure string transforms, no dependencies - kept in its own module so
// build-legacy.mjs stays orchestration-only and this logic is unit-testable.
//
// cqmin is resolved statically against the 120px widget reference cell (widgets render at
// reference size and scale via transform, so 1cqmin = 1.2px is exact for square spans).
import { parseThemeTokens } from './css-custom-properties.mjs';

export const CQMIN_REFERENCE_PX = 1.2;

export const ROOT_FONT_SIZE_PX = 16;

function hexPairToChannel(hex, index) {
  return parseInt(hex.slice(index, index + 2), 16);
}

function formatAlpha(alpha) {
  return alpha.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
}

export function downlevelCssColors(css) {
  return css
    .replace(/#([0-9a-fA-F]{8})\b/g, (match, hex) => {
      const alpha = formatAlpha(hexPairToChannel(hex, 6) / 255);
      return `rgba(${hexPairToChannel(hex, 0)},${hexPairToChannel(hex, 2)},${hexPairToChannel(hex, 4)},${alpha})`;
    })
    .replace(/#([0-9a-fA-F]{4})\b/g, (match, hex) => {
      const expanded = [...hex].map((c) => c + c).join('');
      const alpha = formatAlpha(hexPairToChannel(expanded, 6) / 255);
      return `rgba(${hexPairToChannel(expanded, 0)},${hexPairToChannel(expanded, 2)},${hexPairToChannel(expanded, 4)},${alpha})`;
    });
}

/**
 * The design tokens declared in `:root`.
 *
 * Delegates to the stylesheet parser rather than matching `:root` with a regex. The regex this
 * replaced required the selector to follow `}`, `,` or the start of the file; in the assembled
 * sheet it follows a comment, so it matched nothing and every caller here silently resolved
 * against an empty map (issue #829).
 */
export function parseRootTokens(css) {
  return parseThemeTokens(css).dark;
}

// Substitutes var(--token) (including the fallback form) from the token map. Depth-limited so
// a self-referential token cannot loop; unknown tokens are left in place, which keeps the
// surrounding call unresolvable and therefore untouched.
function resolveTokens(value, tokens, depth = 0) {
  if (!tokens || tokens.size === 0 || depth > 8 || !value.includes('var(')) {
    return value;
  }
  const call = findFunctionCall(value, 'var', 0);
  if (call === null) {
    return value;
  }
  const [name, ...fallback] = splitTopLevel(value.slice(call.argsStart, call.end));
  const replacement = tokens.get(name) ?? (fallback.length > 0 ? fallback.join(',') : null);
  if (replacement === null) {
    return value;
  }
  const substituted = value.slice(0, call.start) + replacement + value.slice(call.end + 1);
  return resolveTokens(substituted, tokens, depth + 1);
}

export function evalLengthPx(value, tokens) {
  const trimmed = resolveTokens(value.trim(), tokens).trim();
  if (trimmed === '0') {
    return 0;
  }
  const match = /^(-?[\d.]+)(px|rem|em|cqmin)$/.exec(trimmed);
  if (!match) {
    return null;
  }
  const n = parseFloat(match[1]);
  switch (match[2]) {
    case 'px': return n;
    case 'rem':
    case 'em': return n * ROOT_FONT_SIZE_PX;
    case 'cqmin': return n * CQMIN_REFERENCE_PX;
    default: return null;
  }
}

function formatPx(value) {
  return `${parseFloat(value.toFixed(2))}px`;
}

function findFunctionCall(css, name, from) {
  const needle = `${name}(`;
  for (let i = css.indexOf(needle, from); i !== -1; i = css.indexOf(needle, i + 1)) {
    const before = i === 0 ? '' : css[i - 1];
    if (/[\w-]/.test(before)) {
      continue;
    }
    let depth = 0;
    for (let j = i + needle.length - 1; j < css.length; j += 1) {
      if (css[j] === '(') {
        depth += 1;
      } else if (css[j] === ')') {
        depth -= 1;
        if (depth === 0) {
          return { start: i, argsStart: i + needle.length, end: j };
        }
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
  for (const ch of args) {
    if (ch === '(') {
      depth += 1;
    } else if (ch === ')') {
      depth -= 1;
    }
    if (ch === ',' && depth === 0) {
      parts.push(current);
      current = '';
      continue;
    }
    current += ch;
  }
  parts.push(current);
  return parts.map((part) => part.trim());
}

function replaceFunctionCalls(css, name, resolve) {
  let result = css;
  let cursor = 0;
  for (;;) {
    const call = findFunctionCall(result, name, cursor);
    if (call === null) {
      return result;
    }
    const args = result.slice(call.argsStart, call.end);
    const replacement = resolve(splitTopLevel(args));
    if (replacement === null) {
      cursor = call.argsStart;
      continue;
    }
    result = result.slice(0, call.start) + replacement + result.slice(call.end + 1);
    cursor = call.start + replacement.length;
  }
}

// min()/max() (Safari 11.1) with viewport-dependent arguments cannot be resolved statically;
// the static arguments still give a usable approximation: for min() the smallest fixed length
// is the cap the author intended, for max() the largest fixed length is the floor. Calls with
// no statically resolvable argument are left unchanged (dropped by old parsers, as before).
export function downlevelMinMax(css, tokens) {
  const resolveWith = (pick) => (parts) => {
    const resolved = parts.map((part) => evalLengthPx(part, tokens)).filter((value) => value !== null);
    if (parts.length < 2 || resolved.length === 0) {
      return null;
    }
    return formatPx(pick(...resolved));
  };
  const result = replaceFunctionCalls(css, 'min', resolveWith(Math.min));
  return replaceFunctionCalls(result, 'max', resolveWith(Math.max));
}

export function downlevelClamp(css, tokens) {
  return replaceFunctionCalls(css, 'clamp', (parts) => {
    if (parts.length !== 3) {
      return null;
    }
    const [min, preferred, max] = parts.map((part) => evalLengthPx(part, tokens));
    if (min === null || max === null) {
      return null;
    }
    const resolved = preferred === null ? (min + max) / 2 : Math.min(Math.max(preferred, min), max);
    return formatPx(resolved);
  });
}

function parseColorLiteral(token) {
  const value = token.trim().toLowerCase();
  if (value === 'transparent') {
    return { r: 0, g: 0, b: 0, a: 0 };
  }
  if (value === 'white') {
    return { r: 255, g: 255, b: 255, a: 1 };
  }
  if (value === 'black') {
    return { r: 0, g: 0, b: 0, a: 1 };
  }
  const hex = /^#([0-9a-f]{3,8})$/.exec(value);
  if (hex) {
    let digits = hex[1];
    if (digits.length === 3 || digits.length === 4) {
      digits = [...digits].map((c) => c + c).join('');
    }
    if (digits.length !== 6 && digits.length !== 8) {
      return null;
    }
    return {
      r: hexPairToChannel(digits, 0),
      g: hexPairToChannel(digits, 2),
      b: hexPairToChannel(digits, 4),
      a: digits.length === 8 ? hexPairToChannel(digits, 6) / 255 : 1
    };
  }
  const rgb = /^rgba?\(([^)]+)\)$/.exec(value);
  if (rgb) {
    const channels = rgb[1].split(/[\s,/]+/).filter(Boolean).map((part) => parseFloat(part));
    if (channels.length !== 3 && channels.length !== 4) {
      return null;
    }
    if (channels.some((channel) => Number.isNaN(channel))) {
      return null;
    }
    return { r: channels[0], g: channels[1], b: channels[2], a: channels.length === 4 ? channels[3] : 1 };
  }
  return null;
}

function formatColor({ r, g, b, a }) {
  const round = (channel) => Math.round(Math.min(255, Math.max(0, channel)));
  if (a >= 1) {
    return `rgb(${round(r)},${round(g)},${round(b)})`;
  }
  if (a <= 0) {
    return 'transparent';
  }
  return `rgba(${round(r)},${round(g)},${round(b)},${formatAlpha(a)})`;
}

function parseMixComponent(part) {
  const trimmed = part.trim();
  const percentMatch = /\s+(-?[\d.]+)%$/.exec(trimmed);
  const color = percentMatch ? trimmed.slice(0, percentMatch.index).trim() : trimmed;
  const percent = percentMatch ? parseFloat(percentMatch[1]) : null;
  if (!color || (percent !== null && (Number.isNaN(percent) || percent < 0))) {
    return null;
  }
  return { color, percent };
}

// color-mix() (Safari 16.2). Two literal colors are mixed for real (sRGB, alpha-premultiplied
// per spec). When a component is dynamic (var()), the mix cannot be resolved statically, so the
// dominant component (weight >= 50%) wins outright - e.g. a 60/40 accent/border mix becomes the
// accent color, and a "12% tint over transparent" becomes transparent. That keeps every such
// declaration parseable on old engines instead of dropped, at the cost of tint precision.
export function downlevelColorMix(css) {
  return replaceFunctionCalls(css, 'color-mix', (parts) => {
    if (parts.length !== 3 || !/^in\s+srgb$/i.test(parts[0])) {
      return null;
    }
    const first = parseMixComponent(parts[1]);
    const second = parseMixComponent(parts[2]);
    if (first === null || second === null) {
      return null;
    }
    let w1 = first.percent;
    let w2 = second.percent;
    if (w1 === null && w2 === null) {
      w1 = 50;
      w2 = 50;
    } else if (w1 === null) {
      w1 = 100 - w2;
    } else if (w2 === null) {
      w2 = 100 - w1;
    }
    const total = w1 + w2;
    if (total <= 0) {
      return null;
    }
    w1 /= total;
    w2 /= total;
    const c1 = parseColorLiteral(first.color);
    const c2 = parseColorLiteral(second.color);
    if (c1 !== null && c2 !== null) {
      const alpha = c1.a * w1 + c2.a * w2;
      if (alpha === 0) {
        return 'transparent';
      }
      return formatColor({
        r: (c1.r * c1.a * w1 + c2.r * c2.a * w2) / alpha,
        g: (c1.g * c1.a * w1 + c2.g * c2.a * w2) / alpha,
        b: (c1.b * c1.a * w1 + c2.b * c2.a * w2) / alpha,
        a: alpha
      });
    }
    return w1 >= w2 ? first.color : second.color;
  });
}

// The lead delimiter (`;`/`{`/start-of-string) and the `inset` keyword can have whitespace
// between them in unminified CSS (a newline plus indentation), and whitespace around the colon.
// The lead is matched with no gap allowed to `inset` itself so `box-shadow: inset ...` (where
// `inset` follows a colon, not a delimiter) and `inset-inline: ...` (no colon right after `inset`)
// both stay untouched.
export function downlevelInset(css) {
  return css.replace(/(^|[;{])\s*inset\s*:\s*([^;}]+)/g, (match, lead, value) => {
    const parts = value.trim().split(/\s+/);
    if (parts.length < 1 || parts.length > 4) {
      return match;
    }
    const [top, right = top, bottom = top, left = right] = parts;
    return `${lead}top:${top};right:${right};bottom:${bottom};left:${left}`;
  });
}

// iOS 9 has no `object-fit`. The `ofi` polyfill convention reads this marker off
// `getComputedStyle(img).fontFamily` to find the images to patch in JS - a font-family survives
// engines that drop `object-fit` outright, where a custom property or class would not always be
// queryable the same way. The original declaration is kept so modern engines are unaffected.
// Both fits are marked, unlike the Angular original's `cover` only: this client's sheets frame
// artwork and the logo with `contain`, which an engine without object-fit stretches just as wrong.
export function downlevelObjectFit(css) {
  return css.replace(/object-fit\s*:\s*(cover|contain)\b/g,
    (match, fit) => `${match};font-family:'-md-object-fit-${fit}'`);
}

// iOS 9 has no `env()`. A safe-area inset inside a `calc()` makes the whole declaration invalid, so
// the engine drops it rather than falling back - which silently unpositioned every fixed element that
// keeps clear of the notch (the settings trigger, the setup banner, the reconnect indicator). Modern
// engines still see the real `env()`; only the legacy bundle gets the substitution.
//
// A declared fallback wins, otherwise the inset is zero: no engine that lacks `env()` has a notch,
// a home indicator or a rounded corner to keep clear of in the first place.
export function downlevelEnv(css) {
  let result = css;
  let previous;
  // Repeated because an inset can be nested inside another function's arguments.
  do {
    previous = result;
    result = result.replace(/env\(\s*[a-z-]+\s*(?:,\s*([^()]*?)\s*)?\)/g,
      (match, fallback) => (fallback === undefined || fallback === '' ? '0px' : fallback));
  } while (result !== previous);
  return result;
}

export function downlevelCss(css, tokens) {
  let result = downlevelCssColors(css);
  result = downlevelColorMix(result);
  result = downlevelMinMax(result, tokens);
  result = downlevelClamp(result, tokens);
  result = downlevelInset(result);
  result = downlevelObjectFit(result);
  result = downlevelEnv(result);
  return result.replace(/(-?[\d.]+)cqmin\b/g, (match, n) => `${parseFloat((n * CQMIN_REFERENCE_PX).toFixed(2))}px`);
}
