import { computed, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DownloadAdbPlatformToolsResponse, UpdateAdbSettingsResponse, UserNotification } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { NotificationCenterService } from '../../../services/notification-center.service';
import { SettingsModalService } from '../../../services/settings-modal.service';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { PluginAdbConsentDialogComponent } from './plugin-adb-consent-dialog.component';

describe('PluginAdbConsentDialogComponent', () => {
  let fixture: ComponentFixture<PluginAdbConsentDialogComponent>;
  let notifications: ReturnType<typeof signal<UserNotification[]>>;
  let dismissSpy: jasmine.Spy;
  let settingsOpenSpy: jasmine.Spy;
  let updateAdbSettingsSpy: jasmine.Spy;

  const adbRequest: UserNotification = {
    id: 'adb-1',
    sequence: 1,
    timestamp: '2026-09-21T10:00:00+00:00',
    severity: 'Warning',
    kind: 'Security',
    title: 'A plugin wants to use ADB',
    message: 'ADB Probe needs ADB to work with your Android devices, but ADB is turned off in Macro Deck.',
    sourceName: 'ADB Probe',
    actions: [{ kind: 'EnablePluginAdb' }, { kind: 'DismissNotification' }],
  };

  beforeEach(async () => {
    notifications = signal<UserNotification[]>([]);
    dismissSpy = jasmine.createSpy('dismiss');
    settingsOpenSpy = jasmine.createSpy('open');

    await TestBed.configureTestingModule({
      imports: [PluginAdbConsentDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: NotificationCenterService,
          useValue: { notifications: computed(() => notifications()), dismiss: dismissSpy },
        },
        { provide: SettingsModalService, useValue: { open: settingsOpenSpy } },
      ],
    }).compileComponents();

    updateAdbSettingsSpy = spyOn(TestBed.inject(ApiService), 'updateAdbSettings')
      .and.resolveTo({ success: true, error: null, resolvedExecutablePath: 'C:/platform-tools/adb.exe' } as UpdateAdbSettingsResponse);

    fixture = TestBed.createComponent(PluginAdbConsentDialogComponent);
  });

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve));
  }

  function buttons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('button'));
  }

  function button(label: string): HTMLButtonElement {
    return buttons().find(candidate => candidate.textContent!.includes(label))!;
  }

  it('stays out of the way while no plugin is waiting for ADB', async () => {
    notifications.set([{ ...adbRequest, id: 'other', actions: [{ kind: 'OpenLogs' }], title: 'Something else' }]);

    await render();

    expect(fixture.nativeElement.textContent).not.toContain('A plugin wants to use ADB');
  });

  it('asks as soon as a plugin that needs ADB was installed', async () => {
    notifications.set([adbRequest]);

    await render();

    expect(fixture.nativeElement.textContent).toContain('A plugin wants to use ADB');
    expect(fixture.nativeElement.textContent).toContain('ADB is turned off in Macro Deck');
  });

  it('turns on ADB and plugin access in one step when the user allows it', async () => {
    notifications.set([adbRequest]);
    await render();

    button('Allow').click();
    await new Promise(resolve => setTimeout(resolve));

    expect(updateAdbSettingsSpy).toHaveBeenCalledOnceWith({ enabled: true, allowPlugins: true });
    expect(dismissSpy).toHaveBeenCalledWith('adb-1');
    expect(settingsOpenSpy).not.toHaveBeenCalled();
  });

  it('offers to download adb right away when Macro Deck cannot find it after allowing', async () => {
    updateAdbSettingsSpy.and.resolveTo({ success: true, error: null, resolvedExecutablePath: null } as UpdateAdbSettingsResponse);
    const downloadSpy = spyOn(TestBed.inject(ApiService), 'downloadAdbPlatformTools')
      .and.resolveTo({ success: true, error: null, resolvedExecutablePath: 'C:/platform-tools/adb.exe' } as DownloadAdbPlatformToolsResponse);
    notifications.set([adbRequest]);
    await render();

    button('Allow').click();
    await settle();
    notifications.set([]);
    await render();

    expect(dismissSpy).toHaveBeenCalledWith('adb-1');
    expect(fixture.nativeElement.textContent).toContain('Macro Deck cannot find adb');

    button('Download platform-tools').click();
    await settle();
    await render();

    expect(downloadSpy).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).not.toContain('Macro Deck cannot find adb');
    expect(settingsOpenSpy).not.toHaveBeenCalled();
  });

  it('keeps offering the download and says why when it fails', async () => {
    updateAdbSettingsSpy.and.resolveTo({ success: true, error: null, resolvedExecutablePath: null } as UpdateAdbSettingsResponse);
    spyOn(TestBed.inject(ApiService), 'downloadAdbPlatformTools')
      .and.resolveTo({ success: false, error: 'No connection to dl.google.com' } as DownloadAdbPlatformToolsResponse);
    notifications.set([adbRequest]);
    await render();

    button('Allow').click();
    await settle();
    await render();
    button('Download platform-tools').click();
    await settle();
    await render();

    expect(fixture.nativeElement.textContent).toContain('Macro Deck cannot find adb');
    expect(fixture.nativeElement.textContent).toContain('No connection to dl.google.com');
  });

  it('closes on Not now but keeps the request in the notifications', async () => {
    notifications.set([adbRequest]);
    await render();

    button('Not now').click();
    await render();

    expect(updateAdbSettingsSpy).not.toHaveBeenCalled();
    expect(dismissSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).not.toContain('A plugin wants to use ADB');
  });

  it('opens the ADB settings when the change could not be saved', async () => {
    updateAdbSettingsSpy.and.resolveTo({ success: false, error: 'adb not found' } as UpdateAdbSettingsResponse);
    notifications.set([adbRequest]);
    await render();

    button('Allow').click();
    await new Promise(resolve => setTimeout(resolve));

    expect(dismissSpy).not.toHaveBeenCalled();
    expect(settingsOpenSpy).toHaveBeenCalledWith('adb');
  });
});
