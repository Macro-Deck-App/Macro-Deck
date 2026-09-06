import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GetKeyRingProtectionResponse, GetLockScreenSettingsResponse } from '@macro-deck/runtime';
import { ApiService, AuthService, AuthState } from '@shared';
import { SecuritySettingsComponent } from './security-settings.component';
import { EMPTY } from 'rxjs';
import { SettingsModalService } from '../../../../services/settings-modal.service';

type AuthStub = {
  username: () => string;
  state: ReturnType<typeof signal<AuthState>>;
  trusted: ReturnType<typeof signal<boolean>>;
  changeUsername: jasmine.Spy;
  changePassword: jasmine.Spy;
  logout: jasmine.Spy;
};

function keyRingProtection(overrides: Partial<GetKeyRingProtectionResponse> = {}): GetKeyRingProtectionResponse {
  return {
    state: 'Protected',
    lockReason: 'None',
    unprotectedReason: 'None',
    backend: 'MacOsKeychain',
    backendAvailable: true,
    backendUnavailableReason: null,
    kekId: 'kek-1',
    escrowWrapCount: 1,
    recoveryKeyExported: true,
    migrationPending: false,
    ...overrides,
  };
}

describe('SecuritySettingsComponent - lock screen setting (issue #462)', () => {
  let api: jasmine.SpyObj<ApiService>;
  let auth: AuthStub;

  function installApi(state: GetLockScreenSettingsResponse): void {
    api.getLockScreenSettings.and.resolveTo(state);
    api.updateLockScreenSettings.and.callFake(request => Promise.resolve({ ...state, ...request }));
  }

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getLockScreenSettings',
      'updateLockScreenSettings',
      'getKeyRingProtection',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    api.getKeyRingProtection.and.resolveTo(keyRingProtection());
    installApi({ enabled: false });

    auth = {
      username: () => 'admin',
      state: signal<AuthState>('authenticated'),
      trusted: signal(false),
      changeUsername: jasmine.createSpy('changeUsername'),
      changePassword: jasmine.createSpy('changePassword'),
      logout: jasmine.createSpy('logout').and.resolveTo(undefined),
    };

    await TestBed.configureTestingModule({
      imports: [SecuritySettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: AuthService, useValue: auth },
      ],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<SecuritySettingsComponent>> {
    const fixture = TestBed.createComponent(SecuritySettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function toggle(f: ComponentFixture<SecuritySettingsComponent>): HTMLInputElement {
    return f.nativeElement.querySelector('.ts-input');
  }

  it('reflects the loaded lock-screen setting', async () => {
    installApi({ enabled: true });
    const fixture = await create();

    expect(toggle(fixture).checked).toBeTrue();
    expect(fixture.componentInstance.lockScreenEnabled()).toBeTrue();
  });

  it('calls updateLockScreenSettings with the new value on change', async () => {
    installApi({ enabled: false });
    const fixture = await create();

    toggle(fixture).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.updateLockScreenSettings).toHaveBeenCalledWith({ enabled: true });
    expect(fixture.componentInstance.lockScreenEnabled()).toBeTrue();
  });

  it('rolls back and reloads when the update fails', async () => {
    installApi({ enabled: false });
    const fixture = await create();

    api.updateLockScreenSettings.and.rejectWith(new Error('nope'));

    await fixture.componentInstance.setLockScreenEnabled(true);
    await fixture.whenStable();

    expect(api.getLockScreenSettings.calls.count()).toBe(2);
    expect(fixture.componentInstance.lockScreenEnabled()).toBeFalse();
  });

  describe('key ring protection (ADR 0047, issue #672)', () => {
    it('renders the backend name and state from the DTO', async () => {
      api.getKeyRingProtection.and.resolveTo(keyRingProtection({ backend: 'MacOsKeychain', state: 'Protected' }));
      const fixture = await create();

      const text = (fixture.nativeElement as HTMLElement).textContent;
      expect(text).toContain('macOS Keychain');
      expect(text).toContain('Protected');
    });

    it('warns that the backend is unreachable without leaking the untranslated diagnostic', async () => {
      api.getKeyRingProtection.and.resolveTo(keyRingProtection({
        backendAvailable: false,
        backendUnavailableReason: 'D-Bus session unavailable',
      }));
      const fixture = await create();

      const element: HTMLElement = fixture.nativeElement;
      expect(element.querySelector('.security__note--warning')).toBeTruthy();
      expect(element.textContent).toContain('Secret storage could not be reached');

      // The reason is English text the host authors, not platform output, so it belongs in the log
      // rather than in front of someone reading the app in another language.
      expect(element.textContent).not.toContain('D-Bus session unavailable');
    });

    it('does not show the backend-unavailable warning when the backend is reachable', async () => {
      api.getKeyRingProtection.and.resolveTo(keyRingProtection({ backendAvailable: true }));
      const fixture = await create();

      expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Secret storage could not be reached');
    });

    it('shows the matching reason sentence when unprotected, and the migration-pending note', async () => {
      api.getKeyRingProtection.and.resolveTo(keyRingProtection({
        state: 'Unprotected',
        unprotectedReason: 'RecoveryKeyNotExported',
        migrationPending: true,
      }));
      const fixture = await create();

      const text = (fixture.nativeElement as HTMLElement).textContent;
      expect(text).toContain('Export your backup recovery key');
      expect(text).toContain('Some key ring files are still readable');
    });
  });

  // Issue #839: the desktop UI has to offer a way out of a session it can actually end.
  describe('signing out of a remote session (issue #839)', () => {
    function signOutButton(fixture: ComponentFixture<SecuritySettingsComponent>): HTMLElement | null {
      const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
      return buttons.find(button => button.textContent?.trim() === 'Sign out') ?? null;
    }

    it('offers no way out of a loopback session, which has none to end', async () => {
      auth.trusted.set(true);
      const fixture = await create();

      expect(signOutButton(fixture)).toBeNull();
    });

    it('offers a sign-out for a token session', async () => {
      auth.trusted.set(false);
      const fixture = await create();

      expect(signOutButton(fixture)).not.toBeNull();
    });

    it('offers nothing while the session is already gone', async () => {
      auth.state.set('loggedOut');
      const fixture = await create();

      expect(signOutButton(fixture)).toBeNull();
    });

    it('ends the session only once the user confirms', async () => {
      const fixture = await create();

      signOutButton(fixture)!.click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(auth.logout).not.toHaveBeenCalled();

      await fixture.componentInstance.confirmSignOut();

      expect(auth.logout).toHaveBeenCalled();
    });

    it('leaves no settings dialog over the login form the shell falls back to', async () => {
      const fixture = await create();
      const settingsModal = TestBed.inject(SettingsModalService);
      settingsModal.open('security');

      await fixture.componentInstance.confirmSignOut();

      expect(settingsModal.isOpen()).toBeFalse();
    });
  });
});
