import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppStrings, PluginRuntimeInfo, StoreCatalogItemBody, StoreRatingSummaryBody } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { DeveloperModeService } from '../../services/developer-mode.service';
import { PluginRuntimeService } from '../../services/plugin-runtime.service';
import { StoreRatingsService } from '../../services/store-ratings.service';
import { StoreExtensionCardComponent } from './store-extension-card.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('StoreExtensionCardComponent trust chip', () => {
  let fixture: ComponentFixture<StoreExtensionCardComponent>;
  let runtimePlugins: ReturnType<typeof signal<PluginRuntimeInfo[]>>;

  function item(overrides: Partial<StoreCatalogItemBody> = {}): StoreCatalogItemBody {
    return {
      kind: 'Plugin',
      id: 'com.acme.deck-tools',
      name: 'Deck Tools',
      latestVersion: '1.0.0',
      installState: 'Installed',
      trust: 'PublisherVerified',
      hasIcon: false,
      ...overrides,
    };
  }

  function runtime(pluginId: string, takenOverByDevelopmentBuild: boolean): PluginRuntimeInfo {
    return { pluginId, takenOverByDevelopmentBuild } as PluginRuntimeInfo;
  }

  function setup(itemOverrides: Partial<StoreCatalogItemBody> = {}, plugins: PluginRuntimeInfo[] = []): void {
    runtimePlugins = signal(plugins);

    TestBed.configureTestingModule({
      imports: [StoreExtensionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: { plugins: runtimePlugins } },
        {
          provide: DeveloperModeService,
          useValue: { enabled: signal(false), ensureLoaded: jasmine.createSpy('ensureLoaded').and.resolveTo() },
        },
      ],
    });

    fixture = TestBed.createComponent(StoreExtensionCardComponent);
    fixture.componentRef.setInput('item', item(itemOverrides));
    fixture.detectChanges();
  }

  function metaText(): string {
    return (fixture.nativeElement as HTMLElement).querySelector('.card-tags')?.textContent ?? '';
  }

  function translate(key: string): string {
    return TestBed.inject(LocalizationService).translateKey(key);
  }

  it('shows the verified chip for a verified plugin that is not taken over', () => {
    setup({}, [runtime('com.acme.deck-tools', false)]);

    expect(metaText()).toContain(translate(AppStrings.Store.PublisherVerified));
    expect(metaText()).not.toContain(translate('macrodeck.app:Developer.ManagedPlugins.TakenOverBadge'));
  });

  it('replaces the verified chip while a development build has taken the plugin over', () => {
    setup({}, [runtime('com.acme.deck-tools', true)]);

    expect(metaText()).toContain(translate('macrodeck.app:Developer.ManagedPlugins.TakenOverBadge'));
    expect(metaText()).not.toContain(translate(AppStrings.Store.PublisherVerified));
  });

  it('follows the runtime state when the takeover ends', () => {
    setup({}, [runtime('com.acme.deck-tools', true)]);

    runtimePlugins.set([runtime('com.acme.deck-tools', false)]);
    fixture.detectChanges();

    expect(metaText()).toContain(translate(AppStrings.Store.PublisherVerified));
  });

  it('marks an installed plugin whose signing certificate was revoked and no longer calls it verified', () => {
    setup({ signingRevoked: true }, [runtime('com.acme.deck-tools', true)]);

    expect(metaText()).toContain(translate(AppStrings.Store.SigningRevoked));
    expect(metaText()).not.toContain(translate(AppStrings.Store.PublisherVerified));
    expect(metaText()).not.toContain(translate('macrodeck.app:Developer.ManagedPlugins.TakenOverBadge'));
  });

  it('shows no revoked marker for a host that does not report revocation', () => {
    setup({}, [runtime('com.acme.deck-tools', false)]);

    expect(metaText()).not.toContain(translate(AppStrings.Store.SigningRevoked));
  });

  it('ignores a takeover of a different id and of non-plugin kinds', () => {
    setup({ kind: 'IconPack', trust: 'RegistryAuthenticated' }, [runtime('com.acme.deck-tools', true)]);

    expect(metaText()).not.toContain(translate('macrodeck.app:Developer.ManagedPlugins.TakenOverBadge'));
  });
});

describe('StoreExtensionCardComponent rating', () => {
  let fixture: ComponentFixture<StoreExtensionCardComponent>;

  beforeEach(() => {
    for (const key of Object.keys(localStorage).filter(key => key.startsWith('md.localization.'))) {
      localStorage.removeItem(key);
    }
  });

  function setup(ratings: Map<string, StoreRatingSummaryBody>, installs = new Map<string, number>()): void {
    TestBed.configureTestingModule({
      imports: [StoreExtensionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: { plugins: signal([]) } },
        {
          provide: StoreRatingsService,
          useValue: {
            ratings: signal<ReadonlyMap<string, StoreRatingSummaryBody>>(ratings),
            installs: signal<ReadonlyMap<string, number>>(installs),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(StoreExtensionCardComponent);
    fixture.componentRef.setInput('item', {
      kind: 'Plugin',
      id: 'com.acme.deck-tools',
      name: 'Deck Tools',
      latestVersion: '1.0.0',
      installState: 'NotInstalled',
      trust: 'RegistryAuthenticated',
      hasIcon: false,
    } satisfies StoreCatalogItemBody);
    fixture.detectChanges();
  }

  function meta(): HTMLElement {
    return (fixture.nativeElement as HTMLElement).querySelector('.card-stats')!;
  }

  function installs(): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('.card-installs');
  }

  it('shows the stars, the rating and the rating count when the item has ratings', () => {
    setup(new Map([['com.acme.deck-tools', { rating: 4.5, ratingCount: 12 }]]));
    const localization = TestBed.inject(LocalizationService);

    expect(meta().querySelector('shared-store-rating-stars')).not.toBeNull();
    expect(meta().textContent).toContain(localization.translateKey(AppStrings.Store.Reviews.RatingCount, { count: 12 }));
    const formatted = new Intl.NumberFormat(localization.culture(), { minimumFractionDigits: 1 }).format(4.5);
    expect(meta().textContent).toContain(formatted);
    expect(meta().querySelector('[role="img"]')?.getAttribute('aria-label'))
      .toBe(localization.translateKey(AppStrings.Store.Reviews.StarsLabel, { rating: formatted }));
  });

  it('shows no rating when the item has no ratings or none are known', () => {
    setup(new Map([['com.acme.deck-tools', { rating: null, ratingCount: 0 }]]));
    expect(meta().querySelector('shared-store-rating-stars')).toBeNull();

    TestBed.resetTestingModule();
    setup(new Map());
    expect(meta().querySelector('shared-store-rating-stars')).toBeNull();
  });

  it('shows the install count compactly, and announces the exact count to readers who cannot see it', () => {
    setup(new Map([['com.acme.deck-tools', { rating: 4.5, ratingCount: 12 }]]), new Map([['com.acme.deck-tools', 1234]]));

    expect(installs()!.textContent).toContain('1.2K');
    expect(installs()!.getAttribute('role')).toBe('img');
    expect(installs()!.getAttribute('aria-label')).toBe('1,234 installs');
  });

  it('shows the install count of an item nobody has rated yet', () => {
    setup(new Map(), new Map([['com.acme.deck-tools', 1]]));

    expect(installs()!.textContent).toContain('1');
    expect(installs()!.getAttribute('role')).toBe('img');
    expect(installs()!.getAttribute('aria-label')).toBe('1 install');
    expect(meta().querySelector('shared-store-rating-stars')).toBeNull();
  });

  it('shows no install count when none is known', () => {
    setup(new Map([['com.acme.deck-tools', { rating: 4.5, ratingCount: 12 }]]));

    expect(installs()).toBeNull();
  });
});

describe('StoreExtensionCardComponent publisher link', () => {
  it('links the publisher to everything that publisher offers', () => {
    TestBed.configureTestingModule({
      imports: [StoreExtensionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: { plugins: signal([]) } },
      ],
    });
    const fixture = TestBed.createComponent(StoreExtensionCardComponent);
    fixture.componentRef.setInput('item', {
      kind: 'Plugin', id: 'com.pyflat.hotkeys', name: 'Hotkeys', publisher: 'PyFlat', latestVersion: '1.0.0',
      installState: 'NotInstalled', trust: 'RegistryAuthenticated', hasIcon: false,
    } satisfies StoreCatalogItemBody);
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a.card-publisher')!;
    expect(link.getAttribute('href')).toBe('/store?publisher=PyFlat');
  });
});

describe('StoreExtensionCardComponent install state', () => {
  function render(installState: StoreCatalogItemBody['installState']): HTMLElement {
    TestBed.configureTestingModule({
      imports: [StoreExtensionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: { plugins: signal([]) } },
        {
          provide: DeveloperModeService,
          useValue: { enabled: signal(false), ensureLoaded: jasmine.createSpy('ensureLoaded').and.resolveTo() },
        },
      ],
    });

    const fixture = TestBed.createComponent(StoreExtensionCardComponent);
    fixture.componentRef.setInput('item', {
      kind: 'Plugin',
      id: 'com.suchbyte.macrogotchi',
      name: 'Macrogotchi',
      latestVersion: '1.1.0',
      installedVersion: '1.0.5',
      installState,
      trust: 'PublisherVerified',
      hasIcon: false,
    } satisfies StoreCatalogItemBody);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lets the update button speak for an available update instead of repeating it in a badge', () => {
    const card = render('UpdateAvailable');

    expect(card.querySelector('shared-store-state-badge')).toBeNull();
    expect(card.textContent).toContain(TestBed.inject(LocalizationService)
      .translateKey(AppStrings.Store.UpdateTo, { version: '1.1.0' }));
  });

  it('still names the state of an extension that is up to date', () => {
    const card = render('Installed');

    expect(card.textContent).toContain(TestBed.inject(LocalizationService)
      .translateKey(AppStrings.Store.InstalledVersion, { version: '1.0.5' }));
  });
});
