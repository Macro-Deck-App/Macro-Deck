import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { GetLogSourcesResponse, GetLogsResponse, LogEntriesAppendedNotification, LogEntry, LogEntryLevel, LogEntrySource } from '@macro-deck/runtime';
import { ApiService, ConnectionState } from '@shared';
import { LogService } from './log.service';

describe('LogService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let connectionState: BehaviorSubject<ConnectionState>;
  let appended: Subject<LogEntriesAppendedNotification>;
  let service: LogService;

  function entry(id: string, overrides: Partial<LogEntry> = {}): LogEntry {
    return {
      id,
      timestamp: '2026-07-28T10:00:00+00:00',
      level: LogEntryLevel.Information,
      source: LogEntrySource.Host,
      message: id,
      ...overrides,
    };
  }

  function page(entries: LogEntry[], overrides: Partial<GetLogsResponse> = {}): GetLogsResponse {
    return { entries, hasMore: false, tailAnchor: 'anchor', ...overrides };
  }

  async function flush(): Promise<void> {
    for (let i = 0; i < 12; i++) {
      await Promise.resolve();
    }
  }

  beforeEach(() => {
    connectionState = new BehaviorSubject<ConnectionState>('connected');
    appended = new Subject<LogEntriesAppendedNotification>();
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      ['onNotification', 'getLogs', 'getLogSources', 'invoke'],
      {
        connectionState$: connectionState.asObservable(),
        connectionStateSignal: signal<ConnectionState>('connected').asReadonly(),
      },
    );
    apiSpy.onNotification.and.callFake(() => appended.asObservable() as Observable<never>);
    apiSpy.getLogs.and.resolveTo(page([]));
    apiSpy.getLogSources.and.resolveTo({ sources: [] } as GetLogSourcesResponse);
    apiSpy.invoke.and.resolveTo(undefined);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(LogService);
  });

  it('reads a page and then applies pushed entries', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h1'), entry('h2')]));

    service.watch();
    await service.setQuery({});
    appended.next({ entries: [entry('h3')] });

    expect(service.entries().map(e => e.id)).toEqual(['h1', 'h2', 'h3']);
  });

  it('subscribes with the page tail anchor so the stream resumes where the page ended', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h1')], { tailAnchor: '1|h20260806:512|b' }));

    service.watch();
    await service.setQuery({ search: 'boom' });
    await flush();

    expect(apiSpy.invoke).toHaveBeenCalledWith('SubscribeLogs', { search: 'boom' }, '1|h20260806:512|b');
  });

  it('drops an entry a page and a push both delivered', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h1'), entry('h2')]));

    service.watch();
    await service.setQuery({});
    appended.next({ entries: [entry('h2'), entry('h3')] });

    expect(service.entries().map(e => e.id)).toEqual(['h1', 'h2', 'h3']);
  });

  // The regression this guards: dedup by an ordering comparison would drop every bootstrapper
  // entry, because its file is read after the host's and its lines can predate what is shown.
  it('keeps a pushed entry that is older than the newest one already held', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h9', { timestamp: '2026-07-28T10:00:09+00:00' })]));

    service.watch();
    await service.setQuery({});
    appended.next({
      entries: [entry('b1', { timestamp: '2026-07-28T10:00:05+00:00', source: LogEntrySource.Bootstrapper })],
    });

    expect(service.entries().map(e => e.id)).toContain('b1');
  });

  it('loadOlder prepends the previous page and advances the cursor', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h3')], { hasMore: true, olderCursor: 'cursor-1' }));

    service.watch();
    await service.setQuery({});

    apiSpy.getLogs.and.resolveTo(page([entry('h1'), entry('h2')], { hasMore: false }));
    await service.loadOlder();

    expect(apiSpy.getLogs).toHaveBeenCalledWith(jasmine.objectContaining({ before: 'cursor-1' }));
    expect(service.entries().map(e => e.id)).toEqual(['h1', 'h2', 'h3']);
    expect(service.hasMore()).toBeFalse();
  });

  it('does not ask for older entries when there are none', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h1')], { hasMore: false }));
    service.watch();
    await service.setQuery({});
    apiSpy.getLogs.calls.reset();

    await service.loadOlder();

    expect(apiSpy.getLogs).not.toHaveBeenCalled();
  });

  it('unsubscribes before re-querying, so no entry from the old filter lands in the new window', async () => {
    service.watch();
    await service.setQuery({});
    apiSpy.invoke.calls.reset();

    await service.setQuery({ levels: [LogEntryLevel.Error] });
    await flush();

    expect(apiSpy.invoke.calls.first().args[0]).toBe('UnsubscribeLogs');
  });

  it('discards a response the user has already filtered past', async () => {
    service.watch();
    let resolveFirst: (value: GetLogsResponse) => void = () => undefined;
    apiSpy.getLogs.and.returnValue(new Promise<GetLogsResponse>(resolve => (resolveFirst = resolve)));

    const stale = service.setQuery({ search: 'old' });
    apiSpy.getLogs.and.resolveTo(page([entry('fresh')]));
    await service.setQuery({ search: 'new' });

    resolveFirst(page([entry('stale')]));
    await stale;

    expect(service.entries().map(e => e.id)).toEqual(['fresh']);
  });

  it('releases every entry and the subscription when the last watcher leaves', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h1')]));
    service.watch();
    service.watch();
    await service.setQuery({});

    service.unwatch();
    expect(service.entries().length).toBe(1, 'a second watcher still has it open');

    service.unwatch();
    expect(apiSpy.invoke).toHaveBeenCalledWith('UnsubscribeLogs');
    expect(service.entries()).toEqual([]);
    expect(service.hasLoaded()).toBeFalse();
  });

  it('rebuilds from the files after a reconnect', async () => {
    service.watch();
    await flush();
    apiSpy.getLogs.calls.reset();

    connectionState.next('disconnected');
    connectionState.next('connected');
    await flush();

    expect(apiSpy.getLogs).toHaveBeenCalled();
  });

  it('does not re-query on a reconnect once nobody watches anymore', async () => {
    service.watch();
    await flush();
    service.unwatch();
    apiSpy.getLogs.calls.reset();

    connectionState.next('connected');
    await flush();

    expect(apiSpy.getLogs).not.toHaveBeenCalled();
  });

  // The host reads an absent level list as "all levels", so an empty one cannot be sent as a filter.
  it('shows nothing, and asks the host nothing, when no level is selected', async () => {
    service.watch();
    await flush();
    apiSpy.getLogs.calls.reset();

    await service.setQuery({ levels: [] });

    expect(apiSpy.getLogs).not.toHaveBeenCalled();
    expect(service.entries()).toEqual([]);
  });

  // The rail must not need a refetch to notice an integration that starts logging mid-session.
  it('joins a source the live stream reveals to the rail, without duplicating a known one', async () => {
    apiSpy.getLogSources.and.resolveTo({ sources: [{ source: LogEntrySource.Host }] });
    service.watch();
    await service.setQuery({});
    await flush();

    appended.next({
      entries: [entry('h1')],
      sources: [
        { source: LogEntrySource.Host },
        { source: LogEntrySource.Integration, sourceId: 'spotify' },
      ],
    });

    expect(service.sources()).toEqual([
      { source: LogEntrySource.Host },
      { source: LogEntrySource.Integration, sourceId: 'spotify' },
    ]);
  });

  it('reads the sources once rather than on every filter change', async () => {
    apiSpy.getLogSources.and.resolveTo({ sources: [{ source: LogEntrySource.Host }] });
    service.watch();
    await service.setQuery({});
    await flush();
    apiSpy.getLogSources.calls.reset();

    await service.setQuery({ search: 'boom' });
    await flush();

    expect(apiSpy.getLogSources).not.toHaveBeenCalled();
  });

  it('releases the oldest page once the window is full, keeping older entries reachable', async () => {
    const first = Array.from({ length: LogService.WINDOW_LIMIT }, (_, i) => entry(`h${i}`));
    apiSpy.getLogs.and.resolveTo(page(first, { hasMore: true, olderCursor: 'cursor-0' }));

    service.watch();
    await service.setQuery({});

    apiSpy.getLogs.and.resolveTo(page([entry('older')], { hasMore: true, olderCursor: 'cursor-1' }));
    await service.loadOlder();
    appended.next({ entries: [entry('live', { timestamp: '2026-07-28T11:00:00+00:00' })] });

    expect(service.entries().length).toBeLessThanOrEqual(LogService.WINDOW_LIMIT);
    expect(service.entries().map(e => e.id)).not.toContain('older', 'the oldest page was released');
    expect(service.hasMore()).toBeTrue();
  });

  // The acceptance criterion behind the whole explorer: however long the history is, the browser
  // holds a bounded window, and it is the oldest end that goes.
  it('never holds more than the window limit, dropping the oldest entries', async () => {
    const first = Array.from({ length: 200 }, (_, i) => entry(`p${i}`));
    apiSpy.getLogs.and.resolveTo(page(first, { hasMore: true, olderCursor: 'cursor-0' }));

    service.watch();
    await service.setQuery({});

    for (let batch = 0; batch < 10; batch++) {
      appended.next({
        entries: Array.from({ length: 200 }, (_, i) => entry(`s${batch * 200 + i}`)),
      });
    }

    const ids = service.entries().map(e => e.id);
    expect(ids.length).toBe(LogService.WINDOW_LIMIT);
    expect(ids[0]).toBe('s0');
    expect(ids[LogService.WINDOW_LIMIT - 1]).toBe('s1999');
    expect(ids).not.toContain('p0');
  });

  // An implementation that asked for the whole history and sliced it in the browser would fail here.
  it('asks the host for one page at a time', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h1')], { hasMore: true, olderCursor: 'cursor-1' }));

    service.watch();
    await flush();
    apiSpy.getLogs.calls.reset();
    await service.setQuery({});

    expect(apiSpy.getLogs).toHaveBeenCalledTimes(1);
    expect(apiSpy.getLogs.calls.mostRecent().args[0]?.limit).toBe(200);
    expect(apiSpy.getLogs.calls.mostRecent().args[0]?.before).toBeUndefined();

    await service.loadOlder();

    expect(apiSpy.getLogs.calls.mostRecent().args[0]?.limit).toBe(200);
    expect(apiSpy.getLogs.calls.mostRecent().args[0]?.before).toBe('cursor-1');
  });

  it('prepends an older page without disturbing the entries already held', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h5'), entry('h6')], { hasMore: true, olderCursor: 'cursor-1' }));

    service.watch();
    await service.setQuery({});
    const held = service.entries();

    apiSpy.getLogs.and.resolveTo(page([entry('h1'), entry('h2')], { hasMore: false }));
    await service.loadOlder();

    expect(service.entries().map(e => e.id)).toEqual(['h1', 'h2', 'h5', 'h6']);
    expect(service.entries()[2]).toBe(held[0]);
    expect(service.entries()[3]).toBe(held[1]);
  });

  it('does not stack a second older-page request while one is in flight', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h5')], { hasMore: true, olderCursor: 'cursor-1' }));
    service.watch();
    await service.setQuery({});
    apiSpy.getLogs.calls.reset();

    let resolveOlder: (value: GetLogsResponse) => void = () => undefined;
    apiSpy.getLogs.and.returnValue(new Promise<GetLogsResponse>(resolve => (resolveOlder = resolve)));

    const first = service.loadOlder();
    const second = service.loadOlder();
    resolveOlder(page([entry('h1')], { hasMore: false }));
    await Promise.all([first, second]);

    expect(apiSpy.getLogs).toHaveBeenCalledTimes(1);
  });

  it('reset drops the window, so the next query starts from the newest page again', async () => {
    apiSpy.getLogs.and.resolveTo(page([entry('h1')], { hasMore: true, olderCursor: 'cursor-1' }));
    service.watch();
    await service.setQuery({});

    service.reset();

    expect(service.entries()).toEqual([]);
    expect(service.hasMore()).toBeFalse();
    expect(service.hasLoaded()).toBeFalse();

    apiSpy.getLogs.calls.reset();
    await service.setQuery({});

    expect(apiSpy.getLogs.calls.mostRecent().args[0]?.before).toBeUndefined();
  });

  it('settles into loaded-but-empty and still subscribes where the page ends', async () => {
    apiSpy.getLogs.and.resolveTo(page([], { tailAnchor: 'anchor-empty' }));

    service.watch();
    await service.setQuery({});
    await flush();

    expect(service.hasLoaded()).toBeTrue();
    expect(service.isLoading()).toBeFalse();
    expect(service.entries()).toEqual([]);
    expect(apiSpy.invoke).toHaveBeenCalledWith('SubscribeLogs', {}, 'anchor-empty');
  });

  it('sends the time bounds to the page read, the live stream and every older page', async () => {
    const bounds = { from: '2026-07-28T09:00:00.000Z', to: '2026-07-28T11:00:00.000Z' };
    apiSpy.getLogs.and.resolveTo(page([entry('h5')], { hasMore: true, olderCursor: 'cursor-1' }));

    service.watch();
    await service.setQuery(bounds);
    await flush();

    expect(apiSpy.getLogs.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining(bounds));
    expect(apiSpy.invoke).toHaveBeenCalledWith('SubscribeLogs', bounds, 'anchor');

    await service.loadOlder();

    expect(apiSpy.getLogs.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining(bounds));
  });
});
