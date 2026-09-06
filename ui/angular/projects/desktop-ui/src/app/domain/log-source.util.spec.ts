import { LogEntry, LogEntryLevel, LogEntrySource, LogSourceSummary } from '@macro-deck/runtime';
import { LOG_LEVEL_FILTERS, defaultLogLevelFilters, formatLogEntryForCopy, formatLogTime, integrationLogSources, logLevelFilterOf, logQueryOf, logTimeRangeBounds } from './log-source.util';

describe('log-source.util', () => {
  function summary(overrides: Partial<LogSourceSummary> = {}): LogSourceSummary {
    return { source: LogEntrySource.Host, ...overrides };
  }

  describe('logLevelFilterOf', () => {
    it('folds Verbose into the Debug chip, which has no chip of its own', () => {
      expect(logLevelFilterOf(LogEntryLevel.Verbose)).toBe(LogEntryLevel.Debug);
      expect(logLevelFilterOf(LogEntryLevel.Error)).toBe(LogEntryLevel.Error);
    });
  });

  describe('defaultLogLevelFilters', () => {
    it('Development starts at Debug and above', () => {
      const levels = defaultLogLevelFilters(true);

      expect(levels.size).toBe(5);
      expect(levels.has(LogEntryLevel.Debug)).toBeTrue();
      expect(levels.has(LogEntryLevel.Information)).toBeTrue();
      expect(levels.has(LogEntryLevel.Warning)).toBeTrue();
      expect(levels.has(LogEntryLevel.Error)).toBeTrue();
      expect(levels.has(LogEntryLevel.Fatal)).toBeTrue();
      expect(levels.has(LogEntryLevel.Verbose)).toBeFalse();
    });

    it('Production and Beta hide Debug and Information', () => {
      const levels = defaultLogLevelFilters(false);

      expect(levels.size).toBe(3);
      expect(levels.has(LogEntryLevel.Warning)).toBeTrue();
      expect(levels.has(LogEntryLevel.Error)).toBeTrue();
      expect(levels.has(LogEntryLevel.Fatal)).toBeTrue();
      expect(levels.has(LogEntryLevel.Debug)).toBeFalse();
      expect(levels.has(LogEntryLevel.Information)).toBeFalse();
      expect(levels.has(LogEntryLevel.Verbose)).toBeFalse();
    });

    it('the Development default asks the host for no filter at all', () => {
      const query = logQueryOf({ kind: 'all' }, defaultLogLevelFilters(true), '');

      expect(query).toEqual({ levels: undefined, search: undefined });
    });

    it('the Production default asks for Warning and above only', () => {
      const query = logQueryOf({ kind: 'all' }, defaultLogLevelFilters(false), '');

      expect(query.levels?.length).toBe(3);
      expect(query.levels).toContain(LogEntryLevel.Warning);
      expect(query.levels).toContain(LogEntryLevel.Error);
      expect(query.levels).toContain(LogEntryLevel.Fatal);
      expect(query.levels).not.toContain(LogEntryLevel.Verbose);
      expect(query.levels).not.toContain(LogEntryLevel.Debug);
      expect(query.levels).not.toContain(LogEntryLevel.Information);
    });

    it('returns a fresh set on every call', () => {
      const first = defaultLogLevelFilters(true) as Set<LogEntryLevel>;
      const second = defaultLogLevelFilters(true) as Set<LogEntryLevel>;

      expect(first).not.toBe(second);
    });
  });

  describe('logQueryOf', () => {
    const allLevels = new Set(LOG_LEVEL_FILTERS);

    it('sends no filter at all when nothing is narrowed down', () => {
      expect(logQueryOf({ kind: 'all' }, allLevels, '')).toEqual({ levels: undefined, search: undefined });
    });

    it('expands the Debug chip to cover Verbose', () => {
      const query = logQueryOf({ kind: 'all' }, new Set([LogEntryLevel.Debug]), '');

      expect(query.levels).toEqual([LogEntryLevel.Verbose, LogEntryLevel.Debug]);
    });

    it('maps each tier to the host filter', () => {
      expect(logQueryOf({ kind: 'host' }, allLevels, '').source).toBe(LogEntrySource.Host);
      expect(logQueryOf({ kind: 'bootstrapper' }, allLevels, '').source).toBe(LogEntrySource.Bootstrapper);
    });

    it('sends an integration as a source plus its id', () => {
      const query = logQueryOf({ kind: 'integration', integrationId: 'obs' }, allLevels, '');

      expect(query.source).toBe(LogEntrySource.Integration);
      expect(query.integrationId).toBe('obs');
    });

    it('trims the search term and omits it when blank', () => {
      expect(logQueryOf({ kind: 'all' }, allLevels, '  boom  ').search).toBe('boom');
      expect(logQueryOf({ kind: 'all' }, allLevels, '   ').search).toBeUndefined();
    });

    it('carries the time bounds alongside every other filter', () => {
      const bounds = { from: '2026-07-28T09:00:00.000Z', to: '2026-07-28T10:00:00.000Z' };

      const query = logQueryOf({ kind: 'integration', integrationId: 'obs' }, allLevels, 'boom', bounds);

      expect(query.from).toBe(bounds.from);
      expect(query.to).toBe(bounds.to);
      expect(query.integrationId).toBe('obs');
      expect(query.search).toBe('boom');
    });

    it('sends no bounds when none were chosen', () => {
      const query = logQueryOf({ kind: 'all' }, allLevels, '');

      expect(query.from).toBeUndefined();
      expect(query.to).toBeUndefined();
    });
  });

  describe('logTimeRangeBounds', () => {
    const now = new Date('2026-07-28T12:00:00.000Z');

    it('the whole log has no bounds at all', () => {
      expect(logTimeRangeBounds({ preset: 'all' }, now)).toEqual({});
    });

    // A `to` pinned when the range was chosen would filter the live stream to nothing seconds later.
    it('a relative window is a lower bound only', () => {
      expect(logTimeRangeBounds({ preset: 'last15m' }, now)).toEqual({ from: '2026-07-28T11:45:00.000Z' });
      expect(logTimeRangeBounds({ preset: 'last1h' }, now)).toEqual({ from: '2026-07-28T11:00:00.000Z' });
      expect(logTimeRangeBounds({ preset: 'last6h' }, now)).toEqual({ from: '2026-07-28T06:00:00.000Z' });
      expect(logTimeRangeBounds({ preset: 'last24h' }, now)).toEqual({ from: '2026-07-27T12:00:00.000Z' });
      expect(logTimeRangeBounds({ preset: 'last7d' }, now)).toEqual({ from: '2026-07-21T12:00:00.000Z' });
    });

    it('a custom range passes both chosen instants through', () => {
      const bounds = logTimeRangeBounds({
        preset: 'custom',
        customFrom: '2026-07-28T08:00:00.000Z',
        customTo: '2026-07-28T09:00:00.000Z',
      }, now);

      expect(bounds).toEqual({ from: '2026-07-28T08:00:00.000Z', to: '2026-07-28T09:00:00.000Z' });
    });

    it('a custom range with only a start stays open-ended', () => {
      const bounds = logTimeRangeBounds({
        preset: 'custom',
        customFrom: '2026-07-28T08:00:00.000Z',
        customTo: '',
      }, now);

      expect(bounds).toEqual({ from: '2026-07-28T08:00:00.000Z' });
    });

    it('drops an unparsable bound rather than sending it', () => {
      const bounds = logTimeRangeBounds({ preset: 'custom', customFrom: 'not a date' }, now);

      expect(bounds).toEqual({});
    });

    // There is no invalid state to explain to the user: an inverted pair is the same window.
    it('swaps an inverted pair', () => {
      const bounds = logTimeRangeBounds({
        preset: 'custom',
        customFrom: '2026-07-28T09:00:00.000Z',
        customTo: '2026-07-28T08:00:00.000Z',
      }, now);

      expect(bounds).toEqual({ from: '2026-07-28T08:00:00.000Z', to: '2026-07-28T09:00:00.000Z' });
    });
  });

  describe('formatLogEntryForCopy', () => {
    function entry(overrides: Partial<LogEntry> = {}): LogEntry {
      return {
        id: 'h1',
        timestamp: '2026-07-28T10:00:00+00:00',
        level: LogEntryLevel.Information,
        source: LogEntrySource.Host,
        message: 'Host started',
        ...overrides,
      };
    }

    it('renders the line the way the row reads', () => {
      expect(formatLogEntryForCopy(entry(), 'Startup'))
        .toBe('2026-07-28T10:00:00+00:00 [INF] Startup: Host started');
    });

    it('appends the exception, so a multi-line failure copies as one entry', () => {
      const text = formatLogEntryForCopy(
        entry({ level: LogEntryLevel.Error, exception: 'System.Exception: boom\n   at Foo()' }),
        'Startup');

      expect(text).toBe(
        '2026-07-28T10:00:00+00:00 [ERR] Startup: Host started\nSystem.Exception: boom\n   at Foo()');
    });
  });

  describe('integrationLogSources', () => {
    it('lists one entry per integration the host counted, sorted by display name', () => {
      const sources = [
        summary({ source: LogEntrySource.Integration, sourceId: 'spotify' }),
        summary({ source: LogEntrySource.Integration, sourceId: 'obs' }),
        summary(),
      ];

      expect(integrationLogSources(sources, id => (id === 'obs' ? 'OBS Studio' : 'Spotify'))).toEqual([
        { integrationId: 'obs', name: 'OBS Studio' },
        { integrationId: 'spotify', name: 'Spotify' },
      ]);
    });

    it('falls back to the id when the integration is unknown to the client', () => {
      const sources = [summary({ source: LogEntrySource.Integration, sourceId: 'app.third-party' })];

      expect(integrationLogSources(sources, id => id)[0].name).toBe('app.third-party');
    });
  });

  describe('formatLogTime', () => {
    it('renders the local wall-clock time of the entry', () => {
      const local = new Date(2026, 6, 28, 14, 5, 9);

      expect(formatLogTime(local.toISOString())).toBe('14:05:09');
    });

    it('passes an unparsable timestamp through unchanged', () => {
      expect(formatLogTime('not a date')).toBe('not a date');
    });
  });
});
