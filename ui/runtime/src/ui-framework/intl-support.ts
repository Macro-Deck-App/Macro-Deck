// What the time and duration formatters do on an engine with no Intl (issue #829): the floor reaches
// iOS 9 / Safari 9, which have no Intl at all, and formatToParts only arrives in Safari 13 - so
// the formatters threw mid-render and the deck came up with no widgets rather than an unformatted one.
// The fallbacks produce no words on purpose: there is no locale data to render a weekday from, and an
// invented English one would put untranslated text in front of a reader whose deck is in Czech.
export function hasIntlParts(): boolean {
  if (cached === null) cached = detect();
  return cached;
}

let cached: boolean | null = null;

export function setIntlSupportForTesting(value: boolean | null): void {
  cached = value;
}

function detect(): boolean {
  try {
    const constructor = (globalThis as { Intl?: { DateTimeFormat?: unknown } }).Intl?.DateTimeFormat;
    if (typeof constructor !== 'function') return false;
    // Present since iOS 10 but only useful from Safari 13; the formatters need the parts, not the
    // string, so an Intl without them is no better than none.
    return typeof new Intl.DateTimeFormat().formatToParts === 'function';
  } catch {
    return false;
  }
}

export function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}
