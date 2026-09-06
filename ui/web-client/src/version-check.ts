export const UI_COMMIT_META = 'macro-deck-ui-commit';

export const RELOAD_PARAM = 'md-reload';

const MARKER_KEY = 'macro-deck.reloaded-for';
const OVERLAY_ID = 'macro-deck-outdated-ui';

const PREPARE_RELOAD_TIMEOUT_MS = 3000;

export type VersionCheckAction = 'ok' | 'reload' | 'report';

export interface OutdatedUiText {
  heading: string;
  body: string;
}

export interface VersionCheckDeps {
  doc: Document;
  storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
  currentUrl(): string;
  replaceUrl(url: string): void;
  replaceUrlInPlace(url: string): void;
  fetchHostCommit(baseUrl: string, nonce: string): Promise<string | null>;
  now(): number;
  prepareReload?(): Promise<void>;
  outdatedText?: OutdatedUiText;
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

export function showOutdatedUiError(doc: Document, text: OutdatedUiText): void {
  if (doc.getElementById(OVERLAY_ID)) return;

  const overlay = doc.createElement('div');
  overlay.id = OVERLAY_ID;
  overlay.setAttribute('role', 'alert');
  overlay.style.cssText = [
    'position:fixed', 'top:0', 'right:0', 'bottom:0', 'left:0', 'z-index:2147483647', 'display:flex',
    'align-items:center', 'justify-content:center', 'padding:24px',
    'background:#101014', 'color:#f5f5f7',
    'font:16px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif', 'text-align:center',
  ].join(';');

  const message = doc.createElement('div');
  message.style.cssText = 'max-width:32rem';

  const heading = doc.createElement('h1');
  heading.textContent = text.heading;
  heading.style.cssText = 'margin:0 0 12px;font-size:1.35rem;font-weight:600';

  const body = doc.createElement('p');
  body.textContent = text.body;
  body.style.cssText = 'margin:0';

  message.appendChild(heading);
  message.appendChild(body);
  overlay.appendChild(message);

  const children = doc.body.children;
  for (let index = 0; index < children.length; index++) {
    (children[index] as HTMLElement).inert = true;
  }

  doc.body.appendChild(overlay);
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
    case 'ok':
      deps.storage.removeItem(MARKER_KEY);
      if (attempts[1] !== null) deps.replaceUrlInPlace(withoutCacheBust(currentUrl));
      return;
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
    default:
      console.error(
        'Macro Deck UI is stale: built from ' + uiCommit + ', host runs ' + hostCommit
        + '. A reload did not fix it.',
      );
      if (deps.outdatedText) showOutdatedUiError(deps.doc, deps.outdatedText);
      return;
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
