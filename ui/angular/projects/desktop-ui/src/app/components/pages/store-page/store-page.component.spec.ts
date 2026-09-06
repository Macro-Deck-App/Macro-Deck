import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, Subject } from 'rxjs';

import { AppStrings, GetStoreCatalogResponse, StoreCatalogItemBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, SegmentedControlComponent, ToastService } from '@shared';
import { SelectComponent } from '../../forms/select/select.component';
import { StoreExtensionCardComponent } from '../../store/store-extension-card.component';
import { StoreSectionComponent } from '../../store/store-section.component';
import { StorePageComponent } from './store-page.component';

interface CatalogOptions {
  kinds?: string[];
  search?: string;
  section?: string;
  skip?: number;
  take?: number;
}

interface Catalog {
  grid: StoreCatalogItemBody[];
  gridTotal?: number;
  featured?: StoreCatalogItemBody[];
  newest?: StoreCatalogItemBody[];
  recentlyUpdated?: StoreCatalogItemBody[];
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
    return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(button => button.textContent?.trim() === label);
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

  async function createFixture(configuration: Partial<Catalog> = {}): Promise<void> {
    catalog = { grid: [item('p1')], ...configuration };
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getStoreCatalog', 'refreshStoreRegistry', 'getStoreOperations', 'installStoreExtension',
      'retryStoreOperation', 'getStoreExtensionIconUrl', 'uninstallStoreExtension',
    ]);
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
          return Promise.resolve(respond(catalog.grid.slice(skip, skip + take),
            catalog.gridTotal ?? catalog.grid.length));
        }
      }
    });
    api.getStoreOperations.and.resolveTo({ operations: [] });
    api.getStoreExtensionIconUrl.and.returnValue('');
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('connected') });

    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [StorePageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => null } } },
        },
      ],
    });

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

  it('renders one card per catalog result', async () => {
    await createFixture({ grid: items(3) });

    expect(cardCount()).toBe(3);
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

    expect(calls().length).toBeGreaterThan(0);
    for (const call of calls()) {
      expect(call.kinds).toEqual(['Plugin', 'IconPack']);
    }

    const filter = fixture.debugElement.query(By.directive(SegmentedControlComponent))
      .componentInstance as SegmentedControlComponent;
    expect(filter.options.map(option => option.value)).toEqual(['all', 'Plugin', 'IconPack']);
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

    const badges = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-store-state-badge .badge'));
    expect(badges.length).toBeGreaterThan(0);
    expect(badges.every(badge => badge.textContent?.trim() === translate(AppStrings.Store.State.Installed))).toBeTrue();
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

  it('shows the discovery row once the grid holds more than the row does', async () => {
    await createFixture({ grid: items(4), newest: items(3), recentlyUpdated: items(3) });

    expect(sections().length).toBe(2);
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
    expect(sort.options.map(option => option.value)).toEqual(['name', 'newest', 'recentlyUpdated']);

    search('hue');
    await settle();
    expect(sort.options.map(option => option.value)).toEqual(['all', 'name', 'newest', 'recentlyUpdated']);
    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ search: 'hue', section: 'all' }));

    search('');
    await settle();
    expect(sort.options.map(option => option.value)).toEqual(['name', 'newest', 'recentlyUpdated']);
    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ section: 'name' }));
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
});
