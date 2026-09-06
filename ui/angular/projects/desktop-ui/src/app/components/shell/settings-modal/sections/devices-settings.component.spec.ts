import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Device, Profile } from '@macro-deck/runtime';
import { AuthService, DeviceIdentityService, ProfileService, ToastService } from '@shared';
import { DeviceService } from '../../../../services/device.service';
import { DevicesSettingsComponent } from './devices-settings.component';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';

function device(id: string, overrides: Partial<Device> = {}): Device {
  return {
    id,
    name: `Device ${id}`,
    nameIsCustom: false,
    clientType: 'web-client',
    formFactor: 'desktop',
    online: true,
    connectionCount: 1,
    hasActiveSession: true,
    lastSeenAt: '2026-08-03T00:00:00Z',
    createdAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

function profile(id: string, name: string): Profile {
  return {
    id,
    name,
    order: 0,
    layoutType: 'Grid',
    isVirtual: false,
    layout: { rows: 3, columns: 5, rowsLocked: false, columnsLocked: false },
    defaultRows: 3,
    defaultColumns: 5,
    defaultBackground: null,
    defaultSpacing: null,
    defaultBorderRadius: null,
  };
}

describe('DevicesSettingsComponent', () => {
  let fixture: ComponentFixture<DevicesSettingsComponent>;
  let deviceServiceSpy: jasmine.SpyObj<DeviceService>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let devicesSignal: WritableSignal<Device[]>;
  let loadErrorSignal: WritableSignal<string | null>;

  function configure(
    devices: Device[],
    thisDeviceId: string | null = null,
    liveDeviceId: string | null = null,
    profiles: Profile[] = [],
  ): void {
    deviceServiceSpy = jasmine.createSpyObj<DeviceService>(
      'DeviceService', ['load', 'rename', 'logout', 'remove', 'setStartupProfile', 'openProfile']);
    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['logout']);
    devicesSignal = signal<Device[]>(devices);
    loadErrorSignal = signal<string | null>(null);
    Object.defineProperty(deviceServiceSpy, 'devices', { value: devicesSignal });
    Object.defineProperty(deviceServiceSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(deviceServiceSpy, 'loadError', { value: loadErrorSignal });
    deviceServiceSpy.load.and.resolveTo();
    deviceServiceSpy.rename.and.resolveTo(null);
    deviceServiceSpy.logout.and.resolveTo(true);
    deviceServiceSpy.remove.and.resolveTo({ success: true });
    deviceServiceSpy.setStartupProfile.and.resolveTo({ success: true });
    deviceServiceSpy.openProfile.and.resolveTo({ success: true });

    TestBed.configureTestingModule({
      imports: [DevicesSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: DeviceService, useValue: deviceServiceSpy },
        { provide: DeviceIdentityService, useValue: { deviceId: thisDeviceId } },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: ProfileService, useValue: { sortedProfiles: () => profiles } },
      ],
    });
    Object.defineProperty(authServiceSpy, 'currentDeviceId', { value: signal(liveDeviceId) });
    authServiceSpy.logout.and.resolveTo();
  }

  async function create(): Promise<ComponentFixture<DevicesSettingsComponent>> {
    const f = TestBed.createComponent(DevicesSettingsComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('loads devices on init', async () => {
    configure([]);
    fixture = await create();

    expect(deviceServiceSpy.load).toHaveBeenCalled();
  });

  it('shows the empty state when there are no devices', async () => {
    configure([]);
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.devices-empty')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.devices-row')).toBeNull();
  });

  it('shows the load error banner when present', async () => {
    configure([]);
    loadErrorSignal.set('Failed to load devices');
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.devices-error')).toBeTruthy();
  });

  it('renders one row per device with its name and type badge', async () => {
    configure([
      device('d1', { name: 'Alice iPhone', clientType: 'web-client' }),
      device('d2', { name: 'Kiosk', clientType: 'admin-ui' }),
    ]);
    fixture = await create();

    const rows = fixture.nativeElement.querySelectorAll('.devices-row');
    expect(rows.length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Alice iPhone');
    expect(text).toContain('Web Client');
    expect(text).toContain('Kiosk');
    expect(text).toContain('Admin UI');
  });

  it('renders the secondary line with browser, platform and last-seen', async () => {
    configure([device('d1', { browser: 'Chrome 141', platform: 'Windows' })]);
    fixture = await create();

    const secondary = fixture.nativeElement.querySelector('.devices-secondary')?.textContent as string;
    expect(secondary).toContain('Chrome 141');
    expect(secondary).toContain('Windows');
    expect(secondary).toContain('last seen');
  });

  it('marks the current device with a "This device" marker and sorts it first', async () => {
    configure([device('d1', { name: 'Other', online: false }), device('d2', { name: 'Mine' })], 'd2');
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.devices-this-marker')).toBeTruthy();
    const names = Array.from(fixture.nativeElement.querySelectorAll('.devices-name'))
      .map(el => (el as HTMLElement).textContent);
    expect(names[0]).toBe('Mine');
  });

  it("prefers AuthService's live device id over DeviceIdentityService's stored id for This device", async () => {
    configure([device('d1'), device('d2')], 'd1', 'd2');
    fixture = await create();

    expect(fixture.componentInstance.isThisDevice(device('d1'))).toBeFalse();
    expect(fixture.componentInstance.isThisDevice(device('d2'))).toBeTrue();
  });

  it('falls back to the stored device id when there is no live value yet', async () => {
    configure([device('d1')], 'd1', null);
    fixture = await create();

    expect(fixture.componentInstance.isThisDevice(fixture.componentInstance.devices()[0])).toBeTrue();
  });

  describe('session state (issue #250)', () => {
    it('shows an offline device with no active session as "Signed out"', async () => {
      configure([device('d1', { online: false, hasActiveSession: false, connectionCount: 0 })]);
      fixture = await create();

      expect(fixture.nativeElement.querySelector('.devices-signedout-marker')?.textContent).toContain('Signed out');
      const dot = fixture.nativeElement.querySelector('.devices-online-dot') as HTMLElement;
      expect(dot.title).toBe('Signed out');
    });

    it('shows a plain offline device (still holding a session) without a "Signed out" marker', async () => {
      configure([device('d1', { online: false, hasActiveSession: true, connectionCount: 0 })]);
      fixture = await create();

      expect(fixture.nativeElement.querySelector('.devices-signedout-marker')).toBeNull();
      const dot = fixture.nativeElement.querySelector('.devices-online-dot') as HTMLElement;
      expect(dot.title).toBe('Offline');
    });

    it('includes the connection count in the online indicator once more than one tab is connected', async () => {
      configure([device('d1', { online: true, connectionCount: 3 })]);
      fixture = await create();

      const dot = fixture.nativeElement.querySelector('.devices-online-dot') as HTMLElement;
      expect(dot.title).toBe('Online · 3 connections');
    });

    it('keeps the plain "Online" title for a single connection', async () => {
      configure([device('d1', { online: true, connectionCount: 1 })]);
      fixture = await create();

      const dot = fixture.nativeElement.querySelector('.devices-online-dot') as HTMLElement;
      expect(dot.title).toBe('Online');
    });
  });

  describe('provider-registered devices', () => {
    it('names the providing integration and marks the row as a provided device', async () => {
      configure([
        device('d1', {
          name: 'Stream Deck XL',
          clientType: 'provider',
          providerId: 'com.example.deck',
          providerName: 'Stream Deck',
          providerDeviceId: 'SERIAL-1',
        }),
      ]);
      fixture = await create();

      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('Provided device');
      expect(text).toContain('Stream Deck');
      expect(fixture.nativeElement.querySelector('.devices-provider-badge')).toBeTruthy();
    });

    it('offers no Log out action, because the device never signed in', async () => {
      configure([
        device('d1', { clientType: 'provider', providerId: 'com.example.deck', hasActiveSession: false }),
      ]);
      fixture = await create();

      fixture.componentInstance.setMenuOpen('d1', true);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelectorAll('.devices-menu-logout').length).toBe(0);
      expect(fixture.nativeElement.querySelectorAll('.devices-menu-remove').length).toBe(1);
    });
  });

  it('disables the Log out menu item only for the current device', async () => {
    configure([device('d1'), device('d2')], 'd1');
    fixture = await create();

    const logoutDisabledFor = async (deviceId: string): Promise<boolean> => {
      fixture.componentInstance.setMenuOpen(deviceId, true);
      fixture.detectChanges();
      await fixture.whenStable();

      const buttons = Array.from(fixture.nativeElement.querySelectorAll('.devices-menu-logout')) as
        HTMLButtonElement[];
      expect(buttons.length).toBe(1);

      return buttons[0].disabled;
    };

    expect(await logoutDisabledFor('d1')).toBeTrue();
    expect(await logoutDisabledFor('d2')).toBeFalse();
  });

  describe('rename', () => {
    it('opens the rename modal pre-filled with the current name and saves through the service', async () => {
      configure([device('d1', { name: 'Old name' })]);
      fixture = await create();

      fixture.componentInstance.startRename(fixture.componentInstance.devices()[0]);
      expect(fixture.componentInstance.renameValue()).toBe('Old name');

      fixture.componentInstance.renameValue.set('New name');
      await fixture.componentInstance.commitRename();

      expect(deviceServiceSpy.rename).toHaveBeenCalledWith('d1', 'New name');
      expect(fixture.componentInstance.renamingDevice()).toBeNull();
    });

    it('does not submit an empty (whitespace-only) name', async () => {
      configure([device('d1')]);
      fixture = await create();

      fixture.componentInstance.startRename(fixture.componentInstance.devices()[0]);
      fixture.componentInstance.renameValue.set('   ');
      await fixture.componentInstance.commitRename();

      expect(deviceServiceSpy.rename).not.toHaveBeenCalled();
    });

    it('closes the row menu, so it does not linger behind the rename modal', async () => {
      configure([device('d1')]);
      fixture = await create();
      const component = fixture.componentInstance;

      component.setMenuOpen('d1', true);
      expect(component.openMenuDeviceId()).toBe('d1');

      component.startRename(component.devices()[0]);

      expect(component.openMenuDeviceId()).toBeNull();
    });
  });

  describe('logout', () => {
    it('requires confirmation before logging a device out', async () => {
      configure([device('d1')]);
      fixture = await create();

      fixture.componentInstance.requestLogout(fixture.componentInstance.devices()[0]);
      expect(fixture.componentInstance.logoutCandidate()).not.toBeNull();
      expect(deviceServiceSpy.logout).not.toHaveBeenCalled();

      await fixture.componentInstance.confirmLogout();

      expect(deviceServiceSpy.logout).toHaveBeenCalledWith('d1');
      expect(fixture.componentInstance.logoutCandidate()).toBeNull();
    });

    it('refuses to request a logout for the current device', async () => {
      configure([device('d1')], 'd1');
      fixture = await create();

      fixture.componentInstance.requestLogout(fixture.componentInstance.devices()[0]);

      expect(fixture.componentInstance.logoutCandidate()).toBeNull();
    });
  });

  describe('remove', () => {
    it('opens the rendered Remove device confirmation and cancel leaves the service untouched', async () => {
      configure([device('d1')]);
      fixture = await create();

      fixture.componentInstance.setMenuOpen('d1', true);
      fixture.detectChanges();
      await fixture.whenStable();
      const removeButton = Array.from(fixture.nativeElement.querySelectorAll('button'))
        .find(button => (button as HTMLElement).textContent?.includes('Remove device')) as HTMLButtonElement;
      expect(removeButton).toBeDefined();

      removeButton.click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(deviceServiceSpy.remove).not.toHaveBeenCalled();
      const modal = fixture.nativeElement.querySelector('shared-confirmation-modal') as HTMLElement;
      expect(modal).toBeTruthy();
      const cancelButton = Array.from(modal.querySelectorAll('button'))
        .find(button => (button as HTMLElement).textContent?.trim() === 'Cancel') as HTMLButtonElement;
      expect(cancelButton).toBeDefined();

      cancelButton.click();
      fixture.detectChanges();
      await new Promise(resolve => setTimeout(resolve, 160));
      fixture.detectChanges();

      expect(deviceServiceSpy.remove).not.toHaveBeenCalled();
      expect(fixture.componentInstance.removeCandidate()).toBeNull();
    });

    it('connects the rendered confirmation to the removal service', async () => {
      configure([device('d1')]);
      fixture = await create();

      fixture.componentInstance.setMenuOpen('d1', true);
      fixture.detectChanges();
      await fixture.whenStable();
      const removeButton = Array.from(fixture.nativeElement.querySelectorAll('button'))
        .find(button => (button as HTMLElement).textContent?.includes('Remove device')) as HTMLButtonElement;
      removeButton.click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const modal = fixture.nativeElement.querySelector('shared-confirmation-modal') as HTMLElement;
      const confirmButton = Array.from(modal.querySelectorAll('button'))
        .find(button => (button as HTMLElement).textContent?.trim() === 'Remove device') as HTMLButtonElement;
      expect(confirmButton).toBeDefined();
      confirmButton.click();
      fixture.detectChanges();
      await new Promise(resolve => setTimeout(resolve, 160));
      fixture.detectChanges();

      expect(deviceServiceSpy.remove).toHaveBeenCalledWith('d1');
    });

    it('requires confirmation and canceling does not call the service', async () => {
      configure([device('d1')]);
      fixture = await create();

      fixture.componentInstance.requestRemove(fixture.componentInstance.devices()[0]);
      fixture.componentInstance.cancelRemove();

      expect(deviceServiceSpy.remove).not.toHaveBeenCalled();
      expect(fixture.componentInstance.removeCandidate()).toBeNull();
    });

    it('keeps Remove enabled for both the current and another device', async () => {
      configure([device('d1'), device('d2')], 'd1');
      fixture = await create();

      const removeDisabledFor = async (deviceId: string): Promise<boolean> => {
        fixture.componentInstance.setMenuOpen(deviceId, true);
        fixture.detectChanges();
        await fixture.whenStable();
        const button = fixture.nativeElement.querySelector('.devices-menu-item:last-child') as HTMLButtonElement;
        return button.disabled;
      };

      expect(await removeDisabledFor('d1')).toBeFalse();
      expect(await removeDisabledFor('d2')).toBeFalse();
    });

    it('removes another device without logging out the current device', async () => {
      configure([device('d1'), device('d2')], 'd1');
      fixture = await create();

      fixture.componentInstance.requestRemove(fixture.componentInstance.devices().find(d => d.id === 'd2')!);
      await fixture.componentInstance.confirmRemove();

      expect(deviceServiceSpy.remove).toHaveBeenCalledWith('d2');
      expect(authServiceSpy.logout).not.toHaveBeenCalled();
      expect(fixture.componentInstance.removeCandidate()).toBeNull();
    });

    it('removes the current device and logs out after removal succeeds', async () => {
      configure([device('d1')], 'd1');
      fixture = await create();

      fixture.componentInstance.requestRemove(fixture.componentInstance.devices()[0]);
      await fixture.componentInstance.confirmRemove();

      expect(deviceServiceSpy.remove).toHaveBeenCalledWith('d1');
      expect(authServiceSpy.logout).toHaveBeenCalled();
      expect(fixture.componentInstance.removeCandidate()).toBeNull();
    });

    it('shows the host failure and keeps the confirmation open', async () => {
      configure([device('d1')]);
      deviceServiceSpy.remove.and.resolveTo({
        success: false,
        error: { code: 'NotFound', message: 'Could not remove the device' },
      });
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');

      fixture.componentInstance.requestRemove(fixture.componentInstance.devices()[0]);
      await fixture.componentInstance.confirmRemove();

      expect(toastSpy).toHaveBeenCalledWith('Could not remove the device', { variant: 'error' });
      expect(fixture.componentInstance.removeCandidate()).not.toBeNull();
      expect(authServiceSpy.logout).not.toHaveBeenCalled();
    });

    it('shows the localized failure and keeps the confirmation open when removal rejects', async () => {
      configure([device('d1')]);
      deviceServiceSpy.remove.and.rejectWith(new Error('offline'));
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');

      fixture.componentInstance.requestRemove(fixture.componentInstance.devices()[0]);
      await fixture.componentInstance.confirmRemove();

      expect(toastSpy).toHaveBeenCalledWith('Could not remove the device', { variant: 'error' });
      expect(fixture.componentInstance.removeCandidate()).not.toBeNull();
      expect(authServiceSpy.logout).not.toHaveBeenCalled();
    });
  });

  describe('startup profile (issue #251)', () => {
    it('shows "Default" for a device with no startup-profile assignment', async () => {
      configure([device('d1')]);
      fixture = await create();

      const text = fixture.nativeElement.querySelector('.devices-startup-line')?.textContent as string;
      expect(text).toContain('Default');
      expect(fixture.nativeElement.querySelector('.devices-startup-warning')).toBeNull();
    });

    it('shows the profile name when startupProfileName is set', async () => {
      configure([device('d1', { startupProfileId: 'p1', startupProfileName: 'Streaming' })]);
      fixture = await create();

      const text = fixture.nativeElement.querySelector('.devices-startup-line')?.textContent as string;
      expect(text).toContain('Streaming');
      expect(fixture.nativeElement.querySelector('.devices-startup-warning')).toBeNull();
    });

    it('shows a warning when startupProfileId is set but startupProfileName is absent', async () => {
      configure([device('d1', { startupProfileId: 'p1' })]);
      fixture = await create();

      const warning = fixture.nativeElement.querySelector('.devices-startup-warning');
      expect(warning).toBeTruthy();
      expect(warning?.textContent).toContain('Unavailable');
    });

    it('disables "Open profile on device" for an offline device', async () => {
      configure([device('d1', { online: false })]);
      fixture = await create();

      fixture.componentInstance.setMenuOpen('d1', true);
      fixture.detectChanges();
      await fixture.whenStable();

      const openButton = fixture.nativeElement.querySelector('.devices-menu-open') as HTMLButtonElement;
      expect(openButton.disabled).toBeTrue();
    });

    it('leaves "Open profile on device" enabled for an online device', async () => {
      configure([device('d1', { online: true })]);
      fixture = await create();

      fixture.componentInstance.setMenuOpen('d1', true);
      fixture.detectChanges();
      await fixture.whenStable();

      const openButton = fixture.nativeElement.querySelector('.devices-menu-open') as HTMLButtonElement;
      expect(openButton.disabled).toBeFalse();
    });

    it('commits the Default option by calling setStartupProfile with null', async () => {
      configure(
        [device('d1', { startupProfileId: 'p1', startupProfileName: 'Streaming' })],
        null, null, [profile('p1', 'Streaming')]);
      fixture = await create();

      fixture.componentInstance.startStartupProfile(fixture.componentInstance.devices()[0]);
      fixture.componentInstance.startupProfileDraft.set('');
      await fixture.componentInstance.commitStartupProfile();

      expect(deviceServiceSpy.setStartupProfile).toHaveBeenCalledWith('d1', null);
    });

    it('commits a picked profile by calling setStartupProfile with its id', async () => {
      configure([device('d1')], null, null, [profile('p1', 'Streaming')]);
      fixture = await create();

      fixture.componentInstance.startStartupProfile(fixture.componentInstance.devices()[0]);
      fixture.componentInstance.startupProfileDraft.set('p1');
      await fixture.componentInstance.commitStartupProfile();

      expect(deviceServiceSpy.setStartupProfile).toHaveBeenCalledWith('d1', 'p1');
    });

    it('toasts the error message when saving the startup profile fails', async () => {
      configure([device('d1')], null, null, [profile('p1', 'Streaming')]);
      deviceServiceSpy.setStartupProfile.and.resolveTo({
        success: false, error: { code: 'ValidationError', message: 'That profile no longer exists.' },
      });
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');

      fixture.componentInstance.startStartupProfile(fixture.componentInstance.devices()[0]);
      fixture.componentInstance.startupProfileDraft.set('p1');
      await fixture.componentInstance.commitStartupProfile();

      expect(toastSpy).toHaveBeenCalledWith('That profile no longer exists.', { variant: 'error' });
    });

    it('toasts the error message when opening a profile on a device fails', async () => {
      configure([device('d1')], null, null, [profile('p1', 'Streaming')]);
      deviceServiceSpy.openProfile.and.resolveTo({
        success: false, error: { code: 'Offline', message: 'The device is offline.' },
      });
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');

      fixture.componentInstance.startOpenProfile(fixture.componentInstance.devices()[0]);
      fixture.componentInstance.openProfileDraft.set('p1');
      await fixture.componentInstance.commitOpenProfile();

      expect(deviceServiceSpy.openProfile).toHaveBeenCalledWith('d1', 'p1');
      expect(toastSpy).toHaveBeenCalledWith('The device is offline.', { variant: 'error' });
      expect(fixture.componentInstance.openProfileDevice()).toBeNull();
    });
  });
});
