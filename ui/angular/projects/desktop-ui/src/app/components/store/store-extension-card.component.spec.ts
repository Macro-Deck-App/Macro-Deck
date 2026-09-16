import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppStrings, PluginRuntimeInfo, StoreCatalogItemBody } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { DeveloperModeService } from '../../services/developer-mode.service';
import { PluginRuntimeService } from '../../services/plugin-runtime.service';
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
