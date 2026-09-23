import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, OnDestroy, OnInit, afterNextRender, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { AppStrings, StoreCatalogItemBody, StoreCatalogSection, StoreExtensionKind } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { StoreFooterComponent } from '../../store/store-footer.component';
import { StorePageHeaderComponent } from '../../store/store-page-header.component';
import { StoreSectionComponent, StoreUnsignedInstallRequest } from '../../store/store-section.component';
import { StoreAccessService } from '../../../services/store-access.service';
import { StoreBrowseStateService, isHistoryNavigation, isStoreDetailUrl } from '../../../services/store-browse-state.service';
import { ConnectAccountService } from '../../../services/connect-account.service';
import { StoreCatalogService } from '../../../services/store-catalog.service';
import { StoreOperationService } from '../../../services/store-operation.service';
import { StoreRatingsService } from '../../../services/store-ratings.service';
import { UpdateModalService } from '../../../services/update-modal.service';
import { UpdateService } from '../../../services/update.service';
import { storeKindIcon, storeUninstallErrorKey, storeUninstallMessageKey } from '../../../util/store-operation-display';

type KindFilter = 'all' | StoreExtensionKind;

interface KindOption {
  value: KindFilter;
  icon: string;
  labelKey: string;
}

const BROWSE_KINDS: StoreExtensionKind[] = ['Plugin', 'IconPack'];

const KIND_OPTIONS: KindOption[] = [
  { value: 'all', icon: 'grid', labelKey: AppStrings.Store.Page.KindAll },
  { value: 'Plugin', icon: storeKindIcon('Plugin'), labelKey: AppStrings.Store.Page.KindPlugins },
  { value: 'IconPack', icon: storeKindIcon('IconPack'), labelKey: AppStrings.Store.Page.KindIconPacks },
];

const DISCOVERY_TAKE = 6;

const ROWS_MIN_CATALOG = DISCOVERY_TAKE * 2;

const FEATURED_TAKE = 50;

const DEFAULT_SORT: StoreCatalogSection = 'popular';

const SORT_SECTIONS: readonly StoreCatalogSection[] = ['all', 'popular', 'name', 'newest', 'recentlyUpdated'];
const BEST_MATCH_SORT: StoreCatalogSection = 'all';

export const STORE_SUPPORTED_ONLY_STORAGE_KEY = 'macrodeck.store.supportedOnly';

@Component({
  selector: 'app-store-page',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    ConfirmationModalComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    InputComponent,
    SelectComponent,
    StoreFooterComponent,
    StorePageHeaderComponent,
    StoreSectionComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-page.component.html',
  styleUrls: ['./store-page.component.scss'],
})
export class StorePageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);
  protected readonly catalog = inject(StoreCatalogService);
  protected readonly operations = inject(StoreOperationService);
  private readonly ratings = inject(StoreRatingsService);
  private readonly updates = inject(UpdateService);
  private readonly updateModal = inject(UpdateModalService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly browseState = inject(StoreBrowseStateService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly search = signal('');
  protected readonly kind = signal<KindFilter>('all');
  protected readonly sort = signal<StoreCatalogSection>(DEFAULT_SORT);
  protected readonly supportedOnly = signal(readSupportedOnly());
  protected readonly publisher = signal<string | null>(null);
  protected readonly tag = signal<string | null>(null);
  protected readonly narrowed = computed(() => !!this.publisher() || !!this.tag());

  protected readonly searching = computed(() => this.search().trim() !== '');

  protected readonly storeUnlocked = inject(StoreAccessService).unlocked;

  // The overlay keeps an invited tester out of the unreleased catalog, not out of the builds they test.
  protected readonly signedIn = inject(ConnectAccountService).isSignedIn;

  protected readonly updatesAvailable = this.updates.hasBridge;

  protected readonly kindCounts = signal<ReadonlyMap<KindFilter, number>>(new Map());
  protected readonly unfilteredTotal = signal<number | null>(null);

  protected readonly kindOptions = computed(() => KIND_OPTIONS.map(option => ({
    ...option,
    label: this.localization.translateKey(option.labelKey),
    count: this.kindCounts().get(option.value) ?? null,
  })));

  protected readonly sortOptions = computed<SelectOption[]>(() => [
    ...(this.searching()
      ? [{ value: BEST_MATCH_SORT, label: this.localization.translateKey(AppStrings.Store.Page.SortBestMatch) }]
      : []),
    { value: 'popular', label: this.localization.translateKey(AppStrings.Store.Page.SortPopular) },
    { value: 'name', label: this.localization.translateKey(AppStrings.Store.Page.SortName) },
    { value: 'newest', label: this.localization.translateKey(AppStrings.Store.Page.SortNewest) },
    { value: 'recentlyUpdated', label: this.localization.translateKey(AppStrings.Store.Page.SortRecentlyUpdated) },
  ]);

  protected readonly featuredItems = signal<StoreCatalogItemBody[]>([]);
  private readonly popularCandidates = signal<StoreCatalogItemBody[]>([]);
  private readonly newestItems = signal<StoreCatalogItemBody[]>([]);
  private readonly recentlyUpdatedItems = signal<StoreCatalogItemBody[]>([]);

  protected readonly featuredKeys = computed<ReadonlySet<string>>(() =>
    new Set(this.featuredItems().map(key)));

  // The rows are this page's own snapshot, so they need the supersede rule the catalog cache has:
  // two discovery loads racing a filter change could otherwise leave the rows on the older filter
  // while the grid below shows the newer one.
  private discoveryGeneration = 0;
  private countGeneration = 0;

  protected readonly showRows = computed(() =>
    !this.searching() && !this.narrowed() && this.catalog.total() >= ROWS_MIN_CATALOG);

  protected readonly featuredRow = computed(() => this.featuredItems().slice(0, DISCOVERY_TAKE));

  protected readonly popularRow = computed(() => {
    const installs = this.ratings.installs();
    const shown = new Set(this.featuredRow().map(key));
    return this.popularCandidates()
      .filter(item => !shown.has(key(item)) && (installs.get(item.id) ?? 0) > 0)
      .slice(0, DISCOVERY_TAKE);
  });

  protected readonly freshRow = computed(() => {
    const shown = new Set([...this.featuredRow(), ...this.popularRow()].map(key));
    return mergeFresh(this.newestItems(), this.recentlyUpdatedItems(), shown);
  });

  protected readonly showUnavailable = computed(() => {
    const registry = this.catalog.registry();
    return registry !== null && !registry.hasCatalog;
  });

  protected readonly showEmptyCatalog = computed(() =>
    !this.showUnavailable() && !this.catalog.isLoading() && !this.catalog.loadError()
    && this.catalog.items().length === 0 && !this.searching() && !this.narrowed() && this.kind() === 'all'
    && (this.unfilteredTotal() ?? 0) === 0);

  protected readonly showEmptyResults = computed(() =>
    !this.showUnavailable() && !this.showEmptyCatalog() && !this.catalog.isLoading()
    && !this.catalog.loadError() && this.catalog.items().length === 0);

  protected readonly resultCount = computed(() =>
    this.localization.translateKey(AppStrings.Store.Page.ResultCount, { count: this.catalog.total() }));

  protected readonly hiddenCount = computed(() => {
    const unfiltered = this.unfilteredTotal();
    if (!this.supportedOnly() || unfiltered === null) {
      return 0;
    }
    return Math.max(0, unfiltered - this.catalog.total());
  });

  protected readonly hiddenMessage = computed(() =>
    this.localization.translateKey(AppStrings.Store.Page.HiddenUnsupported, { count: this.hiddenCount() }));

  protected readonly pendingUninstall = signal<StoreCatalogItemBody | null>(null);

  protected readonly uninstallHeading = computed(() => this.localization.translateKey(AppStrings.Store.UninstallHeading));

  protected readonly uninstallMessage = computed(() => {
    const item = this.pendingUninstall();
    if (!item) {
      return '';
    }
    return this.localization.translateKey(storeUninstallMessageKey(item.kind), { name: item.name });
  });

  protected readonly browseHeading = computed(() => {
    const tag = this.tag();
    if (tag) {
      return this.localization.translateKey(AppStrings.Store.Page.TagHeading, { tag });
    }
    const publisher = this.publisher();
    if (publisher) {
      return this.localization.translateKey(AppStrings.Store.Page.PublisherHeading, { publisher });
    }
    if (this.searching()) {
      return this.localization.translateKey(AppStrings.Store.Page.SearchResultsFor, { query: this.search().trim() });
    }
    switch (this.kind()) {
      case 'Plugin': return this.localization.translateKey(AppStrings.Store.Page.KindPlugins);
      case 'IconPack': return this.localization.translateKey(AppStrings.Store.Page.KindIconPacks);
      default: return this.localization.translateKey(AppStrings.Store.Page.AllSectionHeading);
    }
  });

  constructor() {
    effect(() => {
      const ids = [
        ...this.featuredItems(), ...this.popularCandidates(), ...this.newestItems(),
        ...this.recentlyUpdatedItems(), ...this.catalog.items(),
      ].map(item => item.id);
      this.ratings.ratings();
      untracked(() => void this.ratings.ensure(ids));
    });

    // catalog.items() reloads on this event through StoreCatalogService, but the discovery rows are
    // this page's own snapshot, filled only by loadDiscovery() - so without this they go stale after
    // any install/uninstall and disagree with the grid on install state.
    this.api.onNotification('StoreCatalogChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (!this.searching()) {
          void this.loadDiscovery();
        }
        void this.loadCounts();
      });
  }

  async ngOnInit(): Promise<void> {
    const returning = isHistoryNavigation(this.router.currentNavigation() ?? this.router.lastSuccessfulNavigation());
    this.applyParams(this.route.snapshot.queryParamMap);
    await this.runQuery();
    if (returning) {
      await this.restoreBrowsePosition();
    }

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      // An address this page wrote itself lags behind typing; only an outside change is applied.
      if (this.writtenParams.has(paramsKey(params.get('q'), params.get('kind'), params.get('sort'), params.get('publisher'), params.get('tag')))) {
        return;
      }
      if (this.applyParams(params)) {
        void this.runQuery();
      }
    });
  }

  private lastScrollTop = 0;
  private destroyed = false;
  private readonly writtenParams = new Map<string, number>();

  protected onScroll(event: Event): void {
    this.lastScrollTop = (event.target as HTMLElement).scrollTop;
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (!isStoreDetailUrl(this.router.currentNavigation()?.finalUrl?.toString())) {
      this.browseState.forget('discover');
      return;
    }
    this.browseState.save('discover', {
      key: this.browseKey(),
      scrollTop: this.lastScrollTop,
      itemCount: this.catalog.items().length,
    });
  }

  protected clearPublisher(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { publisher: null },
      queryParamsHandling: 'merge',
    });
  }

  protected clearTag(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tag: null },
      queryParamsHandling: 'merge',
    });
  }

  protected onSearchChange(value: string): void {
    const wasSearching = this.searching();
    this.search.set(value);

    // "Best match" has no meaning without a term, and leaving it selected would silently be another
    // order under a label that claims otherwise - so the two defaults follow the search box.
    if (!wasSearching && this.searching() && this.sort() === DEFAULT_SORT) {
      this.sort.set(BEST_MATCH_SORT);
    } else if (wasSearching && !this.searching() && this.sort() === BEST_MATCH_SORT) {
      this.sort.set(DEFAULT_SORT);
    }

    this.syncUrl();
    void this.runQuery();
  }

  protected onKindChange(value: KindFilter): void {
    if (value === this.kind()) {
      return;
    }
    this.kind.set(value);
    this.syncUrl();
    void this.runQuery();
  }

  protected onSortChange(value: StoreCatalogSection): void {
    this.sort.set(value);
    this.syncUrl();
    void this.catalog.load(this.gridQuery());
  }

  protected onSupportedOnlyChange(value: boolean): void {
    this.supportedOnly.set(value);
    writeSupportedOnly(value);
    void this.runQuery();
  }

  protected async onLoadMore(): Promise<void> {
    await this.catalog.loadMore();
  }

  protected openTests(): void {
    void this.router.navigate(['/store/tests']);
  }

  protected async onRefreshed(): Promise<void> {
    await Promise.all([this.searching() ? Promise.resolve() : this.loadDiscovery(), this.loadCounts()]);
  }

  protected async onInstall(item: StoreCatalogItemBody): Promise<void> {
    await this.operations.install(item.kind, item.id);
  }

  protected async onInstallUnsigned(request: StoreUnsignedInstallRequest): Promise<void> {
    await this.operations.install(request.item.kind, request.item.id, request.version, true);
  }

  protected onCheckForUpdates(): void {
    void this.updates.check();
    this.updateModal.open();
  }

  protected onUninstallRequested(item: StoreCatalogItemBody): void {
    this.pendingUninstall.set(item);
  }

  protected cancelUninstall(): void {
    this.pendingUninstall.set(null);
  }

  protected async confirmUninstall(): Promise<void> {
    const item = this.pendingUninstall();
    this.pendingUninstall.set(null);
    if (!item) {
      return;
    }

    const response = await this.operations.uninstall(item.kind, item.id);
    if (!response.success) {
      const detailKey = storeUninstallErrorKey(response.error?.code);
      this.toasts.show(this.localization.translateKey(AppStrings.Store.UninstallFailed), {
        detail: detailKey ? this.localization.translateKey(detailKey) : undefined,
        variant: 'error',
      });
    }
  }

  protected async onRetry(item: StoreCatalogItemBody): Promise<void> {
    const operation = this.operations.operationFor(item.kind, item.id)();
    if (operation) {
      await this.operations.retry(operation.id);
    }
  }

  private applyParams(params: ParamMap): boolean {
    const search = params.get('q') ?? '';
    const tag = params.get('tag')?.trim().toLowerCase() || null;
    const publisher = tag ? null : params.get('publisher')?.trim() || null;
    if (tag && params.get('publisher')) {
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { publisher: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
    const kindParam = params.get('kind');
    const kind: KindFilter = kindParam && KIND_OPTIONS.some(option => option.value === kindParam)
      ? kindParam as KindFilter
      : 'all';
    const sortParam = params.get('sort');
    const sort: StoreCatalogSection = sortParam && SORT_SECTIONS.includes(sortParam as StoreCatalogSection)
      ? sortParam as StoreCatalogSection
      : search.trim() ? BEST_MATCH_SORT : DEFAULT_SORT;

    const changed = search !== this.search() || publisher !== this.publisher() || tag !== this.tag()
      || kind !== this.kind() || sort !== this.sort();
    this.search.set(search);
    this.publisher.set(publisher);
    this.tag.set(tag);
    this.kind.set(kind);
    this.sort.set(sort);
    return changed;
  }

  private syncUrl(): void {
    const defaultSort = this.searching() ? BEST_MATCH_SORT : DEFAULT_SORT;
    const q = this.search().trim() || null;
    const kind = this.kind() === 'all' ? null : this.kind();
    const sort = this.sort() === defaultSort ? null : this.sort();
    const written = paramsKey(q, kind, sort, this.publisher(), this.tag());
    this.writtenParams.set(written, (this.writtenParams.get(written) ?? 0) + 1);
    const release = () => {
      const count = (this.writtenParams.get(written) ?? 1) - 1;
      if (count > 0) {
        this.writtenParams.set(written, count);
      } else {
        this.writtenParams.delete(written);
      }
    };
    void Promise.resolve(this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q, kind, sort },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    })).finally(release);
  }

  private scroller(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('.store-page');
  }

  private browseKey(): string {
    return JSON.stringify(this.gridQuery());
  }

  private async restoreBrowsePosition(): Promise<void> {
    const snapshot = this.browseState.restore('discover', this.browseKey());
    if (!snapshot) {
      return;
    }
    while (!this.destroyed && this.catalog.items().length < snapshot.itemCount && this.catalog.hasMore() && !this.catalog.loadError()) {
      const before = this.catalog.items().length;
      await this.catalog.loadMore();
      if (this.catalog.items().length === before) {
        break;
      }
    }
    if (this.destroyed) {
      return;
    }
    afterNextRender(() => {
      const scroller = this.scroller();
      if (scroller) {
        scroller.scrollTop = snapshot.scrollTop;
      }
    }, { injector: this.injector });
  }

  private effectiveKinds(): StoreExtensionKind[] {
    const kind = this.kind();
    return kind === 'all' ? BROWSE_KINDS : [kind];
  }

  private gridQuery() {
    return {
      kinds: this.effectiveKinds(),
      search: this.search().trim() || undefined,
      section: this.sort(),
      supportedOnly: this.supportedOnly() || undefined,
      publisher: this.publisher() ?? undefined,
      tag: this.tag() ?? undefined,
    };
  }

  private async runQuery(): Promise<void> {
    await Promise.all([
      this.catalog.load(this.gridQuery()),
      this.searching() || this.narrowed() ? Promise.resolve() : this.loadDiscovery(),
      this.loadCounts(),
    ]);
  }

  private async loadCounts(): Promise<void> {
    const generation = ++this.countGeneration;
    const search = this.search().trim() || undefined;
    const supportedOnly = this.supportedOnly() || undefined;
    const publisher = this.publisher() ?? undefined;
    const tag = this.tag() ?? undefined;
    const count = (kinds: StoreExtensionKind[], onlySupported: boolean | undefined) =>
      this.api.getStoreCatalog({ kinds, search, publisher, tag, supportedOnly: onlySupported, take: 1 })
        .then(response => response.total ?? 0)
        .catch(() => null);

    const [all, ...perKind] = await Promise.all([
      count(BROWSE_KINDS, supportedOnly),
      ...BROWSE_KINDS.map(kind => count([kind], supportedOnly)),
    ]);
    const unfiltered = supportedOnly ? await count(this.effectiveKinds(), undefined) : null;
    if (generation !== this.countGeneration) {
      return;
    }

    const counts = new Map<KindFilter, number>();
    if (all !== null) {
      counts.set('all', all);
    }
    BROWSE_KINDS.forEach((kind, index) => {
      const value = perKind[index];
      if (value !== null) {
        counts.set(kind, value);
      }
    });
    this.kindCounts.set(counts);
    this.unfilteredTotal.set(unfiltered);
  }

  private async loadDiscovery(): Promise<void> {
    const kinds = this.effectiveKinds();
    const supportedOnly = this.supportedOnly() || undefined;
    const generation = ++this.discoveryGeneration;
    const query = (section: StoreCatalogSection, take: number) =>
      this.api.getStoreCatalog({ kinds, section, take, supportedOnly })
        .then(response => response.items ?? [])
        .catch(() => [] as StoreCatalogItemBody[]);

    const [featured, popular, newest, recentlyUpdated] = await Promise.all([
      query('featured', FEATURED_TAKE),
      query('popular', DISCOVERY_TAKE * 3),
      query('newest', DISCOVERY_TAKE),
      query('recentlyUpdated', DISCOVERY_TAKE),
    ]);
    if (generation !== this.discoveryGeneration) {
      return;
    }

    this.featuredItems.set(featured);
    this.popularCandidates.set(popular);
    this.newestItems.set(newest);
    this.recentlyUpdatedItems.set(recentlyUpdated);
  }
}

function paramsKey(...values: (string | null | undefined)[]): string {
  return JSON.stringify(values.map(value => value || null));
}

function key(item: StoreCatalogItemBody): string {
  return `${item.kind}:${item.id}`;
}

function readSupportedOnly(): boolean {
  try {
    return globalThis.localStorage?.getItem(STORE_SUPPORTED_ONLY_STORAGE_KEY) !== 'false';
  } catch {
    return true;
  }
}

function writeSupportedOnly(value: boolean): void {
  try {
    globalThis.localStorage?.setItem(STORE_SUPPORTED_ONLY_STORAGE_KEY, String(value));
  } catch {
    // Remembering the choice is a convenience; the filter still applies for this visit.
  }
}

function mergeFresh(newest: StoreCatalogItemBody[],
  recentlyUpdated: StoreCatalogItemBody[],
  excluded: ReadonlySet<string>): StoreCatalogItemBody[] {
  const merged = new Map<string, StoreCatalogItemBody>();
  for (const item of [...newest, ...recentlyUpdated]) {
    const id = key(item);
    if (!excluded.has(id)) {
      merged.set(id, merged.get(id) ?? item);
    }
  }

  return [...merged.values()]
    .sort((left, right) => activity(right) - activity(left) || left.name.localeCompare(right.name))
    .slice(0, DISCOVERY_TAKE);
}

function activity(item: StoreCatalogItemBody): number {
  const created = item.createdAt ? Date.parse(item.createdAt) : 0;
  const updated = item.updatedAt ? Date.parse(item.updatedAt) : 0;
  return Math.max(Number.isNaN(created) ? 0 : created, Number.isNaN(updated) ? 0 : updated);
}
