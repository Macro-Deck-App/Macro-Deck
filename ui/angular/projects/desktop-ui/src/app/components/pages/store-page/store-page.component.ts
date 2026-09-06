import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AppStrings, StoreCatalogItemBody, StoreCatalogSection, StoreExtensionKind } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, SegmentedControlComponent, SegmentedOption, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { StoreSectionComponent } from '../../store/store-section.component';
import { StoreCatalogService } from '../../../services/store-catalog.service';
import { StoreOperationService } from '../../../services/store-operation.service';
import { storeUninstallErrorKey, storeUninstallMessageKey } from '../../../util/store-operation-display';

type KindFilter = 'all' | StoreExtensionKind;

const BROWSE_KINDS: StoreExtensionKind[] = ['Plugin', 'IconPack'];

const DISCOVERY_TAKE = 6;

const DEFAULT_SORT: StoreCatalogSection = 'name';
const BEST_MATCH_SORT: StoreCatalogSection = 'all';

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
    SegmentedControlComponent,
    SelectComponent,
    StoreSectionComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-page.component.html',
  styleUrls: ['./store-page.component.scss'],
})
export class StorePageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);
  protected readonly catalog = inject(StoreCatalogService);
  protected readonly operations = inject(StoreOperationService);

  protected readonly search = signal('');
  protected readonly kind = signal<KindFilter>('all');
  protected readonly sort = signal<StoreCatalogSection>(DEFAULT_SORT);

  protected readonly searching = computed(() => this.search().trim() !== '');

  protected readonly kindOptions = computed<SegmentedOption[]>(() => [
    { value: 'all', label: this.localization.translateKey(AppStrings.Store.Page.KindAll) },
    { value: 'Plugin', label: this.localization.translateKey(AppStrings.Store.Page.KindPlugins) },
    { value: 'IconPack', label: this.localization.translateKey(AppStrings.Store.Page.KindIconPacks) },
  ]);

  protected readonly sortOptions = computed<SelectOption[]>(() => [
    ...(this.searching()
      ? [{ value: BEST_MATCH_SORT, label: this.localization.translateKey(AppStrings.Store.Page.SortBestMatch) }]
      : []),
    { value: 'name', label: this.localization.translateKey(AppStrings.Store.Page.SortName) },
    { value: 'newest', label: this.localization.translateKey(AppStrings.Store.Page.SortNewest) },
    { value: 'recentlyUpdated', label: this.localization.translateKey(AppStrings.Store.Page.SortRecentlyUpdated) },
  ]);

  protected readonly featuredItems = signal<StoreCatalogItemBody[]>([]);
  protected readonly freshItems = signal<StoreCatalogItemBody[]>([]);

  // The rows are this page's own snapshot, so they need the supersede rule the catalog cache has:
  // two discovery loads racing a filter change could otherwise leave the rows on the older filter
  // while the grid below shows the newer one.
  private discoveryGeneration = 0;

  protected readonly showFeaturedRow = computed(() => this.rowEarnsItsPlace(this.featuredItems().length));
  protected readonly showFreshRow = computed(() => this.rowEarnsItsPlace(this.freshItems().length));

  protected readonly showUnavailable = computed(() => {
    const registry = this.catalog.registry();
    return registry !== null && !registry.hasCatalog;
  });

  protected readonly showEmptyCatalog = computed(() =>
    !this.showUnavailable() && !this.catalog.isLoading() && !this.catalog.loadError()
    && this.catalog.items().length === 0 && !this.searching() && this.kind() === 'all');

  protected readonly showEmptyResults = computed(() =>
    !this.showUnavailable() && !this.showEmptyCatalog() && !this.catalog.isLoading()
    && !this.catalog.loadError() && this.catalog.items().length === 0);

  protected readonly resultCount = computed(() =>
    this.localization.translateKey(AppStrings.Store.Page.ResultCount, { count: this.catalog.total() }));

  protected readonly pendingUninstall = signal<StoreCatalogItemBody | null>(null);

  protected readonly uninstallHeading = computed(() => this.localization.translateKey(AppStrings.Store.UninstallHeading));

  protected readonly uninstallMessage = computed(() => {
    const item = this.pendingUninstall();
    if (!item) {
      return '';
    }
    return this.localization.translateKey(storeUninstallMessageKey(item.kind), { name: item.name });
  });

  protected readonly featuredHeading = computed(() =>
    this.localization.translateKey(AppStrings.Store.Page.FeaturedSectionHeading));
  protected readonly freshHeading = computed(() =>
    this.localization.translateKey(AppStrings.Store.Page.NewAndUpdatedSectionHeading));
  protected readonly resultsHeading = computed(() =>
    this.searching()
      ? this.localization.translateKey(AppStrings.Store.Page.SearchResultsSectionHeading)
      : this.localization.translateKey(AppStrings.Store.Page.AllSectionHeading));

  constructor() {
    // catalog.items() reloads on this event through StoreCatalogService, but the discovery rows are
    // this page's own snapshot, filled only by loadDiscovery() - so without this they go stale after
    // any install/uninstall and disagree with the grid on install state. Skipped while a search is
    // active: the rows are not rendered then.
    this.api.onNotification('StoreCatalogChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (!this.searching()) {
          void this.loadDiscovery();
        }
      });
  }

  async ngOnInit(): Promise<void> {
    const query = this.route.snapshot.queryParamMap.get('q') ?? '';
    this.search.set(query);
    this.sort.set(query.trim() ? BEST_MATCH_SORT : DEFAULT_SORT);
    await this.runQuery();
  }

  protected onSearchChange(value: string): void {
    const wasSearching = this.searching();
    this.search.set(value);

    // "Best match" has no meaning without a term, and leaving it selected would silently be name
    // order under a label that claims otherwise - so the two defaults follow the search box.
    if (!wasSearching && this.searching() && this.sort() === DEFAULT_SORT) {
      this.sort.set(BEST_MATCH_SORT);
    } else if (wasSearching && !this.searching() && this.sort() === BEST_MATCH_SORT) {
      this.sort.set(DEFAULT_SORT);
    }

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: value.trim() || null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    void this.runQuery();
  }

  protected onKindChange(value: string): void {
    this.kind.set(value as KindFilter);
    void this.runQuery();
  }

  protected onSortChange(value: StoreCatalogSection): void {
    this.sort.set(value);
    // Only the grid takes the user's ordering: the discovery rows are defined by their own sections.
    void this.catalog.load(this.gridQuery());
  }

  protected async onLoadMore(): Promise<void> {
    await this.catalog.loadMore();
  }

  protected async onRefresh(): Promise<void> {
    await this.catalog.refreshRegistry();
    if (!this.searching()) {
      await this.loadDiscovery();
    }
  }

  protected async onInstall(item: StoreCatalogItemBody): Promise<void> {
    await this.operations.install(item.kind, item.id);
  }

  protected async onInstallUnsigned(item: StoreCatalogItemBody): Promise<void> {
    await this.operations.install(item.kind, item.id, undefined, true);
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

  private effectiveKinds(): StoreExtensionKind[] {
    const kind = this.kind();
    return kind === 'all' ? BROWSE_KINDS : [kind];
  }

  private gridQuery() {
    return {
      kinds: this.effectiveKinds(),
      search: this.search().trim() || undefined,
      section: this.sort(),
    };
  }

  private rowEarnsItsPlace(rowLength: number): boolean {
    return !this.searching() && rowLength > 0 && this.catalog.total() > rowLength;
  }

  private async runQuery(): Promise<void> {
    await this.catalog.load(this.gridQuery());

    if (!this.searching()) {
      await this.loadDiscovery();
    }
  }

  private async loadDiscovery(): Promise<void> {
    const kinds = this.effectiveKinds();
    const generation = ++this.discoveryGeneration;
    const [featured, newest, recentlyUpdated] = await Promise.all([
      this.api.getStoreCatalog({ kinds, section: 'featured', take: DISCOVERY_TAKE }),
      this.api.getStoreCatalog({ kinds, section: 'newest', take: DISCOVERY_TAKE }),
      this.api.getStoreCatalog({ kinds, section: 'recentlyUpdated', take: DISCOVERY_TAKE }),
    ]);
    if (generation !== this.discoveryGeneration) {
      return;
    }

    const featuredItems = featured.items ?? [];
    this.featuredItems.set(featuredItems);
    // The featured picks are only held back from the fresh row when they are actually on screen
    // above it - a pick the count gate hides must not disappear from both rows.
    const excluded = this.rowEarnsItsPlace(featuredItems.length) ? featuredItems : [];
    this.freshItems.set(mergeFresh(newest.items ?? [], recentlyUpdated.items ?? [], excluded));
  }
}

function mergeFresh(newest: StoreCatalogItemBody[],
  recentlyUpdated: StoreCatalogItemBody[],
  excluded: StoreCatalogItemBody[]): StoreCatalogItemBody[] {
  const key = (item: StoreCatalogItemBody) => `${item.kind}:${item.id}`;
  const skip = new Set(excluded.map(key));
  const merged = new Map<string, StoreCatalogItemBody>();
  for (const item of [...newest, ...recentlyUpdated]) {
    const id = key(item);
    if (!skip.has(id)) {
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
