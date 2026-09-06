import { formatUtcOffset, isValidTimeZone, listTimeZones, timeZoneLabel, timeZoneOffsetLabel, timeZoneOffsetMinutes, timeZoneOptionLabel, timeZoneSearchTerms } from './time-zones';

const WINTER = new Date('2024-01-15T12:00:00Z');
const SUMMER = new Date('2024-07-15T12:00:00Z');

describe('time zones', () => {
  it('lists sorted zones including well-known ones', () => {
    const zones = listTimeZones();

    expect(zones.length).toBeGreaterThan(0);
    expect(zones).toContain('America/New_York');
    expect(zones).toContain('Europe/Berlin');
    expect([...zones].sort()).toEqual(zones);
  });

  it('accepts known zones and rejects unknown or empty ones', () => {
    expect(isValidTimeZone('America/New_York')).toBeTrue();
    expect(isValidTimeZone('UTC')).toBeTrue();
    expect(isValidTimeZone('Nowhere/Atlantis')).toBeFalse();
    expect(isValidTimeZone('')).toBeFalse();
  });

  it('derives a readable label from the zone id', () => {
    expect(timeZoneLabel('America/New_York')).toBe('New York');
    expect(timeZoneLabel('America/Argentina/Buenos_Aires')).toBe('Buenos Aires');
    expect(timeZoneLabel('UTC')).toBe('UTC');
    expect(timeZoneLabel('')).toBe('');
  });

  describe('timeZoneOffsetMinutes', () => {
    it('follows the zone DST rules at the given instant', () => {
      expect(timeZoneOffsetMinutes('Europe/Berlin', WINTER)).toBe(60);
      expect(timeZoneOffsetMinutes('Europe/Berlin', SUMMER)).toBe(120);
      expect(timeZoneOffsetMinutes('America/New_York', WINTER)).toBe(-300);
      expect(timeZoneOffsetMinutes('America/New_York', SUMMER)).toBe(-240);
      // Control: Tokyo has no DST, so a DST-blind implementation that always adds an hour in
      // July must not accidentally pass by "fixing" this one.
      expect(timeZoneOffsetMinutes('Asia/Tokyo', WINTER)).toBe(540);
      expect(timeZoneOffsetMinutes('Asia/Tokyo', SUMMER)).toBe(540);
      expect(timeZoneOffsetMinutes('UTC', WINTER)).toBe(0);
      expect(timeZoneOffsetMinutes('UTC', SUMMER)).toBe(0);
    });

    it('carries sub-hour offsets through as minutes', () => {
      expect(timeZoneOffsetMinutes('Asia/Kolkata', WINTER)).toBe(330);
      expect(timeZoneOffsetMinutes('America/St_Johns', WINTER)).toBe(-210);
      expect(timeZoneOffsetMinutes('America/St_Johns', SUMMER)).toBe(-150);
      expect(timeZoneOffsetMinutes('Pacific/Chatham', WINTER)).toBe(825);
      expect(timeZoneOffsetMinutes('Pacific/Chatham', SUMMER)).toBe(765);
    });

    it('returns null for an unknown or empty zone, not a wrong offset', () => {
      expect(timeZoneOffsetMinutes('Nowhere/Atlantis', WINTER)).toBeNull();
      expect(timeZoneOffsetMinutes('', WINTER)).toBeNull();
      expect(timeZoneOffsetMinutes('UTC', WINTER)).toBe(0);
    });
  });

  describe('formatUtcOffset', () => {
    it('is signed, zero-padded, and always HH:MM', () => {
      expect(formatUtcOffset(0)).toBe('UTC+00:00');
      expect(formatUtcOffset(60)).toBe('UTC+01:00');
      expect(formatUtcOffset(120)).toBe('UTC+02:00');
      expect(formatUtcOffset(-300)).toBe('UTC-05:00');
      expect(formatUtcOffset(330)).toBe('UTC+05:30');
      expect(formatUtcOffset(-210)).toBe('UTC-03:30');
      expect(formatUtcOffset(765)).toBe('UTC+12:45');
      expect(formatUtcOffset(-570)).toBe('UTC-09:30');
      expect(formatUtcOffset(840)).toBe('UTC+14:00');
    });
  });

  describe('timeZoneOffsetLabel and timeZoneOptionLabel', () => {
    it('compose city, id and the offset at the pinned instant', () => {
      expect(timeZoneOffsetLabel('Europe/Berlin', SUMMER)).toBe('UTC+02:00');
      expect(timeZoneOptionLabel('Europe/Berlin', SUMMER)).toBe('Berlin (Europe/Berlin, UTC+02:00)');
      expect(timeZoneOffsetLabel('Europe/Berlin', WINTER)).toBe('UTC+01:00');
      expect(timeZoneOptionLabel('Europe/Berlin', WINTER)).toBe('Berlin (Europe/Berlin, UTC+01:00)');

      expect(timeZoneOptionLabel('America/New_York', SUMMER))
        .toBe('New York (America/New_York, UTC-04:00)');
      expect(timeZoneOptionLabel('America/Argentina/Buenos_Aires', SUMMER))
        .toBe('Buenos Aires (America/Argentina/Buenos_Aires, UTC-03:00)');
      expect(timeZoneOptionLabel('Asia/Kolkata', SUMMER))
        .toBe('Kolkata (Asia/Kolkata, UTC+05:30)');
    });

    it('degrades to the bare city/id label when the offset is unknown, never assuming UTC', () => {
      expect(timeZoneOffsetLabel('Nowhere/Atlantis', SUMMER)).toBe('');
      expect(timeZoneOptionLabel('Nowhere/Atlantis', SUMMER)).toBe('Atlantis (Nowhere/Atlantis)');
      expect(timeZoneOptionLabel('', SUMMER)).toBe('');
    });
  });

  describe('timeZoneSearchTerms', () => {
    const matches = (id: string, at: Date, needle: string): boolean =>
      timeZoneSearchTerms(id, at).some(term => term.includes(needle));

    it('produces only lowercase tokens', () => {
      for (const term of timeZoneSearchTerms('Europe/Berlin', SUMMER)) {
        expect(term).toBe(term.toLowerCase());
      }
    });

    it('covers city, id and offset spellings for Europe/Berlin in summer', () => {
      for (const needle of [
        'berlin', 'europe/berlin', 'utc+2', 'utc+02:00', 'gmt+2', 'gmt+02:00', '+02:00', '+2', '02:00',
      ]) {
        expect(matches('Europe/Berlin', SUMMER, needle)).withContext(needle).toBeTrue();
      }
    });

    it('answers to the winter offset, and not the summer one, at a winter instant', () => {
      for (const needle of ['utc+1', 'gmt+1', '+01:00', '01:00']) {
        expect(matches('Europe/Berlin', WINTER, needle)).withContext(needle).toBeTrue();
      }
      expect(matches('Europe/Berlin', WINTER, 'utc+2')).toBeFalse();
    });

    it('covers America/New_York in summer and excludes its winter offset', () => {
      for (const needle of ['new york', 'america/new_york', 'utc-4', 'gmt-4', '-04:00', '04:00']) {
        expect(matches('America/New_York', SUMMER, needle)).withContext(needle).toBeTrue();
      }
      expect(matches('America/New_York', SUMMER, 'utc-5')).toBeFalse();
    });

    it('covers the sub-hour offset for Asia/Kolkata', () => {
      for (const needle of ['utc+5:30', 'utc+05:30', '+05:30', '05:30']) {
        expect(matches('Asia/Kolkata', SUMMER, needle)).withContext(needle).toBeTrue();
      }
    });

    it('covers the zero offset for UTC', () => {
      for (const needle of ['utc', 'utc+0', '+00:00']) {
        expect(matches('UTC', SUMMER, needle)).withContext(needle).toBeTrue();
      }
    });
  });
});
