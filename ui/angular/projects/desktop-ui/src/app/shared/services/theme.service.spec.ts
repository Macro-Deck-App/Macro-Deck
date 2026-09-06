import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { ApiService, ConnectionState } from '../transport';
import { DEFAULT_ACCENT_COLOR, DEFAULT_THEME_MODE, ThemeService } from './theme.service';

describe('ThemeService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let connectionState: ReturnType<typeof signal<ConnectionState>>;

  function resetAppearanceRoot(): void {
    localStorage.clear();
    document.documentElement.classList.remove('light', 'dark');
    document.documentElement.style.removeProperty('--color-accent');
    document.documentElement.style.removeProperty('--color-accent-hover');
    document.documentElement.style.removeProperty('--color-accent-muted');
  }

  afterEach(resetAppearanceRoot);

  beforeEach(() => {
    resetAppearanceRoot();

    notifications = new Map();
    connectionState = signal<ConnectionState>('disconnected');
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getAppearanceSettings',
      'updateAppearanceSettings',
      'onNotification',
    ], { connectionStateSignal: connectionState });
    apiSpy.getAppearanceSettings.and.resolveTo({ themeMode: 'dark', accentColor: '#123456' });
    apiSpy.updateAppearanceSettings.and.resolveTo({ themeMode: 'light', accentColor: '#abcdef' });
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
  });

  function create(): ThemeService {
    return TestBed.inject(ThemeService);
  }

  it('starts with the documented defaults', () => {
    const service = create();

    expect(service.themeMode()).toBe(DEFAULT_THEME_MODE);
    expect(service.accentColor()).toBe(DEFAULT_ACCENT_COLOR);
  });

  it('resolves explicit modes regardless of system preference', () => {
    const service = create();

    service.setThemeMode('light');
    expect(service.resolvedTheme()).toBe('light');

    service.setThemeMode('dark');
    expect(service.resolvedTheme()).toBe('dark');
  });

  it('applies the resolved theme class and accent variables to the document root', () => {
    const service = create();

    service.setThemeMode('dark');
    service.setAccentColor('#ff0000');
    TestBed.tick();

    const root = document.documentElement;
    expect(root.classList.contains('dark')).toBeTrue();
    expect(root.style.getPropertyValue('--color-accent')).toBe('#ff0000');
    expect(root.style.getPropertyValue('--color-accent-muted')).toContain('rgba(255, 0, 0');
  });

  it('writes nothing that rescales the root font size', () => {
    const service = create();

    service.setThemeMode('dark');
    service.setAccentColor('#ff0000');
    TestBed.tick();

    expect(document.documentElement.style.getPropertyValue('--ui-scale')).toBe('');
    expect(document.documentElement.style.fontSize).toBe('');
  });

  it('persists changes to the host', () => {
    const service = create();

    service.setAccentColor('#00ff00');

    expect(apiSpy.updateAppearanceSettings).toHaveBeenCalledWith(
      jasmine.objectContaining({ accentColor: '#00ff00' })
    );
  });

  it('reconciles with the host on load', async () => {
    const service = create();

    await service.loadFromHost();

    expect(apiSpy.getAppearanceSettings).toHaveBeenCalled();
    expect(service.themeMode()).toBe('dark');
    expect(service.accentColor()).toBe('#123456');
  });

  it('applies a pushed appearance change live without persisting it back', () => {
    const service = create();

    notifications.get('AppearanceChangedEvent')!.next({ themeMode: 'light', accentColor: '#abcdef' });

    expect(service.themeMode()).toBe('light');
    expect(service.accentColor()).toBe('#abcdef');
    // Applying a host push must not trigger another save (which would loop back to the host).
    expect(apiSpy.updateAppearanceSettings).not.toHaveBeenCalled();
  });

  it('restores cached values for an instant pre-connection paint', () => {
    localStorage.setItem('md.appearance.themeMode', 'light');
    localStorage.setItem('md.appearance.accentColor', '#abcabc');

    const service = create();

    expect(service.themeMode()).toBe('light');
    expect(service.accentColor()).toBe('#abcabc');
  });

  it('reads the host colours again whenever the connection comes up', async () => {
    const service = create();
    await Promise.resolve();
    apiSpy.getAppearanceSettings.calls.reset();
    apiSpy.getAppearanceSettings.and.resolveTo({ themeMode: 'light', accentColor: '#abcdef' });

    connectionState.set('connected');
    TestBed.tick();
    await Promise.resolve();

    expect(apiSpy.getAppearanceSettings).toHaveBeenCalled();
    expect(service.accentColor()).toBe('#abcdef');
  });

  it('falls back to addListener when MediaQueryList is not an EventTarget (legacy WebKit)', () => {
    const addListener = jasmine.createSpy('addListener');
    const legacyQuery = { matches: true, addListener } as unknown as MediaQueryList;
    spyOn(window, 'matchMedia').and.returnValue(legacyQuery);

    const service = create();

    expect(addListener).toHaveBeenCalledTimes(1);
    expect(service.resolvedTheme()).toBe('dark');

    const onChange = addListener.calls.mostRecent().args[0] as (event: MediaQueryListEvent) => void;
    onChange({ matches: false } as MediaQueryListEvent);
    expect(service.resolvedTheme()).toBe('light');
  });
});
