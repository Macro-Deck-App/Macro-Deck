import { HardwareInputMap } from '../client-input/hardware-input-map';

export interface WebClientTargetCapabilities {
  readonly serviceWorker: boolean;
  readonly wakeLock: boolean;
  readonly clientSettings: boolean;
  readonly deviceSetupPrompts: boolean;
}

export interface HostEndpointCandidate {
  readonly baseUrl: string;
}

export type HostEndpointStrategy =
  | { readonly kind: 'origin' }
  | { readonly kind: 'probe'; readonly candidates: readonly HostEndpointCandidate[] };

export interface WebClientTarget {
  readonly id: string;
  readonly hostEndpoint: HostEndpointStrategy;
  readonly hardwareInput?: HardwareInputMap;
  readonly capabilities: WebClientTargetCapabilities;
}

export const DEFAULT_WEB_CLIENT_TARGET: WebClientTarget = {
  id: 'default',
  hostEndpoint: { kind: 'origin' },
  capabilities: {
    serviceWorker: true,
    wakeLock: true,
    clientSettings: true,
    deviceSetupPrompts: true,
  },
};

export interface HostEndpointEnvironment {
  readonly origin: string;
  readonly fetch: (input: string, init?: RequestInit) => Promise<Response>;
}

export type HostUrlResolver = () => Promise<string | null>;

const PROBE_PATH = '/api/system/build-info';

const PROBE_TIMEOUT_MS = 1500;

export function createHostUrlResolver(
  strategy: HostEndpointStrategy,
  environment: HostEndpointEnvironment,
): HostUrlResolver {
  if (strategy.kind === 'origin') {
    return () => Promise.resolve(environment.origin);
  }

  // The origin is always tried last: the page was served from somewhere, so it is the one address
  // known to work, and it is what keeps a plain LAN visit working for a target whose forwarded
  // ports are absent.
  const candidates: string[] = [];
  for (let index = 0; index < strategy.candidates.length; index++) {
    candidates.push(strategy.candidates[index].baseUrl);
  }
  candidates.push(environment.origin);

  return () => {
    let attempt = Promise.resolve<string | null>(null);
    for (let index = 0; index < candidates.length; index++) {
      const baseUrl = candidates[index];
      attempt = attempt.then(found => found !== null
        ? found
        : respondsAsHost(baseUrl, environment).then(ok => (ok ? baseUrl : null)));
    }
    return attempt;
  };
}

function respondsAsHost(baseUrl: string, environment: HostEndpointEnvironment): Promise<boolean> {
  const abort = typeof AbortController === 'function' ? new AbortController() : null;
  const timer = abort === null ? null : setTimeout(() => abort.abort(), PROBE_TIMEOUT_MS);
  const init: RequestInit = { cache: 'no-store' };
  if (abort !== null) init.signal = abort.signal;

  return environment.fetch(`${baseUrl}${PROBE_PATH}`, init).then(
    response => {
      if (timer !== null) clearTimeout(timer);
      return response.ok;
    },
    () => {
      if (timer !== null) clearTimeout(timer);
      return false;
    });
}
