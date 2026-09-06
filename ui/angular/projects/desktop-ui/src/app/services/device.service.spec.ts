import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { Device } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { DeviceService } from './device.service';

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

describe('DeviceService', () => {
  let service: DeviceService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getDevices', 'renameDevice', 'logoutDevice', 'removeDevice', 'setDeviceStartupProfile', 'openProfileOnDevice',
    ]);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getDevices.and.resolveTo({ devices: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(DeviceService);
    api = apiSpy;
  });

  describe('cache repair', () => {
    it('pulls a fresh snapshot when the transport connects', async () => {
      api.getDevices.and.resolveTo({ devices: [device('d1')] });

      connectionState.set('connected');
      await service.load();
      TestBed.tick();

      expect(api.getDevices).toHaveBeenCalled();
      expect(service.devices().map(d => d.id)).toEqual(['d1']);
    });

    it('pulls again on a reconnect, so devices missed while offline are repaired', async () => {
      connectionState.set('connected');
      TestBed.tick();
      await service.load();
      const afterFirst = api.getDevices.calls.count();

      connectionState.set('disconnected');
      TestBed.tick();
      connectionState.set('connected');
      TestBed.tick();
      await service.load();

      expect(api.getDevices.calls.count()).toBeGreaterThan(afterFirst);
    });

    it('collapses concurrent loads into one request', async () => {
      const before = api.getDevices.calls.count();

      await Promise.all([service.load(), service.load(), service.load()]);

      expect(api.getDevices.calls.count()).toBe(before + 1);
    });

    it('keeps a push that arrived while the snapshot was in flight', async () => {
      const stale = device('d1', { name: 'Old name' });
      const fresh = device('d1', { name: 'New name' });
      let release: (value: { devices: Device[] }) => void = () => undefined;
      api.getDevices.and.returnValue(new Promise(resolve => {
        release = resolve;
      }));

      const load = service.load();
      push('DeviceChangedEvent', { device: fresh });
      release({ devices: [stale] });
      await load;

      expect(service.devices()).toEqual([fresh]);
    });

    it('keeps a removal that arrived while the snapshot was in flight', async () => {
      const doomed = device('d1');
      let release: (value: { devices: Device[] }) => void = () => undefined;
      api.getDevices.and.returnValue(new Promise(resolve => {
        release = resolve;
      }));

      const load = service.load();
      push('DeviceRemovedEvent', { deviceId: doomed.id });
      release({ devices: [doomed] });
      await load;

      expect(service.devices()).toEqual([]);
    });
  });

  describe('push subscriptions', () => {
    it('upserts a device on DeviceChangedEvent', () => {
      push('DeviceChangedEvent', { device: device('d1') });

      expect(service.devices().map(d => d.id)).toEqual(['d1']);

      push('DeviceChangedEvent', { device: device('d1', { name: 'Renamed' }) });

      expect(service.devices().length).toBe(1);
      expect(service.devices()[0].name).toBe('Renamed');
    });

    it('removes a device on DeviceRemovedEvent', () => {
      push('DeviceChangedEvent', { device: device('d1') });
      push('DeviceRemovedEvent', { deviceId: 'd1' });

      expect(service.devices()).toEqual([]);
    });
  });

  describe('rename', () => {
    it('upserts the returned device on success', async () => {
      api.renameDevice.and.resolveTo({ success: true, device: device('d1', { name: 'New name' }) });

      const result = await service.rename('d1', 'New name');

      expect(result?.name).toBe('New name');
      expect(service.devices()[0].name).toBe('New name');
    });

    it('returns null and leaves the cache untouched on failure', async () => {
      api.renameDevice.and.resolveTo({ success: false, error: { code: 'nope', message: 'nope' } });

      const result = await service.rename('d1', 'New name');

      expect(result).toBeNull();
      expect(service.devices()).toEqual([]);
    });
  });

  describe('logout', () => {
    it('returns true on success', async () => {
      api.logoutDevice.and.resolveTo({ success: true });

      expect(await service.logout('d1')).toBeTrue();
      expect(api.logoutDevice).toHaveBeenCalledWith('d1');
    });

    it('returns false on failure', async () => {
      api.logoutDevice.and.resolveTo({ success: false, error: { code: 'nope', message: 'nope' } });

      expect(await service.logout('d1')).toBeFalse();
    });
  });

  describe('remove', () => {
    it('removes the device from the cache after a successful API response', async () => {
      const target = device('d1');
      service.devices.set([target]);
      api.removeDevice.and.resolveTo({ success: true });

      const response = await service.remove(target.id);

      expect(api.removeDevice).toHaveBeenCalledWith('d1');
      expect(response.success).toBeTrue();
      expect(service.devices()).toEqual([]);
    });

    it('preserves the cache for a rejected removal response', async () => {
      const target = device('d1');
      service.devices.set([target]);
      api.removeDevice.and.resolveTo({ success: false, error: { code: 'NotFound', message: 'Missing' } });

      const response = await service.remove(target.id);

      expect(response.success).toBeFalse();
      expect(service.devices()).toEqual([target]);
    });

    it('preserves the cache and propagates a rejected removal promise', async () => {
      const target = device('d1');
      const error = new Error('offline');
      service.devices.set([target]);
      api.removeDevice.and.rejectWith(error);

      await expectAsync(service.remove(target.id)).toBeRejectedWith(error);

      expect(service.devices()).toEqual([target]);
    });
  });

  describe('setStartupProfile (issue #251)', () => {
    it('upserts the returned device on success', async () => {
      const updated = device('d1', { startupProfileId: 'p1', startupProfileName: 'Home' });
      api.setDeviceStartupProfile.and.resolveTo({ success: true, device: updated });

      const result = await service.setStartupProfile('d1', 'p1');

      expect(api.setDeviceStartupProfile).toHaveBeenCalledWith('d1', { profileId: 'p1' });
      expect(result.device).toEqual(updated);
      expect(service.devices()).toEqual([updated]);
    });

    it('returns the failure response and leaves the cache untouched', async () => {
      const failure = { success: false, error: { code: 'ValidationError', message: 'That profile no longer exists.' } };
      api.setDeviceStartupProfile.and.resolveTo(failure);

      const result = await service.setStartupProfile('d1', 'p1');

      expect(result).toEqual(failure);
      expect(service.devices()).toEqual([]);
    });

    it('clears the assignment with a null profileId', async () => {
      api.setDeviceStartupProfile.and.resolveTo({ success: true, device: device('d1') });

      await service.setStartupProfile('d1', null);

      expect(api.setDeviceStartupProfile).toHaveBeenCalledWith('d1', { profileId: null });
    });
  });

  describe('openProfile (issue #251)', () => {
    it('returns the host response verbatim', async () => {
      const response = { success: true };
      api.openProfileOnDevice.and.resolveTo(response);

      const result = await service.openProfile('d1', 'p1');

      expect(api.openProfileOnDevice).toHaveBeenCalledWith('d1', { profileId: 'p1' });
      expect(result).toBe(response);
    });

    it('returns a failure response verbatim, without touching the device cache', async () => {
      const response = { success: false, error: { code: 'NotConnected', message: 'The device is not connected' } };
      api.openProfileOnDevice.and.resolveTo(response);

      const result = await service.openProfile('d1', 'p1');

      expect(result).toBe(response);
      expect(service.devices()).toEqual([]);
    });
  });
});
