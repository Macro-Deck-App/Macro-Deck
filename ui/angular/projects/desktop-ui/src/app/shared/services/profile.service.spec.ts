import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { type IpcProfile } from '@macro-deck/runtime';

import { ApiService } from '../transport';
import { ProfileService } from './profile.service';

describe('ProfileService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: ProfileService;

  function ipcProfile(overrides: Partial<IpcProfile> = {}): IpcProfile {
    return {
      id: 'p1',
      name: 'Home',
      order: 0,
      layoutType: 'Grid',
      isVirtual: false,
      layout: { rows: 3, columns: 5, rowsLocked: false, columnsLocked: false },
      defaultRows: 3,
      defaultColumns: 5,
      ...overrides,
    };
  }

  beforeEach(() => {
    notifications = new Map();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getProfiles', 'createProfile', 'updateProfile', 'deleteProfile',
    ]);
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(ProfileService);
  });

  describe('mapIpcProfile', () => {
    it('maps the two new defaults from the wire, defaulting to null when absent', async () => {
      apiSpy.getProfiles.and.resolveTo({
        profiles: [ipcProfile({ defaultWidgetSpacing: 16, defaultWidgetBorderRadius: 24 }), ipcProfile({ id: 'p2' })],
      });

      await service.loadProfiles();

      const [withDefaults, withoutDefaults] = service.profiles();
      expect(withDefaults.defaultSpacing).toBe(16);
      expect(withDefaults.defaultBorderRadius).toBe(24);
      expect(withoutDefaults.defaultSpacing).toBeNull();
      expect(withoutDefaults.defaultBorderRadius).toBeNull();
    });
  });

  describe('createProfile', () => {
    it('forwards the two new defaults to the host', async () => {
      apiSpy.createProfile.and.resolveTo({ success: true, profile: ipcProfile() });

      await service.createProfile('Home', { defaultWidgetSpacing: 10, defaultWidgetBorderRadius: 20 });

      expect(apiSpy.createProfile).toHaveBeenCalledWith(jasmine.objectContaining({
        defaultWidgetSpacing: 10,
        defaultWidgetBorderRadius: 20,
      }));
    });
  });

  describe('updateProfile', () => {
    it('forwards -1 through unchanged, as the caller-supplied sentinel', async () => {
      apiSpy.updateProfile.and.resolveTo({ success: true, profile: ipcProfile() });

      await service.updateProfile('p1', { defaultWidgetSpacing: -1, defaultWidgetBorderRadius: -1 });

      expect(apiSpy.updateProfile).toHaveBeenCalledWith(jasmine.objectContaining({
        defaultWidgetSpacing: -1,
        defaultWidgetBorderRadius: -1,
      }));
    });

    it('returns the host error without updating local state on rejection', async () => {
      apiSpy.updateProfile.and.resolveTo({
        success: false,
        error: { code: 'GridTooSmall', message: 'Home needs at least 5 x 3 to keep every widget inside' },
      });

      const result = await service.updateProfile('p1', { defaultRows: 1 });

      expect(result.success).toBeFalse();
      expect(result.error?.message).toContain('needs at least 5 x 3');
    });
  });

  describe('loadProfiles selection (issue #251)', () => {
    beforeEach(() => {
      apiSpy.getProfiles.and.resolveTo({
        profiles: [ipcProfile({ id: 'p1', order: 0 }), ipcProfile({ id: 'p2', order: 1 })],
      });
    });

    it('cold start: selects the preferred profile id when it is present in the loaded list', async () => {
      await service.loadProfiles('p2');

      expect(service.selectedProfileId()).toBe('p2');
    });

    it('unresolvable: falls back to the first sorted profile when the preferred id is not in the loaded list', async () => {
      await service.loadProfiles('gone');

      expect(service.selectedProfileId()).toBe('p1');
    });

    it('reconnect: leaves an existing, still-valid selection alone rather than overriding it', async () => {
      service.selectedProfileId.set('p2');

      await service.loadProfiles('p1');

      expect(service.selectedProfileId()).toBe('p2');
    });

    it('regression: a call with no preferred id still selects the first sorted profile', async () => {
      await service.loadProfiles();

      expect(service.selectedProfileId()).toBe('p1');
    });
  });

  describe('device grid constraint', () => {
    const constrained = () => ipcProfile({
      layout: {
        rows: 3,
        columns: 5,
        rowsLocked: false,
        columnsLocked: false,
        constraint: {
          rows: 3, columns: 5, minRows: 3, maxRows: 3, minColumns: 5, maxColumns: 5,
          rowsLocked: true, columnsLocked: true, deviceName: 'Stream Deck',
        },
        compatibility: { status: 'ok', deviceNames: [] },
      },
    });

    it('does not report a device-constrained profile as locked', async () => {
      apiSpy.getProfiles.and.resolveTo({ profiles: [constrained()] });
      await service.loadProfiles();
      service.selectedProfileId.set('p1');

      expect(service.isCurrentProfileLocked()).toBeFalse();
      expect(service.currentGridConstraint()?.rowsLocked).toBeTrue();
    });

    it('reports no constraint for a profile no device claims', async () => {
      apiSpy.getProfiles.and.resolveTo({ profiles: [ipcProfile()] });
      await service.loadProfiles();
      service.selectedProfileId.set('p1');

      expect(service.currentGridConstraint()).toBeNull();
      expect(service.currentLayoutCompatibility()).toBeNull();
    });

    it('surfaces a conflict but keeps an ok status silent', async () => {
      const conflicted = ipcProfile({
        layout: {
          rows: 3, columns: 5, rowsLocked: false, columnsLocked: false,
          compatibility: { status: 'conflictingDevices', deviceNames: ['Deck A', 'Deck B'] },
        },
      });
      apiSpy.getProfiles.and.resolveTo({ profiles: [conflicted] });
      await service.loadProfiles();
      service.selectedProfileId.set('p1');

      expect(service.currentLayoutCompatibility()?.deviceNames).toEqual(['Deck A', 'Deck B']);
      expect(service.isCurrentProfileLocked()).toBeFalse();
    });
  });

  describe('event propagation', () => {
    it('applies a ProfileUpdatedEvent, mapping the two new defaults', () => {
      service.profiles.set([{
        id: 'p1', name: 'Home', order: 0, layoutType: 'Grid', isVirtual: false,
        layout: { rows: 3, columns: 5, rowsLocked: false, columnsLocked: false },
        defaultRows: 3, defaultColumns: 5, defaultBackground: null, defaultSpacing: null, defaultBorderRadius: null,
      }]);

      notifications.get('ProfileUpdatedEvent')!.next({
        profile: ipcProfile({ defaultWidgetSpacing: 8, defaultWidgetBorderRadius: 12 }),
      });

      expect(service.profiles()[0].defaultSpacing).toBe(8);
      expect(service.profiles()[0].defaultBorderRadius).toBe(12);
    });
  });
});
