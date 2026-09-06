import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { ApiService } from '../transport';
import { DEFAULT_CULTURE, LocalizationService } from './localization.service';

describe('LocalizationService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;

  beforeEach(() => {
    localStorage.clear();

    notifications = new Map();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getLocalization',
      'updateLocalizationSettings',
      'onNotification',
    ]);
    apiSpy.getLocalization.and.resolveTo({
      culture: 'de-DE',
      fallbackCulture: 'en',
      translations: { 'macrodeck:Common.Save': 'Speichern' },
      followSystem: false,
      availableCultures: ['en', 'de-DE'],
    });
    apiSpy.updateLocalizationSettings.and.resolveTo({
      success: true,
      error: null,
      culture: 'de-DE',
      fallbackCulture: 'en',
      followSystem: false,
    });
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

  afterEach(() => localStorage.clear());

  function create(): LocalizationService {
    return TestBed.inject(LocalizationService);
  }

  // A notification-driven refetch is started without a handle to it, and it persists the catalog it
  // loads. Settled here, that write lands before afterEach clears rather than in the next spec's run.
  const settle = async (): Promise<void> => { for (let turn = 0; turn < 4; turn++) await Promise.resolve(); };

  it('starts with the documented default culture', () => {
    const service = create();

    expect(service.culture()).toBe(DEFAULT_CULTURE);
    expect(service.fallbackCulture()).toBe(DEFAULT_CULTURE);
  });

  it('resolves a known key against the catalog fetched from the host', async () => {
    const service = create();
    await service.loadFromHost();

    expect(service.translate('macrodeck', 'Common.Save')).toBe('Speichern');
  });

  it('renders an unresolvable key as the exact bracketed fallback, never blank', async () => {
    const service = create();
    await service.loadFromHost();

    expect(service.translate('macrodeck', 'Nope.NotThere')).toBe('[[macrodeck:Nope.NotThere]]');
  });

  it('paints the bundled default language before any catalog has loaded, and never throws', () => {
    const service = create();

    expect(() => service.translate('macrodeck', 'Common.Save')).not.toThrow();
    // The client ships every key's default-language text, so a migrated string reads as words on the
    // first frame; only a key no catalog carries at all falls through to the bracketed form.
    expect(service.translate('macrodeck', 'Common.Save')).toBe('Save');
    expect(service.translate('macrodeck', 'Nope.NotThere')).toBe('[[macrodeck:Nope.NotThere]]');
  });

  it('the host catalog overrides a stale localStorage cache rather than the cache winning', async () => {
    localStorage.setItem('md.localization.culture', 'fr');
    localStorage.setItem('md.localization.fallbackCulture', 'en');
    localStorage.setItem('md.localization.translations', JSON.stringify({ 'macrodeck:Common.Save': 'Enregistrer' }));

    const service = create();
    expect(service.culture()).toBe('fr');
    expect(service.translate('macrodeck', 'Common.Save')).toBe('Enregistrer');

    await service.loadFromHost();

    expect(service.culture()).toBe('de-DE');
    expect(service.translate('macrodeck', 'Common.Save')).toBe('Speichern');
  });

  it('refetches the catalog when the host pushes a culture change', async () => {
    create();

    notifications.get('LocalizationCultureChangedEvent')!.next({ culture: 'de-DE', fallbackCulture: 'en' });
    await settle();

    expect(apiSpy.getLocalization).toHaveBeenCalled();
  });

  it('refetches the catalog when the host pushes a catalog change for a plugin scope', async () => {
    create();

    notifications.get('LocalizationCatalogChangedEvent')!.next({ scope: 'plugin:com.example.spotify' });
    await settle();

    expect(apiSpy.getLocalization).toHaveBeenCalled();
  });

  it('switching culture calls the host rather than persisting the choice as its own authority', async () => {
    const service = create();

    await service.setCulture('de-DE');

    expect(apiSpy.updateLocalizationSettings).toHaveBeenCalledWith({ culture: 'de-DE' });
    expect(apiSpy.getLocalization).toHaveBeenCalled();
    expect(service.culture()).toBe('de-DE');
  });

  it('leaves the current state untouched when the host rejects the requested culture', async () => {
    const service = create();
    await service.loadFromHost();
    apiSpy.updateLocalizationSettings.and.resolveTo({
      success: false,
      error: "'xx' is not a recognized culture.",
      culture: 'de-DE',
      fallbackCulture: 'en',
      followSystem: false,
    });

    await service.setCulture('xx');

    expect(service.culture()).toBe('de-DE');
    expect(apiSpy.getLocalization).toHaveBeenCalledTimes(1);
  });

  it('translateKey splits a fully qualified scope:key token, e.g. a generated Strings.* constant', async () => {
    const service = create();
    await service.loadFromHost();

    expect(service.translateKey('macrodeck:Common.Save')).toBe('Speichern');
  });

  it('lists the cultures the host reports', async () => {
    const service = create();
    await service.loadFromHost();

    expect(service.availableCultures()).toEqual(['en', 'de-DE']);
  });
});
