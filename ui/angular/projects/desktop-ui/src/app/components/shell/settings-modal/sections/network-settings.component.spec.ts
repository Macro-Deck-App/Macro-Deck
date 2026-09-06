import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GetNetworkSettingsResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { NetworkSettingsComponent } from './network-settings.component';
import { EMPTY } from 'rxjs';

describe('NetworkSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;

  const defaults: GetNetworkSettingsResponse = {
    publicPort: 8193,
    defaultPublicPort: 8193,
    activePublicPort: 8193,
    overriddenByEnvironment: false,
    configuredPortIgnored: false,
    publicListenerUnavailable: false,
    restartRequired: false,
    restartSupported: true,
    restartUnsupportedReason: null,
    minimumPublicPort: 1024,
    maximumPublicPort: 65535,
    tlsEnabled: false,
    tlsMode: 'Additional',
    tlsHttpsPort: 8443,
    defaultTlsHttpsPort: 8443,
    activeTlsEnabled: false,
    activeTlsMode: 'Disabled',
    activeTlsHttpsPort: null,
    tlsFailure: '',
    tlsRejection: '',
    tlsCertificateConfigured: false,
    tlsCertificateSource: null,
    tlsCertificateSubject: null,
    tlsCertificateFingerprint: null,
    tlsCertificateNotBefore: null,
    tlsCertificateNotAfter: null,
    tlsCertificateExpired: false,
    tlsCertificateNotYetValid: false,
    tlsCertificateIssuedByAuthority: false,
    tlsAuthorityConfigured: false,
    tlsAuthoritySubject: null,
    tlsAuthorityFingerprint: null,
    tlsAuthorityNotBefore: null,
    tlsAuthorityNotAfter: null,
  };

  function installApi(state: Partial<GetNetworkSettingsResponse> = {}): GetNetworkSettingsResponse {
    const resolved = { ...defaults, ...state };
    api.getNetworkSettings.and.resolveTo(resolved);
    api.updateNetworkSettings.and.callFake(request =>
      Promise.resolve({
        ...resolved,
        publicPort: request.publicPort,
        restartRequired: request.publicPort !== resolved.activePublicPort,
        success: true,
        error: null,
      }));
    return resolved;
  }

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getNetworkSettings',
      'updateNetworkSettings',
      'restartApplication',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    installApi();
    api.restartApplication.and.resolveTo({ success: true, supported: true, error: null });

    await TestBed.configureTestingModule({
      imports: [NetworkSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<NetworkSettingsComponent>> {
    const fixture = TestBed.createComponent(NetworkSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  async function type(fixture: ComponentFixture<NetworkSettingsComponent>, value: string): Promise<void> {
    fixture.componentInstance.draft.set(value);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function save(fixture: ComponentFixture<NetworkSettingsComponent>): Promise<void> {
    await fixture.componentInstance.save();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows the configured port and the port the host is listening on', async () => {
    installApi({ publicPort: 9100, activePublicPort: 8193, restartRequired: true });
    const fixture = await create();

    expect(fixture.componentInstance.draft()).toBe('9100');
    expect(fixture.componentInstance.activePort()).toBe(8193);
    expect(fixture.nativeElement.textContent).toContain('Port 9100 takes effect after Macro Deck restarts');
  });

  it('does not write the setting when the value has not changed', async () => {
    const fixture = await create();

    await save(fixture);

    expect(api.updateNetworkSettings).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
  });

  for (const invalid of ['80', '1023', '70000', 'abc', '']) {
    it(`rejects "${invalid}" without contacting the host`, async () => {
      const fixture = await create();

      await type(fixture, invalid);
      await save(fixture);

      expect(fixture.componentInstance.canSave()).toBeFalse();
      expect(api.updateNetworkSettings).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
    });
  }

  it('marks an out-of-range port as invalid and explains the range', async () => {
    const fixture = await create();

    await type(fixture, '80');

    expect(fixture.componentInstance.portInvalid()).toBeTrue();
    expect(fixture.nativeElement.querySelector('.network__hint')?.textContent)
      .toContain('between 1024 and 65535');
  });

  it('saves a changed port and offers a restart', async () => {
    const fixture = await create();

    await type(fixture, '9100');
    await save(fixture);

    expect(api.updateNetworkSettings).toHaveBeenCalledOnceWith({ publicPort: 9100 });
    expect(fixture.componentInstance.restartRequired()).toBeTrue();
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).not.toBeNull();
  });

  it('restarts through the host once, when confirmed', async () => {
    const fixture = await create();

    await type(fixture, '9100');
    await save(fixture);
    await fixture.componentInstance.restartNow();

    expect(api.restartApplication).toHaveBeenCalledTimes(1);
  });

  it('leaves the restart to the shared notice', async () => {
    const fixture = await create();

    await type(fixture, '9100');
    await save(fixture);
    await fixture.componentInstance.restartNow();

    expect(api.restartApplication).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.error()).toBeNull();
  });

  it('offers no restart when the host cannot restart itself', async () => {
    installApi({ restartSupported: false, restartUnsupportedReason: 'no desktop app' });
    const fixture = await create();

    await type(fixture, '9100');
    await save(fixture);

    expect(fixture.componentInstance.canRestart()).toBeFalse();
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('no desktop app');
  });

  it('says so when the stored port was refused, instead of promising a restart', async () => {
    installApi({ publicPort: 9100, activePublicPort: 8193, configuredPortIgnored: true });
    const fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('could not be used at the last start');
    expect(fixture.componentInstance.restartRequired()).toBeFalse();
  });

  it('warns that the public listener could not be opened, mentioning the port', async () => {
    installApi({ publicListenerUnavailable: true, activePublicPort: 8193 });
    const fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('8193');
    expect(fixture.nativeElement.textContent).toContain('could not open');
  });

  it('does not warn about the public listener when it opened normally', async () => {
    const fixture = await create();

    expect(fixture.nativeElement.textContent).not.toContain('could not open');
  });

  it('warns about the public listener even when the configured port was not otherwise ignored', async () => {
    installApi({ publicListenerUnavailable: true, configuredPortIgnored: false });
    const fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('could not open');
    expect(fixture.nativeElement.textContent).not.toContain('could not be used at the last start');
  });

  it('does not claim a pending port change when only the listener could not be opened', async () => {
    installApi({ publicListenerUnavailable: true, restartRequired: true, publicPort: 8193, activePublicPort: 8193 });
    const fixture = await create();

    expect(fixture.nativeElement.textContent).not.toContain('takes effect after');
    expect(fixture.nativeElement.textContent).toContain('could not open');
  });

  it('still explains a genuinely pending port change while the listener is unavailable', async () => {
    installApi({ publicListenerUnavailable: true, restartRequired: true, publicPort: 9100, activePublicPort: 8193 });
    const fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('takes effect after');
  });

  it('locks the field while an environment override decides the port', async () => {
    installApi({ overriddenByEnvironment: true, publicPort: 9100, activePublicPort: 8194 });
    const fixture = await create();

    await type(fixture, '9200');

    expect(fixture.componentInstance.canSave()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('MACRO_DECK_PORT is set');
  });

  it('reports a rejected value from the host without changing the shown port', async () => {
    const fixture = await create();
    api.updateNetworkSettings.and.resolveTo({
      ...defaults,
      success: false,
      error: 'This port is already used by Macro Deck itself. Choose a different one.',
    });

    await type(fixture, '9100');
    await save(fixture);

    expect(fixture.componentInstance.error())
      .toBe('This port is already used by Macro Deck itself. Choose a different one.');
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
  });

  it('reloads after a failed request', async () => {
    const fixture = await create();
    api.updateNetworkSettings.and.rejectWith(new Error('offline'));

    await type(fixture, '9100');
    await save(fixture);

    expect(api.getNetworkSettings).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.error()).toBe('The port could not be saved.');
  });
});
