import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { GetStoreCatalogResponse, StoreCatalogItemBody, StoreRegistryStatusBody } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { STORE_CATALOG_PAGE_SIZE, StoreCatalogQuery, StoreCatalogService } from './store-catalog.service';

function item(id: string): StoreCatalogItemBody {
  return {
    kind: 'Plugin',
    id,
    name: `Extension ${id}`,
    latestVersion: '1.0.0',
    installState: 'NotInstalled',
    trust: 'RegistryAuthenticated',
    hasIcon: false,
  };
}

function registry(overrides: Partial<StoreRegistryStatusBody> = {}): StoreRegistryStatusBody {
  return {
    hasCatalog: true,
    sequence: 1,
    refreshing: false,
    stale: false,
    ...overrides,
  };
}

const browse: StoreCatalogQuery = { kinds: ['Plugin', 'IconPack'], section: 'name' };

describe('StoreCatalogService', () => {
  let service: StoreCatalogService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getStoreCatalog', 'refreshStoreRegistry',
    ]);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getStoreCatalog.and.resolveTo({ items: [item('p1')], total: 1, registry: registry() });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(StoreCatalogService);
    api = apiSpy;
  });

  it('loads what the caller asked for', async () => {
    await service.load(browse);

    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining(browse));
    expect(service.items().map(entry => entry.id)).toEqual(['p1']);
  });

  it('re-issues the query it was last given when the transport reconnects', async () => {
    await service.load({ kinds: ['IconPack'], search: 'hue', section: 'newest' });
    api.getStoreCatalog.calls.reset();

    connectionState.set('connected');
    TestBed.tick();
    for (let attempt = 0; attempt < 10; attempt++) {
      await Promise.resolve();
    }

    expect(api.getStoreCatalog)
      .toHaveBeenCalledWith(jasmine.objectContaining({ kinds: ['IconPack'], search: 'hue', section: 'newest' }));
  });

  it('asks the host for nothing until it has been given a query', async () => {
    connectionState.set('connected');
    await service.load();

    expect(api.getStoreCatalog).not.toHaveBeenCalled();
  });

  it('keeps the previously loaded items and reports the registry as stale when a reload fails', async () => {
    await service.load(browse);
    expect(service.items().length).toBe(1);

    api.getStoreCatalog.and.rejectWith(new Error('registry unreachable'));

    await service.load({ ...browse, search: 'anything' });

    expect(service.loadError()).toBe('registry unreachable');
    expect(service.items().map(i => i.id)).toEqual(['p1']);
    expect(service.registry()?.stale).toBeTrue();
  });

  it('reports how much there is left to reach, not just what it holds', async () => {
    api.getStoreCatalog.and.resolveTo({ items: [item('p1'), item('p2')], total: 40, registry: registry() });

    await service.load(browse);

    expect(service.total()).toBe(40);
    expect(service.hasMore()).toBeTrue();
  });

  it('appends the next page rather than replacing the one on screen', async () => {
    api.getStoreCatalog.and.resolveTo({ items: [item('p1'), item('p2')], total: 4, registry: registry() });
    await service.load(browse);

    api.getStoreCatalog.and.resolveTo({ items: [item('p3'), item('p4')], total: 4, registry: registry() });
    await service.loadMore();

    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({ skip: 2 }));
    expect(service.items().map(entry => entry.id)).toEqual(['p1', 'p2', 'p3', 'p4']);
    expect(service.hasMore()).toBeFalse();
  });

  it('keeps the pages already loaded when the next one fails', async () => {
    api.getStoreCatalog.and.resolveTo({ items: [item('p1'), item('p2')], total: 40, registry: registry() });
    await service.load(browse);

    api.getStoreCatalog.and.rejectWith(new Error('registry unreachable'));
    await service.loadMore();

    expect(service.items().map(entry => entry.id)).toEqual(['p1', 'p2']);
    expect(service.loadError()).toBe('registry unreachable');
  });

  it('keeps every loaded page when the host says the catalog changed', async () => {
    // More than one page's worth, so the reload genuinely has a second page to put back.
    const published = Array.from({ length: 30 }, (_, index) => item(`p${index + 1}`));
    api.getStoreCatalog.and.callFake((options?: { skip?: number; take?: number }) => {
      const skip = options?.skip ?? 0;
      const take = options?.take ?? published.length;
      return Promise.resolve({
        items: published.slice(skip, skip + take),
        total: published.length,
        registry: registry(),
      });
    });
    await service.load(browse);
    expect(service.items().length).toBe(STORE_CATALOG_PAGE_SIZE);
    await service.loadMore();
    expect(service.items().length).toBe(30);

    notifications.get('StoreCatalogChangedEvent')?.next({});
    for (let attempt = 0; attempt < 10; attempt++) {
      await Promise.resolve();
    }

    expect(service.items()).toHaveSize(30);
    expect(service.items()[29].id).toBe('p30');
  });

  it('keeps the newest query when an earlier one answers late', async () => {
    let resolveFirst: (value: GetStoreCatalogResponse) => void = () => undefined;
    api.getStoreCatalog.and.returnValue(new Promise<GetStoreCatalogResponse>(resolve => {
      resolveFirst = resolve;
    }));
    const first = service.load({ ...browse, search: 'stale' });

    api.getStoreCatalog.and.resolveTo({ items: [item('current')], total: 1, registry: registry() });
    await service.load({ ...browse, search: 'current' });

    resolveFirst({ items: [item('stale')], total: 1, registry: registry() });
    await first;

    expect(service.items().map(entry => entry.id)).toEqual(['current']);
  });

  it('does not append a page while the whole list is being replaced', async () => {
    api.getStoreCatalog.and.resolveTo({ items: [item('p1')], total: 40, registry: registry() });
    await service.load(browse);

    let resolveReload: (value: GetStoreCatalogResponse) => void = () => undefined;
    api.getStoreCatalog.and.returnValue(new Promise<GetStoreCatalogResponse>(resolve => {
      resolveReload = resolve;
    }));
    notifications.get('StoreCatalogChangedEvent')?.next({});
    await Promise.resolve();
    api.getStoreCatalog.calls.reset();

    await service.loadMore();

    expect(api.getStoreCatalog).not.toHaveBeenCalled();
    resolveReload({ items: [item('p1')], total: 40, registry: registry() });
  });

  it('reflects a stale registry status once the host reports one', async () => {
    api.getStoreCatalog.and.resolveTo({ items: [item('p1')], total: 1, registry: registry({ stale: true }) });

    await service.load(browse);

    expect(service.registry()?.stale).toBeTrue();
  });
});
