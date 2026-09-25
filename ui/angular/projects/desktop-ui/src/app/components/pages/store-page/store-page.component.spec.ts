import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Navigation, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Observable, Subject } from 'rxjs';

import { AppStrings, GetConnectSessionResponse, GetStoreCatalogResponse, StoreCatalogItemBody, StoreCategoryBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { ConnectAccountService } from '../../../services/connect-account.service';
import { PluginRuntimeService } from '../../../services/plugin-runtime.service';
import { SelectComponent } from '../../forms/select/select.component';
import { StoreExtensionCardComponent } from '../../store/store-extension-card.component';
import { StoreSectionComponent } from '../../store/store-section.component';
import { StoreBrowseStateService } from '../../../services/store-browse-state.service';
import { STORE_SUPPORTED_ONLY_STORAGE_KEY, StorePageComponent } from './store-page.component';
import { StoreRegistryRefreshModalComponent } from './store-registry-refresh-modal.component';

interface CatalogOptions {
  kinds?: string[];
  search?: string;
  section?: string;
  skip?: number;
  take?: number;
  supportedOnly?: boolean;
}

interface Catalog {
  grid: StoreCatalogItemBody[];
  gridTotal?: number;
  featured?: StoreCatalogItemBody[];
  newest?: StoreCatalogItemBody[];
  recentlyUpdated?: StoreCatalogItemBody[];
  installs?: Record<string, number>;
  unsupportedCount?: number;
  categories?: StoreCategoryBody[] | 'unsupported';
}

function item(id: string, overrides: Partial<StoreCatalogItemBody> = {}): StoreCatalogItemBody {
  return {
    kind: 'Plugin',
    id,
    name: `Extension ${id}`,
    latestVersion: '1.0.0',
    installState: 'NotInstalled',
    trust: 'RegistryAuthenticated',
    hasIcon: false,
    ...overrides,
  };
}

function items(count: number, prefix = 'p'): StoreCatalogItemBody[] {
  return Array.from({ length: count }, (_, index) => item(`${prefix}${index + 1}`));
}

describe('StorePageComponent', () => {
  let fixture: ComponentFixture<StorePageComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let notifications: Map<string, Subject<unknown>>;
  let catalog: Catalog;
  let account: { isSignedIn: WritableSignal<boolean>; session: WritableSignal<GetConnectSessionResponse | null> };

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  function respond(list: StoreCatalogItemBody[], total = list.length): GetStoreCatalogResponse {
    return {
      items: list,
      total,
      registry: { hasCatalog: true, sequence: 1, refreshing: false, stale: false },
    };
  }

  function calls(): CatalogOptions[] {
    return api.getStoreCatalog.calls.all().map(call => (call.args[0] ?? {}) as CatalogOptions);
  }

  function sections(): StoreSectionComponent[] {
    return fixture.debugElement.queryAll(By.directive(StoreSectionComponent))
      .map(node => node.componentInstance as StoreSectionComponent)
      .filter(section => section.items().length > 0);
  }

  function cardCount(): number {
    return fixture.debugElement.queryAll(By.directive(StoreExtensionCardComponent)).length;
  }

  function findButton(label: string): HTMLElement | undefined {
    return Array.from<HTMLElement>(document.body.querySelectorAll('button'))
      .find(button => button.textContent?.trim() === label);
  }

  function sectionNamed(key: string): StoreSectionComponent | undefined {
    return sections().find(section => section.heading() === translate(key));
  }

  async function chooseFromStoreMenu(key: string): Promise<void> {
    const trigger = fixture.nativeElement.querySelector(
      `button[aria-label="${translate(AppStrings.Store.Page.MoreActionsAriaLabel)}"]`) as HTMLElement;
    trigger.click();
    await settle();
    findButton(translate(key))!.click();
    await settle();
  }

  function translate(key: string, params?: Record<string, unknown>): string {
    return TestBed.inject(LocalizationService).translateKey(key, params);
  }

  async function settle(): Promise<void> {
    for (let attempt = 0; attempt < 20; attempt++) {
      await fixture.whenStable();
      fixture.detectChanges();
    }
  }

  let queryParams: BehaviorSubject<ParamMap>;
  let initialParams: Record<string, string> = {};
  let currentNavigation: Navigation | null = null;

  async function createFixture(configuration: Partial<Catalog> = {}, beforeCreate?: () => void): Promise<void> {
    queryParams = new BehaviorSubject<ParamMap>(convertToParamMap({}));
    catalog = { grid: [item('p1')], ...configuration };
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getStoreCatalog', 'refreshStoreRegistry', 'getStoreOperations', 'installStoreExtension',
      'retryStoreOperation', 'getStoreExtensionIconUrl', 'uninstallStoreExtension', 'getStoreStatus', 'getStoreRatings',
      'getStoreInstalls', 'getStoreCategories',
    ]);
    api.getStoreCategories.and.callFake(() => catalog.categories === 'unsupported'
      ? Promise.reject(new Error('404'))
      : Promise.resolve({ categories: catalog.categories ?? [] }));
    api.getStoreRatings.and.resolveTo({ available: false, ratings: {} });
    api.getStoreInstalls.and.callFake(() => Promise.resolve(catalog.installs
      ? { available: true, installs: catalog.installs }
      : { available: false, installs: {} }));
    api.getStoreStatus.and.resolveTo({
      registry: { hasCatalog: true, sequence: 1, refreshing: false, stale: false },
      developerMode: false,
      refreshRun: null,
    });
    api.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    api.getStoreCatalog.and.callFake((options?: CatalogOptions) => {
      switch (options?.section) {
        case 'featured':
          return Promise.resolve(respond(catalog.featured ?? []));
        case 'newest':
          return Promise.resolve(respond(catalog.newest ?? []));
        case 'recentlyUpdated':
          return Promise.resolve(respond(catalog.recentlyUpdated ?? []));
        default: {
          const skip = options?.skip ?? 0;
          const take = options?.take ?? catalog.grid.length;
          const hidden = options?.supportedOnly ? 0 : catalog.unsupportedCount ?? 0;
          return Promise.resolve(respond(catalog.grid.slice(skip, skip + take),
            (catalog.gridTotal ?? catalog.grid.length) + hidden));
        }
      }
    });
    api.getStoreOperations.and.resolveTo({ operations: [] });
    api.getStoreExtensionIconUrl.and.callFake((_kind, _id, sha256) => (sha256 ? `icon-${sha256}` : ''));
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('connected') });

    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate', 'currentNavigation', 'lastSuccessfulNavigation']);
    routerSpy.navigate.and.resolveTo(true);
    routerSpy.currentNavigation.and.callFake((() => currentNavigation) as never);
    routerSpy.lastSuccessfulNavigation.and.returnValue(null);
    account = { isSignedIn: signal(false), session: signal(null) };

    TestBed.configureTestingModule({
      imports: [StorePageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ConnectAccountService, useValue: account },
        { provide: PluginRuntimeService, useValue: { plugins: signal([]) } },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: { get: (name: string) => initialParams[name] ?? null } },
            queryParamMap: queryParams.asObservable(),
          },
        },
      ],
    });

    beforeCreate?.();
    fixture = TestBed.createComponent(StorePageComponent);
    fixture.detectChanges();
    await settle();
  }

  function search(term: string): void {
    (fixture.componentInstance as unknown as { onSearchChange(value: string): void }).onSearchChange(term);
  }

  function chooseKind(value: string): void {
    (fixture.componentInstance as unknown as { onKindChange(value: string): void }).onKindChange(value);
  }

  function chooseSort(value: string): void {
    (fixture.componentInstance as unknown as { onSortChange(value: string): void }).onSortChange(value);
  }

  function refreshModalOpen(): boolean {
    return fixture.debugElement.queryAll(By.directive(StoreRegistryRefreshModalComponent)).length > 0;
  }

  it('opens the refresh log and asks the host for exactly one refresh when Refresh Store is chosen from the Store menu', async () => {
    await createFixture();
    api.refreshStoreRegistry.and.resolveTo({
      success: true,
      registry: { hasCatalog: true, sequence: 2, refreshing: false, stale: false },
    });

    await chooseFromStoreMenu(AppStrings.Store.Page.RefreshStoreAction);

    expect(refreshModalOpen()).toBeTrue();
    expect(api.refreshStoreRegistry).toHaveBeenCalledTimes(1);
  });

  it('shows a refresh already under way and opens its log instead of starting another', async () => {
    await createFixture();

    push('StoreRegistryRefreshChangedEvent', {
      run: {
        hostInstanceId: 'host-a',
        id: 'run-1',
        revision: 1,
        trigger: 'Scheduled',
        state: 'Running',
        startedAt: '2026-09-15T10:00:00Z',
        filesCompleted: 0,
        filesTotal: 0,
        entries: [],
      },
    });
    await settle();
    expect(fixture.nativeElement.textContent).toContain(translate(AppStrings.Store.RegistryRefresh.InProgressAction));

    await chooseFromStoreMenu(AppStrings.Store.Page.RefreshStoreAction);

    expect(refreshModalOpen()).toBeTrue();
    expect(api.refreshStoreRegistry).not.toHaveBeenCalled();
  });

  it('does not show the log of an earlier refresh when a new one is started', async () => {
    await createFixture();
    push('StoreRegistryRefreshChangedEvent', {
      run: {
        hostInstanceId: 'host-a',
        id: 'earlier',
        revision: 4,
        trigger: 'Scheduled',
        state: 'Succeeded',
        startedAt: '2026-09-15T09:00:00Z',
        filesCompleted: 0,
        filesTotal: 0,
        entries: [{ at: '2026-09-15T09:00:00Z', step: 'Started' }],
      },
    });
    await settle();
    api.refreshStoreRegistry.and.returnValue(new Promise(() => undefined));

    await chooseFromStoreMenu(AppStrings.Store.Page.RefreshStoreAction);

    const modal = fixture.debugElement.query(By.directive(StoreRegistryRefreshModalComponent))
      .componentInstance as StoreRegistryRefreshModalComponent;
    expect(modal.run()).toBeNull();
  });

  it('renders one card per catalog result', async () => {
    await createFixture({ grid: items(3) });

    expect(cardCount()).toBe(3);
  });

  it('loads a card icon by the digest of the icon the catalog currently lists', async () => {
    await createFixture({ grid: [item('p1', { hasIcon: true, iconSha256: 'icon-digest' })] });

    const sources = fixture.debugElement.queryAll(By.directive(StoreExtensionCardComponent))
      .flatMap(card => Array.from<HTMLImageElement>((card.nativeElement as HTMLElement).querySelectorAll('img')))
      .map(img => img.getAttribute('src'));
    expect(sources).toContain('icon-icon-digest');
  });

  it('drives the host search call from the search box', async () => {
    await createFixture();
    api.getStoreCatalog.calls.reset();
    catalog.grid = [item('found')];

    search('spotify');
    await settle();

    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ search: 'spotify' }));
    expect(cardCount()).toBe(1);
  });

  it('constrains the host call with the selected kind filter', async () => {
    await createFixture();
    api.getStoreCatalog.calls.reset();

    chooseKind('IconPack');
    await settle();

    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ kinds: ['IconPack'] }));
  });

  it('never asks the host for a kind the store does not browse', async () => {
    await createFixture({ grid: items(3), newest: items(2, 'n') });

    const gridCalls = calls().filter(call => call.take !== 1);
    expect(gridCalls.length).toBeGreaterThan(0);
    for (const call of gridCalls) {
      expect(call.kinds).toEqual(['Plugin', 'IconPack']);
    }
    for (const call of calls().filter(call => call.take === 1)) {
      expect(call.kinds!.length).toBeGreaterThan(0);
      expect(call.kinds!.every(kind => kind === 'Plugin' || kind === 'IconPack')).toBeTrue();
    }

    const chips = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.category-chip'))
      .map(chip => chip.getAttribute('data-kind'));
    expect(chips).toEqual(['all', 'Plugin', 'IconPack']);
  });

  it('re-issues the discovery queries and reflects updated install state when the catalog changes', async () => {
    await createFixture({ grid: items(20), newest: [item('n1')], recentlyUpdated: [item('n1')] });
    api.getStoreCatalog.calls.reset();

    catalog.grid = items(20).map(entry => ({ ...entry, installState: 'Installed', installedVersion: '1.0.0' }));
    catalog.newest = [item('n1', { installState: 'Installed', installedVersion: '1.0.0' })];
    catalog.recentlyUpdated = catalog.newest;

    push('StoreCatalogChangedEvent', {});
    await settle();

    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ section: 'featured' }));
    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ section: 'newest' }));
    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ section: 'recentlyUpdated' }));

    expect(findButton(translate(AppStrings.Store.Install))).toBeUndefined();
    expect(fixture.nativeElement.textContent)
      .toContain(translate(AppStrings.Store.InstalledVersion, { version: '1.0.0' }));
  });

  it('does not re-issue the discovery queries while a search is active', async () => {
    await createFixture();
    search('spotify');
    await settle();
    api.getStoreCatalog.calls.reset();

    push('StoreCatalogChangedEvent', {});
    await settle();

    expect(api.getStoreCatalog).not.toHaveBeenCalledWith(jasmine.objectContaining({ section: 'newest' }));
    expect(api.getStoreCatalog).not.toHaveBeenCalledWith(jasmine.objectContaining({ section: 'featured' }));
  });

  it('leaves out a discovery row that would just repeat the whole grid', async () => {
    await createFixture({ grid: items(3), newest: items(3), recentlyUpdated: items(3) });

    expect(sections().length).toBe(1);
    expect(cardCount()).toBe(3);
  });

  it('lists every item of a small catalog exactly once, without discovery rows', async () => {
    await createFixture({ grid: items(11), newest: items(3), recentlyUpdated: items(3), featured: [item('p1')] });

    expect(sections().length).toBe(1);
    expect(cardCount()).toBe(11);
  });

  it('shows the discovery rows once the catalog is at least twice a row long', async () => {
    await createFixture({ grid: items(12), newest: items(3, 'n'), recentlyUpdated: items(3, 'n') });

    expect(sectionNamed(AppStrings.Store.Page.NewAndUpdatedSectionHeading)).toBeDefined();
  });

  it('puts each item in at most one discovery row', async () => {
    await createFixture({
      grid: [item('a'), item('b'), item('c'), ...items(20)],
      featured: [item('a')],
      newest: [item('a'), item('b'), item('c')],
      installs: { a: 50, b: 20 },
    });

    expect(sectionNamed(AppStrings.Store.Page.FeaturedSectionHeading)!.items().map(entry => entry.id)).toEqual(['a']);
    expect(sectionNamed(AppStrings.Store.Page.PopularSectionHeading)!.items().map(entry => entry.id)).toEqual(['b']);
    expect(sectionNamed(AppStrings.Store.Page.NewAndUpdatedSectionHeading)!.items().map(entry => entry.id)).toEqual(['c']);
  });

  it('leaves items nobody installed out of the popular row', async () => {
    await createFixture({ grid: items(20) });

    expect(sectionNamed(AppStrings.Store.Page.PopularSectionHeading)).toBeUndefined();
  });

  it('lists an extension once in the new and updated row, however many orderings name it', async () => {
    await createFixture({
      grid: items(40),
      newest: [item('a'), item('b'), item('c')],
      recentlyUpdated: [item('b'), item('c'), item('d')],
    });

    const fresh = sections().find(section => section.heading() === translate(AppStrings.Store.Page.NewAndUpdatedSectionHeading));
    expect(fresh).toBeDefined();
    expect(fresh!.items().map(entry => entry.id)).toEqual(['a', 'b', 'c', 'd']);
  });

  it('does not repeat a featured pick in the new and updated row', async () => {
    await createFixture({
      grid: items(40),
      featured: [item('a')],
      newest: [item('a'), item('b')],
      recentlyUpdated: [item('c')],
    });

    const headings = sections().map(section => section.heading());
    expect(headings).toContain(translate(AppStrings.Store.Page.FeaturedSectionHeading));

    const fresh = sections().find(section => section.heading() === translate(AppStrings.Store.Page.NewAndUpdatedSectionHeading));
    expect(fresh!.items().map(entry => entry.id)).toEqual(['b', 'c']);
  });

  it('adds no featured row at all when the registry publishes no picks', async () => {
    await createFixture({ grid: items(40), newest: items(3, 'n') });

    expect(sections().map(section => section.heading()))
      .not.toContain(translate(AppStrings.Store.Page.FeaturedSectionHeading));
  });

  it('keeps the registry curation order in the featured row whatever the grid is sorted by', async () => {
    await createFixture({
      grid: items(40),
      featured: [item('z', { name: 'Zulu' }), item('a', { name: 'Alpha' }), item('m', { name: 'Mike' })],
    });

    const featured = sections().find(section => section.heading() === translate(AppStrings.Store.Page.FeaturedSectionHeading));
    expect(featured!.items().map(entry => entry.id)).toEqual(['z', 'a', 'm']);
  });

  it('appends the next page instead of replacing what is on screen', async () => {
    await createFixture({ grid: items(40), gridTotal: 40 });
    expect(cardCount()).toBe(24);

    api.getStoreCatalog.calls.reset();
    findButton(translate(AppStrings.Store.Page.LoadMoreAction))!.click();
    await settle();

    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ skip: 24 }));
    expect(cardCount()).toBe(40);
    expect(findButton(translate(AppStrings.Store.Page.LoadMoreAction))).toBeUndefined();
  });

  it('offers nothing more to load when the first page is the whole catalog', async () => {
    await createFixture({ grid: items(12), gridTotal: 12 });

    expect(cardCount()).toBe(12);
    expect(findButton(translate(AppStrings.Store.Page.LoadMoreAction))).toBeUndefined();
  });

  it('starts a new search at the first page rather than appending to the last one', async () => {
    await createFixture({ grid: items(40), gridTotal: 40 });
    findButton(translate(AppStrings.Store.Page.LoadMoreAction))!.click();
    await settle();
    expect(cardCount()).toBe(40);

    catalog.grid = items(3, 'hit');
    catalog.gridTotal = 3;
    api.getStoreCatalog.calls.reset();
    search('hue');
    await settle();

    expect(calls().every(call => (call.skip ?? 0) === 0)).toBeTrue();
    expect(cardCount()).toBe(3);
  });

  it('keeps an explicitly chosen ordering when a search term is typed', async () => {
    await createFixture({ grid: items(5) });
    chooseSort('newest');
    await settle();
    api.getStoreCatalog.calls.reset();

    search('hue');
    await settle();

    expect(api.getStoreCatalog)
      .toHaveBeenCalledWith(jasmine.objectContaining({ search: 'hue', section: 'newest' }));
  });

  it('offers best match only while there is a term to be relevant to', async () => {
    await createFixture({ grid: items(5) });

    const sort = fixture.debugElement.query(By.directive(SelectComponent)).componentInstance as SelectComponent;
    expect(sort.options.map(option => option.value)).toEqual(['popular', 'name', 'newest', 'recentlyUpdated']);

    search('hue');
    await settle();
    expect(sort.options.map(option => option.value)).toEqual(['all', 'popular', 'name', 'newest', 'recentlyUpdated']);
    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ search: 'hue', section: 'all' }));

    search('');
    await settle();
    expect(sort.options.map(option => option.value)).toEqual(['popular', 'name', 'newest', 'recentlyUpdated']);
    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ section: 'popular' }));
  });

  describe('returning from a details page', () => {
    afterEach(() => {
      initialParams = {};
      currentNavigation = null;
    });

    it('reopens with the category and order the reader had chosen', async () => {
      initialParams = { kind: 'IconPack', sort: 'newest' };
      await createFixture({ grid: items(3) });

      expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ kinds: ['IconPack'], section: 'newest' }));
    });

    it('keeps the category and order in the address so going back restores them', async () => {
      await createFixture({ grid: items(3) });

      chooseKind('Plugin');
      chooseSort('name');
      await settle();

      expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
        queryParams: { q: null, kind: 'Plugin', sort: 'name' },
        replaceUrl: true,
      }));
    });

    it('comes back to the same results and scroll position after going back from an entry', async () => {
      localStorage.removeItem(STORE_SUPPORTED_ONLY_STORAGE_KEY);
      await createFixture({ grid: items(60), gridTotal: 60 });
      findButton(translate(AppStrings.Store.Page.LoadMoreAction))!.click();
      await settle();
      expect(cardCount()).toBe(48);
      const state = TestBed.inject(StoreBrowseStateService);
      spyOn(state, 'save').and.callThrough();
      (fixture.componentInstance as unknown as { onScroll(event: Event): void })
        .onScroll({ target: { scrollTop: 640 } } as unknown as Event);

      currentNavigation = { finalUrl: '/store/Plugin/p1', trigger: 'imperative' } as unknown as Navigation;
      fixture.destroy();
      expect(state.save).toHaveBeenCalledWith('discover', jasmine.objectContaining({ scrollTop: 640, itemCount: 48 }));

      currentNavigation = { trigger: 'popstate' } as unknown as Navigation;
      fixture = TestBed.createComponent(StorePageComponent);
      fixture.detectChanges();
      await settle();

      expect(cardCount()).toBe(48);
    });

    it('starts from the top when the Store is opened again rather than returned to', async () => {
      localStorage.removeItem(STORE_SUPPORTED_ONLY_STORAGE_KEY);
      await createFixture({ grid: items(60), gridTotal: 60 });
      findButton(translate(AppStrings.Store.Page.LoadMoreAction))!.click();
      await settle();

      currentNavigation = { finalUrl: '/store/Plugin/p1', trigger: 'imperative' } as unknown as Navigation;
      fixture.destroy();
      currentNavigation = { trigger: 'imperative' } as unknown as Navigation;
      fixture = TestBed.createComponent(StorePageComponent);
      fixture.detectChanges();
      await settle();

      expect(cardCount()).toBe(24);
    });

    it('follows an address that names a different view, such as a publisher link', async () => {
      await createFixture({ grid: items(3) });
      search('obs');
      chooseKind('Plugin');
      await settle();
      api.getStoreCatalog.calls.reset();

      queryParams.next(convertToParamMap({ publisher: 'PyFlat' }));
      await settle();

      expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({
        kinds: ['Plugin', 'IconPack'], search: undefined, publisher: 'PyFlat',
      }));
    });
  });

  describe('publisher filter', () => {
    it('lists only the publisher named in the link and lets the reader go back to everything', async () => {
      await createFixture({ grid: items(3) });
      api.getStoreCatalog.calls.reset();

      queryParams.next(convertToParamMap({ publisher: 'PyFlat' }));
      await settle();

      const gridCalls = calls().filter(call => call.take !== 1);
      expect(gridCalls.length).toBeGreaterThan(0);
      expect(gridCalls.every(call => (call as CatalogOptions & { publisher?: string }).publisher === 'PyFlat')).toBeTrue();
      expect(fixture.nativeElement.textContent).toContain(translate(AppStrings.Store.Page.PublisherHeading, { publisher: 'PyFlat' }));

      findButton(translate(AppStrings.Store.Page.ShowAllAction))!.click();
      expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { publisher: null } }));
    });

    it('says nothing matched rather than that the Store is empty when a publisher has nothing left', async () => {
      await createFixture({ grid: [] });
      queryParams.next(convertToParamMap({ publisher: 'Gone' }));
      await settle();

      expect(fixture.nativeElement.textContent).toContain(translate(AppStrings.Store.Page.NoResultsHeading));
      expect(fixture.nativeElement.textContent).not.toContain(translate(AppStrings.Store.Page.EmptyCatalogHeading));
    });
  });

  describe('tag filter', () => {
    type TaggedOptions = CatalogOptions & { publisher?: string; tag?: string };

    it('lists only items with the tag named in the link, counts included, and lets the reader go back', async () => {
      await createFixture({ grid: items(3) });
      api.getStoreCatalog.calls.reset();

      queryParams.next(convertToParamMap({ tag: 'streaming' }));
      await settle();

      expect(calls().length).toBeGreaterThan(0);
      expect(calls().every(call => (call as TaggedOptions).tag === 'streaming')).toBeTrue();
      expect(fixture.nativeElement.textContent).toContain(translate(AppStrings.Store.Page.TagHeading, { tag: 'streaming' }));
      expect(fixture.nativeElement.textContent).toContain(translate(AppStrings.Store.Page.TagFilterNote, { tag: 'streaming' }));

      findButton(translate(AppStrings.Store.Page.ShowAllAction))!.click();
      expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { tag: null } }));
    });

    it('lets the tag win when an address names both a tag and a publisher', async () => {
      await createFixture({ grid: items(2) });
      api.getStoreCatalog.calls.reset();

      queryParams.next(convertToParamMap({ tag: 'streaming', publisher: 'PyFlat' }));
      await settle();

      expect(calls().every(call => (call as TaggedOptions).tag === 'streaming' && !(call as TaggedOptions).publisher)).toBeTrue();
      expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { publisher: null } }));
      expect(fixture.nativeElement.textContent).not.toContain(translate(AppStrings.Store.Page.PublisherFilterNote, { publisher: 'PyFlat' }));
    });
  });

  describe('categories', () => {
    const music: StoreCategoryBody = { id: 'music', names: { en: 'Music', de: 'Musik' }, count: 2 };
    const streaming: StoreCategoryBody = { id: 'streaming', names: { en: 'Streaming' }, count: 1 };
    const gaming: StoreCategoryBody = { id: 'gaming', names: { en: 'Gaming', de: 'Spiele' }, count: 0 };

    function categoryChips(): HTMLButtonElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('button[data-category]'));
    }

    function chipLabels(): string[] {
      return categoryChips().map(chip => chip.textContent!.trim());
    }

    it('offers the categories that have items, in registry order, named in the reader\'s language', async () => {
      await createFixture({ categories: [streaming, gaming, music] }, () => {
        TestBed.inject(LocalizationService).culture.set('de-DE');
      });

      expect(chipLabels()).toEqual(['Streaming', 'Musik']);
      const group = fixture.nativeElement.querySelector('[role="group"] button[data-category]')!.parentElement as HTMLElement;
      expect(group.getAttribute('aria-label')).toBe(translate(AppStrings.Store.Page.CategoriesHeading));
    });

    it('filters by a category\'s tag when it is chosen, names it in the heading, and clears it when chosen again', async () => {
      await createFixture({ grid: items(3), categories: [music] });

      categoryChips()[0].click();
      expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
        queryParams: { tag: 'music', publisher: null },
      }));

      queryParams.next(convertToParamMap({ tag: 'music' }));
      await settle();

      expect(calls().some(call => (call as CatalogOptions & { tag?: string }).tag === 'music')).toBeTrue();
      expect(categoryChips()[0].getAttribute('aria-pressed')).toBe('true');
      expect(fixture.nativeElement.textContent).toContain(translate(AppStrings.Store.Page.TagHeading, { tag: 'Music' }));

      routerSpy.navigate.calls.reset();
      categoryChips()[0].click();
      expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
        queryParams: { tag: null, publisher: null },
      }));
    });

    it('counts categories for the kind the reader is browsing and the platform filter they chose', async () => {
      localStorage.removeItem(STORE_SUPPORTED_ONLY_STORAGE_KEY);
      await createFixture({ categories: [music] });
      const supportedOnly = api.getStoreCategories.calls.mostRecent().args[1];
      expect(api.getStoreCategories.calls.mostRecent().args[0]).toEqual(['Plugin', 'IconPack']);

      chooseKind('IconPack');
      await settle();
      expect(api.getStoreCategories.calls.mostRecent().args[0]).toEqual(['IconPack']);

      (fixture.componentInstance as unknown as { onSupportedOnlyChange(value: boolean): void })
        .onSupportedOnlyChange(!supportedOnly);
      await settle();
      expect(!!api.getStoreCategories.calls.mostRecent().args[1]).toBe(!supportedOnly);
      localStorage.removeItem(STORE_SUPPORTED_ONLY_STORAGE_KEY);
    });

    it('asks again for categories on the next load after a failed request', async () => {
      await createFixture({ grid: items(2), categories: 'unsupported' });
      catalog.categories = [music];
      api.getStoreCategories.calls.reset();

      search('x');
      await settle();

      expect(api.getStoreCategories).toHaveBeenCalled();
      expect(chipLabels()).toEqual(['Music']);
    });

    it('picks up categories a background registry refresh publishes', async () => {
      await createFixture({ categories: [] });
      expect(categoryChips().length).toBe(0);

      catalog.categories = [music];
      push('StoreCatalogChangedEvent', {});
      await settle();

      expect(chipLabels()).toEqual(['Music']);
    });

    it('shows no category chips when the host cannot list categories, and the Store still loads', async () => {
      await createFixture({ grid: items(2), categories: 'unsupported' });

      expect(categoryChips().length).toBe(0);
      expect(cardCount()).toBe(2);
    });
  });

  describe('platform filter', () => {
    beforeEach(() => localStorage.removeItem(STORE_SUPPORTED_ONLY_STORAGE_KEY));
    afterEach(() => localStorage.removeItem(STORE_SUPPORTED_ONLY_STORAGE_KEY));

    function toggle(): HTMLInputElement {
      return fixture.nativeElement.querySelector('.supported-toggle input');
    }

    it('shows only items available for this platform until the viewer turns the filter off', async () => {
      await createFixture({ grid: items(3) });

      expect(toggle().checked).toBeTrue();
      expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ section: 'popular', supportedOnly: true }));

      api.getStoreCatalog.calls.reset();
      toggle().click();
      await settle();

      expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ section: 'popular', supportedOnly: undefined }));
      expect(localStorage.getItem(STORE_SUPPORTED_ONLY_STORAGE_KEY)).toBe('false');
    });

    it('remembers that the viewer turned the filter off', async () => {
      localStorage.setItem(STORE_SUPPORTED_ONLY_STORAGE_KEY, 'false');
      await createFixture({ grid: items(3) });

      expect(toggle().checked).toBeFalse();
    });

    it('says how many items the filter hides, for the same search, and offers to show them', async () => {
      await createFixture({ grid: items(3), unsupportedCount: 4 });
      api.getStoreCatalog.calls.reset();
      search('deck');
      await settle();

      expect(fixture.nativeElement.querySelector('.hidden-note')?.textContent)
        .toContain(translate(AppStrings.Store.Page.HiddenUnsupported, { count: 4 }));
      const countQueries = calls().filter(call => call.take === 1);
      expect(countQueries.length).toBeGreaterThan(0);
      expect(countQueries.every(call => call.search === 'deck')).toBeTrue();

      findButton(translate(AppStrings.Store.Page.ShowAllAction))!.click();
      await settle();
      expect(fixture.nativeElement.querySelector('.hidden-note')).toBeNull();
    });
  });

  describe('uninstall confirmation', () => {
    function uninstallButtons(): HTMLElement[] {
      return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
        .filter(button => button.textContent?.trim() === translate(AppStrings.Store.Uninstall));
    }

    async function openConfirmation(): Promise<void> {
      uninstallButtons()[0].click();
      fixture.detectChanges();
      await fixture.whenStable();
    }

    it('opens the confirmation when a card requests an uninstall, and calls the operations service exactly once on confirm', async () => {
      await createFixture({ grid: [item('p1', { installState: 'Installed', installedVersion: '1.0.0' })] });
      api.uninstallStoreExtension.and.resolveTo({ success: true });
      await openConfirmation();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).not.toBeNull();
      expect(api.uninstallStoreExtension).not.toHaveBeenCalled();

      jasmine.clock().install();
      try {
        // The confirmation modal's own confirm button carries the same label as the card's, and is
        // the last one in the DOM. ModalComponent plays a 150ms close animation before its confirm
        // output actually fires (see ModalComponent.dismiss).
        uninstallButtons()[uninstallButtons().length - 1].click();
        jasmine.clock().tick(150);
        fixture.detectChanges();
        await fixture.whenStable();
      } finally {
        jasmine.clock().uninstall();
      }

      expect(api.uninstallStoreExtension).toHaveBeenCalledTimes(1);
      expect(api.uninstallStoreExtension).toHaveBeenCalledWith('Plugin', 'p1');
    });

    it('calls the operations service zero times when the confirmation is cancelled', async () => {
      await createFixture({ grid: [item('p1', { installState: 'Installed', installedVersion: '1.0.0' })] });
      api.uninstallStoreExtension.and.resolveTo({ success: true });
      await openConfirmation();

      jasmine.clock().install();
      try {
        findButton(translate('macrodeck:Common.Cancel'))!.click();
        jasmine.clock().tick(150);
        fixture.detectChanges();
        await fixture.whenStable();
      } finally {
        jasmine.clock().uninstall();
      }

      expect(api.uninstallStoreExtension).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
    });

    it('uses the per-kind uninstall message, resolved through the localization service rather than a hardcoded literal', async () => {
      await createFixture({
        grid: [item('ip1', {
          kind: 'IconPack', name: 'Neon Icons', installState: 'Installed', installedVersion: '1.0.0',
        })],
      });
      await openConfirmation();

      const expected = translate(AppStrings.Store.UninstallMessageIconPack, { name: 'Neon Icons' });
      const pluginWording = translate(AppStrings.Store.UninstallMessagePlugin, { name: 'Neon Icons' });
      expect(fixture.nativeElement.textContent).toContain(expected);
      expect(fixture.nativeElement.textContent).not.toContain(pluginWording);
    });

    it('surfaces a failed uninstall with its localized reason, never the raw host message', async () => {
      await createFixture({ grid: [item('p1', { installState: 'Installed', installedVersion: '1.0.0' })] });
      api.uninstallStoreExtension.and.resolveTo({
        success: false,
        error: { code: 'DependencyInUse', message: 'raw host message' },
      });
      await openConfirmation();

      jasmine.clock().install();
      try {
        uninstallButtons()[uninstallButtons().length - 1].click();
        jasmine.clock().tick(150);
        fixture.detectChanges();
        await fixture.whenStable();
      } finally {
        jasmine.clock().uninstall();
      }

      expect(api.uninstallStoreExtension).toHaveBeenCalledWith('Plugin', 'p1');

      const toasts = TestBed.inject(ToastService).toasts();
      expect(toasts.length).toBe(1);
      expect(toasts[0].message).toBe(translate(AppStrings.Store.UninstallFailed));
      expect(toasts[0].detail).toBe(translate(AppStrings.Store.UninstallDependencyInUse));
      expect(toasts[0].detail).not.toBe('raw host message');
    });

    it('renders the confirmation modal outside every card, never inside .card-install', async () => {
      await createFixture({ grid: [item('p1', { installState: 'Installed', installedVersion: '1.0.0' })] });
      await openConfirmation();

      const modal = fixture.nativeElement.querySelector('shared-confirmation-modal');
      expect(modal).not.toBeNull();

      const cards = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-store-extension-card'));
      expect(cards.length).toBeGreaterThan(0);
      for (const card of cards) {
        expect(card.contains(modal)).toBeFalse();
      }

      const cardInstalls = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.card-install'));
      expect(cardInstalls.length).toBeGreaterThan(0);
      for (const cardInstall of cardInstalls) {
        expect(cardInstall.querySelector('shared-confirmation-modal')).toBeNull();
      }
    });
  });

  it('opens the catalog without a signed-in account', async () => {
    await createFixture();

    expect((fixture.nativeElement.querySelector('.store-page') as HTMLElement).hasAttribute('inert')).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('shared-store-extension-card').length).toBeGreaterThan(0);
  });
});
