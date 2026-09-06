import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { WidgetType, WidgetTypeCatalogChangedEvent } from '@macro-deck/runtime';
import { ApiService, WidgetTypeInfo } from '../transport';
import { WidgetTypeCatalogService } from './widget-type-catalog.service';

function typeInfo(overrides: Partial<WidgetTypeInfo> & { id: string }): WidgetTypeInfo {
  return {
    providerId: '',
    isBuiltIn: true,
    defaultData: {},
    supportsConfigUi: false,
    configUiModelVersion: 0,
    ...overrides,
  };
}

describe('WidgetTypeCatalogService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let catalogChanged$: Subject<WidgetTypeCatalogChangedEvent>;
  let service: WidgetTypeCatalogService;

  beforeEach(() => {
    catalogChanged$ = new Subject<WidgetTypeCatalogChangedEvent>();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getWidgetTypes', 'onWidgetTypeCatalogChanged']);
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(catalogChanged$);
    // Disconnected throughout: the reconnect effect must not itself call getWidgetTypes and confuse
    // this suite's call-count assertions, which are about load()/infoFor() triggering fetches.
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(WidgetTypeCatalogService);
  });

  it('resolves the catalogue entry for a known type, fetching the catalogue once for every caller', async () => {
    apiSpy.getWidgetTypes.and.resolveTo({
      success: true,
      types: [typeInfo({ id: WidgetType.Clock, supportsConfigUi: true, configUiModelVersion: 4 })],
    });

    const [clock, again] = await Promise.all([
      service.infoFor(WidgetType.Clock),
      service.infoFor(WidgetType.Clock),
    ]);

    expect(clock).toEqual(typeInfo({ id: WidgetType.Clock, supportsConfigUi: true, configUiModelVersion: 4 }));
    expect(again).toEqual(clock);
    expect(apiSpy.getWidgetTypes).toHaveBeenCalledTimes(1);
  });

  it('resolves null for a type the host does not report', async () => {
    apiSpy.getWidgetTypes.and.resolveTo({ success: true, types: [typeInfo({ id: WidgetType.Clock })] });

    const info = await service.infoFor('com.example.gauge');

    expect(info).toBeNull();
  });

  it('leaves the registry usable when the fetch fails, and does not cache that failure permanently', async () => {
    apiSpy.getWidgetTypes.and.rejectWith(new Error('network down'));

    await expectAsync(service.infoFor(WidgetType.Clock)).toBeResolvedTo(null);
    expect(apiSpy.getWidgetTypes).toHaveBeenCalledTimes(1);

    apiSpy.getWidgetTypes.and.resolveTo({
      success: true,
      types: [typeInfo({ id: WidgetType.Clock, supportsConfigUi: true, configUiModelVersion: 4 })],
    });
    const info = await service.infoFor(WidgetType.Clock);

    expect(info?.supportsConfigUi).toBeTrue();
    expect(apiSpy.getWidgetTypes).toHaveBeenCalledTimes(2);
  });

  it('treats an unsuccessful response the same as a failed fetch', async () => {
    apiSpy.getWidgetTypes.and.resolveTo({ success: false, error: { code: 'x', message: 'nope' }, types: [] });

    await expectAsync(service.infoFor(WidgetType.Clock)).toBeResolvedTo(null);
  });

  it('replaces the cached catalogue when a catalogue-changed event arrives, with no reload needed', async () => {
    apiSpy.getWidgetTypes.and.resolveTo({ success: true, types: [typeInfo({ id: WidgetType.Clock })] });
    await service.load();
    expect(service.types().map(t => t.id)).toEqual([WidgetType.Clock]);

    // A plugin registers a new type after the first fetch - the push carries the whole catalogue, so
    // the type it just registered is visible immediately, without anyone calling load() again.
    catalogChanged$.next({
      types: [typeInfo({ id: WidgetType.Clock }), typeInfo({ id: 'com.example.gauge', isBuiltIn: false })],
    });

    expect(service.types().map(t => t.id)).toEqual([WidgetType.Clock, 'com.example.gauge']);
    expect(apiSpy.getWidgetTypes).toHaveBeenCalledTimes(1);
  });
});
