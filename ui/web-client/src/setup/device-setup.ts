import {
  isSecureContext,
  store,
  type GetDeviceSetupResponse,
  type HttpClient,
  type ReadableStore,
  type WritableStore,
} from '@macro-deck/runtime';

export type CertificateTrust =
  | 'notApplicable'
  | 'unknown'
  | 'checking'
  | 'trusted'
  | 'trustRequired'
  | 'addressNotCovered'
  | 'probeFailed';

const PROBE_TIMEOUT_MS = 5000;

export interface SecureAddressLocation {
  readonly protocol: string;
  readonly hostname: string;
  readonly port: string;
}

// Compared by protocol and port rather than through new URL, which the compatibility floor can
// only reach through a polyfill.
export function resolveSecureAddress(
  location: SecureAddressLocation,
  httpsEnabled: boolean,
  httpsPort: number | null,
): string | null {
  if (!httpsEnabled || httpsPort === null) return null;

  const onSecureOrigin = location.protocol === 'https:' &&
    (location.port === String(httpsPort) || (httpsPort === 443 && location.port === ''));

  return onSecureOrigin ? null : 'https://' + location.hostname + ':' + httpsPort + '/';
}

export interface DeviceSetupLocation extends SecureAddressLocation {
  readonly origin: string;
  assign(url: string): void;
}

export interface ServiceWorkerRegistrationSource {
  getRegistration(): Promise<unknown>;
}

export interface DeviceSetupDependencies {
  location?: DeviceSetupLocation;
  secureContext?(): boolean;
  fetch?(url: string, init: RequestInit): Promise<Response>;
  serviceWorker?: ServiceWorkerRegistrationSource | null;
  timer?: ProbeTimer;
}

export interface ProbeTimer {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
}

const REAL_TIMER: ProbeTimer = {
  set: (callback: () => void, delayMs: number) => setTimeout(callback, delayMs),
  clear: (handle: unknown) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

function defaultLocation(): DeviceSetupLocation {
  return {
    protocol: location.protocol,
    hostname: location.hostname,
    port: location.port,
    origin: location.origin,
    assign: (url: string) => location.assign(url),
  };
}

export class DeviceSetupService {
  private readonly infoStore: WritableStore<GetDeviceSetupResponse | null> =
    store<GetDeviceSetupResponse | null>(null);
  private readonly trustStore: WritableStore<CertificateTrust> = store<CertificateTrust>('unknown');
  private readonly loadingStore: WritableStore<boolean> = store(false);

  private requestToken = 0;

  private readonly location: DeviceSetupLocation;

  constructor(
    private readonly http: HttpClient,
    private readonly deps: DeviceSetupDependencies = {},
  ) {
    this.location = deps.location === undefined ? defaultLocation() : deps.location;
  }

  get info(): ReadableStore<GetDeviceSetupResponse | null> {
    return this.infoStore;
  }

  get certificateTrust(): ReadableStore<CertificateTrust> {
    return this.trustStore;
  }

  get loading(): ReadableStore<boolean> {
    return this.loadingStore;
  }

  httpsUrl(): string | null {
    const info = this.infoStore.get();
    return info === null ? null : resolveSecureAddress(this.location, info.httpsEnabled, info.httpsPort);
  }

  caDownloadUrl(): string | null {
    const info = this.infoStore.get();
    if (info === null || !info.certificateAuthority.available) return null;
    return this.location.origin + info.certificateAuthority.downloadPath;
  }

  openSecureAddress(): void {
    const url = this.httpsUrl();
    if (url !== null) this.location.assign(url);
  }

  async load(): Promise<void> {
    const token = ++this.requestToken;
    this.loadingStore.set(true);
    try {
      const info = await this.http.get<GetDeviceSetupResponse>('/api/device-setup');
      if (token !== this.requestToken) return;
      this.infoStore.set(info);
      await this.evaluateTrust(info, token);
    } catch {
      if (token === this.requestToken) this.trustStore.set('unknown');
    } finally {
      if (token === this.requestToken) this.loadingStore.set(false);
    }
  }

  recheck(): Promise<void> {
    return this.load();
  }

  private async evaluateTrust(info: GetDeviceSetupResponse, token: number): Promise<void> {
    if (!info.httpsEnabled || info.httpsPort === null) {
      this.trustStore.set('notApplicable');
      return;
    }

    if (this.secureContext()) {
      const already = await this.evaluateAlreadySecure();
      if (token === this.requestToken) this.trustStore.set(already);
      return;
    }

    this.trustStore.set('checking');
    const result = await this.probe(info.httpsPort);
    if (token !== this.requestToken) return;

    if (result === 'reachable') {
      this.trustStore.set('trusted');
      return;
    }
    if (result === 'unreachable') {
      this.trustStore.set('probeFailed');
      return;
    }
    // The certificate was rejected outright (a fast TypeError, not our own timeout). Whether
    // installing the CA would actually fix that depends on whether the certificate covers this
    // address at all - the CA can be perfectly trusted and still not help here.
    const names = info.hostCertificateSubjectAlternativeNames;
    const covered = names !== undefined && names !== null &&
      indexOf(names, this.location.hostname) >= 0;
    this.trustStore.set(covered ? 'trustRequired' : 'addressNotCovered');
  }

  // This page loaded over HTTPS, so the handshake already succeeded; the open question is whether the
  // browser did that through a lasting trust decision or a per-session click-through exception.
  // A completed service-worker registration rules the latter out - it still refuses on an exception.
  private async evaluateAlreadySecure(): Promise<CertificateTrust> {
    const serviceWorker = this.serviceWorker();
    if (!serviceWorker) return 'trusted';
    try {
      const registration = await serviceWorker.getRegistration();
      return registration ? 'trusted' : 'trustRequired';
    } catch {
      return 'trusted';
    }
  }

  private async probe(httpsPort: number): Promise<'reachable' | 'unreachable' | 'rejected'> {
    const controller = new AbortController();
    const timer = this.deps.timer === undefined ? REAL_TIMER : this.deps.timer;
    // Not `AbortSignal.timeout` - Safari 16+ only, and the compatibility floor has to run this too.
    const budget = timer.set(() => controller.abort(), PROBE_TIMEOUT_MS);
    const nonce = Math.random().toString(36).slice(2);
    const url = 'https://' + this.location.hostname + ':' + httpsPort +
      '/api/system/build-info?p=' + nonce;
    try {
      const response = await this.probeFetch()(
        url, { mode: 'cors', cache: 'no-store', credentials: 'omit', signal: controller.signal });
      return response.ok ? 'reachable' : 'unreachable';
    } catch {
      // An untrusted certificate makes `fetch` reject with a plain TypeError, indistinguishable by
      // message from a network failure - so the two are told apart by our own timeout instead: a
      // rejection that arrives before it fired is the browser refusing the TLS handshake itself, and
      // one that arrives because of it means the address genuinely could not be reached in time.
      return controller.signal.aborted ? 'unreachable' : 'rejected';
    } finally {
      timer.clear(budget);
    }
  }

  private secureContext(): boolean {
    return this.deps.secureContext === undefined ? isSecureContext() : this.deps.secureContext();
  }

  private serviceWorker(): ServiceWorkerRegistrationSource | null {
    if (this.deps.serviceWorker !== undefined) return this.deps.serviceWorker;
    const container = typeof navigator === 'undefined' ? null : navigator.serviceWorker;
    return container ? container as unknown as ServiceWorkerRegistrationSource : null;
  }

  private probeFetch(): (url: string, init: RequestInit) => Promise<Response> {
    const injected = this.deps.fetch;
    if (injected !== undefined) return (url, init) => injected(url, init);
    return (url, init) => fetch(url, init);
  }
}

function indexOf(values: readonly string[], value: string): number {
  for (let index = 0; index < values.length; index++) {
    if (values[index] === value) return index;
  }
  return -1;
}
