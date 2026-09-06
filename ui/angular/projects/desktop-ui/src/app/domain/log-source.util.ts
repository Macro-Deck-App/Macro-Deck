import { LogEntry, LogEntryLevel, LogEntrySource, LogQuery, LogSourceSummary } from '@macro-deck/runtime';

export type LogSourceFilter =
  | { kind: 'all' }
  | { kind: 'host' }
  | { kind: 'bootstrapper' }
  | { kind: 'integration'; integrationId: string };

export const LOG_LEVEL_FILTERS: readonly LogEntryLevel[] = [
  LogEntryLevel.Debug,
  LogEntryLevel.Information,
  LogEntryLevel.Warning,
  LogEntryLevel.Error,
  LogEntryLevel.Fatal,
];

export function logLevelFilterOf(level: LogEntryLevel): LogEntryLevel {
  return level === LogEntryLevel.Verbose ? LogEntryLevel.Debug : level;
}

export function defaultLogLevelFilters(isDevelopmentBuild: boolean): ReadonlySet<LogEntryLevel> {
  return isDevelopmentBuild
    ? new Set(LOG_LEVEL_FILTERS)
    : new Set([LogEntryLevel.Warning, LogEntryLevel.Error, LogEntryLevel.Fatal]);
}

export type LogTimeRangePreset =
  | 'all'
  | 'last15m'
  | 'last1h'
  | 'last6h'
  | 'last24h'
  | 'last7d'
  | 'custom';

export const LOG_TIME_RANGE_PRESETS: readonly LogTimeRangePreset[] = [
  'all',
  'last15m',
  'last1h',
  'last6h',
  'last24h',
  'last7d',
  'custom',
];

export interface LogTimeRange {
  preset: LogTimeRangePreset;
  customFrom?: string;
  customTo?: string;
}

export interface LogTimeBounds {
  from?: string;
  to?: string;
}

export const DEFAULT_LOG_TIME_RANGE: LogTimeRange = { preset: 'last1h' };

const RELATIVE_WINDOW_MS: Partial<Record<LogTimeRangePreset, number>> = {
  last15m: 15 * 60_000,
  last1h: 60 * 60_000,
  last6h: 6 * 60 * 60_000,
  last24h: 24 * 60 * 60_000,
  last7d: 7 * 24 * 60 * 60_000,
};

export function logTimeRangeBounds(range: LogTimeRange, now: Date = new Date()): LogTimeBounds {
  if (range.preset === 'custom') {
    const from = validInstant(range.customFrom);
    const to = validInstant(range.customTo);
    if (from && to && Date.parse(from) > Date.parse(to)) {
      return { from: to, to: from };
    }

    return { ...(from ? { from } : {}), ...(to ? { to } : {}) };
  }

  const window = RELATIVE_WINDOW_MS[range.preset];

  return window === undefined ? {} : { from: new Date(now.getTime() - window).toISOString() };
}

function validInstant(value: string | undefined): string | undefined {
  if (!value?.trim()) {
    return undefined;
  }

  return Number.isNaN(Date.parse(value)) ? undefined : value;
}

export function logQueryOf(
  source: LogSourceFilter,
  enabledLevels: ReadonlySet<LogEntryLevel>,
  search: string,
  bounds: LogTimeBounds = {},
): LogQuery {
  const levels = [...enabledLevels].flatMap(level =>
    level === LogEntryLevel.Debug ? [LogEntryLevel.Verbose, LogEntryLevel.Debug] : [level]);

  const query: LogQuery = {
    levels: levels.length === LOG_LEVEL_FILTERS.length + 1 ? undefined : levels,
    search: search.trim() || undefined,
    ...bounds,
  };

  switch (source.kind) {
    case 'all':
      return query;
    case 'host':
      return { ...query, source: LogEntrySource.Host };
    case 'bootstrapper':
      return { ...query, source: LogEntrySource.Bootstrapper };
    case 'integration':
      return { ...query, source: LogEntrySource.Integration, integrationId: source.integrationId };
  }
}

export interface IntegrationLogSource {
  integrationId: string;
  name: string;
}

export function integrationLogSources(
  sources: readonly LogSourceSummary[],
  integrationName: (integrationId: string) => string,
): IntegrationLogSource[] {
  return sources
    .filter(summary => summary.source === LogEntrySource.Integration && summary.sourceId)
    .map(summary => ({
      integrationId: summary.sourceId!,
      name: integrationName(summary.sourceId!),
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

export function logLevelCode(level: LogEntryLevel): string {
  switch (level) {
    case LogEntryLevel.Verbose:
      return 'VRB';
    case LogEntryLevel.Debug:
      return 'DBG';
    case LogEntryLevel.Information:
      return 'INF';
    case LogEntryLevel.Warning:
      return 'WRN';
    case LogEntryLevel.Error:
      return 'ERR';
    case LogEntryLevel.Fatal:
      return 'FTL';
  }
}

export function formatLogEntryForCopy(entry: LogEntry, origin: string): string {
  const line = `${entry.timestamp} [${logLevelCode(entry.level)}] ${origin}: ${entry.message}`;

  return entry.exception ? `${line}\n${entry.exception}` : line;
}

export function formatLogTime(timestamp: string): string {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) {
    return timestamp;
  }

  const pad = (value: number): string => value.toString().padStart(2, '0');

  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}
