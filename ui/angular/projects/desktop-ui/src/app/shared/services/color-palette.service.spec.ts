import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ColorPaletteChangedEvent } from '@macro-deck/runtime';
import { ApiService, ColorPaletteResponse } from '../transport';
import { ColorPaletteService } from './color-palette.service';
import { ToastService } from './toast.service';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('ColorPaletteService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let changed$: Subject<ColorPaletteChangedEvent>;
  let connection: ReturnType<typeof signal<string>>;
  let service: ColorPaletteService;
  let toasts: ToastService;

  beforeEach(() => {
    changed$ = new Subject<ColorPaletteChangedEvent>();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService',
      ['getColorPalette', 'setColorPaletteEntry', 'restoreDefaultColorPalette', 'onColorPaletteChanged', 'onNotification']);
    apiSpy.onNotification.and.returnValue(new Subject());
    apiSpy.onColorPaletteChanged.and.returnValue(changed$);
    apiSpy.getColorPalette.and.resolveTo({ success: true, colors: ['#3ff4ee'] });
    connection = signal('disconnected');
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connection });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(ColorPaletteService);
    toasts = TestBed.inject(ToastService);
  });

  it('loads the host palette once connected', async () => {
    connection.set('connected');
    TestBed.tick();
    await apiSpy.getColorPalette.calls.mostRecent().returnValue;

    expect(service.colors()).toEqual(['#3ff4ee']);
  });

  it('adopts a colour another window added', () => {
    changed$.next({ colors: ['#111111', '#222222'] });

    expect(service.colors()).toEqual(['#111111', '#222222']);
  });

  it('offers the default colours until the host answers', () => {
    expect(service.colors()).toContain('#ef4444');
    expect(service.colors().length).toBe(7);
  });

  it('shows an added colour at once and keeps what the host confirms', async () => {
    changed$.next({ colors: [] });
    let resolve!: (response: ColorPaletteResponse) => void;
    apiSpy.setColorPaletteEntry.and.returnValue(new Promise(r => resolve = r));

    const adding = service.add('#ABCDEF');
    expect(service.colors()).toEqual(['#abcdef']);

    resolve({ success: true, colors: ['#111111', '#abcdef'] });
    await adding;

    expect(apiSpy.setColorPaletteEntry).toHaveBeenCalledWith('#abcdef', true);
    expect(service.colors()).toEqual(['#111111', '#abcdef']);
  });

  it('puts the palette back and says so when the host refuses', async () => {
    changed$.next({ colors: ['#111111'] });
    apiSpy.setColorPaletteEntry.and.resolveTo({
      success: false,
      error: { code: 'COLOR_PALETTE_LIMIT', message: 'full' },
      colors: ['#111111'],
    });
    const show = spyOn(toasts, 'show');

    await service.add('#222222');

    expect(service.colors()).toEqual(['#111111']);
    expect(show).toHaveBeenCalledWith(jasmine.any(String), jasmine.objectContaining({ variant: 'error' }));
  });

  it('removes a colour without asking the host twice for one that is already gone', async () => {
    changed$.next({ colors: ['#111111', '#222222'] });
    apiSpy.setColorPaletteEntry.and.resolveTo({ success: true, colors: ['#222222'] });

    await service.remove('#111111');
    await service.remove('#111111');

    expect(service.colors()).toEqual(['#222222']);
    expect(apiSpy.setColorPaletteEntry).toHaveBeenCalledTimes(1);
  });

  it('takes the default colours the host restores', async () => {
    changed$.next({ colors: [] });
    apiSpy.restoreDefaultColorPalette.and.resolveTo({ success: true, colors: ['#ef4444', '#3b82f6'] });

    await service.restoreDefaults();

    expect(service.colors()).toEqual(['#ef4444', '#3b82f6']);
  });
});
