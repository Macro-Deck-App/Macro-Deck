import { HttpClient, LocalizationCatalog, type GetDeviceSetupResponse } from '@macro-deck/runtime';
import { DeviceSetupService, type DeviceSetupLocation } from './device-setup';
import { createDeviceSetupWizard, type PwaAvailability } from './device-setup-wizard';
import { type PlatformNavigator } from '../pwa/platform';

const DESKTOP: PlatformNavigator = { userAgent: 'Mozilla/5.0 (Windows NT 10.0) Chrome/120.0', platform: 'Win32' };
const IOS: PlatformNavigator = { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)', platform: 'iPhone', maxTouchPoints: 5 };
const ANDROID: PlatformNavigator = { userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/120.0 Mobile', platform: 'Linux armv8l', maxTouchPoints: 5 };

const LOCATION: DeviceSetupLocation = {
  protocol: 'http:',
  hostname: 'deck.local',
  port: '8193',
  origin: 'http://deck.local:8193',
  assign: () => undefined,
};

function makeInfo(overrides?: Partial<GetDeviceSetupResponse>): GetDeviceSetupResponse {
  const info: GetDeviceSetupResponse = {
    instanceName: 'Kitchen Mac',
    httpsEnabled: true,
    httpsPort: 8443,
    certificateAuthority: {
      available: true,
      downloadPath: '/api/device-setup/certificate-authority.crt',
      subject: 'Macro Deck Local CA',
      fingerprintSha256: 'AA:BB:CC:DD',
      notAfter: '2030-01-01T00:00:00Z',
    },
    hostCertificateSubjectAlternativeNames: ['deck.local'],
  };
  return { ...info, ...(overrides === undefined ? {} : overrides) };
}

interface Setup {
  service: DeviceSetupService;
  loaded: Promise<void>;
}

function service(
  info: GetDeviceSetupResponse,
  probe: () => Promise<Response>,
): Setup {
  const http = new HttpClient({ baseUrl: () => '' });
  spyOn(http, 'get').and.callFake(() => Promise.resolve(info as never));
  const created = new DeviceSetupService(http, {
    location: LOCATION,
    secureContext: () => false,
    serviceWorker: null,
    fetch: probe,
  });
  return { service: created, loaded: created.load() };
}

const refused = (): Promise<Response> => Promise.reject(new TypeError('Failed to fetch'));
const answered = (): Promise<Response> => Promise.resolve({ ok: true } as Response);

async function open(options?: {
  info?: GetDeviceSetupResponse;
  probe?: () => Promise<Response>;
  navigator?: PlatformNavigator;
  availability?: PwaAvailability;
}): Promise<HTMLElement> {
  const chosen = options === undefined ? {} : options;
  const setup = service(
    chosen.info === undefined ? makeInfo() : chosen.info,
    chosen.probe === undefined ? refused : chosen.probe);
  await setup.loaded;

  const wizard = createDeviceSetupWizard({
    deviceSetup: setup.service,
    localization: new LocalizationCatalog(),
    navigator: chosen.navigator === undefined ? DESKTOP : chosen.navigator,
    hostname: LOCATION.hostname,
    pwaInstall: chosen.availability === undefined ? undefined : {
      availability: () => chosen.availability as PwaAvailability,
      promptInstall: () => Promise.resolve('accepted'),
    },
  });
  document.body.appendChild(wizard.element);
  // The wizard re-checks as it opens; this settles after that one, so the assertions see a
  // finished state rather than the `checking` the re-check passes through.
  await setup.service.load();
  return wizard.element;
}

function text(root: HTMLElement): string {
  return root.textContent === null ? '' : root.textContent;
}

function visible(root: HTMLElement, selector: string): boolean {
  const found = root.querySelector(selector);
  return found !== null && !found.hasAttribute('hidden');
}

describe('device setup wizard', () => {
  afterEach(() => {
    while (document.body.firstChild !== null) document.body.removeChild(document.body.firstChild);
  });

  it('renders the iOS steps, not the Android ones, on an iOS device', async () => {
    const element = await open({ navigator: IOS });

    expect(text(element)).toContain('Install on iPhone or iPad');
    expect(text(element)).not.toContain('Install on Android');
    expect(element.querySelectorAll('ol').length).toBe(1);
  });

  it('renders the Android steps, not the iOS ones, on an Android device', async () => {
    const element = await open({ navigator: ANDROID });

    expect(text(element)).toContain('Install on Android');
    expect(text(element)).not.toContain('Install on iPhone or iPad');
    expect(element.querySelectorAll('ol').length).toBe(1);
  });

  it('shows no numbered install steps on desktop, only the plain desktop instructions', async () => {
    const element = await open();

    expect(text(element)).toContain('Install on a computer');
    expect(element.querySelectorAll('ol').length).toBe(0);
  });

  it('names the certificate authority in the iOS trust step, so the right profile is picked', async () => {
    const element = await open({ navigator: IOS });

    expect(text(element)).toContain('Macro Deck Local CA');
  });

  it('states that the OS will ask for confirmation and that Macro Deck cannot touch the trust store itself', async () => {
    const element = await open();

    expect(text(element)).toContain('ask for confirmation');
    expect(text(element)).toContain('Macro Deck cannot change those settings');
  });

  it('names the address the certificate does not cover, so it is clear which one has to change', async () => {
    const element = await open({
      info: makeInfo({ hostCertificateSubjectAlternativeNames: ['other.example'] }),
    });

    expect(text(element)).toContain('deck.local');
    expect(text(element)).toContain('does not cover the address');
  });

  it('offers the certificate download from this page\'s own origin', async () => {
    const element = await open();

    const link = element.querySelector<HTMLAnchorElement>('.wc-setup-download')!;
    expect(link.hasAttribute('hidden')).toBeFalse();
    expect(link.href).toBe('http://deck.local:8193/api/device-setup/certificate-authority.crt');
  });

  it('offers no download at all when the host has no certificate authority to hand out', async () => {
    const element = await open({
      info: makeInfo({
        certificateAuthority: {
          available: false,
          downloadPath: '/api/device-setup/certificate-authority.crt',
          subject: null,
          fingerprintSha256: null,
          notAfter: null,
        },
      }),
    });

    expect(visible(element, '.wc-setup-download')).toBeFalse();
    expect(visible(element, '.wc-setup-fingerprint-block')).toBeFalse();
  });

  it('shows the fingerprint so it can be compared against the one the desktop app states', async () => {
    const element = await open();

    expect(element.querySelector('.wc-setup-fingerprint')!.textContent).toBe('AA:BB:CC:DD');
  });

  it('says what to do next only once the certificate has actually been downloaded', async () => {
    const element = await open();
    expect(visible(element, '.wc-setup-downloaded')).toBeFalse();

    const link = element.querySelector<HTMLAnchorElement>('.wc-setup-download')!;
    // jsdom has no navigation, and the real link is a download rather than one anyway.
    link.addEventListener('click', event => event.preventDefault());
    link.click();

    expect(visible(element, '.wc-setup-downloaded')).toBeTrue();
  });

  it('offers the secure address from this insecure page, with the warning that goes with it', async () => {
    const element = await open();

    expect(visible(element, '.wc-setup-open-secure')).toBeTrue();
    expect(text(element)).toContain('signing in again');
  });

  it('offers no secure address when the host serves no https', async () => {
    const element = await open({ info: makeInfo({ httpsEnabled: false, httpsPort: null }) });

    expect(visible(element, '.wc-setup-open-secure')).toBeFalse();
  });

  it('says the app step is blocked while the certificate is not trusted', async () => {
    const element = await open({ availability: 'promptable' });

    expect(text(element)).toContain('Install the certificate first');
  });

  it('offers the manual install guidance rather than a prompt where the browser has none', async () => {
    const element = await open({ navigator: IOS, availability: 'manualOnly' });

    expect(element.querySelector('.wc-setup-install .wc-btn')).toBeNull();
    expect(element.querySelector('.wc-setup-install .wc-setup-hint')).not.toBeNull();
  });

  it('re-checks trust as it opens, so a certificate installed since the last check is picked up', async () => {
    const setup = service(makeInfo(), answered);
    await setup.loaded;
    const recheck = spyOn(setup.service, 'recheck').and.resolveTo(undefined);

    createDeviceSetupWizard({
      deviceSetup: setup.service,
      localization: new LocalizationCatalog(),
      navigator: DESKTOP,
    });

    expect(recheck).toHaveBeenCalled();
  });

  it('reports the close once, however many times it is asked to close', async () => {
    const setup = service(makeInfo(), answered);
    await setup.loaded;
    let closes = 0;
    const wizard = createDeviceSetupWizard({
      deviceSetup: setup.service,
      localization: new LocalizationCatalog(),
      navigator: DESKTOP,
      onClose: () => closes++,
    });
    document.body.appendChild(wizard.element);

    wizard.element.querySelector<HTMLElement>('.wc-setup-close')!.click();
    wizard.close();

    expect(closes).toBe(1);
    expect(wizard.element.parentNode).toBeNull();
  });

  it('follows a trust state that changes while it is open', async () => {
    let trusted = false;
    const setup = service(makeInfo(), () => trusted ? answered() : refused());
    await setup.loaded;
    const wizard = createDeviceSetupWizard({
      deviceSetup: setup.service,
      localization: new LocalizationCatalog(),
      navigator: DESKTOP,
      hostname: LOCATION.hostname,
    });
    await setup.service.load();
    expect(text(wizard.element)).toContain('does not trust');

    trusted = true;
    await setup.service.recheck();

    expect(text(wizard.element)).not.toContain('does not trust');
  });
});
