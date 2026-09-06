import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppStrings, StoreCatalogItemBody, StoreOperationBody } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { DeveloperModeService } from '../../services/developer-mode.service';
import { StoreInstallButtonComponent } from './store-install-button.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('StoreInstallButtonComponent unsigned consent', () => {
  let fixture: ComponentFixture<StoreInstallButtonComponent>;
  let developerMode: { enabled: ReturnType<typeof signal<boolean>>; ensureLoaded: jasmine.Spy };

  function item(overrides: Partial<StoreCatalogItemBody> = {}): StoreCatalogItemBody {
    return {
      kind: 'Plugin',
      id: 'com.acme.deck-tools',
      name: 'Deck Tools',
      latestVersion: '1.0.0',
      installState: 'NotInstalled',
      trust: 'RegistryAuthenticated',
      hasIcon: false,
      ...overrides,
    };
  }

  function failedOperation(error: string): StoreOperationBody {
    return {
      id: 'op-1',
      kind: 'Install',
      extensionKind: 'Plugin',
      packageId: 'com.acme.deck-tools',
      version: '1.0.0',
      displayName: 'Deck Tools',
      state: 'Failed',
      bytesDownloaded: 0,
      startedAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:01Z',
      error,
      canRetry: true,
    };
  }

  async function setup(operation: StoreOperationBody, enabled: boolean,
                       itemOverrides: Partial<StoreCatalogItemBody> = {}): Promise<void> {
    developerMode = { enabled: signal(enabled), ensureLoaded: jasmine.createSpy('ensureLoaded').and.resolveTo() };

    TestBed.configureTestingModule({
      imports: [StoreInstallButtonComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: DeveloperModeService, useValue: developerMode },
      ],
    });

    fixture = TestBed.createComponent(StoreInstallButtonComponent);
    fixture.componentRef.setInput('item', item(itemOverrides));
    fixture.componentRef.setInput('operation', operation);
    fixture.componentRef.setInput('size', 'lg');
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function installAnywhereButton(): HTMLElement | null {
    return Array.from(fixture.nativeElement.querySelectorAll('button'))
      .find(button => (button as HTMLElement).textContent?.includes('Install anyway')) as HTMLElement ?? null;
  }

  it('offers installing anyway after an unsigned refusal while developer mode is on', async () => {
    await setup(failedOperation('UnsignedNotPermitted'), true);

    expect(installAnywhereButton()).not.toBeNull();
  });

  it('does not offer installing anyway while developer mode is off', async () => {
    await setup(failedOperation('UnsignedNotPermitted'), false);

    expect(installAnywhereButton()).toBeNull();
  });

  it('never offers installing anyway for a signature that failed to verify', async () => {
    await setup(failedOperation('SignatureInvalid'), true);

    expect(installAnywhereButton()).toBeNull();
  });

  it('never offers installing anyway for a trust downgrade', async () => {
    await setup(failedOperation('TrustDowngrade'), true);

    expect(installAnywhereButton()).toBeNull();
  });

  it('emits consent only after the warning is confirmed', async () => {
    await setup(failedOperation('UnsignedNotPermitted'), true);

    const emitted: number[] = [];
    fixture.componentInstance.installUnsigned.subscribe(() => emitted.push(1));

    installAnywhereButton()!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(emitted.length).toBe(0);

    const confirm = Array.from(fixture.nativeElement.querySelectorAll('button'))
      .filter(button => (button as HTMLElement).textContent?.includes('Install anyway'))
      .pop() as HTMLElement;
    confirm.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(emitted.length).toBe(1);
  });
});

describe('StoreInstallButtonComponent uninstall', () => {
  let fixture: ComponentFixture<StoreInstallButtonComponent>;
  let localization: LocalizationService;

  function item(overrides: Partial<StoreCatalogItemBody> = {}): StoreCatalogItemBody {
    return {
      kind: 'Plugin',
      id: 'com.acme.deck-tools',
      name: 'Deck Tools',
      latestVersion: '1.0.0',
      installState: 'NotInstalled',
      trust: 'RegistryAuthenticated',
      hasIcon: false,
      ...overrides,
    };
  }

  async function setup(itemOverrides: Partial<StoreCatalogItemBody> = {}): Promise<void> {
    TestBed.configureTestingModule({
      imports: [StoreInstallButtonComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: DeveloperModeService, useValue: { enabled: signal(false), ensureLoaded: jasmine.createSpy() } },
      ],
    });

    fixture = TestBed.createComponent(StoreInstallButtonComponent);
    localization = TestBed.inject(LocalizationService);
    fixture.componentRef.setInput('item', item(itemOverrides));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function findButton(text: string): HTMLElement | null {
    return Array.from(fixture.nativeElement.querySelectorAll('button'))
      .find(button => (button as HTMLElement).textContent?.trim() === text) as HTMLElement ?? null;
  }

  it('offers an uninstall control once the extension is installed', async () => {
    await setup({ installState: 'Installed', installedVersion: '1.0.0' });

    expect(findButton(localization.translateKey(AppStrings.Store.Uninstall))).not.toBeNull();
  });

  it('offers no uninstall control while the extension is not installed', async () => {
    await setup({ installState: 'NotInstalled' });

    expect(findButton(localization.translateKey(AppStrings.Store.Uninstall))).toBeNull();
  });

  it('emits uninstall directly on click, with no confirmation of its own', async () => {
    await setup({ installState: 'Installed', installedVersion: '1.0.0' });
    const emitted: number[] = [];
    fixture.componentInstance.uninstall.subscribe(() => emitted.push(1));

    findButton(localization.translateKey(AppStrings.Store.Uninstall))!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(emitted.length).toBe(1);
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
  });
});
