import { setIntlSupportForTesting } from './intl-support';
import { formatDuration } from '../macrodeck-components/progress';
import { formatDate, formatTimeRun, isSupportedZone, zonedClockParts } from '../macrodeck-components/time';

describe('formatting without Intl', () => {
  const instant = new Date(2026, 2, 4, 9, 7, 5);

  afterEach(() => setIntlSupportForTesting(null));

  it('reads the clock hands off the device instead of throwing', () => {
    setIntlSupportForTesting(false);

    expect(zonedClockParts(instant, 'America/New_York')).toEqual({ h: 9, m: 7, s: 5 });
  });

  it('claims no time zone it cannot resolve', () => {
    // The zone database is what Intl is. Saying a zone is supported here would have the clock
    // report another city's time while showing the device's.
    setIntlSupportForTesting(false);

    expect(isSupportedZone('America/New_York')).toBeFalse();
  });

  it('formats a time with padded minutes and seconds', () => {
    setIntlSupportForTesting(false);

    expect(formatTimeRun(instant, undefined, true, 'en-US')).toEqual({
      before: '9:07', seconds: ':05', after: '',
    });
  });

  it('leaves the seconds out when they were not asked for', () => {
    setIntlSupportForTesting(false);

    expect(formatTimeRun(instant, undefined, false, 'en-US')).toEqual({
      before: '9:07', seconds: '', after: '',
    });
  });

  it('formats a date in digits, which every language reads the same way', () => {
    // A weekday or a month name would have to be invented in one language and shown to readers of
    // the six others the app ships.
    setIntlSupportForTesting(false);

    expect(formatDate(instant, undefined, 'de-DE')).toBe('2026-03-04');
  });

  it('still pads a duration, which toLocaleString stops doing without Intl', () => {
    // `Number.prototype.toLocaleString` exists on these engines but ignores its options, so the
    // padding silently vanished and a track position read "3:5".
    setIntlSupportForTesting(false);

    expect(formatDuration(185_000, 'en-US')).toBe('3:05');
  });

  it('formats the same duration through Intl where there is one', () => {
    setIntlSupportForTesting(true);

    expect(formatDuration(185_000, 'en-US')).toBe('3:05');
  });
});
