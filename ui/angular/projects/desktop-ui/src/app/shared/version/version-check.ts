import { DestroyRef, EnvironmentProviders, InjectionToken, inject, isDevMode, provideAppInitializer } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, distinctUntilChanged, filter, map } from 'rxjs';

import { ApiService, ConnectionState, HOST_URL_RESOLVER, HostUrlResolver } from '../transport';

export const UI_COMMIT_META = 'macro-deck-ui-commit';

export const RELOAD_PARAM = 'md-reload';

const MARKER_KEY = 'macro-deck.reloaded-for';
const OVERLAY_ID = 'macro-deck-outdated-ui';

export type VersionCheckAction = 'ok' | 'reload' | 'report';

export interface VersionCheckDeps {
  doc: Document;
  storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
  currentUrl: () => string;
  replaceUrl: (url: string) => void;
  replaceUrlInPlace: (url: string) => void;
  fetchHostCommit: (baseUrl: string, nonce: string) => Promise<string | null>;
  now: () => number;
  prepareReload?: () => Promise<void>;
}

const PREPARE_RELOAD_TIMEOUT_MS = 3000;

async function runPrepareReload(hook: (() => Promise<void>) | undefined): Promise<void> {
  if (!hook) {
    return;
  }
  await new Promise<void>(resolve => {
    let settled = false;
    const settle = (): void => {
      if (!settled) {
        settled = true;
        resolve();
      }
    };
    const timer = setTimeout(settle, PREPARE_RELOAD_TIMEOUT_MS);
    hook().then(
      () => {
        clearTimeout(timer);
        settle();
      },
      () => {
        clearTimeout(timer);
        settle();
      },
    );
  });
}

export const VERSION_CHECK_PREPARE_RELOAD = new InjectionToken<() => Promise<void>>(
  'VERSION_CHECK_PREPARE_RELOAD',
  { providedIn: 'root', factory: () => async () => undefined },
);

export function readUiCommit(doc: Document): string | null {
  const meta = doc.querySelector<HTMLMetaElement>(`meta[name="${UI_COMMIT_META}"]`);
  const commit = meta?.content.trim();
  return commit ? commit : null;
}

export async function fetchHostCommit(baseUrl: string, nonce: string): Promise<string | null> {
  try {
    // The whole premise is a client cache that ignores the host's no-store header, so the probe
    // carries its own cache buster rather than trusting the fetch cache mode (which the ES5
    // bundle's XHR-based fetch polyfill ignores anyway).
    const response = await fetch(`${baseUrl}/api/system/build-info?t=${nonce}`, {
      headers: { Accept: 'application/json' },
      cache: 'no-store',
    });
    if (!response.ok) {
      return null;
    }
    const body = (await response.json()) as { commit?: string | null };
    return body.commit?.trim() || null;
  } catch {
    return null;
  }
}

export function decideVersionAction(
  uiCommit: string,
  hostCommit: string | null,
  reloadedFor: readonly (string | null)[]
): VersionCheckAction {
  if (!hostCommit || hostCommit.toLowerCase() === uiCommit.toLowerCase()) {
    return 'ok';
  }

  return reloadedFor.some(entry => entry?.toLowerCase() === hostCommit.toLowerCase())
    ? 'report'
    : 'reload';
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

export function showOutdatedUiError(doc: Document): void {
  if (doc.getElementById(OVERLAY_ID)) {
    return;
  }

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
  heading.textContent = 'This page is out of date';
  heading.style.cssText = 'margin:0 0 12px;font-size:1.35rem;font-weight:600';

  const body = doc.createElement('p');
  body.textContent =
    'Your browser is still using an older version of the Macro Deck interface than the one this ' +
    'host runs. Reloading did not replace it, so please clear the browser cache and reload the page.';
  body.style.cssText = 'margin:0';

  message.append(heading, body);
  overlay.append(message);

  for (const child of Array.from(doc.body.children)) {
    (child as HTMLElement).inert = true;
  }

  doc.body.append(overlay);
}

export async function runVersionCheck(
  resolveBaseUrl: HostUrlResolver,
  deps: VersionCheckDeps
): Promise<void> {
  const uiCommit = readUiCommit(deps.doc);
  if (!uiCommit) {
    return;
  }

  let baseUrl: string | null = null;
  try {
    baseUrl = await resolveBaseUrl();
  } catch {
    baseUrl = null;
  }
  if (!baseUrl) {
    return;
  }

  const currentUrl = deps.currentUrl();
  const hostCommit = await deps.fetchHostCommit(baseUrl, `${uiCommit}-${deps.now()}`);
  const attempts = [deps.storage.getItem(MARKER_KEY), readCacheBust(currentUrl)];

  switch (decideVersionAction(uiCommit, hostCommit, attempts)) {
    case 'ok':
      deps.storage.removeItem(MARKER_KEY);
      if (attempts[1] !== null) {
        deps.replaceUrlInPlace(withoutCacheBust(currentUrl));
      }
      return;
    case 'reload': {
      const commit = hostCommit ?? '';
      // Built before the marker is stored so a browser without URL support cannot end up
      // marked as reloaded without ever having reloaded.
      const target = withCacheBust(currentUrl, commit);
      deps.storage.setItem(MARKER_KEY, commit);
      await runPrepareReload(deps.prepareReload);
      deps.replaceUrl(target);
      return;
    }
    case 'report':
      console.error(
        `Macro Deck UI is stale: built from ${uiCommit}, host runs ${hostCommit}. A reload did not fix it.`
      );
      showOutdatedUiError(deps.doc);
      return;
  }
}

export function connectionEstablished(state$: Observable<ConnectionState>): Observable<void> {
  return state$.pipe(
    distinctUntilChanged(),
    filter(state => state === 'connected'),
    map(() => undefined),
  );
}

export function createVersionCheckRunner(
  resolveBaseUrl: HostUrlResolver,
  deps: VersionCheckDeps
): () => void {
  let running = false;

  return () => {
    if (running) {
      return;
    }
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

function browserDeps(prepareReload: () => Promise<void>): VersionCheckDeps {
  return {
    doc: document,
    storage: sessionStorage,
    currentUrl: () => window.location.href,
    replaceUrl: (url) => window.location.replace(url),
    replaceUrlInPlace: (url) => window.history.replaceState(window.history.state, '', url),
    fetchHostCommit,
    now: () => Date.now(),
    prepareReload,
  };
}

export function provideVersionCheck(): EnvironmentProviders[] {
  return [
    provideAppInitializer(() => {
      if (isDevMode()) {
        return;
      }

      const resolveBaseUrl = inject(HOST_URL_RESOLVER);
      const api = inject(ApiService);
      const destroyRef = inject(DestroyRef);
      const prepareReload = inject(VERSION_CHECK_PREPARE_RELOAD);
      const check = createVersionCheckRunner(resolveBaseUrl, browserDeps(prepareReload));

      check();
      connectionEstablished(api.connectionState$)
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe(check);
    }),
  ];
}
