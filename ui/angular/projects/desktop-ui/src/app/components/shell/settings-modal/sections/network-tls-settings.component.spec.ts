import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GetNetworkSettingsResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { NetworkTlsSettingsComponent } from './network-tls-settings.component';
import { EMPTY } from 'rxjs';

describe('NetworkTlsSettingsComponent', () => {
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
    tlsCertificateConfigured: true,
    tlsCertificateSource: 'LocalCa',
    tlsCertificateSubject: 'CN=macro-deck.local',
    tlsCertificateFingerprint: 'AA:BB',
    tlsCertificateNotBefore: null,
    tlsCertificateNotAfter: null,
    tlsCertificateExpired: false,
    tlsCertificateNotYetValid: false,
    tlsCertificateIssuedByAuthority: true,
    tlsAuthorityConfigured: true,
    tlsAuthoritySubject: 'CN=Macro Deck Local CA (deck-pc)',
    tlsAuthorityFingerprint: 'CC:DD',
    tlsAuthorityNotBefore: null,
    tlsAuthorityNotAfter: null,
  };

  let current: GetNetworkSettingsResponse;

  function respond(patch: Partial<GetNetworkSettingsResponse> = {}) {
    current = { ...current, ...patch };
    return { ...current, success: true, error: null };
  }

  beforeEach(async () => {
    // This suite reads the chrome in English, and LocalizationService restores whatever catalog the
    // last suite left in localStorage: an inherited German cache turns Save into Speichern.
    localStorage.clear();

    current = { ...defaults };
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'updateNetworkSettings',
      'updateNetworkTlsCertificate',
      'reissueNetworkTlsCertificate',
      'regenerateNetworkTlsCertificateAuthority',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    api.updateNetworkSettings.and.callFake(request =>
      Promise.resolve(respond(request as Partial<GetNetworkSettingsResponse>)));
    api.reissueNetworkTlsCertificate.and.callFake(() => Promise.resolve(respond({
      tlsCertificateConfigured: true,
      tlsCertificateSource: 'LocalCa',
      tlsCertificateSubject: 'CN=macro-deck.local',
      tlsCertificateFingerprint: 'AA:BB',
    })));
    api.regenerateNetworkTlsCertificateAuthority.and.callFake(() => Promise.resolve(respond({
      tlsAuthorityConfigured: true,
      tlsAuthorityFingerprint: 'EE:FF',
      tlsCertificateFingerprint: 'GG:HH',
    })));
    api.updateNetworkTlsCertificate.and.callFake(() => Promise.resolve(respond({
      tlsCertificateConfigured: true,
      tlsCertificateSource: 'Custom',
      tlsCertificateSubject: 'CN=example.com',
    })));

    await TestBed.configureTestingModule({
      imports: [NetworkTlsSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function create(
    state: Partial<GetNetworkSettingsResponse> = {}
  ): Promise<ComponentFixture<NetworkTlsSettingsComponent>> {
    current = { ...defaults, ...state };
    const fixture = TestBed.createComponent(NetworkTlsSettingsComponent);
    fixture.componentRef.setInput('state', current);
    fixture.componentInstance.stateChange.subscribe((next: GetNetworkSettingsResponse) => {
      fixture.componentRef.setInput('state', next);
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  async function settle(fixture: ComponentFixture<NetworkTlsSettingsComponent>): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function segmentButton(fixture: ComponentFixture<NetworkTlsSettingsComponent>, text: string): HTMLButtonElement {
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('.seg-option'));
    const match = buttons.find(button => button.textContent?.trim() === text);
    if (!match) {
      throw new Error(`No segmented option "${text}"`);
    }
    return match;
  }

  function findButton(fixture: ComponentFixture<NetworkTlsSettingsComponent>, text: string): HTMLButtonElement | undefined {
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('shared-button button'));
    return buttons.find(button => button.textContent?.trim() === text);
  }

  it('disables the HTTPS toggle and explains why while no certificate is configured', async () => {
    const fixture = await create({ tlsCertificateConfigured: false });

    const toggleInput = fixture.nativeElement.querySelector('shared-toggle-switch input') as HTMLInputElement;
    expect(toggleInput.disabled).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Add a certificate before turning HTTPS on.');
  });

  it('enables the HTTPS toggle once a certificate is configured', async () => {
    const fixture = await create({ tlsCertificateConfigured: true });

    const toggleInput = fixture.nativeElement.querySelector('shared-toggle-switch input') as HTMLInputElement;
    expect(toggleInput.disabled).toBeFalse();
  });

  it('shows the replace-mode warning naming the OAuth redirect consequence, and still allows saving replace mode', async () => {
    const fixture = await create({
      tlsEnabled: true,
      tlsCertificateConfigured: true,
      tlsMode: 'Additional',
      publicPort: 9443,
    });

    segmentButton(fixture, 'Replace HTTP listener').click();
    await settle(fixture);

    expect(api.updateNetworkSettings).toHaveBeenCalledOnceWith({ publicPort: 9443, tlsMode: 'Replace' });
    expect(fixture.nativeElement.textContent)
      .toContain('https://127.0.0.1:9443/api/integrations/oauth/callback');
    expect(fixture.nativeElement.textContent).toContain('re-registered with each provider');
    expect(fixture.nativeElement.textContent).toContain('can no longer download it');
    expect(fixture.nativeElement.textContent).toContain('phones connected over USB');
    expect(segmentButton(fixture, 'Replace HTTP listener').getAttribute('aria-pressed')).toBe('true');
  });

  it('disables saving an HTTPS port equal to the listening port, without contacting the host', async () => {
    const fixture = await create({
      tlsEnabled: true,
      tlsCertificateConfigured: true,
      tlsMode: 'Additional',
      publicPort: 8193,
      tlsHttpsPort: 8443,
    });

    fixture.componentInstance.httpsPortDraft.set('8193');
    await settle(fixture);

    expect(fixture.componentInstance.canSaveHttpsPort()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('must be different from the port Macro Deck already uses');
    expect(api.updateNetworkSettings).not.toHaveBeenCalled();
  });

  it('disables saving an HTTPS port outside the reported range, without contacting the host', async () => {
    const fixture = await create({
      tlsEnabled: true,
      tlsCertificateConfigured: true,
      tlsMode: 'Additional',
      minimumPublicPort: 1024,
      maximumPublicPort: 65535,
    });

    fixture.componentInstance.httpsPortDraft.set('80');
    await settle(fixture);

    expect(fixture.componentInstance.canSaveHttpsPort()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('must be between 1024 and 65535');
    expect(api.updateNetworkSettings).not.toHaveBeenCalled();
  });

  it('saves a valid HTTPS port with exactly the expected request body', async () => {
    const fixture = await create({
      tlsEnabled: true,
      tlsCertificateConfigured: true,
      tlsMode: 'Additional',
      publicPort: 8193,
      tlsHttpsPort: 8443,
    });

    fixture.componentInstance.httpsPortDraft.set('9443');
    await settle(fixture);
    await fixture.componentInstance.saveHttpsPort();
    await settle(fixture);

    expect(api.updateNetworkSettings).toHaveBeenCalledOnceWith({ publicPort: 8193, tlsHttpsPort: 9443 });
  });

  it('toggling HTTPS sends exactly publicPort and tlsEnabled, no other field', async () => {
    const fixture = await create({ tlsCertificateConfigured: true, publicPort: 8193 });

    await fixture.componentInstance.setHttpsEnabled(true);
    await settle(fixture);

    expect(api.updateNetworkSettings).toHaveBeenCalledOnceWith({ publicPort: 8193, tlsEnabled: true });
  });

  it('shows a specific message for a TLS failure', async () => {
    const fixture = await create({
      tlsEnabled: true,
      tlsCertificateConfigured: true,
      tlsFailure: 'KeyUnreadable',
    });

    expect(fixture.nativeElement.textContent).toContain('could not be read on this computer');
  });

  it('shows a different, specific message for a TLS rejection', async () => {
    const fixture = await create({
      tlsEnabled: true,
      tlsCertificateConfigured: true,
      tlsRejection: 'HttpsPortConflictsWithPublicPort',
    });

    expect(fixture.nativeElement.textContent).toContain('conflicts with the port Macro Deck already uses');
  });

  it('renders distinct copy for tlsFailure and tlsRejection', async () => {
    const failureFixture = await create({ tlsFailure: 'CertificateInvalid' });
    const rejectionFixture = await create({ tlsRejection: 'HttpsPortConflictsWithLoopbackPort' });

    expect(failureFixture.nativeElement.textContent).not.toEqual(rejectionFixture.nativeElement.textContent);
    expect(failureFixture.nativeElement.textContent).toContain('invalid and could not be used');
    expect(rejectionFixture.nativeElement.textContent).toContain('used by Macro Deck itself');
  });

  it('asks for confirmation before reissuing an existing certificate', async () => {
    const fixture = await create({
      tlsCertificateConfigured: true,
      tlsCertificateSource: 'LocalCa',
      tlsCertificateSubject: 'CN=macro-deck.local',
    });

    findButton(fixture, 'Reissue host certificate')!.click();
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).not.toBeNull();
    expect(api.reissueNetworkTlsCertificate).not.toHaveBeenCalled();

    fixture.componentInstance.confirmRegenerate();
    await settle(fixture);

    expect(api.reissueNetworkTlsCertificate).toHaveBeenCalledTimes(1);
  });

  it('warns when the certificate has expired', async () => {
    const fixture = await create({ tlsCertificateConfigured: true, tlsCertificateExpired: true });

    expect(fixture.nativeElement.textContent).toContain('expired');
  });

  it('warns when the certificate is not yet valid', async () => {
    const fixture = await create({ tlsCertificateConfigured: true, tlsCertificateNotYetValid: true });

    expect(fixture.nativeElement.textContent).toContain('not valid yet');
  });

  it('shows the certificate source as a read-only row', async () => {
    const fixture = await create({ tlsCertificateConfigured: true, tlsCertificateSource: 'Custom' });

    expect(fixture.nativeElement.textContent).toContain('Custom certificate');
  });

  it('renders the certificate and authority actions and no source picker', async () => {
    const fixture = await create({ tlsCertificateConfigured: true });

    expect(findButton(fixture, 'Reissue host certificate')).toBeTruthy();
    expect(findButton(fixture, 'Upload certificate')).toBeTruthy();
    // Replacing the authority is the destructive action and lives with the authority, not the
    // certificate: reissuing keeps every device that already trusts this installation working.
    expect(findButton(fixture, 'Create a new authority')).toBeTruthy();
    expect(findButton(fixture, 'Remove certificate')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('shared-segmented-control')).toBeFalsy();
    expect(
      Array.from(fixture.nativeElement.querySelectorAll('.seg-option'))
        .map(option => (option as HTMLElement).textContent?.trim())
    ).not.toContain('Custom certificate');
  });

  it('opens the upload modal when "Upload certificate" is clicked', async () => {
    const fixture = await create({ tlsCertificateConfigured: true });

    expect(fixture.nativeElement.querySelector('app-network-tls-certificate-upload-modal')).toBeFalsy();

    findButton(fixture, 'Upload certificate')!.click();
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('app-network-tls-certificate-upload-modal')).toBeTruthy();
  });

  describe('upload modal', () => {
    async function openUploadModal(
      state: Partial<GetNetworkSettingsResponse> = {}
    ): Promise<ComponentFixture<NetworkTlsSettingsComponent>> {
      const fixture = await create({ tlsCertificateConfigured: true, ...state });
      findButton(fixture, 'Upload certificate')!.click();
      await settle(fixture);
      return fixture;
    }

    function modalSaveButton(fixture: ComponentFixture<NetworkTlsSettingsComponent>): HTMLButtonElement {
      const buttons: HTMLButtonElement[] = Array.from(
        fixture.nativeElement.querySelectorAll('app-network-tls-certificate-upload-modal shared-button button'));
      const match = buttons.find(button => button.textContent?.trim() === 'Save');
      if (!match) throw new Error('No Save button in the upload modal');
      return match;
    }

    function setField(
      fixture: ComponentFixture<NetworkTlsSettingsComponent>, ariaLabel: string, value: string
    ): void {
      const textarea = fixture.nativeElement.querySelector(`textarea[aria-label="${ariaLabel}"]`) as HTMLTextAreaElement;
      textarea.value = value;
      textarea.dispatchEvent(new Event('input'));
    }

    it('disables Save until both fields have content', async () => {
      const fixture = await openUploadModal();

      expect(modalSaveButton(fixture).disabled).toBeTrue();

      setField(fixture, 'Certificate PEM', '-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----');
      await settle(fixture);
      expect(modalSaveButton(fixture).disabled).toBeTrue();

      setField(fixture, 'Private key PEM', '-----BEGIN PRIVATE KEY-----\nxyz\n-----END PRIVATE KEY-----');
      await settle(fixture);
      expect(modalSaveButton(fixture).disabled).toBeFalse();
    });

    it('saves with exactly the two fields, then closes the modal and clears them', async () => {
      const fixture = await openUploadModal();

      setField(fixture, 'Certificate PEM', '-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----');
      setField(fixture, 'Private key PEM', '-----BEGIN PRIVATE KEY-----\nxyz\n-----END PRIVATE KEY-----');
      await settle(fixture);
      modalSaveButton(fixture).click();
      await settle(fixture);
      await new Promise(resolve => setTimeout(resolve, 200));
      await settle(fixture);

      expect(api.updateNetworkTlsCertificate).toHaveBeenCalledOnceWith({
        certificatePem: '-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----',
        privateKeyPem: '-----BEGIN PRIVATE KEY-----\nxyz\n-----END PRIVATE KEY-----',
      });
      expect(fixture.nativeElement.querySelector('app-network-tls-certificate-upload-modal')).toBeFalsy();
    });

    it('keeps the modal open with both fields intact and shows the host error when the pair is rejected', async () => {
      api.updateNetworkTlsCertificate.and.callFake(() => Promise.resolve({
        ...current,
        success: false,
        error: 'The certificate and private key do not match.',
      }));
      const fixture = await openUploadModal();

      setField(fixture, 'Certificate PEM', '-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----');
      setField(fixture, 'Private key PEM', '-----BEGIN PRIVATE KEY-----\nxyz\n-----END PRIVATE KEY-----');
      await settle(fixture);
      modalSaveButton(fixture).click();
      await settle(fixture);

      expect(fixture.nativeElement.querySelector('app-network-tls-certificate-upload-modal')).toBeTruthy();
      expect(fixture.nativeElement.textContent).toContain('do not match');
      const certificateField = fixture.nativeElement.querySelector('textarea[aria-label="Certificate PEM"]') as HTMLTextAreaElement;
      const keyField = fixture.nativeElement.querySelector('textarea[aria-label="Private key PEM"]') as HTMLTextAreaElement;
      expect(certificateField.value).toContain('abc');
      expect(keyField.value).toContain('xyz');
    });

    it('never renders any stored key material, even though certificate metadata is shown', async () => {
      const fixture = await openUploadModal({
        tlsCertificateSource: 'Custom',
        tlsCertificateSubject: 'CN=example.com',
        tlsCertificateFingerprint: 'AA:BB:CC',
      });

      expect(fixture.nativeElement.textContent).toContain('AA:BB:CC');
      const keyField = fixture.nativeElement.querySelector('textarea[aria-label="Private key PEM"]') as HTMLTextAreaElement;
      expect(keyField.value).toBe('');
    });
  });
});
