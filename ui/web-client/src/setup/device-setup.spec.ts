import { HttpClient, type GetDeviceSetupResponse } from '@macro-deck/runtime';
import {
  DeviceSetupService,
  resolveSecureAddress,
  type DeviceSetupDependencies,
  type DeviceSetupLocation,
} from './device-setup';

const HTTP_LOCATION: DeviceSetupLocation = {
  protocol: 'http:',
  hostname: 'deck.local',
  port: '8193',
  origin: 'http://deck.local:8193',
  assign: () => undefined,
};

function makeInfo(overrides?: Partial<GetDeviceSetupResponse>): GetDeviceSetupResponse {
  const info: GetDeviceSetupResponse = {
    instanceName: 'Living Room Mac',
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

function httpClient(info: GetDeviceSetupResponse | Error): HttpClient {
  const client = new HttpClient({ baseUrl: () => '' });
  spyOn(client, 'get').and.callFake(() =>
    info instanceof Error ? Promise.reject(info) : Promise.resolve(info as never));
  return client;
}

function create(
  info: GetDeviceSetupResponse | Error,
  deps?: Partial<DeviceSetupDependencies>,
): DeviceSetupService {
  return new DeviceSetupService(httpClient(info), {
    location: HTTP_LOCATION,
    secureContext: () => false,
    serviceWorker: null,
    fetch: () => Promise.reject(new TypeError('Failed to fetch')),
    ...(deps === undefined ? {} : deps),
  });
}

describe('resolveSecureAddress', () => {
  const http = { protocol: 'http:', hostname: 'deck.local', port: '8193' };

  it('offers the secure address while the page is served over plain http', () => {
    expect(resolveSecureAddress(http, true, 8443)).toBe('https://deck.local:8443/');
  });

  it('offers nothing once the page is already the secure address', () => {
    expect(resolveSecureAddress({ protocol: 'https:', hostname: 'deck.local', port: '8443' }, true, 8443))
      .toBeNull();
  });

  it('offers nothing when the secure address is the implicit https port', () => {
    expect(resolveSecureAddress({ protocol: 'https:', hostname: 'deck.local', port: '' }, true, 443))
      .toBeNull();
  });

  it('still offers the secure address from a different https port', () => {
    expect(resolveSecureAddress({ protocol: 'https:', hostname: 'deck.local', port: '9999' }, true, 8443))
      .toBe('https://deck.local:8443/');
  });

  it('offers nothing when the host serves no https', () => {
    expect(resolveSecureAddress(http, false, 8443)).toBeNull();
    expect(resolveSecureAddress(http, true, null)).toBeNull();
  });
});

describe('DeviceSetupService', () => {
  it('skips the certificate step entirely when the host serves no HTTPS at all', async () => {
    const service = create(makeInfo({ httpsEnabled: false, httpsPort: null }));

    await service.load();

    expect(service.certificateTrust.get()).toBe('notApplicable');
  });

  it('reports trusted once the secure address answers from this http page', async () => {
    const service = create(makeInfo(), {
      fetch: () => Promise.resolve({ ok: true } as Response),
    });

    await service.load();

    expect(service.certificateTrust.get()).toBe('trusted');
  });

  it('reports trust required when the certificate is refused for an address it covers', async () => {
    const service = create(makeInfo());

    await service.load();

    expect(service.certificateTrust.get()).toBe('trustRequired');
  });

  it('reports the address as not covered, distinctly from trustRequired, when it is absent from the SAN list', async () => {
    const service = create(makeInfo({ hostCertificateSubjectAlternativeNames: ['other.example'] }));

    await service.load();

    expect(service.certificateTrust.get()).toBe('addressNotCovered');
  });

  it('reports a probe failure, not a trust problem, when the probe is the one that gave up', async () => {
    let expire: (() => void) | null = null;
    const service = create(makeInfo(), {
      timer: {
        set: (callback: () => void) => {
          expire = callback;
          return 1;
        },
        clear: () => undefined,
      },
      fetch: (_url, init) => new Promise((_resolve, reject) => {
        const signal = init.signal;
        if (signal) signal.addEventListener('abort', () => reject(new TypeError('aborted')));
        if (expire !== null) expire();
      }),
    });

    await service.load();

    expect(service.certificateTrust.get()).toBe('probeFailed');
  });

  it('gives the probe five seconds before it gives up on the address', async () => {
    const budgets: number[] = [];
    const service = create(makeInfo(), {
      timer: {
        set: (_callback: () => void, delayMs: number) => {
          budgets.push(delayMs);
          return 1;
        },
        clear: () => undefined,
      },
    });

    await service.load();

    expect(budgets).toEqual([5000]);
  });

  it('reports a probe failure when the secure address answers with an error status', async () => {
    const service = create(makeInfo(), {
      fetch: () => Promise.resolve({ ok: false } as Response),
    });

    await service.load();

    expect(service.certificateTrust.get()).toBe('probeFailed');
  });

  it('leaves the trust unknown when the host would not say anything about its setup', async () => {
    const service = create(new Error('offline'));

    await service.load();

    expect(service.certificateTrust.get()).toBe('unknown');
    expect(service.info.get()).toBeNull();
  });

  it('does not probe at all from a page that is already on a trustworthy origin', async () => {
    let probes = 0;
    const service = create(makeInfo(), {
      secureContext: () => true,
      serviceWorker: { getRegistration: () => Promise.resolve({}) },
      fetch: () => {
        probes++;
        return Promise.reject(new TypeError('Failed to fetch'));
      },
    });

    await service.load();

    expect(probes).toBe(0);
    expect(service.certificateTrust.get()).toBe('trusted');
  });

  it('still asks for trust on a secure page whose service worker was refused', async () => {
    const service = create(makeInfo(), {
      secureContext: () => true,
      serviceWorker: { getRegistration: () => Promise.resolve(undefined) },
    });

    await service.load();

    expect(service.certificateTrust.get()).toBe('trustRequired');
  });

  it('builds the certificate-authority download link from this page\'s own origin, not the https one', async () => {
    const service = create(makeInfo());

    await service.load();

    expect(service.caDownloadUrl())
      .toBe('http://deck.local:8193/api/device-setup/certificate-authority.crt');
  });

  it('offers no download link while the host has no certificate authority to offer', async () => {
    const service = create(makeInfo({
      certificateAuthority: {
        available: false,
        downloadPath: '/api/device-setup/certificate-authority.crt',
        subject: null,
        fingerprintSha256: null,
        notAfter: null,
      },
    }));

    await service.load();

    expect(service.caDownloadUrl()).toBeNull();
  });

  it('sends the device to the secure address it offers', async () => {
    const visited: string[] = [];
    const service = create(makeInfo(), {
      location: { ...HTTP_LOCATION, assign: (url: string) => visited.push(url) },
    });

    await service.load();
    service.openSecureAddress();

    expect(visited).toEqual(['https://deck.local:8443/']);
  });
});
