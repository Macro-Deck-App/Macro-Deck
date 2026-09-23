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

  function failedOperation(error: string, overrides: Partial<StoreOperationBody> = {}): StoreOperationBody {
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
      canRetry: false,
      ...overrides,
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

  it('asks again for the version that was refused when that version was chosen on purpose', async () => {
    await setup(failedOperation('UnsignedNotPermitted', { version: '0.9.0', versionPinned: true }), true,
      { latestVersion: '1.0.0' });
    fixture.componentRef.setInput('targetVersion', '0.9.0');
    fixture.detectChanges();

    const emitted: (string | undefined)[] = [];
    fixture.componentInstance.installUnsigned.subscribe(version => emitted.push(version));
    installAnywhereButton()!.click();
    fixture.detectChanges();
    (Array.from(fixture.nativeElement.querySelectorAll('button'))
      .filter(button => (button as HTMLElement).textContent?.includes('Install anyway')).pop() as HTMLElement).click();

    expect(emitted).toEqual(['0.9.0']);
  });

  it('asks for whatever is latest after refusing an install nobody pinned', async () => {
    await setup(failedOperation('UnsignedNotPermitted', { version: '1.0.0', versionPinned: false }), true);

    const emitted: (string | undefined)[] = [];
    fixture.componentInstance.installUnsigned.subscribe(version => emitted.push(version));
    installAnywhereButton()!.click();
    fixture.detectChanges();
    (Array.from(fixture.nativeElement.querySelectorAll('button'))
      .filter(button => (button as HTMLElement).textContent?.includes('Install anyway')).pop() as HTMLElement).click();

    expect(emitted).toEqual([undefined]);
  });
});

describe('StoreInstallButtonComponent version target', () => {
  let fixture: ComponentFixture<StoreInstallButtonComponent>;

  function item(overrides: Partial<StoreCatalogItemBody> = {}): StoreCatalogItemBody {
    return {
      kind: 'Plugin',
      id: 'com.acme.deck-tools',
      name: 'Deck Tools',
      latestVersion: '2.0.0',
      installState: 'NotInstalled',
      trust: 'RegistryAuthenticated',
      hasIcon: false,
      ...overrides,
    };
  }

  function text(key: string, params?: Record<string, unknown>): string {
    return TestBed.inject(LocalizationService).translateKey(key, params);
  }

  async function render(itemOverrides: Partial<StoreCatalogItemBody>, targetVersion: string | null,
                        inputs: Record<string, unknown> = {}, operation: StoreOperationBody | null = null): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [StoreInstallButtonComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: DeveloperModeService, useValue: { enabled: signal(false), ensureLoaded: jasmine.createSpy().and.resolveTo() } },
      ],
    });
    fixture = TestBed.createComponent(StoreInstallButtonComponent);
    fixture.componentRef.setInput('item', item(itemOverrides));
    fixture.componentRef.setInput('targetVersion', targetVersion);
    fixture.componentRef.setInput('operation', operation);
    fixture.componentRef.setInput('size', 'lg');
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function failed(error: string, overrides: Partial<StoreOperationBody> = {}): StoreOperationBody {
    return {
      id: 'op-9', kind: 'Install', extensionKind: 'Plugin', packageId: 'com.acme.deck-tools', version: '2.0.0',
      displayName: 'Deck Tools', state: 'Failed', bytesDownloaded: 0, startedAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:01Z', error, canRetry: false, ...overrides,
    };
  }

  function buttonLabels(host: HTMLElement): string[] {
    return Array.from(host.querySelectorAll('button')).map(button => button.textContent?.trim() ?? '');
  }

  it('installs the latest version when nothing is installed', async () => {
    expect(buttonLabels(await render({}, '2.0.0'))).toContain(text(AppStrings.Store.Install));
  });

  it('names an older version when nothing is installed and that version is selected', async () => {
    expect(buttonLabels(await render({}, '1.0.0'))).toContain(text(AppStrings.Store.InstallVersion, { version: '1.0.0' }));
  });

  it('shows the selected version as installed when it is the installed one', async () => {
    const host = await render({ installState: 'UpdateAvailable', installedVersion: '1.0.0' }, '1.0.0');
    expect(host.querySelector('.installed-status')?.textContent).toContain(text(AppStrings.Store.InstalledVersion, { version: '1.0.0' }));
    expect(buttonLabels(host)).not.toContain(text(AppStrings.Store.InstalledVersion, { version: '1.0.0' }));
  });

  it('offers an update when a newer version than the installed one is selected', async () => {
    const host = await render({ installState: 'UpdateAvailable', installedVersion: '1.0.0' }, '1.5.0');
    expect(buttonLabels(host)).toContain(text(AppStrings.Store.UpdateTo, { version: '1.5.0' }));
  });

  it('offers a downgrade when an older version than the installed one is selected', async () => {
    const host = await render({ installState: 'Installed', installedVersion: '2.0.0' }, '1.0.0');
    expect(buttonLabels(host)).toContain(text(AppStrings.Store.DowngradeTo, { version: '1.0.0' }));
  });

  it('refuses a selected version that cannot be installed, and says why', async () => {
    const host = await render({}, '1.0.0', { targetInstallable: false, targetUnavailableReason: 'Nope' });
    const button = host.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBeTrue();
    expect(button.textContent?.trim()).toBe(text(AppStrings.Store.VersionUnavailable));
  });

  it('offers a Macro Deck update instead of retrying a version that needs a newer Macro Deck', async () => {
    const host = await render({}, '2.0.0', { updatesAvailable: true }, failed('RequiresNewerMacroDeck'));
    const labels = buttonLabels(host);
    expect(labels).toContain(text('macrodeck.app:Settings.Update.CheckForUpdatesAction'));
    expect(labels).not.toContain(text('macrodeck:Common.Retry'));
    expect(labels).not.toContain(text(AppStrings.Store.Install));
  });

  it('does not offer installing the same incompatible version again', async () => {
    const host = await render({}, '2.0.0', { otherVersionsAvailable: true }, failed('Incompatible'));
    const labels = buttonLabels(host);
    expect(labels).toContain(text(AppStrings.Store.ChooseVersion));
    expect(labels).not.toContain(text(AppStrings.Store.Install));
  });

  it('offers the normal action again once another version is selected after a failure', async () => {
    const host = await render({}, '1.0.0', {}, failed('Incompatible'));
    expect(buttonLabels(host)).toContain(text(AppStrings.Store.InstallVersion, { version: '1.0.0' }));
  });

  it('keeps Uninstall available while an update failed for good', async () => {
    const host = await render({ installState: 'UpdateAvailable', installedVersion: '1.0.0' }, '2.0.0', {},
      failed('Incompatible', { kind: 'Update', previousVersion: '1.0.0' }));
    expect(buttonLabels(host)).toContain(text(AppStrings.Store.Uninstall));
  });

  it('puts settings first and uninstall beside it for an installed plugin that has settings', async () => {
    const host = await render({ installState: 'Installed', installedVersion: '2.0.0' }, '2.0.0', { manageAction: 'settings' });
    const emitted: string[] = [];
    fixture.componentInstance.manage.subscribe(action => emitted.push(action));

    expect(buttonLabels(host)).toContain(text(AppStrings.Store.Page.OpenSettingsAction));
    const uninstall = host.querySelector('.manage-uninstall button') as HTMLElement;
    expect(uninstall.getAttribute('aria-label')).toBe(text(AppStrings.Store.Uninstall));
    (host.querySelector('.manage-main button') as HTMLElement).click();
    expect(emitted).toEqual(['settings']);
  });

  it('names uninstall in full when there are no settings to open', async () => {
    const host = await render({ installState: 'Installed', installedVersion: '2.0.0' }, '2.0.0');

    expect(buttonLabels(host)).toContain(text(AppStrings.Store.Uninstall));
    expect(host.querySelector('.manage-uninstall')).toBeNull();
  });

  it('leaves a failed test build to the Tests tab', async () => {
    const host = await render({}, '2.0.0', {}, failed('TestBuildUnavailable', { kind: 'TestInstall' }));
    expect(buttonLabels(host)).toContain(text(AppStrings.Store.Install));
  });

  it('still offers Retry for a failure that trying again can fix', async () => {
    const host = await render({}, '2.0.0', {}, failed('DownloadFailed', { canRetry: true }));
    expect(buttonLabels(host)).toContain(text('macrodeck:Common.Retry'));
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

describe('StoreInstallButtonComponent after an update', () => {
  it('shows the version that is now installed once the update finishes', async () => {
    TestBed.configureTestingModule({
      imports: [StoreInstallButtonComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: DeveloperModeService, useValue: { enabled: signal(false), ensureLoaded: jasmine.createSpy().and.resolveTo() } },
      ],
    });
    const fixture = TestBed.createComponent(StoreInstallButtonComponent);
    fixture.componentRef.setInput('item', {
      kind: 'Plugin',
      id: 'com.suchbyte.macrogotchi',
      name: 'Macrogotchi',
      latestVersion: '1.1.0',
      installedVersion: '1.0.5',
      installState: 'UpdateAvailable',
      trust: 'PublisherVerified',
      hasIcon: false,
    } satisfies StoreCatalogItemBody);
    fixture.componentRef.setInput('operation', {
      id: 'op-1',
      kind: 'Update',
      extensionKind: 'Plugin',
      packageId: 'com.suchbyte.macrogotchi',
      version: '1.1.0',
      displayName: 'Macrogotchi',
      state: 'Completed',
      bytesDownloaded: 2291529,
      startedAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:10Z',
      canRetry: false,
    } satisfies StoreOperationBody);
    fixture.detectChanges();
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      TestBed.inject(LocalizationService).translateKey(AppStrings.Store.InstalledVersion, { version: '1.1.0' }));
  });
});

describe('StoreInstallButtonComponent with a test build installed', () => {
  let fixture: ComponentFixture<StoreInstallButtonComponent>;

  async function setup(overrides: Partial<StoreCatalogItemBody>, operation: StoreOperationBody | null = null): Promise<void> {
    TestBed.configureTestingModule({
      imports: [StoreInstallButtonComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: DeveloperModeService, useValue: { enabled: signal(false), ensureLoaded: () => Promise.resolve() } },
      ],
    });

    fixture = TestBed.createComponent(StoreInstallButtonComponent);
    fixture.componentRef.setInput('item', {
      kind: 'Plugin',
      id: 'com.acme.deck-tools',
      name: 'Deck Tools',
      latestVersion: '1.1.0',
      installState: 'Installed',
      installedVersion: '1.2.0',
      trust: 'RegistryAuthenticated',
      hasIcon: false,
      ...overrides,
    } satisfies StoreCatalogItemBody);
    fixture.componentRef.setInput('operation', operation);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function button(text: string): HTMLElement | null {
    return Array.from(fixture.nativeElement.querySelectorAll('button'))
      .find(candidate => (candidate as HTMLElement).textContent?.includes(text)) as HTMLElement ?? null;
  }

  it('says a test build is active and offers returning to the published version', async () => {
    await setup({ installedTestBuild: '42' });
    const installs: void[] = [];
    fixture.componentInstance.install.subscribe(() => installs.push(undefined));

    button('Return to Store version 1.1.0')!.click();

    expect(fixture.nativeElement.textContent).toContain('Test build 42');
    expect(installs.length).toBe(1);
  });

  it('offers the return even when the published version is newer than the test build', async () => {
    await setup({ installedTestBuild: '42', installState: 'UpdateAvailable', installedVersion: '1.0.0' });

    expect(button('Return to Store version 1.1.0')).not.toBeNull();
  });

  it('still says so once the test build install that put it there has completed', async () => {
    await setup({ installedTestBuild: '42' }, {
      id: 'op-test',
      kind: 'TestInstall',
      extensionKind: 'Plugin',
      packageId: 'com.acme.deck-tools',
      version: '1.2.0',
      displayName: 'Deck Tools',
      state: 'Completed',
      bytesDownloaded: 0,
      startedAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:01Z',
      canRetry: false,
    });

    expect(fixture.nativeElement.textContent).toContain('Test build 42');
    expect(button('Return to Store version 1.1.0')).not.toBeNull();
  });

  it('shows the returned Store version while the catalog still names the replaced test build', async () => {
    await setup({ installedTestBuild: '42', installedVersion: '1.2.0' }, {
      id: 'op-return',
      kind: 'Update',
      extensionKind: 'Plugin',
      packageId: 'com.acme.deck-tools',
      version: '1.1.0',
      displayName: 'Deck Tools',
      state: 'Completed',
      bytesDownloaded: 0,
      startedAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:01Z',
      canRetry: false,
    });

    expect(button('Return to Store version')).toBeNull();
  });

  it('offers nothing about test builds for a regular Store install', async () => {
    await setup({ installedVersion: '1.1.0' });

    expect(button('Return to Store version')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Test build');
  });
});
