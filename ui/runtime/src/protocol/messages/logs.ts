export enum LogEntryLevel {
  Verbose = 'Verbose',
  Debug = 'Debug',
  Information = 'Information',
  Warning = 'Warning',
  Error = 'Error',
  Fatal = 'Fatal',
}

export enum LogEntrySource {
  Host = 'Host',
  Bootstrapper = 'Bootstrapper',
  Integration = 'Integration',
}

export interface LogEntry {
  id: string;
  timestamp: string;
  level: LogEntryLevel;
  source: LogEntrySource;
  sourceId?: string;
  category?: string;
  message: string;
  exception?: string;
}

export interface LogQuery {
  levels?: LogEntryLevel[];
  source?: LogEntrySource;
  integrationId?: string;
  category?: string;
  search?: string;
  from?: string;
  to?: string;
}

export interface GetLogsRequest extends LogQuery {
  limit?: number;
  before?: string;
}

export interface GetLogsResponse {
  entries: LogEntry[];
  olderCursor?: string;
  hasMore: boolean;
  tailAnchor: string;
}

export interface LogSourceSummary {
  source: LogEntrySource;
  sourceId?: string;
}

export interface GetLogSourcesResponse {
  sources: LogSourceSummary[];
}

export interface LogEntriesAppendedNotification {
  entries: LogEntry[];
  sources?: LogSourceSummary[];
}
