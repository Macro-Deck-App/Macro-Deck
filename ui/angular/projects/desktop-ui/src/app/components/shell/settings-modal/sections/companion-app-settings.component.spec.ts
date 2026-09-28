import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompanionAppDevice, CompanionAppStatus, CompanionLicenseStatus, TransportError } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { EMPTY } from 'rxjs';
import { DeveloperModeService } from '../../../../services/developer-mode.service';
import { FileSaveService } from '../../../../services/file-save.service';
import { SettingsModalService } from '../../../../services/settings-modal.service';
import { CompanionAppSettingsComponent } from './companion-app-settings.component';

const UNLICENSED: CompanionLicenseStatus = {
  licensed: false,
  licenseId: null,
  source: null,
  keyId: null,
  issuedAt: null,
  purchasedAt: null,
  billingId: null,
  isTest: false,
  accountSync: 'unknown',
  issuePending: false,
  nextIssueAttemptAt: null,
};

const LICENSED: CompanionLicenseStatus = {
  ...UNLICENSED,
  licensed: true,
  licenseId: '0190f3a2b4c64d8e9f0a1b2c3d4e5f60',
  source: 'google-play',
  keyId: 'prod-2026',
  issuedAt: Date.UTC(2026, 0, 1),
};

function device(overrides: Partial<CompanionAppDevice>): CompanionAppDevice {
  return {
    serial: 'R58M12ABCDE',
    name: 'Pixel_8',
    state: 'NotInstalled',
    installedVersion: null,
    installedVersionCode: null,
    error: null,
    ...overrides,
  };
}

function status(overrides: Partial<CompanionAppStatus> = {}): CompanionAppStatus {
  return {
    latestVersion: '26.1.0',
    latestVersionCode: 9,
    publishedAt: Date.UTC(2026, 8, 24),
    lastCheckedAt: Date.UTC(2026, 8, 24),
    checking: false,
    checkFailed: false,
    autoUpdate: false,
    adbEnabled: true,
    devices: [],
    connectedApps: [],
    ...overrides,
  };
}

describe('CompanionAppSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  let settingsModal: SettingsModalService;
  let fileSave: jasmine.SpyObj<FileSaveService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getCompanionLicense',
      'getCompanionApp',
      'checkCompanionAppUpdate',
      'installCompanionApp',
      'updateCompanionAppSettings',
      'onNotification',
      'onAdbStateChanged',
      'downloadCompanionApk',
    ]);
    fileSave = jasmine.createSpyObj<FileSaveService>('FileSaveService', ['save']);
    api.getCompanionLicense.and.resolveTo(UNLICENSED);
    api.getCompanionApp.and.resolveTo(status());
    api.onNotification.and.returnValue(EMPTY);
    api.onAdbStateChanged.and.returnValue(EMPTY);

    await TestBed.configureTestingModule({
      imports: [CompanionAppSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: DeveloperModeService, useValue: { enabled: signal(false) } },
        { provide: FileSaveService, useValue: fileSave },
      ],
    }).compileComponents();
    settingsModal = TestBed.inject(SettingsModalService);
  });

  async function create(): Promise<ComponentFixture<CompanionAppSettingsComponent>> {
    const fixture = TestBed.createComponent(CompanionAppSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<unknown>, testId: string): string | undefined {
    return (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`)?.textContent?.trim();
  }

  it('warns that an ADB install only runs as a trial while the host has no license', async () => {
    const fixture = await create();

    const notice = text(fixture, 'companion-app-no-license');
    expect(notice).toContain('7-day trial');
    expect(notice).toContain('App Store');
    expect(notice).toContain('Google Play');
  });

  it('explains that the installed app is not the Google Play version and needs no Play services', async () => {
    const fixture = await create();

    const note = text(fixture, 'companion-app-direct-build');
    expect(note).toContain('not the one from Google Play');
    expect(note).toContain('does not need Google Play services');
  });

  it('drops the trial warning once the host holds a purchased license', async () => {
    api.getCompanionLicense.and.resolveTo(LICENSED);

    const fixture = await create();

    expect(text(fixture, 'companion-app-no-license')).toBeUndefined();
  });

  it('keeps the trial warning for a test license', async () => {
    api.getCompanionLicense.and.resolveTo({ ...LICENSED, isTest: true, source: 'test' });

    const fixture = await create();

    expect(text(fixture, 'companion-app-no-license')).toBeDefined();
  });

  it('sends the user to the ADB settings while ADB is off', async () => {
    api.getCompanionApp.and.resolveTo(status({ adbEnabled: false }));
    const fixture = await create();

    const notice = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="companion-app-adb-disabled"]')!;
    (notice.querySelector('button') as HTMLButtonElement).click();

    expect(settingsModal.activeCategory()).toBe('adb');
    expect(fixture.nativeElement.querySelector('[data-testid="companion-app-device"]')).toBeNull();
  });

  it('installs the app on a device that does not have it', async () => {
    const before = device({ state: 'NotInstalled' });
    const after = device({ state: 'UpToDate', installedVersion: '26.1.0', installedVersionCode: 9 });
    api.getCompanionApp.and.resolveTo(status({ devices: [before] }));
    api.installCompanionApp.and.resolveTo({ success: true, error: null, status: status({ devices: [after] }) });
    const fixture = await create();

    (fixture.nativeElement.querySelector('[data-testid="companion-app-install"] button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.installCompanionApp).toHaveBeenCalledWith('R58M12ABCDE');
    expect(text(fixture, 'companion-app-device-state')).toBe('Up to date (26.1.0)');
    expect(fixture.nativeElement.querySelector('[data-testid="companion-app-install"]')).toBeNull();
  });

  it('offers an update from the installed to the latest version', async () => {
    api.getCompanionApp.and.resolveTo(status({
      devices: [device({ state: 'UpdateAvailable', installedVersion: '26.0.0', installedVersionCode: 8 })],
    }));

    const fixture = await create();

    expect(text(fixture, 'companion-app-device-state')).toBe('Update available: 26.0.0 to 26.1.0');
    expect(fixture.nativeElement.querySelector('[data-testid="companion-app-update"]')).not.toBeNull();
  });

  it('leaves a Google Play install and a non-Android device without an action', async () => {
    api.getCompanionApp.and.resolveTo(status({
      devices: [
        device({ serial: 'a', state: 'InstalledFromPlayStore', installedVersion: '26.0.0' }),
        device({ serial: 'b', state: 'NotAndroid' }),
      ],
    }));

    const fixture = await create();

    expect(fixture.nativeElement.querySelectorAll('[data-testid="companion-app-device"]').length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-testid="companion-app-install"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="companion-app-update"]')).toBeNull();
  });

  it('explains a failed install in words rather than a code', async () => {
    api.getCompanionApp.and.resolveTo(status({
      devices: [device({ state: 'UpdateAvailable', installedVersion: '26.0.0', error: 'IncompatibleSignature' })],
    }));

    const fixture = await create();

    expect(text(fixture, 'companion-app-device-error')).toContain('Uninstall it on the device first');
  });

  it('flags a connected app that is behind the latest release', async () => {
    api.getCompanionApp.and.resolveTo(status({
      connectedApps: [
        { deviceId: '1', name: 'Kitchen tablet', model: 'Tab A', appVersion: '26.0.3', updateAvailable: true },
        { deviceId: '2', name: 'Desk phone', model: 'Pixel 8', appVersion: '26.1.0', updateAvailable: false },
      ],
    }));

    const fixture = await create();

    const rows = Array.from(fixture.nativeElement.querySelectorAll('[data-testid="companion-app-connected"]')) as HTMLElement[];
    expect(rows.map(row => row.querySelector('[data-testid="companion-app-newer"]')?.textContent?.trim() ?? null))
      .toEqual(['Newer version available: 26.1.0', null]);
  });

  it('saves the verified APK through the app file dialog', async () => {
    const blob = new Blob([new Uint8Array([1, 2, 3])]);
    api.downloadCompanionApk.and.resolveTo({ blob, fileName: 'macro-deck-companion-26.1.0.apk' });
    fileSave.save.and.resolveTo({ status: 'saved', viaDialog: true, path: '/tmp/macro-deck-companion-26.1.0.apk' });
    const fixture = await create();

    (fixture.nativeElement.querySelector('[data-testid="companion-app-save-apk"] button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fileSave.save).toHaveBeenCalledWith(blob, 'macro-deck-companion-26.1.0.apk');
    expect(text(fixture, 'companion-app-apk-message')).toBe('The APK was saved.');
  });

  it('says so when the APK cannot be downloaded', async () => {
    api.downloadCompanionApk.and.rejectWith(new Error('502'));
    const fixture = await create();

    await fixture.componentInstance.saveApk();
    fixture.detectChanges();

    expect(fileSave.save).not.toHaveBeenCalled();
    expect(text(fixture, 'companion-app-apk-message')).toContain('could not be downloaded');
  });

  it('names a failed security check instead of a generic download error', async () => {
    api.downloadCompanionApk.and.rejectWith(new TransportError(502, 'Download failed (502)', 'VerificationFailed'));
    const fixture = await create();

    await fixture.componentInstance.saveApk();
    fixture.detectChanges();

    expect(text(fixture, 'companion-app-apk-message')).toContain('failed its security check');
  });

  it('saves the auto-update choice', async () => {
    api.updateCompanionAppSettings.and.resolveTo(status({ autoUpdate: true }));
    const fixture = await create();

    fixture.componentInstance.setAutoUpdate(true);
    await fixture.whenStable();

    expect(api.updateCompanionAppSettings).toHaveBeenCalledWith(true);
    expect(fixture.componentInstance.status()?.autoUpdate).toBeTrue();
  });
});
