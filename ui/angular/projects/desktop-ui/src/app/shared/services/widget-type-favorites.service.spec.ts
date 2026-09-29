import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { WidgetTypeFavoritesChangedEvent } from '@macro-deck/runtime';
import { ApiService, WidgetTypeFavoritesResponse } from '../transport';
import { ToastService } from './toast.service';
import { WidgetTypeFavoritesService } from './widget-type-favorites.service';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('WidgetTypeFavoritesService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let changed$: Subject<WidgetTypeFavoritesChangedEvent>;
  let connection: ReturnType<typeof signal<string>>;
  let service: WidgetTypeFavoritesService;
  let toasts: ToastService;

  beforeEach(() => {
    changed$ = new Subject<WidgetTypeFavoritesChangedEvent>();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService',
      ['getWidgetTypeFavorites', 'setWidgetTypeFavorite', 'onWidgetTypeFavoritesChanged', 'onNotification']);
    apiSpy.onNotification.and.returnValue(new Subject());
    apiSpy.onWidgetTypeFavoritesChanged.and.returnValue(changed$);
    apiSpy.getWidgetTypeFavorites.and.resolveTo({ success: true, typeIds: ['clock'] });
    connection = signal('disconnected');
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connection });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(WidgetTypeFavoritesService);
    toasts = TestBed.inject(ToastService);
  });

  it('loads the host favorites once connected', async () => {
    connection.set('connected');
    TestBed.tick();
    await apiSpy.getWidgetTypeFavorites.calls.mostRecent().returnValue;

    expect([...service.ids()]).toEqual(['clock']);
  });

  it('adopts a change made in another UI', () => {
    changed$.next({ typeIds: ['weather', 'clock'] });

    expect(service.isFavorite('weather')).toBeTrue();
    expect(service.isFavorite('clock')).toBeTrue();
  });

  it('shows the star at once and keeps what the host confirms', async () => {
    let resolve!: (response: WidgetTypeFavoritesResponse) => void;
    apiSpy.setWidgetTypeFavorite.and.returnValue(new Promise(r => resolve = r));

    const toggling = service.toggle('slider');
    expect(service.isFavorite('slider')).toBeTrue();

    resolve({ success: true, typeIds: ['slider'] });
    await toggling;

    expect(apiSpy.setWidgetTypeFavorite).toHaveBeenCalledWith('slider', true);
    expect([...service.ids()]).toEqual(['slider']);
  });

  it('ignores a reply that a newer toggle has already overtaken', async () => {
    const replies: ((response: WidgetTypeFavoritesResponse) => void)[] = [];
    apiSpy.setWidgetTypeFavorite.and.callFake(() => new Promise(r => replies.push(r)));

    const first = service.toggle('slider');
    const second = service.toggle('clock');
    replies[1]({ success: true, typeIds: ['slider', 'clock'] });
    await second;
    replies[0]({ success: true, typeIds: ['slider'] });
    await first;

    expect([...service.ids()].sort()).toEqual(['clock', 'slider']);
  });

  it('undoes only its own toggle when saving fails, keeping a change another UI made meanwhile', async () => {
    let reject!: (error: Error) => void;
    apiSpy.setWidgetTypeFavorite.and.returnValue(new Promise((_, r) => reject = r));

    const toggling = service.toggle('slider');
    changed$.next({ typeIds: ['weather', 'slider'] });
    reject(new Error('offline'));
    await toggling;

    expect(service.isFavorite('slider')).toBeFalse();
    expect(service.isFavorite('weather')).toBeTrue();
    expect(toasts.toasts().map(toast => toast.variant)).toEqual(['error']);
  });

  it('takes the host list when the host refuses the change', async () => {
    apiSpy.setWidgetTypeFavorite.and.resolveTo({
      success: false,
      error: { code: 'FAVORITES_LIMIT', message: 'At most 256 widgets can be favorites.' },
      typeIds: ['clock'],
    });

    await service.toggle('slider');

    expect([...service.ids()]).toEqual(['clock']);
    expect(toasts.toasts()[0].detail).toBe('At most 256 widgets can be favorites.');
  });
});
