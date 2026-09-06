import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import {
  AdbDevice,
  AdbStateChangedEvent,
  GetAdbSettingsResponse,
  UpdateAdbSettingsResponse,
} from '@macro-deck/runtime';
import {
  ApiService,
} from '@shared';
import { AdbSettingsComponent } from './adb-settings.component';

describe('AdbSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  let adbStateChanged: Subject<AdbStateChangedEvent>;

  const device: AdbDevice = {
    serial: 'R58M12ABCDE',
    state: 'Device',
    model: 'Pixel 7',
    manufacturer: 'Google',
    authorized: true,
    isDefault: true,
    tunnelEstablished: true,
    tunnelDevicePort: 55001,
    tunnelError: null,
  };

  const defaults: GetAdbSettingsResponse = {
    enabled: true,
    executablePath: null,
    resolvedExecutablePath: '/usr/local/bin/adb',
    executableSource: 'Path',
    adbVersion: '1.0.41',
    serverReachable: true,
    serverStartedByMacroDeck: false,
    usbConnectionsEnabled: true,
    defaultDeviceSerial: 'R58M12ABCDE',
    activePublicPort: 8193,
    deviceSidePortCandidates: [58193, 58194],
    devices: [device],
    lastError: null,
    lastErrorAt: null,
    previousShutdownWasUnclean: false,
    staleTunnelsCleaned: 0,
    supported: true,
    unsupportedReason: null,
  };

  function installApi(state: Partial<GetAdbSettingsResponse> = {}): GetAdbSettingsResponse {
    const resolved = { ...defaults, ...state };
    api.getAdbSettings.and.resolveTo(resolved);
    api.updateAdbSettings.and.callFake(request => Promise.resolve<UpdateAdbSettingsResponse>({
      ...resolved,
      enabled: request.enabled ?? resolved.enabled,
      executablePath: request.executablePath ?? null,
      usbConnectionsEnabled: request.usbConnectionsEnabled ?? resolved.usbConnectionsEnabled,
      defaultDeviceSerial: request.defaultDeviceSerial ?? null,
      success: true,
      error: null,
    }));
    return resolved;
  }

  beforeEach(async () => {
    adbStateChanged = new Subject<AdbStateChangedEvent>();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getAdbSettings',
      'updateAdbSettings',
      'restartAdbServer',
      'downloadAdbPlatformTools',
      'onAdbStateChanged',
      'onNotification',
    ]);
    api.onAdbStateChanged.and.returnValue(adbStateChanged.asObservable());
    api.onNotification.and.returnValue(EMPTY);
    api.restartAdbServer.and.resolveTo({ ...defaults, success: true, error: null });
    api.downloadAdbPlatformTools.and.resolveTo({ ...defaults, success: true, error: null });
    installApi();

    await TestBed.configureTestingModule({
      imports: [AdbSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<AdbSettingsComponent>> {
    const fixture = TestBed.createComponent(AdbSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function toggleInputs(fixture: ComponentFixture<AdbSettingsComponent>): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-toggle-switch .ts-input'));
  }

  it('renders the device list from the stubbed response, with state chip copy and the USB column', async () => {
    const unauthorized: AdbDevice = {
      serial: 'OTHERSERIAL',
      state: 'Unauthorized',
      model: null,
      manufacturer: null,
      authorized: false,
      isDefault: false,
      tunnelEstablished: false,
      tunnelDevicePort: null,
      tunnelError: 'Port already in use',
    };
    installApi({ devices: [device, unauthorized] });

    const fixture = await create();

    const rows = fixture.nativeElement.querySelectorAll('.adb__device-row');
    expect(rows.length).toBe(2);

    expect(rows[0].textContent).toContain('Pixel 7');
    expect(rows[0].textContent).toContain('Google');
    expect(rows[0].textContent).not.toContain('R58M12ABCDE');
    expect(rows[0].textContent).toContain('•••••••BCDE');
    expect(rows[0].textContent).toContain('Ready');
    expect(rows[0].textContent).toContain('Ready on port 55001');

    expect(rows[1].textContent).toContain('RIAL');
    expect(rows[1].textContent).toContain('Not authorized - confirm the dialog on your phone');
    expect(rows[1].textContent).toContain('Port already in use');
  });

  it('falls back to the serial as the device name when no model was reported', async () => {
    installApi({ devices: [{ ...device, model: null, manufacturer: null }] });

    const fixture = await create();

    expect(fixture.nativeElement.querySelector('.adb__device-name').textContent.trim()).toBe('R58M12ABCDE');
  });

  it('shows an empty state and lets Rescan refetch when there are no devices', async () => {
    installApi({ devices: [] });
    const fixture = await create();

    expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeTruthy();

    installApi({ devices: [device] });
    const rescanButton = fixture.nativeElement.querySelector(
      'shared-button[settingsSectionAction] button') as HTMLButtonElement;
    rescanButton.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.getAdbSettings).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('.adb__device-row')).not.toBeNull();
  });

  it('sets a device as the default through its row action', async () => {
    installApi({
      devices: [device, { ...device, serial: 'SECONDSERIAL', model: 'Pixel 8', isDefault: false }],
    });
    const fixture = await create();

    const rows = fixture.nativeElement.querySelectorAll('.adb__device-row');
    (rows[1].querySelector('shared-button button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(api.updateAdbSettings).toHaveBeenCalledOnceWith(jasmine.objectContaining({ defaultDeviceSerial: 'SECONDSERIAL' }));
  });

  it('disables and explains the toggle when adb is unsupported', async () => {
    installApi({ supported: false, unsupportedReason: 'adb is not available on this platform build.' });
    const fixture = await create();

    expect(toggleInputs(fixture)[0].disabled).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('adb is not available on this platform build.');
  });

  it('issues an update with enabled: true when the "Enable ADB" toggle is switched on', async () => {
    installApi({ enabled: false });
    const fixture = await create();

    toggleInputs(fixture)[0].click();
    await fixture.whenStable();

    expect(api.updateAdbSettings).toHaveBeenCalledOnceWith(jasmine.objectContaining({ enabled: true }));
  });

  it('never mentions reverse tunnels or forwarding in the USB connections copy', async () => {
    const fixture = await create();

    const text = fixture.nativeElement.textContent as string;
    expect(text.toLowerCase()).not.toContain('adb reverse');
    expect(text.toLowerCase()).not.toContain('adb forward');
    expect(text.toLowerCase()).not.toContain('tunnel');
  });

  it('renders the error banner when a write reports success: false', async () => {
    const fixture = await create();
    api.updateAdbSettings.and.resolveTo({
      ...defaults,
      success: false,
      error: 'The adb executable was not found at the configured path.',
    });

    await fixture.componentInstance.setEnabled(false);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The adb executable was not found at the configured path.');
  });

  it('refetches and re-renders the device list when an AdbStateChangedEvent arrives', async () => {
    const fixture = await create();
    expect(fixture.nativeElement.textContent).not.toContain('Pixel 8');

    installApi({
      devices: [device, { ...device, serial: 'SECONDSERIAL', model: 'Pixel 8', isDefault: false }],
    });
    adbStateChanged.next({ changedAt: new Date().toISOString() });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.getAdbSettings).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).toContain('Pixel 8');
  });

  it('shows the "not found" banner when enabled but nothing was resolved', async () => {
    installApi({ enabled: true, resolvedExecutablePath: null, executableSource: 'None' });
    const fixture = await create();

    expect(fixture.nativeElement.querySelector('shared-error-banner')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('platform-tools');
  });

  it('does not show the "not found" banner while ADB itself is disabled', async () => {
    installApi({ enabled: false, resolvedExecutablePath: null, executableSource: 'None' });
    const fixture = await create();

    expect(fixture.componentInstance.notFoundWhileEnabled()).toBeFalse();
  });

  it('shows the download button when enabled and nothing was resolved', async () => {
    installApi({ enabled: true, resolvedExecutablePath: null, executableSource: 'None' });
    const fixture = await create();

    const button = fixture.nativeElement.querySelector('.adb__download shared-button button');
    expect(button).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Download platform-tools');
    expect(fixture.nativeElement.textContent).toContain('Google');
  });

  it('hides the download button once an executable path is resolved', async () => {
    const fixture = await create();

    expect(fixture.nativeElement.querySelector('.adb__download')).toBeNull();
  });

  it('hides the download button while ADB itself is disabled', async () => {
    installApi({ enabled: false, resolvedExecutablePath: null, executableSource: 'None' });
    const fixture = await create();

    expect(fixture.nativeElement.querySelector('.adb__download')).toBeNull();
  });

  it('downloads platform-tools and applies the returned state on success', async () => {
    installApi({ enabled: true, resolvedExecutablePath: null, executableSource: 'None' });
    const fixture = await create();
    api.downloadAdbPlatformTools.and.resolveTo({
      ...defaults,
      resolvedExecutablePath: '/opt/platform-tools/platform-tools/adb',
      executableSource: 'Configured',
      success: true,
      error: null,
    });

    const downloadButton = fixture.nativeElement.querySelector('.adb__download shared-button button') as HTMLButtonElement;
    downloadButton.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.downloadAdbPlatformTools).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('/opt/platform-tools/platform-tools/adb');
    expect(fixture.nativeElement.querySelector('.adb__download')).toBeNull();
  });

  it('shows the returned error message when the download reports success: false', async () => {
    installApi({ enabled: true, resolvedExecutablePath: null, executableSource: 'None' });
    const fixture = await create();
    api.downloadAdbPlatformTools.and.resolveTo({
      ...defaults,
      resolvedExecutablePath: null,
      executableSource: 'None',
      success: false,
      error: 'Android platform-tools could not be downloaded. Check your internet connection and try again.',
    });

    await fixture.componentInstance.downloadPlatformTools();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent)
      .toContain('Android platform-tools could not be downloaded. Check your internet connection and try again.');
  });

  it('shows only the enable switch while ADB is disabled', async () => {
    installApi({ enabled: false, devices: [device] });
    const fixture = await create();

    const headings = Array.from(
      fixture.nativeElement.querySelectorAll('.settings-section__title') as NodeListOf<HTMLElement>,
    ).map(heading => heading.textContent!.trim());

    expect(headings).toEqual(['ADB']);
    expect(fixture.nativeElement.querySelector('.adb__device-row')).toBeNull();
  });

  it('reveals the rest once ADB is enabled', async () => {
    installApi({ enabled: true, devices: [device] });
    const fixture = await create();

    const headings = Array.from(
      fixture.nativeElement.querySelectorAll('.settings-section__title') as NodeListOf<HTMLElement>,
    ).map(heading => heading.textContent!.trim());

    expect(headings).toContain('Devices');
    expect(fixture.nativeElement.querySelector('.adb__device-row')).not.toBeNull();
  });

  it('shows a readable label for where the executable was resolved from', async () => {
    installApi({ executableSource: 'Configured' });
    const fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('Configured manually');
  });

  it('saves the explicit executable path once it differs from the loaded value', async () => {
    const fixture = await create();

    fixture.componentInstance.pathDraft.set('/opt/android-sdk/platform-tools/adb');
    fixture.detectChanges();
    await fixture.componentInstance.savePath();

    expect(api.updateAdbSettings)
      .toHaveBeenCalledOnceWith(jasmine.objectContaining({ executablePath: '/opt/android-sdk/platform-tools/adb' }));
  });

  it('asks for confirmation before restarting the ADB server', async () => {
    const fixture = await create();

    const restartButton = fixture.nativeElement.querySelector('.adb__actions shared-button button') as HTMLButtonElement;
    restartButton.click();
    fixture.detectChanges();

    expect(api.restartAdbServer).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Android Studio');

    await fixture.componentInstance.restartServer();

    expect(api.restartAdbServer).toHaveBeenCalledTimes(1);
  });

  it('renders diagnostics: the last error with its timestamp, and the unclean-shutdown cleanup count', async () => {
    installApi({
      lastError: 'The adb server could not be reached.',
      lastErrorAt: '2026-01-02T03:04:05.000Z',
      previousShutdownWasUnclean: true,
      staleTunnelsCleaned: 2,
    });
    const fixture = await create();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('The adb server could not be reached.');
    expect(text).toContain('did not shut down cleanly last time');
    expect(text).toContain('2 leftover USB connection(s)');
  });

  it('reloads after a failed request', async () => {
    const fixture = await create();
    api.updateAdbSettings.and.rejectWith(new Error('offline'));

    await fixture.componentInstance.setEnabled(false);

    expect(api.getAdbSettings).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.error()).toBe('The ADB settings could not be saved.');
  });
});
