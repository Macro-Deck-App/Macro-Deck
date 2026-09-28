import {
  currentOutdatedUi,
  diagnoseOutdatedUi,
  fetchServedUiCommit,
  showOutdatedUi,
  type OutdatedUiText,
  type OutdatedUiVariant,
} from '@macro-deck/runtime';

export const UI_COMMIT_META = 'macro-deck-ui-commit';

export const RELOAD_PARAM = 'md-reload';

const MARKER_KEY = 'macro-deck.reloaded-for';
const WORKER_FILE = 'macro-deck-worker.js';
const SHELL_CACHE_PREFIX = 'macro-deck-shell-';

const PREPARE_RELOAD_TIMEOUT_MS = 3000;
const HARD_REFRESH_TIMEOUT_MS = 3000;

export type VersionCheckAction = 'ok' | 'reload' | 'report';

export interface OutdatedUiSetup {
  text(variant: OutdatedUiVariant, deviceCommit: string, hostCommit: string): OutdatedUiText;
  onTextChange?(listener: () => void): () => void;
  hardRefresh(hostCommit: string): void;
  reload(hostCommit: string): void;
}

export interface VersionCheckDeps {
  doc: Document;
  storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
  currentUrl(): string;
  replaceUrl(url: string): void;
  replaceUrlInPlace(url: string): void;
  fetchHostCommit(baseUrl: string, nonce: string): Promise<string | null>;
  fetchServedCommit(nonce: string): Promise<string | null>;
  now(): number;
  prepareReload?(): Promise<void>;
  outdated?: OutdatedUiSetup;
}

export interface UpdatePreparation {
  updatePending(): boolean;
  backgroundCheck(): Promise<void>;
  activateNow(): Promise<void>;
}

export interface VersionCheckOptions {
  baseUrl(): string;
  connectionState?: { subscribe(listener: (state: string) => void): () => void };
  update?: UpdatePreparation;
  deps?: Partial<VersionCheckDeps>;
}

export function readUiCommit(doc: Document): string | null {
  const meta = doc.querySelector('meta[name="' + UI_COMMIT_META + '"]') as HTMLMetaElement | null;
  if (meta === null) return null;
  const commit = meta.content ? meta.content.replace(/^\s+|\s+$/g, '') : '';
  return commit ? commit : null;
}

export function fetchHostCommit(baseUrl: string, nonce: string): Promise<string | null> {
  // The whole premise is a client cache that ignores the host's no-store header, so the probe
  // carries its own cache buster rather than trusting the fetch cache mode (which the ES5 bundle's
  // XHR-based fetch polyfill ignores anyway).
  return fetch(baseUrl + '/api/system/build-info?t=' + encodeURIComponent(nonce), {
    headers: { Accept: 'application/json' },
    cache: 'no-store',
  }).then(response => {
    if (!response.ok) return null;
    return response.json().then((body: { commit?: string | null }) => {
      const commit = body && typeof body.commit === 'string'
        ? body.commit.replace(/^\s+|\s+$/g, '')
        : '';
      return commit ? commit : null;
    });
  }).catch(() => null);
}

export function decideVersionAction(
  uiCommit: string,
  hostCommit: string | null,
  reloadedFor: ReadonlyArray<string | null>,
): VersionCheckAction {
  if (!hostCommit || hostCommit.toLowerCase() === uiCommit.toLowerCase()) return 'ok';

  for (let index = 0; index < reloadedFor.length; index++) {
    const entry = reloadedFor[index];
    if (entry && entry.toLowerCase() === hostCommit.toLowerCase()) return 'report';
  }
  return 'reload';
}

export function readCacheBust(url: string): string | null {
  return new URL(url).searchParams.get(RELOAD_PARAM);
}

export function withCacheBust(url: string, commit: string): string {
  const parsed = new URL(url);
  parsed.searchParams.set(RELOAD_PARAM, commit);
  return parsed.toString();
}

export function withoutCacheBust(url: string): string {
  const parsed = new URL(url);
  parsed.searchParams.delete(RELOAD_PARAM);
  return parsed.toString();
}

export interface HardRefreshEnvironment {
  serviceWorker: ServiceWorkerContainer | null;
  caches: CacheStorage | null;
  baseUri: string;
  hostReachable(): Promise<boolean>;
  currentUrl(): string;
  replaceUrl(url: string): void;
}

function dropOwnWorker(env: HardRefreshEnvironment): Promise<void> {
  const container = env.serviceWorker;
  if (!container || !container.controller) return Promise.resolve();
  const ownScript = new URL(WORKER_FILE, env.baseUri).href;
  return container.getRegistration().then(registration => {
    if (!registration) return undefined;
    const worker = registration.active || registration.waiting || registration.installing;
    if (!worker || worker.scriptURL !== ownScript) return undefined;
    return registration.unregister().then(() => undefined);
  });
}

function dropShellCaches(env: HardRefreshEnvironment): Promise<void> {
  const storage = env.caches;
  if (!storage) return Promise.resolve();
  return storage.keys().then(names => {
    const drops: Array<Promise<boolean>> = [];
    for (let index = 0; index < names.length; index++) {
      if (names[index].indexOf(SHELL_CACHE_PREFIX) === 0) drops.push(storage.delete(names[index]));
    }
    return Promise.all(drops).then(() => undefined);
  });
}

export function hardRefresh(hostCommit: string, env: HardRefreshEnvironment): Promise<void> {
  const target = withCacheBust(env.currentUrl(), hostCommit);
  // Without a reachable host the worker's shell is the only thing that can still paint this page.
  let cleanup: Promise<void>;
  try {
    cleanup = env.hostReachable().then(
      reachable => (reachable
        ? dropOwnWorker(env).catch(() => undefined).then(() => dropShellCaches(env)).catch(() => undefined)
        : undefined),
      () => undefined,
    );
  } catch {
    cleanup = Promise.resolve();
  }
  const cap = new Promise<void>(resolve => {
    setTimeout(resolve, HARD_REFRESH_TIMEOUT_MS);
  });
  return Promise.race([cleanup, cap]).then(() => env.replaceUrl(target));
}

function runPrepareReload(hook: (() => Promise<void>) | undefined): Promise<void> {
  if (!hook) return Promise.resolve();
  return new Promise<void>(resolve => {
    let settled = false;
    const settle = (): void => {
      if (settled) return;
      settled = true;
      resolve();
    };
    const timer = setTimeout(settle, PREPARE_RELOAD_TIMEOUT_MS);
    const done = (): void => {
      clearTimeout(timer);
      settle();
    };
    hook().then(done, done);
  });
}

export async function runVersionCheck(
  resolveBaseUrl: () => string | Promise<string>,
  deps: VersionCheckDeps,
): Promise<void> {
  const uiCommit = readUiCommit(deps.doc);
  if (!uiCommit) return;

  let baseUrl: string | null = null;
  try {
    baseUrl = await resolveBaseUrl();
  } catch {
    baseUrl = null;
  }
  if (!baseUrl) return;

  const currentUrl = deps.currentUrl();
  const hostCommit = await deps.fetchHostCommit(baseUrl, uiCommit + '-' + deps.now());
  const attempts = [deps.storage.getItem(MARKER_KEY), readCacheBust(currentUrl)];

  switch (decideVersionAction(uiCommit, hostCommit, attempts)) {
    case 'ok': {
      if (hostCommit === null) return;
      deps.storage.removeItem(MARKER_KEY);
      const shown = currentOutdatedUi(deps.doc);
      if (shown) shown.remove();
      if (attempts[1] !== null) deps.replaceUrlInPlace(withoutCacheBust(currentUrl));
      return;
    }
    case 'reload': {
      const commit = hostCommit === null ? '' : hostCommit;
      // Built before the marker is stored so a browser without URL support cannot end up marked as
      // reloaded without ever having reloaded.
      const target = withCacheBust(currentUrl, commit);
      deps.storage.setItem(MARKER_KEY, commit);
      await runPrepareReload(deps.prepareReload);
      deps.replaceUrl(target);
      return;
    }
    default: {
      const staleFor = hostCommit === null ? '' : hostCommit;
      const servedCommit = await deps.fetchServedCommit(uiCommit + '-' + deps.now());
      const variant = diagnoseOutdatedUi(staleFor, servedCommit);
      console.error(
        'Macro Deck UI is stale: built from ' + uiCommit + ', host runs ' + staleFor
        + ', host serves ' + servedCommit + '. A reload did not fix it.',
      );
      const outdated = deps.outdated;
      if (!outdated) return;
      const shown = currentOutdatedUi(deps.doc);
      if (shown && shown.variant === 'installation' && variant === 'device') {
        outdated.hardRefresh(staleFor);
        return;
      }
      showOutdatedUi(deps.doc, {
        variant,
        text: shownVariant => outdated.text(shownVariant, uiCommit, staleFor),
        onAction: variant === 'device'
          ? () => outdated.hardRefresh(staleFor)
          : () => outdated.reload(staleFor),
        onTextChange: outdated.onTextChange,
      });
      return;
    }
  }
}

export function createVersionCheckRunner(
  resolveBaseUrl: () => string | Promise<string>,
  deps: VersionCheckDeps,
): () => void {
  let running = false;

  return () => {
    if (running) return;
    running = true;
    try {
      runVersionCheck(resolveBaseUrl, deps)
        .catch(() => undefined)
        .then(() => {
          running = false;
        });
    } catch {
      running = false;
    }
  };
}

function prepareReloadWith(update: UpdatePreparation): () => Promise<void> {
  return () => update.backgroundCheck().then(
    () => (update.updatePending() ? update.activateNow() : undefined),
    () => undefined,
  );
}

function browserDeps(deps: Partial<VersionCheckDeps> | undefined): VersionCheckDeps {
  const overrides = deps === undefined ? {} : deps;
  const base: VersionCheckDeps = {
    doc: document,
    storage: sessionStorage,
    currentUrl: () => window.location.href,
    replaceUrl: url => window.location.replace(url),
    replaceUrlInPlace: url => window.history.replaceState(window.history.state, '', url),
    fetchHostCommit: fetchHostCommit,
    fetchServedCommit: nonce => fetchServedUiCommit(document.baseURI, nonce),
    now: () => Date.now(),
  };
  const target = base as unknown as Record<string, unknown>;
  const source = overrides as unknown as Record<string, unknown>;
  const keys = Object.keys(source);
  for (let index = 0; index < keys.length; index++) {
    if (source[keys[index]] !== undefined) target[keys[index]] = source[keys[index]];
  }
  return base;
}

export function startVersionCheck(options: VersionCheckOptions): () => void {
  const deps = browserDeps(options.deps);
  if (options.update && deps.prepareReload === undefined) {
    deps.prepareReload = prepareReloadWith(options.update);
  }

  const check = createVersionCheckRunner(options.baseUrl, deps);
  check();

  if (!options.connectionState) return () => undefined;

  let connected = false;
  return options.connectionState.subscribe(state => {
    const live = state === 'connected';
    if (live && !connected) check();
    connected = live;
  });
}
