import { provideZonelessChangeDetection, signal } from '@angular/core';
import { CdkVirtualScrollViewport } from '@angular/cdk/scrolling';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import type {
  GetLogsResponse,
  IpcIntegration,
  LogEntriesAppendedNotification,
  LogEntry,
} from '@macro-deck/runtime';
import type {
  ConnectionState,
  LocalizationKey,
} from '@shared';
import { LogEntryLevel, LogEntrySource } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { TextClipboardService } from '../../../../services/text-clipboard.service';
import { NavigationService } from '../../../../services/navigation.service';
import { LogsTabComponent } from './logs-tab.component';

const HOUR_MS = 60 * 60 * 1000;

function entry(id: string, overrides: Partial<LogEntry> = {}): LogEntry {
  return {
    id,
    timestamp: '2026-07-28T10:00:00+00:00',
    level: LogEntryLevel.Information,
    source: LogEntrySource.Host,
    message: 'Host started',
    ...overrides,
  };
}

function page(entries: LogEntry[], overrides: Partial<GetLogsResponse> = {}): GetLogsResponse {
  return { entries, hasMore: false, tailAnchor: 'anchor', ...overrides };
}

function ipcIntegration(id: string, name: string): IpcIntegration {
  return {
    id,
    name,
    version: '1.0.0',
    isInternal: true,
    enabled: true,
    actionCount: 0,
    variableCount: 0,
    supportsConfigFlow: false,
    allowsMultipleConfigurations: false,
    configuredEntryCount: 0,
    hasIcon: false,
    issueCount: 0,
  };
}

describe('LogsTabComponent', () => {
  let fixture: ComponentFixture<LogsTabComponent>;
  let component: LogsTabComponent;
  let apiSpy: jasmine.SpyObj<ApiService>;
  let appended: Subject<LogEntriesAppendedNotification>;
  let entries: LogEntry[];

  const exceptionText = 'System.Exception: boom\n   at Spotify.Refresh()';

  beforeEach(async () => {
    entries = [
      entry('h1', { category: 'Startup' }),
      entry('b1', { source: LogEntrySource.Bootstrapper, message: 'Host answered on port 51234' }),
      entry('i1', {
        source: LogEntrySource.Integration,
        sourceId: 'obs',
        level: LogEntryLevel.Warning,
        category: 'ObsConnection',
        message: 'Reconnecting to OBS',
      }),
      entry('i2', {
        source: LogEntrySource.Integration,
        sourceId: 'spotify',
        level: LogEntryLevel.Error,
        message: 'Token refresh failed',
        exception: exceptionText,
      }),
      entry('h2', { level: LogEntryLevel.Debug, message: 'Cache warmed' }),
    ];

    appended = new Subject<LogEntriesAppendedNotification>();
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      ['getLogs', 'getLogSources', 'getIntegrations', 'onNotification', 'invoke', 'getNotifications'],
      {
        connectionState$: new BehaviorSubject<ConnectionState>('connected').asObservable(),
        connectionStateSignal: signal<ConnectionState>('connected').asReadonly(),
      },
    );
    apiSpy.getNotifications.and.resolveTo({ notifications: [] });
    apiSpy.getLogs.and.resolveTo(page(entries));
    apiSpy.getLogSources.and.resolveTo({
      sources: [
        { source: LogEntrySource.Host },
        { source: LogEntrySource.Bootstrapper },
        { source: LogEntrySource.Integration, sourceId: 'obs' },
        { source: LogEntrySource.Integration, sourceId: 'spotify' },
      ],
    });
    apiSpy.getIntegrations.and.resolveTo({
      integrations: [ipcIntegration('obs', 'OBS Studio'), ipcIntegration('spotify', 'Spotify')],
    });
    apiSpy.onNotification.and.callFake((name: string) =>
      (name === 'LogEntriesAppendedNotification' ? appended : new Subject<never>())
        .asObservable() as unknown as Observable<never>);
    apiSpy.invoke.and.resolveTo(undefined);

    TestBed.configureTestingModule({
      imports: [LogsTabComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(LogsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await settle();
  });

  async function settle(target: ComponentFixture<LogsTabComponent> = fixture): Promise<void> {
    for (let i = 0; i < 12; i++) {
      await Promise.resolve();
    }
    await target.whenStable();
  }

  async function settleDebounce(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve, 300));
    await settle();
  }

  function text(key: string): string {
    return TestBed.inject(LocalizationService).translateKey(key as LocalizationKey);
  }

  function lastQuery() {
    return apiSpy.getLogs.calls.mostRecent().args[0]!;
  }

  function viewport(): CdkVirtualScrollViewport {
    return (component as unknown as { viewport: () => CdkVirtualScrollViewport }).viewport()!;
  }

  function layOutViewport(heightPx = 400): void {
    const element = (fixture.nativeElement as HTMLElement)
      .querySelector('cdk-virtual-scroll-viewport') as HTMLElement;
    element.style.height = `${heightPx}px`;
    viewport().checkViewportSize();
    fixture.detectChanges();
  }

  function renderedMessages(): string[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('.log-line')]
      .map(line => line.querySelector('.log-message')?.textContent?.trim() ?? '');
  }

  it('subscribes to the live stream while the page is open and unsubscribes on destroy', () => {
    expect(apiSpy.invoke).toHaveBeenCalledWith('SubscribeLogs', jasmine.anything(), 'anchor');

    fixture.destroy();

    expect(apiSpy.invoke).toHaveBeenCalledWith('UnsubscribeLogs');
  });

  // The host's per-source totals were never trustworthy, so the rail is identity only now.
  it('lists the sources without any count', () => {
    const rail = (fixture.nativeElement as HTMLElement).querySelector('.source-list') as HTMLElement;

    expect(rail.textContent).toContain('OBS Studio');
    expect(rail.textContent).toContain('Spotify');
    expect(rail.querySelectorAll('.rail-item-count').length).toBe(0);
    expect(component.integrationSources()).toEqual([
      { integrationId: 'obs', name: 'OBS Studio' },
      { integrationId: 'spotify', name: 'Spotify' },
    ]);
  });

  it('shows every loaded entry, since the host already filtered them', () => {
    expect(component.visible().map(e => e.id)).toEqual(['h1', 'b1', 'i1', 'i2', 'h2']);
  });

  // Virtualization means only a window of rows exists in the DOM; whatever is there must be a
  // contiguous run of what is loaded, in order.
  it('renders a contiguous window of the loaded entries', () => {
    layOutViewport();
    const rendered = renderedMessages();
    const all = component.visible().map(e => e.message);

    expect(rendered.length).toBeGreaterThan(0);
    expect(rendered.length).toBeLessThanOrEqual(all.length);
    expect(all.join('|')).toContain(rendered.join('|'));
  });

  it('sends the selected source to the host as a query', async () => {
    apiSpy.getLogs.calls.reset();

    component.selectSource({ kind: 'bootstrapper' });
    await settleDebounce();

    expect(apiSpy.getLogs).toHaveBeenCalledWith(
      jasmine.objectContaining({ source: LogEntrySource.Bootstrapper }));
    expect(component.detailTitle()).toBe('Bootstrapper');
  });

  it('sends a selected integration as its id', async () => {
    apiSpy.getLogs.calls.reset();

    component.selectSource({ kind: 'integration', integrationId: 'spotify' });
    await settleDebounce();

    expect(apiSpy.getLogs).toHaveBeenCalledWith(jasmine.objectContaining({ integrationId: 'spotify' }));
    expect(component.detailTitle()).toBe('Spotify');
  });

  it('a Production build opens with Debug and Information hidden', async () => {
    expect(component.isLevelEnabled(LogEntryLevel.Warning)).toBeTrue();
    expect(component.isLevelEnabled(LogEntryLevel.Error)).toBeTrue();
    expect(component.isLevelEnabled(LogEntryLevel.Fatal)).toBeTrue();
    expect(component.isLevelEnabled(LogEntryLevel.Debug)).toBeFalse();
    expect(component.isLevelEnabled(LogEntryLevel.Information)).toBeFalse();

    const query = lastQuery();
    expect(query.levels?.length).toBe(3);
    expect(query.levels).not.toContain(LogEntryLevel.Debug);
    expect(query.levels).not.toContain(LogEntryLevel.Verbose);
    expect(query.levels).not.toContain(LogEntryLevel.Information);
  });

  it('a Beta build behaves like Production', async () => {
    TestBed.inject(NavigationService).setAppVersion('3.0.0-beta.1', true, false);
    const betaFixture = TestBed.createComponent(LogsTabComponent);
    betaFixture.detectChanges();
    await settle(betaFixture);

    const betaComponent = betaFixture.componentInstance;
    expect(betaComponent.isLevelEnabled(LogEntryLevel.Warning)).toBeTrue();
    expect(betaComponent.isLevelEnabled(LogEntryLevel.Error)).toBeTrue();
    expect(betaComponent.isLevelEnabled(LogEntryLevel.Fatal)).toBeTrue();
    expect(betaComponent.isLevelEnabled(LogEntryLevel.Debug)).toBeFalse();
    expect(betaComponent.isLevelEnabled(LogEntryLevel.Information)).toBeFalse();

    const query = lastQuery();
    expect(query.levels?.length).toBe(3);
    expect(query.levels).not.toContain(LogEntryLevel.Debug);
    expect(query.levels).not.toContain(LogEntryLevel.Information);

    betaFixture.destroy();
  });

  it('a Development build opens with every level visible', async () => {
    TestBed.inject(NavigationService).setAppVersion('3.0.0', false, true, 'abc1234');
    const devFixture = TestBed.createComponent(LogsTabComponent);
    devFixture.detectChanges();
    await settle(devFixture);

    const devComponent = devFixture.componentInstance;
    for (const level of [
      LogEntryLevel.Debug,
      LogEntryLevel.Information,
      LogEntryLevel.Warning,
      LogEntryLevel.Error,
      LogEntryLevel.Fatal,
    ]) {
      expect(devComponent.isLevelEnabled(level)).toBeTrue();
    }

    expect(lastQuery().levels).toBeUndefined();

    devFixture.destroy();
  });

  it('the default re-seeds when the build channel arrives late', async () => {
    expect(component.isLevelEnabled(LogEntryLevel.Debug)).toBeFalse();
    apiSpy.getLogs.calls.reset();

    TestBed.inject(NavigationService).setAppVersion('3.0.0', false, true, 'abc1234');
    await settleDebounce();

    for (const level of [
      LogEntryLevel.Debug,
      LogEntryLevel.Information,
      LogEntryLevel.Warning,
      LogEntryLevel.Error,
      LogEntryLevel.Fatal,
    ]) {
      expect(component.isLevelEnabled(level)).toBeTrue();
    }

    expect(lastQuery().levels).toBeUndefined();
  });

  it('a level the user toggled survives the late build channel', async () => {
    component.toggleLevel(LogEntryLevel.Debug);
    component.toggleLevel(LogEntryLevel.Warning);
    await settleDebounce();

    TestBed.inject(NavigationService).setAppVersion('3.0.0', false, true, 'abc1234');
    await settleDebounce();

    expect(component.isLevelEnabled(LogEntryLevel.Debug)).toBeTrue();
    expect(component.isLevelEnabled(LogEntryLevel.Warning)).toBeFalse();
    expect(component.isLevelEnabled(LogEntryLevel.Information)).toBeFalse();
    expect(component.isLevelEnabled(LogEntryLevel.Error)).toBeTrue();
    expect(component.isLevelEnabled(LogEntryLevel.Fatal)).toBeTrue();
  });

  it('toggling a level still reaches the host, and the Debug chip covers Verbose', async () => {
    apiSpy.getLogs.calls.reset();

    component.toggleLevel(LogEntryLevel.Debug);
    await settleDebounce();

    const query = lastQuery();
    expect(query.levels?.length).toBe(5);
    expect(query.levels).toContain(LogEntryLevel.Verbose);
    expect(query.levels).toContain(LogEntryLevel.Debug);
    expect(query.levels).toContain(LogEntryLevel.Warning);
    expect(query.levels).toContain(LogEntryLevel.Error);
    expect(query.levels).toContain(LogEntryLevel.Fatal);
    expect(query.levels).not.toContain(LogEntryLevel.Information);
  });

  it('exposes the Follow control\'s pressed state', () => {
    const buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('shared-button')];
    const follow = buttons.find(button => button.textContent?.includes(text('macrodeck.app:Developer.Logs.FollowAction')));
    const inner = follow?.querySelector('button');

    component.autoScroll.set(true);
    fixture.detectChanges();
    expect(inner?.getAttribute('aria-pressed')).toBe('true');

    component.autoScroll.set(false);
    fixture.detectChanges();
    expect(inner?.getAttribute('aria-pressed')).toBe('false');
  });

  it('shows no Live/Paused badge in the header', () => {
    const header = (fixture.nativeElement as HTMLElement).querySelector('.log-header');

    expect(header?.textContent).not.toContain('Live');
    expect(header?.textContent).not.toContain('Paused');
  });

  it('does not change what the next viewer defaults to when one viewer toggles a level', async () => {
    component.toggleLevel(LogEntryLevel.Debug);
    await settleDebounce();

    const secondFixture = TestBed.createComponent(LogsTabComponent);
    secondFixture.detectChanges();
    await settle(secondFixture);

    expect(secondFixture.componentInstance.isLevelEnabled(LogEntryLevel.Debug)).toBeFalse();

    secondFixture.destroy();
  });

  // Typing must not fire one request per keystroke - the host scans files for each of them.
  it('debounces typing into a single query', async () => {
    apiSpy.getLogs.calls.reset();

    component.onSearchChange('t');
    component.onSearchChange('to');
    component.onSearchChange('token');
    await settleDebounce();

    expect(apiSpy.getLogs).toHaveBeenCalledTimes(1);
    expect(lastQuery().search).toBe('token');
  });

  it('opens on the last hour, left open-ended so the live tail keeps arriving', async () => {
    apiSpy.getLogs.calls.reset();
    const before = Date.now();
    const freshFixture = TestBed.createComponent(LogsTabComponent);
    freshFixture.detectChanges();
    await settle(freshFixture);
    const after = Date.now();

    const query = lastQuery();
    expect(query.to).toBeUndefined();
    expect(Date.parse(query.from!)).toBeGreaterThanOrEqual(before - HOUR_MS);
    expect(Date.parse(query.from!)).toBeLessThanOrEqual(after - HOUR_MS);

    freshFixture.destroy();
  });

  it('sends a custom range as chosen, and a preset replaces both of its bounds', async () => {
    apiSpy.getLogs.calls.reset();

    component.onTimeRangePresetChange('custom');
    component.onCustomFromChange('2026-07-28T09:00:00.000Z');
    component.onCustomToChange('2026-07-28T11:00:00.000Z');
    await settleDebounce();

    expect(lastQuery().from).toBe('2026-07-28T09:00:00.000Z');
    expect(lastQuery().to).toBe('2026-07-28T11:00:00.000Z');

    const before = Date.now();
    component.onTimeRangePresetChange('last6h');
    await settleDebounce();
    const after = Date.now();

    expect(lastQuery().to).toBeUndefined();
    expect(Date.parse(lastQuery().from!)).toBeGreaterThanOrEqual(before - 6 * HOUR_MS);
    expect(Date.parse(lastQuery().from!)).toBeLessThanOrEqual(after - 6 * HOUR_MS);
  });

  // Filtering a page in the browser would leave the rest of the history unreachable, so every
  // control has to land on the host in the same request.
  it('sends source, levels, search and time range to the host in one query', async () => {
    apiSpy.getLogs.calls.reset();

    component.selectSource({ kind: 'integration', integrationId: 'obs' });
    component.onSearchChange('token');
    component.toggleLevel(LogEntryLevel.Information);
    component.onTimeRangePresetChange('custom');
    component.onCustomFromChange('2026-07-28T09:00:00.000Z');
    component.onCustomToChange('2026-07-28T11:00:00.000Z');
    await settleDebounce();

    expect(apiSpy.getLogs).toHaveBeenCalledTimes(1);
    const query = lastQuery();
    expect(query.source).toBe(LogEntrySource.Integration);
    expect(query.integrationId).toBe('obs');
    expect(query.search).toBe('token');
    expect(query.from).toBe('2026-07-28T09:00:00.000Z');
    expect(query.to).toBe('2026-07-28T11:00:00.000Z');
    expect(query.levels).toContain(LogEntryLevel.Information);
    expect(query.limit).toBe(200);
  });

  // A rolling "last hour" would raise the floor of reachable history under the paging cursor.
  it('pages older entries against the same window the query was anchored to', async () => {
    apiSpy.getLogs.and.resolveTo(page(entries, { hasMore: true, olderCursor: 'cursor-1' }));
    component.reload();
    await settle();
    const anchored = lastQuery().from!;

    apiSpy.getLogs.calls.reset();
    apiSpy.getLogs.and.resolveTo(page([entry('older')], { hasMore: false }));
    component.maybeLoadOlder(0);
    await settle();

    expect(lastQuery().before).toBe('cursor-1');
    expect(lastQuery().from).toBe(anchored);
    expect(component.visible().map(e => e.id)).toContain('older');
  });

  it('reset returns the viewer to the query it opens on', async () => {
    component.selectSource({ kind: 'host' });
    component.onSearchChange('boom');
    component.toggleLevel(LogEntryLevel.Debug);
    component.onTimeRangePresetChange('all');
    await settleDebounce();
    expect(component.hasActiveFilters()).toBeTrue();

    apiSpy.getLogs.calls.reset();
    const before = Date.now();
    component.resetFilters();
    await settle();
    const after = Date.now();

    expect(apiSpy.getLogs).toHaveBeenCalledTimes(1);
    const query = lastQuery();
    expect(query.source).toBeUndefined();
    expect(query.search).toBeUndefined();
    expect(query.to).toBeUndefined();
    expect(Date.parse(query.from!)).toBeGreaterThanOrEqual(before - HOUR_MS);
    expect(Date.parse(query.from!)).toBeLessThanOrEqual(after - HOUR_MS);
    expect(query.levels?.length).toBe(3);
    expect(component.isLevelEnabled(LogEntryLevel.Debug)).toBeFalse();
    expect(component.hasActiveFilters()).toBeFalse();
  });

  it('copies one entry with its exception attached, as a single logical event', async () => {
    const copyText = spyOn(TestBed.inject(TextClipboardService), 'copyText')
      .and.resolveTo({ status: 'copied', via: 'clipboard-api' });

    component.selectEntry(entries[3]);
    await component.copyEntry();

    const copied = copyText.calls.mostRecent().args[0];
    expect(copied).toContain('2026-07-28T10:00:00+00:00');
    expect(copied).toContain('[ERR]');
    expect(copied).toContain('Token refresh failed');
    for (const line of exceptionText.split('\n')) {
      expect(copied).toContain(line);
    }
  });

  it('copies the loaded window without reading anything again', async () => {
    const copyText = spyOn(TestBed.inject(TextClipboardService), 'copyText')
      .and.resolveTo({ status: 'copied', via: 'clipboard-api' });
    apiSpy.getLogs.calls.reset();

    await component.copyResults();

    expect(apiSpy.getLogs).not.toHaveBeenCalled();
    const copied = copyText.calls.mostRecent().args[0];
    for (const loaded of component.visible()) {
      expect(copied).toContain(loaded.message);
    }
  });

  it('opens the detail panel on a click, but not on one that ends a text selection', () => {
    component.selectEntry(entries[0]);
    expect(component.selectedEntry()?.id).toBe('h1');

    component.closeDetail();
    spyOn(window, 'getSelection').and.returnValue({ toString: () => 'Host star' } as Selection);
    component.selectEntry(entries[1]);

    expect(component.selectedEntry()).toBeNull();
  });

  it('follows the tail while the view is at the bottom and stops as soon as it is not', () => {
    component.updateFollowState(0);
    expect(component.autoScroll()).toBeTrue();

    component.updateFollowState(400);
    expect(component.autoScroll()).toBeFalse();

    component.updateFollowState(4);
    expect(component.autoScroll()).toBeTrue();
  });

  it('does not ask for older entries once the beginning of the log is reached', () => {
    apiSpy.getLogs.calls.reset();

    component.maybeLoadOlder(0);

    expect(apiSpy.getLogs).not.toHaveBeenCalled();
  });

  // Counting the prepend by id rather than by length is what keeps a live append during the load
  // from being mistaken for history.
  it('restores the read position after a prepend, ignoring an entry the tail appended meanwhile', async () => {
    apiSpy.getLogs.and.resolveTo(page(entries, { hasMore: true, olderCursor: 'cursor-1' }));
    component.reload();
    await settle();
    layOutViewport();

    spyOn(viewport(), 'measureScrollOffset').and.returnValue(500);
    const scrollTo = spyOn(viewport(), 'scrollTo');

    apiSpy.getLogs.and.callFake(async () => {
      appended.next({ entries: [entry('live', { timestamp: '2026-07-28T10:00:01+00:00' })] });
      return page([entry('o1'), entry('o2')], { hasMore: false });
    });

    component.maybeLoadOlder(0);
    await settle();
    await settle();

    expect(component.visible().map(e => e.id)).toEqual(['o1', 'o2', 'h1', 'b1', 'i1', 'i2', 'h2', 'live']);
    expect(scrollTo).toHaveBeenCalledWith({ top: 500 + 2 * component.rowHeight });
  });

  describe('empty states', () => {
    function heading(): string {
      return ((fixture.nativeElement as HTMLElement).querySelector('.es-title')?.textContent ?? '').trim();
    }

    function message(): string {
      return ((fixture.nativeElement as HTMLElement).querySelector('.es-message')?.textContent ?? '').trim();
    }

    function offeredActions(): HTMLButtonElement[] {
      return [...(fixture.nativeElement as HTMLElement).querySelectorAll('shared-empty-state shared-button button')]
        .map(button => button as HTMLButtonElement);
    }

    function offeredAction(key: string): HTMLButtonElement {
      return offeredActions().find(button => (button.textContent ?? '').trim() === text(key))!;
    }

    it('an empty log over the whole history reports an empty log', async () => {
      apiSpy.getLogs.and.resolveTo(page([]));
      component.resetFilters();
      component.onTimeRangePresetChange('all');
      await settleDebounce();
      fixture.detectChanges();

      expect(heading()).toBe(text('macrodeck.app:Developer.Logs.NoEntriesHeading'));
    });

    // The viewer opens on a one-hour window, so "nothing here" has to name the window it is reading.
    it('an empty default window says the view is time-bounded and offers to widen it', async () => {
      apiSpy.getLogs.and.resolveTo(page([]));
      component.resetFilters();
      await settle();
      fixture.detectChanges();

      expect(component.hasActiveFilters()).toBeFalse();
      expect(message()).toBe(text('macrodeck.app:Developer.Logs.NothingMatchesInRangeMessage'));

      offeredAction('macrodeck.app:Developer.Logs.ShowAllTimeAction').click();
      await settle();

      expect(component.timeRange().preset).toBe('all');
      expect(lastQuery().from).toBeUndefined();
    });

    it('a filter that matches nothing offers to reset it', async () => {
      apiSpy.getLogs.and.resolveTo(page([]));
      component.onSearchChange('nothing matches this');
      await settleDebounce();
      fixture.detectChanges();

      expect(heading()).toBe(text('macrodeck.app:Developer.Logs.NothingMatchesHeading'));

      offeredAction('macrodeck.app:Developer.Logs.ResetFiltersAction').click();
      await settle();

      expect(component.hasActiveFilters()).toBeFalse();
    });

    it('says so when every level is turned off', async () => {
      apiSpy.getLogs.and.resolveTo(page([]));
      for (const level of component.enabledLevels()) {
        component.toggleLevel(level);
      }
      await settleDebounce();
      fixture.detectChanges();

      expect(component.enabledLevels().size).toBe(0);
      expect(heading()).toBe(text('macrodeck.app:Developer.Logs.NoLevelsHeading'));
    });
  });
});
