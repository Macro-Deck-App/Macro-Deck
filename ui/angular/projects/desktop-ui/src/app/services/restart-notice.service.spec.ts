import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { GetNetworkSettingsResponse } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { RestartNoticeService } from './restart-notice.service';
import { EMPTY } from 'rxjs';

describe('RestartNoticeService', () => {
  let api: jasmine.SpyObj<ApiService>;
  let toastSpy: jasmine.Spy;
  let service: RestartNoticeService;

  const pending: GetNetworkSettingsResponse = {
    publicPort: 9100,
    defaultPublicPort: 8193,
    activePublicPort: 8193,
    overriddenByEnvironment: false,
    configuredPortIgnored: false,
    publicListenerUnavailable: false,
    restartRequired: true,
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

  beforeEach(() => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getNetworkSettings', 'restartApplication', 'onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    api.getNetworkSettings.and.resolveTo(pending);
    api.restartApplication.and.resolveTo({ success: true, supported: true, error: null });
    toastSpy = jasmine.createSpy('show');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: { show: toastSpy } },
      ],
    });
    service = TestBed.inject(RestartNoticeService);
  });

  it('starts with nothing pending', () => {
    expect(service.required()).toBeFalse();
    expect(service.message()).toBe('');
  });

  it('reports the pending port after a refresh', async () => {
    await service.refresh();

    expect(service.required()).toBeTrue();
    expect(service.canRestart()).toBeTrue();
    expect(service.message()).toContain('9100');
    expect(service.message()).toContain('8193');
  });

  it('does not describe a port change when the same port is simply not open', async () => {
    api.getNetworkSettings.and.resolveTo({
      ...pending,
      publicPort: 8193,
      activePublicPort: 8193,
      publicListenerUnavailable: true,
    });

    await service.refresh();

    expect(service.required()).toBeTrue();
    expect(service.message()).not.toContain('until it restarts');
    expect(service.message()).toContain('8193');
  });

  it('names the HTTPS port, not the public one, when only the HTTPS listener failed to open', async () => {
    api.getNetworkSettings.and.resolveTo({
      ...pending,
      publicPort: 8193,
      activePublicPort: 8193,
      publicListenerUnavailable: false,
      tlsEnabled: true,
      tlsMode: 'Additional',
      tlsHttpsPort: 8194,
      activeTlsEnabled: false,
      activeTlsMode: 'Additional',
      activeTlsHttpsPort: null,
    });

    await service.refresh();

    expect(service.message()).toContain('8194');
    expect(service.message()).not.toContain('8193');
  });

  it('does not claim a port failed when HTTPS was only just configured', async () => {
    api.getNetworkSettings.and.resolveTo({
      ...pending,
      publicPort: 8193,
      activePublicPort: 8193,
      publicListenerUnavailable: false,
      tlsEnabled: true,
      tlsMode: 'Additional',
      tlsHttpsPort: 8194,
      activeTlsEnabled: false,
      activeTlsMode: 'Disabled',
      activeTlsHttpsPort: null,
    });

    await service.refresh();

    expect(service.message()).toContain('HTTPS');
    expect(service.message()).not.toContain('8193');
    expect(service.message()).not.toContain('8194');
  });

  it('promises no retry once HTTPS is switched off after a failed listener', async () => {
    api.getNetworkSettings.and.resolveTo({
      ...pending,
      publicPort: 8193,
      activePublicPort: 8193,
      publicListenerUnavailable: false,
      tlsEnabled: false,
      tlsMode: 'Additional',
      tlsHttpsPort: 8194,
      activeTlsEnabled: false,
      activeTlsMode: 'Additional',
      activeTlsHttpsPort: null,
    });

    await service.refresh();

    expect(service.message()).not.toContain('8194');
  });

  it('keeps the last known state when the request fails', async () => {
    await service.refresh();
    api.getNetworkSettings.and.rejectWith(new Error('offline'));

    await service.refresh();

    expect(service.required()).toBeTrue();
  });

  it('restarts from a browser tab just as from the desktop app', async () => {
    await service.refresh();

    await service.restartNow();

    expect(service.canRestart()).toBeTrue();
    expect(api.restartApplication).toHaveBeenCalledOnceWith('network-port');
  });

  it('says why instead of restarting when the host cannot restart itself', async () => {
    api.getNetworkSettings.and.resolveTo({
      ...pending,
      restartSupported: false,
      restartUnsupportedReason: 'Restarting is only available in the installed desktop app',
    });
    await service.refresh();

    await service.restartNow();

    expect(api.restartApplication).not.toHaveBeenCalled();
    expect(toastSpy).toHaveBeenCalledWith('Restarting is only available in the installed desktop app');
  });

  it('surfaces a refused restart', async () => {
    api.restartApplication.and.resolveTo({ success: false, supported: false, error: 'no desktop app' });
    await service.refresh();

    await service.restartNow();

    expect(toastSpy).toHaveBeenCalledWith('no desktop app', { variant: 'error' });
    expect(service.restarting()).toBeFalse();
  });

  it('treats a dropped connection as the restart working', async () => {
    api.restartApplication.and.rejectWith(new Error('connection lost'));
    await service.refresh();

    await service.restartNow();

    expect(toastSpy).not.toHaveBeenCalled();
    expect(service.restarting()).toBeTrue();
  });
});
