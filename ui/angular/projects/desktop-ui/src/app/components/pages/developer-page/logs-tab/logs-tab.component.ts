import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';
import { FormsModule } from '@angular/forms';

import { AppStrings, LogEntry, LogEntryLevel, LogEntrySource, LogQuery } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, InputComponent, LocalizationKey, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { DateTimePickerComponent } from '../../../forms/datetime-picker/datetime-picker.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { CopyTextModalComponent } from '../../../overlay/copy-text-modal/copy-text-modal.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { RailPageComponent } from '../../../rail-page/rail-page.component';
import { DEFAULT_LOG_TIME_RANGE, LOG_LEVEL_FILTERS, LOG_TIME_RANGE_PRESETS, LogSourceFilter, LogTimeRange, LogTimeRangePreset, defaultLogLevelFilters, formatLogEntryForCopy, formatLogTime, integrationLogSources, logLevelCode, logQueryOf, logTimeRangeBounds } from '../../../../domain/log-source.util';
import { IntegrationService } from '../../../../services/integration.service';
import { LogService } from '../../../../services/log.service';
import { TextClipboardService, clipboardFailureDetail } from '../../../../services/text-clipboard.service';

import { NavigationService } from '../../../../services/navigation.service';

@Component({
  selector: 'app-logs-tab',
  standalone: true,
  imports: [
    ButtonComponent,
    CopyTextModalComponent,
    DateTimePickerComponent,
    EmptyStateComponent,
    FormsModule,
    InputComponent,
    RailItemComponent,
    RailPageComponent,
    ScrollingModule,
    SelectComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './logs-tab.component.html',
  styleUrls: ['./logs-tab.component.scss'],
})
export class LogsTabComponent {
  private static readonly AT_BOTTOM_THRESHOLD_PX = 24;

  private static readonly LOAD_OLDER_INDEX_THRESHOLD = 5;

  private static readonly QUERY_DEBOUNCE_MS = 250;

  protected readonly levels = LOG_LEVEL_FILTERS;

  readonly rowHeight = 22;

  private readonly injector = inject(Injector);
  private readonly logs = inject(LogService);
  private readonly api = inject(ApiService);
  private readonly integrationService = inject(IntegrationService);
  private readonly navigation = inject(NavigationService);
  private readonly localization = inject(LocalizationService);
  private readonly clipboard = inject(TextClipboardService);
  private readonly toasts = inject(ToastService);

  private readonly viewport = viewChild(CdkVirtualScrollViewport);

  readonly source = signal<LogSourceFilter>({ kind: 'all' });
  readonly search = signal('');
  readonly timeRange = signal<LogTimeRange>(DEFAULT_LOG_TIME_RANGE);
  private readonly userLevels = signal<ReadonlySet<LogEntryLevel> | null>(null);

  readonly enabledLevels = computed(() =>
    this.userLevels() ?? defaultLogLevelFilters(this.navigation.isDevelopmentBuild()));

  readonly autoScroll = signal(true);

  readonly isLoading = this.logs.isLoading;
  readonly isLoadingOlder = this.logs.isLoadingOlder;
  readonly hasLoaded = this.logs.hasLoaded;
  readonly hasMore = this.logs.hasMore;

  readonly visible = this.logs.entries;

  readonly selectedId = signal<string | null>(null);

  readonly selectedEntry = computed(() => {
    const id = this.selectedId();

    return id === null ? null : this.visible().find(entry => entry.id === id) ?? null;
  });

  protected readonly manualCopy = signal<{ value: string; message: string } | null>(null);

  readonly integrationSources = computed(() =>
    integrationLogSources(
      this.logs.sources(),
      id => this.integrationService.integrations().find(integration => integration.id === id)?.name ?? id,
    ));

  readonly timeRangeOptions = computed<SelectOption[]>(() =>
    LOG_TIME_RANGE_PRESETS.map(preset => ({
      value: preset,
      label: this.localization.translateKey(timeRangeLabelKey(preset)),
    })));

  private readonly levelsAreDefault = computed(() => {
    const defaults = defaultLogLevelFilters(this.navigation.isDevelopmentBuild());
    const current = this.enabledLevels();

    return current.size === defaults.size && [...current].every(level => defaults.has(level));
  });

  readonly hasActiveFilters = computed(() =>
    this.source().kind !== 'all'
    || this.search().trim() !== ''
    || this.timeRange().preset !== DEFAULT_LOG_TIME_RANGE.preset
    || !this.levelsAreDefault());

  readonly isTimeBounded = computed(() => this.timeRange().preset !== 'all');

  readonly hasNarrowingFilters = computed(() =>
    this.source().kind !== 'all'
    || this.search().trim() !== ''
    || this.isTimeBounded()
    || !this.levelsAreDefault());

  readonly emptyStateMessageKey = computed<LocalizationKey>(() =>
    this.isTimeBounded()
      ? AppStrings.Developer.Logs.NothingMatchesInRangeMessage
      : AppStrings.Developer.Logs.NothingMatchesMessage);

  readonly detailTitle = computed(() => {
    const source = this.source();
    switch (source.kind) {
      case 'all':
        return this.localization.translateKey(AppStrings.Developer.Logs.AllLogsLabel);
      case 'host':
        return this.localization.translateKey(AppStrings.Developer.Logs.HostLabel);
      case 'bootstrapper':
        return this.localization.translateKey(AppStrings.Settings.About.Bootstrapper);
      case 'integration':
        return this.integrationService.integrations()
          .find(integration => integration.id === source.integrationId)?.name ?? source.integrationId;
    }
  });

  private readonly newestId = computed(() => this.visible().at(-1)?.id);

  private debounce?: ReturnType<typeof setTimeout>;

  private queriedLevels = this.enabledLevels();

  private wasConnected = this.api.connectionStateSignal() === 'connected';

  private isRestoringPosition = false;

  constructor() {
    this.logs.watch();
    void this.logs.setQuery(this.currentQuery());

    if (this.integrationService.integrations().length === 0) {
      void this.integrationService.loadIntegrations();
    }

    effect(() => {
      const levels = this.enabledLevels();
      if (levels === this.queriedLevels) {
        return;
      }

      this.queriedLevels = levels;
      this.requery();
    });

    // The service replays the stored query after a reconnect, so a relative range frozen before the
    // disconnect would come back as a stale "last hour". Re-anchor it against the current clock.
    effect(() => {
      const connected = this.api.connectionStateSignal() === 'connected';
      const reconnected = connected && !this.wasConnected;
      this.wasConnected = connected;
      if (reconnected && this.timeRange().preset !== 'custom') {
        void this.logs.setQuery(this.currentQuery());
      }
    });

    effect(onCleanup => {
      const viewport = this.viewport();
      if (!viewport) {
        return;
      }

      // elementScrolled rather than scrolledIndexChange: the latter reports nothing for the small
      // movements that decide whether the view is still pinned to the bottom.
      const subscription = viewport.elementScrolled().subscribe(() => {
        this.updateFollowState(viewport.measureScrollOffset('bottom'));
        this.maybeLoadOlder(viewport.getRenderedRange().start);
      });
      onCleanup(() => subscription.unsubscribe());
    });

    effect(() => {
      this.newestId();
      const follow = this.autoScroll();
      if (!follow || this.isRestoringPosition) {
        return;
      }

      afterNextRender(() => this.scrollToBottom(), { injector: this.injector });
    });

    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.debounce);
      this.logs.unwatch();
    });
  }

  isLevelEnabled(level: LogEntryLevel): boolean {
    return this.enabledLevels().has(level);
  }

  toggleLevel(level: LogEntryLevel): void {
    const next = new Set(this.enabledLevels());
    if (!next.delete(level)) {
      next.add(level);
    }

    this.userLevels.set(next);
  }

  toggleAutoScroll(): void {
    const enabled = !this.autoScroll();
    this.autoScroll.set(enabled);
    if (enabled) {
      this.scrollToBottom();
    }
  }

  updateFollowState(distanceFromBottomPx: number): void {
    if (this.isRestoringPosition) {
      return;
    }

    this.autoScroll.set(distanceFromBottomPx <= LogsTabComponent.AT_BOTTOM_THRESHOLD_PX);
  }

  maybeLoadOlder(firstVisibleIndex: number): void {
    if (this.isRestoringPosition || firstVisibleIndex > LogsTabComponent.LOAD_OLDER_INDEX_THRESHOLD) {
      return;
    }

    if (!this.hasMore() || this.isLoadingOlder()) {
      return;
    }

    void this.loadOlder();
  }

  selectSource(source: LogSourceFilter): void {
    this.source.set(source);
    this.autoScroll.set(true);
    this.requery();
  }

  isSourceSelected(source: LogSourceFilter): boolean {
    const current = this.source();
    if (current.kind !== source.kind) {
      return false;
    }

    return current.kind !== 'integration'
      || (source.kind === 'integration' && current.integrationId === source.integrationId);
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.requery();
  }

  onTimeRangePresetChange(preset: string): void {
    this.timeRange.update(range => ({ ...range, preset: preset as LogTimeRangePreset }));
    this.requery();
  }

  onCustomFromChange(value: string): void {
    this.timeRange.update(range => ({ ...range, customFrom: value }));
    this.requery();
  }

  onCustomToChange(value: string): void {
    this.timeRange.update(range => ({ ...range, customTo: value }));
    this.requery();
  }

  selectEntry(entry: LogEntry): void {
    if ((window.getSelection()?.toString() ?? '') !== '') {
      return;
    }

    this.selectedId.set(entry.id);
  }

  closeDetail(): void {
    this.selectedId.set(null);
  }

  showAllTime(): void {
    clearTimeout(this.debounce);
    this.timeRange.set({ preset: 'all' });
    this.autoScroll.set(true);
    void this.logs.setQuery(this.currentQuery());
  }

  resetFilters(): void {
    clearTimeout(this.debounce);
    this.source.set({ kind: 'all' });
    this.search.set('');
    this.timeRange.set(DEFAULT_LOG_TIME_RANGE);
    this.userLevels.set(null);
    this.selectedId.set(null);
    this.autoScroll.set(true);
    this.queriedLevels = this.enabledLevels();
    void this.logs.setQuery(this.currentQuery());
  }

  async copyEntry(): Promise<void> {
    const entry = this.selectedEntry();
    if (!entry) {
      return;
    }

    await this.copy(
      formatLogEntryForCopy(entry, this.origin(entry)),
      AppStrings.Developer.Logs.EntryCopiedMessage,
    );
  }

  async copyResults(): Promise<void> {
    const text = this.visible()
      .map(entry => formatLogEntryForCopy(entry, this.origin(entry)))
      .join('\n');

    await this.copy(text, AppStrings.Developer.Logs.ResultsCopiedMessage);
  }

  reload(): void {
    this.autoScroll.set(true);
    void this.logs.setQuery(this.currentQuery());
    void this.logs.refreshSources();
  }

  levelLabel(level: LogEntryLevel): string {
    return logLevelCode(level);
  }

  levelClass(level: LogEntryLevel): string {
    return `level-${level.toLowerCase()}`;
  }

  trackEntry(_index: number, entry: LogEntry): string {
    return entry.id;
  }

  time(entry: LogEntry): string {
    return formatLogTime(entry.timestamp, this.localization.timeLocale());
  }

  origin(entry: LogEntry): string {
    if (entry.category) {
      return entry.category;
    }

    return entry.source === LogEntrySource.Bootstrapper
      ? this.localization.translateKey(AppStrings.Settings.About.Bootstrapper)
      : this.localization.translateKey(AppStrings.Developer.Logs.HostLabel);
  }

  private async copy(text: string, copiedMessage: LocalizationKey): Promise<void> {
    const result = await this.clipboard.copyText(text);
    if (result.status === 'copied') {
      this.toasts.show(this.localization.translateKey(copiedMessage));
      return;
    }

    this.manualCopy.set({
      value: text,
      message: this.localization.translateKey(AppStrings.CopyValue.ManualCopyMessage, {
        detail: clipboardFailureDetail(result.reason, this.localization),
      }),
    });
  }

  private currentQuery(): LogQuery {
    return logQueryOf(
      this.source(),
      this.enabledLevels(),
      this.search(),
      logTimeRangeBounds(this.timeRange()),
    );
  }

  private requery(): void {
    clearTimeout(this.debounce);
    this.debounce = setTimeout(() => {
      void this.logs.setQuery(this.currentQuery());
    }, LogsTabComponent.QUERY_DEBOUNCE_MS);
  }

  private async loadOlder(): Promise<void> {
    const viewport = this.viewport();
    if (!viewport) {
      return;
    }

    const anchorId = this.visible()[0]?.id;
    const offset = viewport.measureScrollOffset();
    this.isRestoringPosition = true;

    try {
      await this.logs.loadOlder();
    } catch {
      this.isRestoringPosition = false;
      return;
    }

    const index = anchorId === undefined
      ? 0
      : this.visible().findIndex(entry => entry.id === anchorId);
    const prepended = Math.max(0, index);

    // afterNextRender, not a microtask: the offset is only meaningful once Angular has rendered the
    // new rows and the viewport has recomputed its content size.
    afterNextRender(
      () => {
        viewport.scrollTo({ top: offset + prepended * this.rowHeight });
        this.isRestoringPosition = false;
      },
      { injector: this.injector },
    );
  }

  private scrollToBottom(): void {
    // scrollTo({ bottom: 0 }) rather than scrollToIndex: the latter aligns the last row with the
    // top of the viewport, leaving the tail visually stranded.
    this.viewport()?.scrollTo({ bottom: 0 });
  }
}

function timeRangeLabelKey(preset: LogTimeRangePreset): LocalizationKey {
  switch (preset) {
    case 'all':
      return AppStrings.Developer.Logs.TimeRangeAll;
    case 'last15m':
      return AppStrings.Developer.Logs.TimeRangeLast15m;
    case 'last1h':
      return AppStrings.Developer.Logs.TimeRangeLast1h;
    case 'last6h':
      return AppStrings.Developer.Logs.TimeRangeLast6h;
    case 'last24h':
      return AppStrings.Developer.Logs.TimeRangeLast24h;
    case 'last7d':
      return AppStrings.Developer.Logs.TimeRangeLast7d;
    case 'custom':
      return AppStrings.Developer.Logs.TimeRangeCustom;
  }
}
