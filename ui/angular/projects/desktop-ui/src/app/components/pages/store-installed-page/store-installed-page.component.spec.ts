import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subject } from 'rxjs';

import { AppStrings, StoreAvailableUpdateBody, StoreCatalogItemBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { ConnectAccountService } from '../../../services/connect-account.service';
import { PluginRuntimeService } from '../../../services/plugin-runtime.service';
import { StoreInstalledPageComponent } from './store-installed-page.component';

describe('StoreInstalledPageComponent', () => {
  let fixture: ComponentFixture<StoreInstalledPageComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;

  function item(id: string, installState: StoreCatalogItemBody['installState']): StoreCatalogItemBody {
    return {
      kind: 'Plugin',
      id,
      name: id,
      latestVersion: '1.1.0',
      installedVersion: '1.0.0',
      installState,
      trust: 'PublisherVerified',
      hasIcon: false,
    };
  }

  function update(packageId: string, kind: StoreAvailableUpdateBody['kind'] = 'Plugin'): StoreAvailableUpdateBody {
    return { kind, packageId, name: packageId, installedVersion: '1.0.0', latestVersion: '1.1.0' };
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      await fixture.whenStable();
      fixture.detectChanges();
    }
  }

  async function createFixture(items: StoreCatalogItemBody[], updates: StoreAvailableUpdateBody[]): Promise<void> {
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getStoreCatalog', 'getStoreUpdates', 'installStoreUpdates', 'getStoreOperations',
      'getStoreExtensionIconUrl', 'getStoreRatings', 'getStoreStatus',
    ]);
    api.getStoreStatus.and.resolveTo({
      registry: { hasCatalog: true, sequence: 1, refreshing: false, stale: false },
      developerMode: false,
      refreshRun: null,
    });
    api.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    api.getStoreCatalog.and.resolveTo({
      items,
      total: items.length,
      registry: { hasCatalog: true, sequence: 1, refreshing: false, stale: false },
    });
    api.getStoreUpdates.and.resolveTo({ updates });
    api.installStoreUpdates.and.resolveTo({ operations: [] });
    api.getStoreOperations.and.resolveTo({ operations: [] });
    api.getStoreRatings.and.resolveTo({ available: false, ratings: {} });
    api.getStoreExtensionIconUrl.and.returnValue('');
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('connected') });

    TestBed.configureTestingModule({
      imports: [StoreInstalledPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        {
          provide: ConnectAccountService,
          useValue: {
            isSignedIn: signal(false),
            session: signal(null),
          },
        },
        { provide: PluginRuntimeService, useValue: { plugins: signal([]) } },
        provideRouter([]),
      ],
    });

    fixture = TestBed.createComponent(StoreInstalledPageComponent);
    fixture.detectChanges();
    await settle();
  }

  function updateAllButton(): HTMLButtonElement {
    const label = TestBed.inject(LocalizationService).translateKey(AppStrings.Store.Page.UpdateAllAction);
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(button => button.textContent?.includes(label))!;
  }

  it('lists the installed plugins and icon packs', async () => {
    await createFixture([item('com.acme.hue', 'UpdateAvailable'), item('com.acme.deck', 'Installed')], []);

    expect(api.getStoreCatalog).toHaveBeenCalledWith(jasmine.objectContaining({
      installed: true,
      kinds: ['Plugin', 'IconPack'],
    }));
    expect(fixture.nativeElement.querySelectorAll('shared-store-extension-card').length).toBe(2);
  });

  it('opens without a signed-in account', async () => {
    await createFixture([item('com.acme.hue', 'Installed')], []);

    expect((fixture.nativeElement.querySelector('.store-page') as HTMLElement).hasAttribute('inert')).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('shared-store-extension-card').length).toBe(1);
  });

  it('updates every extension at once from one place', async () => {
    await createFixture([item('com.acme.hue', 'UpdateAvailable')], [update('com.acme.hue')]);

    updateAllButton().click();
    await settle();

    expect(api.installStoreUpdates).toHaveBeenCalledTimes(1);
  });

  it('says how many updates are waiting', async () => {
    await createFixture([item('com.acme.hue', 'UpdateAvailable')], [update('com.acme.hue'), update('com.acme.icons', 'IconPack')]);

    expect(fixture.nativeElement.textContent).toContain(TestBed.inject(LocalizationService)
      .translateKey(AppStrings.Store.Page.InstalledUpdatesAvailable, { count: 2 }));
  });

  it('offers no update all when everything is up to date or only a profile template changed', async () => {
    await createFixture([item('com.acme.deck', 'Installed')], [update('com.acme.template', 'ProfileTemplate')]);

    expect(updateAllButton().disabled).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain(TestBed.inject(LocalizationService)
      .translateKey(AppStrings.Store.Page.InstalledUpToDate));
  });

  it('shows an empty state when nothing is installed', async () => {
    await createFixture([], []);

    expect(fixture.nativeElement.textContent).toContain(TestBed.inject(LocalizationService)
      .translateKey(AppStrings.Store.Page.InstalledEmptyHeading));
  });
});
