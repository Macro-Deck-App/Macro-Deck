import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';

import { AppStrings, StoreExtensionDetailBody, StoreOperationBody, StoreVersionHistoryBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { StoreOperationService } from '../../../services/store-operation.service';
import { StoreDetailPageComponent } from './store-detail-page.component';
import { Observable, Subject } from 'rxjs';

function extension(overrides: Partial<StoreExtensionDetailBody> = {}): StoreExtensionDetailBody {
  return {
    kind: 'Plugin',
    id: 'app.example.plugin',
    name: 'Example Plugin',
    latestVersion: '1.1.0',
    installState: 'NotInstalled',
    trust: 'RegistryAuthenticated',
    hasIcon: false,
    screenshots: [],
    downloadSize: 0,
    supportedOperatingSystems: [],
    languages: [],
    history: [],
    ...overrides,
  };
}

function operation(overrides: Partial<StoreOperationBody> = {}): StoreOperationBody {
  return {
    id: 'op-1',
    kind: 'Install',
    extensionKind: 'Plugin',
    packageId: 'app.example.plugin',
    version: '1.1.0',
    displayName: 'Example Plugin',
    state: 'Downloading',
    bytesDownloaded: 10,
    totalBytes: 100,
    etaSeconds: 20,
    startedAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:01Z',
    canRetry: false,
    ...overrides,
  };
}

describe('StoreDetailPageComponent', () => {
  let fixture: ComponentFixture<StoreDetailPageComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let operationsSpy: jasmine.SpyObj<StoreOperationService>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  // LocalizationService caches the reader's culture in localStorage and restores it on construction,
  // so a spec that ran earlier and switched language leaves this one reading in that language. The
  // language ordering below is stated relative to the reader, so it has to start from a known one
  // rather than from whatever the suite happened to run first.
  beforeEach(() => {
    for (const key of [
      'md.localization.culture',
      'md.localization.fallbackCulture',
      'md.localization.translations',
      'md.localization.availableCultures',
      'md.localization.followSystem',
    ]) {
      localStorage.removeItem(key);
    }
  });

  async function createFixture(
    initialOperation: StoreOperationBody | null,
    extensionOverrides: Partial<StoreExtensionDetailBody> = {},
  ): Promise<void> {
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getStoreExtension', 'getStoreExtensionIconUrl', 'getStoreStatus', 'onNotification',
    ]);
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
    api.getStoreStatus.and.resolveTo({
      developerMode: false,
      registry: { hasCatalog: false, sequence: 0, refreshing: false, stale: false },
    });
    api.getStoreExtension.and.resolveTo({ extension: extension(extensionOverrides) });
    api.getStoreExtensionIconUrl.and.returnValue('');
    api.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });

    operationsSpy = jasmine.createSpyObj<StoreOperationService>('StoreOperationService', [
      'operationFor', 'install', 'retry', 'uninstall',
    ]);
    operationsSpy.operationFor.and.returnValue(signal(initialOperation));
    operationsSpy.install.and.resolveTo(null);
    operationsSpy.retry.and.resolveTo(null);
    operationsSpy.uninstall.and.resolveTo({ success: true });

    const routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [StoreDetailPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: StoreOperationService, useValue: operationsSpy },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'kind' ? 'Plugin' : 'app.example.plugin'),
              },
            },
          },
        },
      ],
    });

    fixture = TestBed.createComponent(StoreDetailPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders progress instead of an Install button when an operation is already in flight at construction', async () => {
    await createFixture(operation({ state: 'Downloading' }));

    const installButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Install');
    expect(installButton).toBeUndefined();

    const progressBar = fixture.nativeElement.querySelector('[role="progressbar"]');
    expect(progressBar).not.toBeNull();
  });

  it('offers a retry for a failed operation with canRetry, and calls the host on click', async () => {
    await createFixture(operation({ state: 'Failed', canRetry: true, errorMessage: 'Download failed' }));

    const retryButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Retry');
    expect(retryButton).withContext('a Retry control should be offered').not.toBeUndefined();

    (retryButton as HTMLElement).click();
    await fixture.whenStable();

    expect(operationsSpy.retry).toHaveBeenCalledWith('op-1');
  });

  it('does not offer a retry for a failed operation that cannot be retried', async () => {
    await createFixture(operation({ state: 'Failed', canRetry: false }));

    const retryButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Retry');
    expect(retryButton).toBeUndefined();
  });

  it("shows the newest version's notes and version label in the What's new? card, not the older one's", async () => {
    const history: StoreVersionHistoryBody[] = [
      { version: '2.0.0', releasedAt: '2026-06-03T00:00:00Z', changelog: 'Newest release notes' },
      { version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog: 'Older release notes' },
    ];
    await createFixture(null, { history });

    const cardText = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(cardText).toContain('Newest release notes');
    expect(cardText).toContain('Version 2.0.0');
    expect(cardText).not.toContain('Older release notes');
  });

  it('hides the version-history trigger when there is a single history entry', async () => {
    await createFixture(null, {
      history: [{ version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog: 'Only release notes' }],
    });
    const historyButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Version history');
    expect(historyButton).toBeUndefined();
  });

  it('lists the reader\'s own language first, then English, then the rest by name', async () => {
    // The reader is on 'en' here (the test catalog's default), so "own language" and "English" are the
    // same rule; what the assertion pins is that English leads and the remainder is name-ordered.
    await createFixture(null, { languages: ['ja', 'de', 'en', 'nl'] });

    const items = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.language-item'))
      .map(item => item.textContent?.trim());

    // Ordered by the name the reader sees, in the reader's own collation - which is why a non-Latin
    // name lands after the Latin ones rather than at its language tag's alphabetical position.
    expect(items).toEqual(['English', 'Deutsch', 'Nederlands', '日本語']);
  });

  it('shows at most four languages in the sidebar and offers View all for the rest', async () => {
    await createFixture(null, { languages: ['en', 'de', 'fr', 'es', 'it', 'nl'] });

    expect(fixture.nativeElement.querySelectorAll('.language-item').length).toBe(4);

    const viewAll = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'View all');
    expect(viewAll).withContext('six languages should offer a View all trigger').not.toBeUndefined();

    (viewAll as HTMLElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    // The modal carries the whole list, in the same order the sidebar sliced its first four from.
    expect(fixture.nativeElement.querySelectorAll('.language-entry').length).toBe(6);
  });

  it('offers no View all when the sidebar already shows every language', async () => {
    await createFixture(null, { languages: ['en', 'de', 'fr', 'es'] });

    const viewAll = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'View all');
    expect(viewAll).toBeUndefined();
  });

  it('shows no language section at all when the registry declares none', async () => {
    await createFixture(null, { languages: [] });

    expect(fixture.nativeElement.querySelector('.languages-group')).toBeNull();
  });

  it('shows the version-history trigger when there are two history entries', async () => {
    await createFixture(null, {
      history: [
        { version: '2.0.0', releasedAt: '2026-06-03T00:00:00Z', changelog: 'Newest release notes' },
        { version: '1.1.0', releasedAt: '2026-01-01T00:00:00Z', changelog: 'Older release notes' },
      ],
    });
    const historyButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-button'))
      .find(btn => btn.textContent?.trim() === 'Version history');
    expect(historyButton).withContext('two history entries should offer a Version history trigger').not.toBeUndefined();
  });

  it('reloads the extension when the catalog changes, so a stale Installed state does not survive an uninstall', async () => {
    await createFixture(null, { installState: 'Installed', installedVersion: '1.1.0' });
    expect(fixture.nativeElement.textContent).toContain('Installed');

    api.getStoreExtension.and.resolveTo({ extension: extension({ installState: 'NotInstalled' }) });
    push('StoreCatalogChangedEvent', {});
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.getStoreExtension).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).not.toContain('Installed');
  });

  it('wires the Uninstall control through to the operations service with the exact kind and id, and surfaces a failure with its localized reason', async () => {
    jasmine.clock().install();
    try {
      await createFixture(null, { kind: 'Plugin', id: 'app.example.plugin', installState: 'Installed', installedVersion: '1.1.0' });
      operationsSpy.uninstall.and.resolveTo({ success: false, error: { code: 'DependencyInUse', message: 'raw host message' } });

      const localization = TestBed.inject(LocalizationService);
      const uninstallButtons = (): HTMLElement[] => Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
        .filter(btn => btn.textContent?.trim() === localization.translateKey(AppStrings.Store.Uninstall));

      uninstallButtons()[0].click();
      fixture.detectChanges();
      await fixture.whenStable();

      // The confirmation modal's own confirm button carries the same label; it is the last one in the
      // DOM. Confirming plays a 150ms close animation before the confirm output actually fires (see
      // ModalComponent.dismiss), so the clock has to be advanced past it.
      uninstallButtons()[uninstallButtons().length - 1].click();
      jasmine.clock().tick(150);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(operationsSpy.uninstall).toHaveBeenCalledWith('Plugin', 'app.example.plugin');

      const toasts = TestBed.inject(ToastService).toasts();
      expect(toasts.length).toBe(1);
      expect(toasts[0].message).toBe(localization.translateKey(AppStrings.Store.UninstallFailed));
      expect(toasts[0].detail).toBe(localization.translateKey(AppStrings.Store.UninstallDependencyInUse));
      expect(toasts[0].detail).not.toBe('raw host message');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('calls the operations service zero times when the uninstall confirmation is cancelled', async () => {
    jasmine.clock().install();
    try {
      await createFixture(null, { kind: 'Plugin', id: 'app.example.plugin', installState: 'Installed', installedVersion: '1.1.0' });

      const localization = TestBed.inject(LocalizationService);
      const findButton = (text: string): HTMLElement | undefined => Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
        .find(btn => btn.textContent?.trim() === text);

      findButton(localization.translateKey(AppStrings.Store.Uninstall))!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).not.toBeNull();

      findButton(localization.translateKey('macrodeck:Common.Cancel'))!.click();
      jasmine.clock().tick(150);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(operationsSpy.uninstall).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the icon-pack-specific confirmation copy, distinct from the plugin one, resolved through the localization service', async () => {
    await createFixture(null, { kind: 'IconPack', name: 'Neon Icons', installState: 'Installed', installedVersion: '1.0.0' });

    const localization = TestBed.inject(LocalizationService);
    const uninstallButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(btn => btn.textContent?.trim() === localization.translateKey(AppStrings.Store.Uninstall));
    uninstallButton!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const expected = localization.translateKey(AppStrings.Store.UninstallMessageIconPack, { name: 'Neon Icons' });
    const pluginWording = localization.translateKey(AppStrings.Store.UninstallMessagePlugin, { name: 'Neon Icons' });
    expect(fixture.nativeElement.textContent).toContain(expected);
    expect(fixture.nativeElement.textContent).not.toContain(pluginWording);
  });

  it('renders the confirmation modal outside the install button, not inside it', async () => {
    await createFixture(null, { installState: 'Installed', installedVersion: '1.1.0' });

    const localization = TestBed.inject(LocalizationService);
    const uninstallButton = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(btn => btn.textContent?.trim() === localization.translateKey(AppStrings.Store.Uninstall));
    uninstallButton!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const modal = fixture.nativeElement.querySelector('shared-confirmation-modal');
    expect(modal).not.toBeNull();

    const installButtons = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('shared-store-install-button'));
    expect(installButtons.length).toBeGreaterThan(0);
    for (const installButtonEl of installButtons) {
      expect(installButtonEl.contains(modal)).toBeFalse();
    }
  });
});
