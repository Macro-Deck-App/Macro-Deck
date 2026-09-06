import {
  formatDateLong,
  formatDateOrdered,
  formatTimeRun,
  formatZoneOffset,
} from './time';

describe('pinned time formats', () => {
  // 2025-12-31, 09:05:07 UTC - single-digit hour, minute and second, so padding is visible in all three.
  const MORNING = new Date(Date.UTC(2025, 11, 31, 9, 5, 7));
  const whole = (run: { before: string; seconds: string; after: string }) =>
    `${run.before}${run.seconds}${run.after}`;

  it('draws a 12-hour face with a day period, whatever the reader writes by default', () => {
    const german = formatTimeRun(MORNING, 'UTC', false, 'de-DE', { cycle: 'h12', padded: false });

    expect(whole(german)).toContain('9:05');
    expect(whole(german)).not.toBe('09:05');
    expect(whole(german).length).toBeGreaterThan('9:05'.length);
  });

  it('leaves the 12-hour hour unpadded, and pads it only when asked', () => {
    const bare = formatTimeRun(MORNING, 'UTC', false, 'en-US', { cycle: 'h12', padded: false });
    const padded = formatTimeRun(MORNING, 'UTC', false, 'en-US', { cycle: 'h12', padded: true });

    // The separator before the day period is locale data - ADR 0064's own caveat - so what is
    // pinned here is the hour, not the punctuation around it.
    expect(whole(bare)).toMatch(/^9:05\s?AM$/);
    expect(whole(padded)).toMatch(/^09:05\s?AM$/);
  });

  it('draws a 24-hour face with no day period', () => {
    const run = formatTimeRun(MORNING, 'UTC', false, 'en-US', { cycle: 'h23', padded: true });

    expect(whole(run)).toBe('09:05');
  });

  it('drops the leading zero of a 24-hour hour even where the language pads it', () => {
    // en-GB's own short time is "09:05"; asking for the unpadded face has to override that, not
    // inherit it. An implementation that only passed `hour: 'numeric'` through returns "09:05" here.
    const run = formatTimeRun(MORNING, 'UTC', false, 'en-GB', { cycle: 'h23', padded: false });

    expect(whole(run)).toBe('9:05');
  });

  it('keeps the seconds a separate part of a pinned run, so they stay drawn smaller', () => {
    const run = formatTimeRun(MORNING, 'UTC', true, 'en-GB', { cycle: 'h23', padded: true });

    expect(run.before).toBe('09:05');
    expect(run.seconds).toBe(':07');
    expect(run.after).toBe('');
  });

  it('reads the hour in the reference zone rather than the device one', () => {
    const newYork = formatTimeRun(MORNING, 'America/New_York', false, 'en-GB',
      { cycle: 'h23', padded: true });

    expect(whole(newYork)).toBe('04:05');
  });

  it('leaves the shape to the reader when no face is pinned', () => {
    const british = formatTimeRun(MORNING, 'UTC', false, 'en-GB');
    const american = formatTimeRun(MORNING, 'UTC', false, 'en-US');

    expect(whole(british)).not.toBe(whole(american));
  });
});

describe('pinned date formats', () => {
  // The last day of the year: the one date where a wrong zone shows a different year, not just a
  // different day.
  const NEW_YEARS_EVE = new Date(Date.UTC(2025, 11, 31, 12, 0, 0));

  it('puts the day first for every reader, including one whose own date is month-first', () => {
    expect(formatDateOrdered(NEW_YEARS_EVE, 'UTC', 'en-US', 'day-first')).toBe('31/12/25');
    expect(formatDateOrdered(NEW_YEARS_EVE, 'UTC', 'de-DE', 'day-first')).toBe('31/12/25');
  });

  it('puts the month first for every reader, including one whose own date is day-first', () => {
    expect(formatDateOrdered(NEW_YEARS_EVE, 'UTC', 'de-DE', 'month-first')).toBe('12/31/25');
    expect(formatDateOrdered(NEW_YEARS_EVE, 'UTC', 'en-US', 'month-first')).toBe('12/31/25');
  });

  it('writes an ISO date with the full year', () => {
    expect(formatDateOrdered(NEW_YEARS_EVE, 'UTC', 'en-US', 'iso')).toBe('2025-12-31');
  });

  it('reads the date in the reference zone, so a deck one zone ahead is on the next day', () => {
    const lateEvening = new Date(Date.UTC(2025, 11, 31, 23, 30, 0));

    expect(formatDateOrdered(lateEvening, 'UTC', 'en-US', 'iso')).toBe('2025-12-31');
    expect(formatDateOrdered(lateEvening, 'Asia/Tokyo', 'en-US', 'iso')).toBe('2026-01-01');
  });

  it('writes the day out in the reader\'s own language', () => {
    const english = formatDateLong(NEW_YEARS_EVE, 'UTC', 'en-US');
    const german = formatDateLong(NEW_YEARS_EVE, 'UTC', 'de-DE');

    expect(english).toContain('Wednesday');
    expect(english).toContain('December');
    expect(german).toContain('Mittwoch');
    expect(german).toContain('Dezember');
  });
});

describe('zone offset', () => {
  const JANUARY = new Date(Date.UTC(2025, 0, 15, 12, 0, 0));
  const JULY = new Date(Date.UTC(2025, 6, 15, 12, 0, 0));

  it('signs a zone behind UTC negative and one ahead positive', () => {
    expect(formatZoneOffset(JANUARY, 'America/New_York')).toBe('UTC-05:00');
    expect(formatZoneOffset(JANUARY, 'Europe/Berlin')).toBe('UTC+01:00');
  });

  it('follows the instant into daylight saving rather than stating one offset per zone', () => {
    expect(formatZoneOffset(JULY, 'America/New_York')).toBe('UTC-04:00');
    expect(formatZoneOffset(JULY, 'Europe/Berlin')).toBe('UTC+02:00');
  });

  it('states a zero offset rather than leaving the sign off', () => {
    expect(formatZoneOffset(JANUARY, 'UTC')).toBe('UTC+00:00');
  });

  it('carries the minutes of a zone that is not a whole hour from UTC', () => {
    expect(formatZoneOffset(JANUARY, 'Asia/Kolkata')).toBe('UTC+05:30');
    expect(formatZoneOffset(JANUARY, 'Pacific/Marquesas')).toBe('UTC-09:30');
  });

  it('is empty when the reference names no zone, so the line disappears instead of stating the reader\'s own', () => {
    expect(formatZoneOffset(JANUARY, undefined)).toBe('');
    expect(formatZoneOffset(JANUARY, 'Not/AZone')).toBe('');
  });
});
