import { HttpClient, LocalizationCatalog, type GetDeviceSetupResponse } from '@macro-deck/runtime';
import { DeviceSetupService, type DeviceSetupLocation } from './device-setup';
import { DismissibleHints, type HintStorage } from './dismissible-hints';
import { createSetupBanner, SETUP_BANNER_HINT } from './setup-banner';

const LOCATION: DeviceSetupLocation = {
  protocol: 'http:',
  hostname: 'deck.local',
  port: '8193',
  origin: 'http://deck.local:8193',
  assign: () => undefined,
};

function memoryStorage(): HintStorage {
  const values: { [key: string]: string } = {};
  return {
    getItem: (key: string) => Object.prototype.hasOwnProperty.call(values, key) ? values[key] : null,
    setItem: (key: string, value: string) => {
      values[key] = value;
    },
  };
}

function info(overrides?: Partial<GetDeviceSetupResponse>): GetDeviceSetupResponse {
  const base: GetDeviceSetupResponse = {
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
  return { ...base, ...(overrides === undefined ? {} : overrides) };
}

function service(
  response: GetDeviceSetupResponse,
  probe: () => Promise<Response>,
): DeviceSetupService {
  const http = new HttpClient({ baseUrl: () => '' });
  spyOn(http, 'get').and.callFake(() => Promise.resolve(response as never));
  return new DeviceSetupService(http, {
    location: LOCATION,
    secureContext: () => false,
    serviceWorker: null,
    fetch: probe,
  });
}

const refused = (): Promise<Response> => Promise.reject(new TypeError('Failed to fetch'));
const answered = (): Promise<Response> => Promise.resolve({ ok: true } as Response);

function banner(
  deviceSetup: DeviceSetupService,
  hints: DismissibleHints,
  onOpen?: () => void,
  httpsPage: () => boolean = () => true,
) {
  return createSetupBanner({
    deviceSetup: deviceSetup,
    localization: new LocalizationCatalog(),
    hints: hints,
    onOpen: onOpen === undefined ? () => undefined : onOpen,
    httpsPage,
  });
}

function shown(element: HTMLElement): boolean {
  return !element.hasAttribute('hidden');
}

describe('setup banner', () => {
  it('stays away while nothing is wrong with the certificate', async () => {
    const setup = service(info(), answered);
    const handle = banner(setup, new DismissibleHints(memoryStorage()));

    await setup.load();

    expect(shown(handle.element)).toBeFalse();
  });

  it('offers the wizard once this device turns out not to trust the certificate', async () => {
    const setup = service(info(), refused);
    const handle = banner(setup, new DismissibleHints(memoryStorage()));

    await setup.load();

    expect(shown(handle.element)).toBeTrue();
  });

  it('stays away on a page reached over plain http, whatever the trust says', async () => {
    const setup = service(info(), refused);
    const handle = banner(setup, new DismissibleHints(memoryStorage()), undefined, () => false);

    await setup.load();

    expect(setup.certificateTrust.get()).toBe('trustRequired');
    expect(shown(handle.element)).toBeFalse();
  });

  it('stays away when the address is the problem rather than the trust', async () => {
    const setup = service(info({ hostCertificateSubjectAlternativeNames: ['other.example'] }), refused);
    const handle = banner(setup, new DismissibleHints(memoryStorage()));

    await setup.load();

    expect(shown(handle.element)).toBeFalse();
  });

  it('goes away when it is dismissed', async () => {
    const setup = service(info(), refused);
    const handle = banner(setup, new DismissibleHints(memoryStorage()));
    await setup.load();

    const buttons = handle.element.querySelectorAll<HTMLButtonElement>('.wc-setup-banner-actions .wc-btn');
    buttons[1].click();

    expect(shown(handle.element)).toBeFalse();
  });

  it('stays away on the next visit of a device that dismissed it', async () => {
    const storage = memoryStorage();
    const first = service(info(), refused);
    const firstBanner = banner(first, new DismissibleHints(storage));
    await first.load();
    firstBanner.element
      .querySelectorAll<HTMLButtonElement>('.wc-setup-banner-actions .wc-btn')[1].click();

    const second = service(info(), refused);
    const secondBanner = banner(second, new DismissibleHints(storage));
    await second.load();

    expect(shown(secondBanner.element)).toBeFalse();
  });

  it('leaves opening the wizard to whoever owns it', async () => {
    const setup = service(info(), refused);
    let opens = 0;
    const handle = banner(setup, new DismissibleHints(memoryStorage()), () => opens++);
    await setup.load();

    handle.element
      .querySelectorAll<HTMLButtonElement>('.wc-setup-banner-actions .wc-btn')[0].click();

    expect(opens).toBe(1);
  });
});

describe('dismissible hints', () => {
  it('remembers a dismissal past a reload, under the hint\'s own id', () => {
    const storage = memoryStorage();

    new DismissibleHints(storage).dismiss(SETUP_BANNER_HINT);

    expect(new DismissibleHints(storage).isDismissed(SETUP_BANNER_HINT)).toBeTrue();
    expect(new DismissibleHints(storage).isDismissed('some.other.hint')).toBeFalse();
  });

  it('keeps the dismissal for this session when storage refuses to remember it', () => {
    const throwing: HintStorage = {
      getItem: () => {
        throw new Error('denied');
      },
      setItem: () => {
        throw new Error('denied');
      },
    };
    const hints = new DismissibleHints(throwing);

    hints.dismiss(SETUP_BANNER_HINT);

    expect(hints.isDismissed(SETUP_BANNER_HINT)).toBeTrue();
  });
});
