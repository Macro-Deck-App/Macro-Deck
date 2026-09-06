const FALLBACK_TIME_ZONES = [
  'UTC',
  'Pacific/Honolulu',
  'America/Anchorage',
  'America/Los_Angeles',
  'America/Denver',
  'America/Chicago',
  'America/New_York',
  'America/Sao_Paulo',
  'Atlantic/Reykjavik',
  'Europe/London',
  'Europe/Berlin',
  'Europe/Athens',
  'Europe/Moscow',
  'Asia/Dubai',
  'Asia/Karachi',
  'Asia/Kolkata',
  'Asia/Bangkok',
  'Asia/Shanghai',
  'Asia/Tokyo',
  'Australia/Sydney',
  'Pacific/Auckland',
];

export function listTimeZones(): string[] {
  const supported = (Intl as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf;
  if (typeof supported !== 'function') {
    return [...FALLBACK_TIME_ZONES];
  }
  try {
    return [...supported.call(Intl, 'timeZone')].sort();
  } catch {
    return [...FALLBACK_TIME_ZONES];
  }
}

export function isValidTimeZone(id: string): boolean {
  if (!id) return false;
  try {
    new Intl.DateTimeFormat(undefined, { timeZone: id });
    return true;
  } catch {
    return false;
  }
}

export function timeZoneLabel(id: string): string {
  if (!id) return '';
  const segments = id.split('/');
  return segments[segments.length - 1].replace(/_/g, ' ');
}

export function timeZoneOffsetMinutes(id: string, at: Date = new Date()): number | null {
  if (!id) return null;
  let parts: Intl.DateTimeFormatPart[];
  try {
    parts = new Intl.DateTimeFormat('en-US', {
      timeZone: id,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    }).formatToParts(at);
  } catch {
    return null;
  }
  const part = (type: string) => Number(parts.find(p => p.type === type)?.value);
  // Some engines report midnight as hour 24 on the same day rather than 0, which would read as a
  // whole day of offset.
  const hour = part('hour') % 24;
  const asUtc = Date.UTC(part('year'), part('month') - 1, part('day'), hour, part('minute'), part('second'));
  return Math.round((asUtc - at.getTime()) / 60000);
}

export function formatUtcOffset(minutes: number): string {
  const sign = minutes < 0 ? '-' : '+';
  const abs = Math.abs(minutes);
  const hh = Math.floor(abs / 60).toString().padStart(2, '0');
  const mm = (abs % 60).toString().padStart(2, '0');
  return `UTC${sign}${hh}:${mm}`;
}

export function timeZoneOffsetLabel(id: string, at?: Date): string {
  const minutes = timeZoneOffsetMinutes(id, at);
  return minutes === null ? '' : formatUtcOffset(minutes);
}

export function timeZoneOptionLabel(id: string, at?: Date): string {
  if (!id) return '';
  const offset = timeZoneOffsetLabel(id, at);
  return offset ? `${timeZoneLabel(id)} (${id}, ${offset})` : `${timeZoneLabel(id)} (${id})`;
}

function offsetSpellings(minutes: number): string[] {
  const sign = minutes < 0 ? '-' : '+';
  const abs = Math.abs(minutes);
  const hours = Math.floor(abs / 60);
  const mins = abs % 60;
  const full = `${hours.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}`;
  const short = mins === 0 ? `${hours}` : `${hours}:${mins.toString().padStart(2, '0')}`;

  const spellings = [
    `utc${sign}${full}`, `gmt${sign}${full}`, `${sign}${full}`, full,
    `utc${sign}${short}`, `gmt${sign}${short}`, `${sign}${short}`,
  ];
  if (minutes === 0) spellings.push('utc', 'gmt');
  return spellings;
}

export function timeZoneSearchTerms(id: string, at?: Date): string[] {
  if (!id) return [];
  const terms = new Set<string>();
  terms.add(id.toLowerCase());
  for (const segment of id.split('/')) {
    if (segment) terms.add(segment.toLowerCase());
  }
  const city = timeZoneLabel(id);
  if (city) terms.add(city.toLowerCase());
  const minutes = timeZoneOffsetMinutes(id, at);
  if (minutes !== null) {
    for (const spelling of offsetSpellings(minutes)) terms.add(spelling);
  }
  return [...terms];
}
