import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import {
  GetNativeUsbSettingsResponse,
  NativeUsbDevice,
  NativeUsbStateChangedEvent,
} from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { UsbSettingsComponent } from './usb-settings.component';

describe('UsbSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  let stateChanged: Subject<NativeUsbStateChangedEvent>;

  const phone: NativeUsbDevice = {
    id: 'android-1-4.2',
    platform: 'Android',
    serial: 'EXAMPLE0001',
    manufacturer: 'Example',
    product: 'Phone 7',
    state: 'Available',
    picked: false,
    remembered: false,
    canConnect: true,
    knownToAdb: false,
  };

  const iphone: NativeUsbDevice = {
    id: 'ios-EXAMPLE-UDID',
    platform: 'Ios',
    serial: 'EXAMPLE-UDID',
    manufacturer: null,
    product: null,
    state: 'Connecting',
    picked: false,
    remembered: false,
    canConnect: false,
    knownToAdb: false,
  };

  const defaults: GetNativeUsbSettingsResponse = {
    enabled: true,
    androidAvailable: true,
    iosAvailable: true,
    bridgeAvailable: true,
    httpsOnly: false,
    devices: [phone],
    rememberedDevices: [],
  };

  beforeEach(async () => {
    stateChanged = new Subject<NativeUsbStateChangedEvent>();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getNativeUsbSettings',
      'updateNativeUsbSettings',
      'connectNativeUsbDevice',
      'forgetNativeUsbDevice',
      'refreshNativeUsbSettings',
      'onNativeUsbStateChanged',
      'onNotification',
    ]);
    api.onNativeUsbStateChanged.and.returnValue(stateChanged.asObservable());
    api.onNotification.and.returnValue(EMPTY);
    api.getNativeUsbSettings.and.resolveTo(defaults);

    await TestBed.configureTestingModule({
      imports: [UsbSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<UsbSettingsComponent>> {
    const fixture = TestBed.createComponent(UsbSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  async function settle(fixture: ComponentFixture<UsbSettingsComponent>): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(fixture: ComponentFixture<UsbSettingsComponent>, selector: string): string {
    return (fixture.nativeElement.querySelector(selector)?.textContent ?? '').trim();
  }

  function buttonsLabelled(fixture: ComponentFixture<UsbSettingsComponent>, label: string): HTMLButtonElement[] {
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .filter(button => button.textContent?.trim() === label);
  }

  it('opens with the feature card: its own title, an experimental tag and the toggle, not the page title again', async () => {
    const fixture = await create();

    const titles = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.settings-section__title'))
      .map(title => title.textContent?.trim());
    expect(titles[0]).toBe('USB without debugging');
    expect(titles).not.toContain('USB connections');
    expect(text(fixture, '.usb__experimental')).toBe('Experimental');
    expect(fixture.nativeElement.querySelector('.usb__feature shared-toggle-switch')).toBeTruthy();
  });

  it('shows each platform as a status chip and the install hint only when it is unavailable', async () => {
    api.getNativeUsbSettings.and.resolveTo({ ...defaults, androidAvailable: false });
    const fixture = await create();

    expect(text(fixture, '.usb__status-android .usb__chip')).toBe('Not available');
    expect(text(fixture, '.usb__status-android')).toContain('Install libusb');
    expect(text(fixture, '.usb__status-ios .usb__chip')).toBe('Available');
    expect(text(fixture, '.usb__status-ios')).not.toContain('Apple Devices');
  });

  it('lists an Android phone by manufacturer and product with a masked serial, a status chip and the connect action', async () => {
    const fixture = await create();

    const row: HTMLElement = fixture.nativeElement.querySelector('.usb__row');
    expect(row.querySelector('.usb__row-name')?.textContent?.trim()).toBe('Example Phone 7');
    expect(row.textContent).not.toContain('EXAMPLE0001');
    expect(row.querySelector('.usb__row-serial')?.textContent).toContain('•••••••0001');
    expect(row.querySelector('.usb__row-state')?.textContent?.trim()).toBe('Not connected');
    expect(row.querySelector('.icon-device-phone')).toBeTruthy();
    expect(buttonsLabelled(fixture, 'Connect without debugging').length).toBe(1);
    expect(fixture.nativeElement.querySelector('.usb__note-android')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.usb__note-ios')).toBeNull();
  });

  it('shows an iPhone once, with a waiting status and hint, no action and only the iOS note', async () => {
    api.getNativeUsbSettings.and.resolveTo({ ...defaults, devices: [iphone] });
    const fixture = await create();

    const row: HTMLElement = fixture.nativeElement.querySelector('.usb__row');
    expect(row.querySelector('.usb__row-name')?.textContent?.trim()).toBe('iPhone or iPad');
    expect(Array.from(row.querySelectorAll('span')).filter(span => span.textContent?.trim() === 'iPhone or iPad').length)
      .toBe(1);
    expect(row.querySelector('.icon-apple')).toBeTruthy();
    expect(row.querySelector('.usb__row-state')?.textContent?.trim()).toBe('Waiting for the app');
    expect(row.querySelector('.usb__row-hint')?.textContent).toContain('Open Macro Deck on the iPhone or iPad.');
    expect(row.querySelector('shared-button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.usb__note-ios')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.usb__note-android')).toBeNull();
  });

  it('warns before connecting a device ADB knows that its ADB sessions will be interrupted', async () => {
    api.getNativeUsbSettings.and.resolveTo({
      ...defaults,
      devices: [{ ...phone, state: 'ServedByAdb', knownToAdb: true }],
    });
    const fixture = await create();

    expect(text(fixture, '.usb__row-adb')).toContain('scrcpy');
    expect(buttonsLabelled(fixture, 'Connect without debugging').length).toBe(1);
  });

  it('shows a phone that is being switched as switching, without a connect button', async () => {
    api.getNativeUsbSettings.and.resolveTo({ ...defaults, devices: [{ ...phone, state: 'Switching', canConnect: false }] });
    const fixture = await create();

    expect(text(fixture, '.usb__row-state')).toBe('Switching');
    expect(fixture.nativeElement.querySelector('.usb__row-state.usb__chip--pending')).toBeTruthy();
    expect(buttonsLabelled(fixture, 'Connect without debugging').length).toBe(0);
  });

  it('offers to connect a stopped phone again and says so', async () => {
    api.getNativeUsbSettings.and.resolveTo({ ...defaults, devices: [{ ...phone, state: 'Stopped', canConnect: true }] });
    const fixture = await create();

    expect(text(fixture, '.usb__row-state')).toBe('Stopped');
    expect(text(fixture, '.usb__row-hint')).toContain('Click Connect without debugging to try again');
    expect(buttonsLabelled(fixture, 'Connect without debugging').length).toBe(1);
  });

  it('picks the device by id and shows the new state', async () => {
    api.connectNativeUsbDevice.and.resolveTo({
      ...defaults,
      devices: [{ ...phone, state: 'Waiting', picked: true, canConnect: false }],
      success: true,
      errorCode: null,
    });
    const fixture = await create();

    buttonsLabelled(fixture, 'Connect without debugging')[0].click();
    await settle(fixture);

    expect(api.connectNativeUsbDevice).toHaveBeenCalledWith({ id: 'android-1-4.2' });
    expect(text(fixture, '.usb__row-state')).toBe('Waiting to connect');
    expect(buttonsLabelled(fixture, 'Connect without debugging').length).toBe(0);
  });

  it('shows an error when the host refuses the pick', async () => {
    api.connectNativeUsbDevice.and.resolveTo({ ...defaults, success: false, errorCode: 'NotAllowed' });
    const fixture = await create();

    buttonsLabelled(fixture, 'Connect without debugging')[0].click();
    await settle(fixture);

    expect(fixture.nativeElement.textContent).toContain('This device cannot be connected right now');
  });

  it('explains that an HTTPS-only host cannot bridge USB connections and disables connecting', async () => {
    api.getNativeUsbSettings.and.resolveTo({ ...defaults, bridgeAvailable: false, httpsOnly: true });
    const fixture = await create();

    expect(text(fixture, '.usb__note')).toContain('only accepts HTTPS');
    expect(buttonsLabelled(fixture, 'Connect without debugging')[0].disabled).toBeTrue();
  });

  it('shows an empty state when no device is plugged in', async () => {
    api.getNativeUsbSettings.and.resolveTo({ ...defaults, devices: [] });
    const fixture = await create();

    expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Plug in a phone or tablet');
  });

  it('lists remembered devices by their last known name and forgets one', async () => {
    api.getNativeUsbSettings.and.resolveTo({
      ...defaults,
      devices: [],
      rememberedDevices: [{ serial: 'EXAMPLE0001', name: 'Example Phone 7' }],
    });
    api.forgetNativeUsbDevice.and.resolveTo({ ...defaults, devices: [], rememberedDevices: [], success: true });
    const fixture = await create();

    const row: HTMLElement = fixture.nativeElement.querySelector('.usb__remembered');
    expect(row.querySelector('.usb__row-name')?.textContent?.trim()).toBe('Example Phone 7');
    expect(row.textContent).toContain('•••••••0001');
    buttonsLabelled(fixture, 'Forget')[0].click();
    await settle(fixture);

    expect(api.forgetNativeUsbDevice).toHaveBeenCalledWith({ serial: 'EXAMPLE0001' });
    expect(fixture.nativeElement.querySelector('.usb__remembered')).toBeNull();
  });

  it('searches for devices again on the host when asked to', async () => {
    api.refreshNativeUsbSettings.and.resolveTo({ ...defaults, devices: [] });
    const fixture = await create();

    fixture.nativeElement.querySelector('[settingsSectionAction] button, shared-button[settingsSectionAction] button')
      ?.click();
    await settle(fixture);

    expect(api.refreshNativeUsbSettings).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.usb__row')).toBeNull();
  });

  it('turns USB without debugging off and hides everything but the feature card', async () => {
    api.updateNativeUsbSettings.and.resolveTo({ ...defaults, enabled: false, devices: [], success: true });
    const fixture = await create();

    const toggle: HTMLInputElement = fixture.nativeElement.querySelector('shared-toggle-switch .ts-input');
    toggle.click();
    await settle(fixture);

    expect(api.updateNativeUsbSettings).toHaveBeenCalledWith({ enabled: false });
    expect(fixture.nativeElement.querySelectorAll('shared-settings-section').length).toBe(1);
  });

  it('refetches when the host reports a state change', async () => {
    const fixture = await create();
    api.getNativeUsbSettings.and.resolveTo({ ...defaults, devices: [{ ...phone, state: 'Linked', canConnect: false }] });

    stateChanged.next({ changedAt: '2026-01-01T00:00:00Z' });
    await settle(fixture);

    expect(text(fixture, '.usb__row-state')).toBe('Connected');
    expect(fixture.nativeElement.querySelector('.usb__row-state.usb__chip--ok')).toBeTruthy();
  });
});
