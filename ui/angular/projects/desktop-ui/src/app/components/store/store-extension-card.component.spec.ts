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
    return (fixture.nativeElement as HTMLElement).querySelector('.card-meta')?.textContent ?? '';
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

  function setup(ratings: Map<string, StoreRatingSummaryBody>): void {
    TestBed.configureTestingModule({
      imports: [StoreExtensionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: { plugins: signal([]) } },
        { provide: StoreRatingsService, useValue: { ratings: signal<ReadonlyMap<string, StoreRatingSummaryBody>>(ratings) } },
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
    return (fixture.nativeElement as HTMLElement).querySelector('.card-meta')!;
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
});
