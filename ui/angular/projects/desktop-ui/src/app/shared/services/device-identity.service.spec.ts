import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CLIENT_TYPE, DeviceIdentityService } from './device-identity.service';

describe('DeviceIdentityService', () => {
  beforeEach(() => localStorage.clear());

  function configure(clientType: 'web-client' | 'admin-ui' | 'native' = 'web-client'): DeviceIdentityService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: CLIENT_TYPE, useValue: clientType },
      ],
    });
    return TestBed.inject(DeviceIdentityService);
  }

  it('starts with no device id when nothing was ever stored', () => {
    const service = configure();
    expect(service.deviceId).toBeNull();
  });

  it('builds login info carrying the client type and no stored credential', () => {
    const service = configure('admin-ui');

    const info = service.buildLoginInfo();

    expect(info.clientType).toBe('admin-ui');
    expect(info.deviceId).toBeUndefined();
    expect(info.deviceSecret).toBeUndefined();
    expect(info.appVersion).toBeUndefined();
    expect(typeof info.proposedName).toBe('string');
  });

  it('adopts a freshly-minted credential and persists it under a client-type-namespaced key', () => {
    const service = configure('web-client');

    service.adopt({ deviceId: 'device-1', deviceSecret: 'secret-1' });

    expect(service.deviceId).toBe('device-1');
    expect(localStorage.getItem('md.device.web-client.id')).toBe('device-1');
    expect(localStorage.getItem('md.device.web-client.secret')).toBe('secret-1');
  });

  it('sends the adopted credential back on the next login info', () => {
    const service = configure();
    service.adopt({ deviceId: 'device-1', deviceSecret: 'secret-1' });

    const info = service.buildLoginInfo();

    expect(info.deviceId).toBe('device-1');
    expect(info.deviceSecret).toBe('secret-1');
  });

  it('keeps the stored secret when a later adopt omits it (existing device, no fresh secret)', () => {
    const service = configure();
    service.adopt({ deviceId: 'device-1', deviceSecret: 'secret-1' });

    service.adopt({ deviceId: 'device-1' });

    expect(service.buildLoginInfo().deviceSecret).toBe('secret-1');
  });

  it('ignores an adopt call without a deviceId', () => {
    const service = configure();

    service.adopt(undefined);
    service.adopt({ deviceId: '' } as unknown as { deviceId: string });

    expect(service.deviceId).toBeNull();
  });

  it('namespaces storage keys per client type so the web client and admin UI never collide', () => {
    const webClient = configure('web-client');
    webClient.adopt({ deviceId: 'web-device' });

    const adminUi = configure('admin-ui');
    adminUi.adopt({ deviceId: 'admin-device' });

    expect(localStorage.getItem('md.device.web-client.id')).toBe('web-device');
    expect(localStorage.getItem('md.device.admin-ui.id')).toBe('admin-device');
    expect(adminUi.deviceId).toBe('admin-device');
  });

  it('restores a previously persisted device id on construction', () => {
    localStorage.setItem('md.device.web-client.id', 'restored-device');
    localStorage.setItem('md.device.web-client.secret', 'restored-secret');

    const service = configure('web-client');

    expect(service.deviceId).toBe('restored-device');
    expect(service.buildLoginInfo().deviceSecret).toBe('restored-secret');
  });

  it('degrades to an in-memory credential for the session when localStorage throws', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('blocked');
    spyOn(Storage.prototype, 'setItem').and.throwError('blocked');

    const service = configure();
    expect(service.deviceId).toBeNull();

    service.adopt({ deviceId: 'device-1', deviceSecret: 'secret-1' });

    expect(service.deviceId).toBe('device-1');
    expect(service.buildLoginInfo().deviceSecret).toBe('secret-1');
  });
});
