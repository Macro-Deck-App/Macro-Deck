import { hasIntlParts, pad2 } from '../ui-framework/intl-support';
import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeRaw } from '../ui-framework/node-properties.util';

export interface UiTimeRef {
  zone?: string;
}

export const UiTimeFormats = {
  Time: 'time',
  Date: 'date',
  ZoneName: 'zone-name',
  ZoneOffset: 'zone-offset',
  Time12Hour: 'time-12h',
  Time12HourPadded: 'time-12h-padded',
  Time24Hour: 'time-24h',
  Time24HourUnpadded: 'time-24h-unpadded',
  DateDayFirst: 'date-day-first',
  DateMonthFirst: 'date-month-first',
  DateIso: 'date-iso',
  DateLong: 'date-long',
} as const;

export const UI_TIME_FORMATS_WELL_KNOWN: readonly string[] = [
  UiTimeFormats.Time, UiTimeFormats.Date, UiTimeFormats.ZoneName, UiTimeFormats.ZoneOffset,
  UiTimeFormats.Time12Hour, UiTimeFormats.Time12HourPadded,
  UiTimeFormats.Time24Hour, UiTimeFormats.Time24HourUnpadded,
  UiTimeFormats.DateDayFirst, UiTimeFormats.DateMonthFirst,
  UiTimeFormats.DateIso, UiTimeFormats.DateLong,
];

export interface UiHourStyle {
  cycle: 'h12' | 'h23';
  padded: boolean;
}

export type UiDateOrder = 'day-first' | 'month-first' | 'iso';

const TIME_MARKER = '$time';

export function nodeTimeRef(node: UiNode | null | undefined, key: string): UiTimeRef | undefined {
  const raw = nodeRaw(node, key);
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;

  const marker = (raw as Record<string, unknown>)[TIME_MARKER];
  if (typeof marker !== 'object' || marker === null || Array.isArray(marker)) return undefined;

  const zone = (marker as { zone?: unknown }).zone;
  return typeof zone === 'string' && zone.length > 0 ? { zone } : {};
}

export function isSupportedZone(zone: string | undefined): boolean {
  if (zone === undefined || !hasIntlParts()) return false;
  try {
    new Intl.DateTimeFormat(undefined, { timeZone: zone });
    return true;
  } catch {
    return false;
  }
}

export function zonedClockParts(
  instant: Date,
  zone: string | undefined,
): { h: number; m: number; s: number } {
  if (!hasIntlParts()) {
    // The device's own zone, which is the only one an engine without a zone database has.
    return { h: instant.getHours(), m: instant.getMinutes(), s: instant.getSeconds() };
  }
  const parts = new Intl.DateTimeFormat('en-US', {
    hourCycle: 'h23',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    ...(isSupportedZone(zone) ? { timeZone: zone } : {}),
  }).formatToParts(instant);

  const numeric = (type: Intl.DateTimeFormatPartTypes): number =>
    Number(parts.filter(part => part.type === type)[0]?.value ?? 0);

  return { h: numeric('hour'), m: numeric('minute'), s: numeric('second') };
}

export interface UiTimeRun {
  before: string;
  seconds: string;
  after: string;
}

export function formatTimeRun(
  instant: Date,
  zone: string | undefined,
  withSeconds: boolean,
  locale: string,
  hour?: UiHourStyle,
): UiTimeRun {
  if (!hasIntlParts()) {
    // 24-hour: which of the two a language writes is locale data, and there is none here. A producer
    // that asked for a 12-hour face gets this one anyway - there is no day-period text to write it with.
    const hours = hour?.padded ? pad2(instant.getHours()) : String(instant.getHours());
    const before = `${hours}:${pad2(instant.getMinutes())}`;
    return withSeconds
      ? { before, seconds: `:${pad2(instant.getSeconds())}`, after: '' }
      : { before, seconds: '', after: '' };
  }
  const timeZone = isSupportedZone(zone) ? zone : undefined;
  const parts = repadHour(new Intl.DateTimeFormat(locale, {
    // Asked for as two digits whenever the producer pinned the face, so the padding is decided by
    // `repadHour` alone: `hour: 'numeric'` is a *request*, and a locale whose pattern uses HH answers it
    // with two digits anyway - which is exactly the leading zero this format promises to control.
    hour: hour ? '2-digit' : 'numeric',
    minute: '2-digit',
    ...(hour ? { hourCycle: hour.cycle } : {}),
    ...(withSeconds ? { second: '2-digit' } : {}),
    ...(timeZone ? { timeZone } : {}),
  }).formatToParts(instant), hour, locale);

  if (!withSeconds) {
    return { before: parts.map(part => part.value).join(''), seconds: '', after: '' };
  }

  const secondIndex = parts.findIndex(part => part.type === 'second');
  if (secondIndex < 0) {
    return { before: parts.map(part => part.value).join(''), seconds: '', after: '' };
  }

  // The literal in front of the seconds belongs to them: it is the separator this language uses, and
  // leaving it at the previous size would put a full-size colon against reduced digits.
  const start = secondIndex > 0 && parts[secondIndex - 1].type === 'literal' ? secondIndex - 1 : secondIndex;

  const join = (from: number, to: number) => parts.slice(from, to).map(part => part.value).join('');

  return {
    before: join(0, start),
    seconds: join(start, secondIndex + 1),
    after: join(secondIndex + 1, parts.length),
  };
}

function zeroDigit(locale: string): string {
  return localeNumber(0, locale, 1);
}

// A time run is repainted once a second, per clock on the deck, and each repaint asks for the digits
// again - so the formatters are kept rather than rebuilt. Keyed by what constructs them; a deck sees one
// locale and at most three widths.
const numberFormats = new Map<string, Intl.NumberFormat | null>();

function localeNumber(value: number, locale: string, digits: number): string {
  const key = `${locale}|${digits}`;
  let format = numberFormats.get(key);
  if (format === undefined) {
    try {
      format = new Intl.NumberFormat(locale, { minimumIntegerDigits: digits, useGrouping: false });
    } catch {
      format = null;
    }
    numberFormats.set(key, format);
  }

  if (format !== null) {
    return format.format(value);
  }

  let padded = String(value);
  while (padded.length < digits) padded = `0${padded}`;
  return padded;
}

function repadHour(
  parts: Intl.DateTimeFormatPart[],
  hour: UiHourStyle | undefined,
  locale: string,
): Intl.DateTimeFormatPart[] {
  if (hour === undefined) return parts;

  const zero = zeroDigit(locale);
  return parts.map(part => {
    if (part.type !== 'hour') return part;

    const digits = Array.from(part.value);
    if (hour.padded) {
      return digits.length > 1 ? part : { ...part, value: zero + part.value };
    }
    return digits.length > 1 && digits[0] === zero
      ? { ...part, value: digits.slice(1).join('') }
      : part;
  });
}

function zonedDateParts(
  instant: Date,
  zone: string | undefined,
): { year: number; month: number; day: number } {
  const parts = new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    ...(isSupportedZone(zone) ? { timeZone: zone } : {}),
  }).formatToParts(instant);

  const numeric = (type: Intl.DateTimeFormatPartTypes): number =>
    Number(parts.filter(part => part.type === type)[0]?.value ?? 0);

  return { year: numeric('year'), month: numeric('month'), day: numeric('day') };
}

export function formatDateOrdered(
  instant: Date,
  zone: string | undefined,
  locale: string,
  order: UiDateOrder,
): string {
  if (!hasIntlParts()) {
    const y = instant.getFullYear();
    const m = pad2(instant.getMonth() + 1);
    const d = pad2(instant.getDate());
    if (order === 'iso') return `${y}-${m}-${d}`;
    return order === 'day-first' ? `${d}/${m}/${pad2(y % 100)}` : `${m}/${d}/${pad2(y % 100)}`;
  }

  const { year, month, day } = zonedDateParts(instant, zone);
  const two = (value: number) => localeNumber(value, locale, 2);

  if (order === 'iso') return `${localeNumber(year, locale, 4)}-${two(month)}-${two(day)}`;

  const shortYear = two(year % 100);
  return order === 'day-first'
    ? `${two(day)}/${two(month)}/${shortYear}`
    : `${two(month)}/${two(day)}/${shortYear}`;
}

export function formatDateLong(instant: Date, zone: string | undefined, locale: string): string {
  // No locale data means no names to write it with, so this degrades to the same numeric date `date`
  // does rather than inventing an English weekday.
  if (!hasIntlParts()) return formatDate(instant, zone, locale);

  const timeZone = isSupportedZone(zone) ? zone : undefined;
  return new Intl.DateTimeFormat(locale, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    ...(timeZone ? { timeZone } : {}),
  }).format(instant);
}

export function formatZoneOffset(instant: Date, zone: string | undefined): string {
  if (!isSupportedZone(zone)) return '';

  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: zone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(instant);

  const numeric = (type: Intl.DateTimeFormatPartTypes): number =>
    Number(parts.filter(part => part.type === type)[0]?.value ?? 0);

  const asUtc = Date.UTC(numeric('year'), numeric('month') - 1, numeric('day'),
    numeric('hour'), numeric('minute'), numeric('second'));
  const minutes = Math.round((asUtc - instant.getTime()) / 60000);
  const sign = minutes < 0 ? '-' : '+';
  const magnitude = Math.abs(minutes);

  return `UTC${sign}${pad2(Math.floor(magnitude / 60))}:${pad2(magnitude % 60)}`;
}

export function formatDate(instant: Date, zone: string | undefined, locale: string): string {
  if (!hasIntlParts()) {
    // Numeric and word-free: a weekday or a month name would have to be invented in one language.
    return `${instant.getFullYear()}-${pad2(instant.getMonth() + 1)}-${pad2(instant.getDate())}`;
  }
  const timeZone = isSupportedZone(zone) ? zone : undefined;
  return new Intl.DateTimeFormat(locale, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    ...(timeZone ? { timeZone } : {}),
  }).format(instant);
}

export function formatZoneName(zone: string | undefined): string {
  if (!isSupportedZone(zone)) return '';

  const segments = zone!.split('/');
  return segments[segments.length - 1].replace(/_/g, ' ');
}
