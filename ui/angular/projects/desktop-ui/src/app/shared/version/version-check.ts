import {
  DestroyRef,
  EnvironmentProviders,
  InjectionToken,
  Injector,
  inject,
  isDevMode,
  provideAppInitializer,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  AppStrings,
  Strings,
  currentOutdatedUi,
  diagnoseOutdatedUi,
  fetchServedUiCommit,
  showOutdatedUi,
  type OutdatedUiText,
  type OutdatedUiVariant,
} from '@macro-deck/runtime';
import { Observable, distinctUntilChanged, filter, map, skip } from 'rxjs';

import { LocalizationService } from '../localization/localization.service';
import { ApiService, ConnectionState, HOST_URL_RESOLVER, HostUrlResolver } from '../transport';

export const UI_COMMIT_META = 'macro-deck-ui-commit';

export const RELOAD_PARAM = 'md-reload';

const MARKER_KEY = 'macro-deck.reloaded-for';

export type VersionCheckAction = 'ok' | 'reload' | 'report';

export interface OutdatedUiSetup {
  text: (variant: OutdatedUiVariant, deviceCommit: string, hostCommit: string) => OutdatedUiText;
  onTextChange?: (listener: () => void) => () => void;
  refresh: (hostCommit: string) => void;
}

export interface VersionCheckDeps {
  doc: Document;
  storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
  currentUrl: () => string;
  replaceUrl: (url: string) => void;
  replaceUrlInPlace: (url: string) => void;
  fetchHostCommit: (baseUrl: string, nonce: string) => Promise<string | null>;
  fetchServedCommit: (nonce: string) => Promise<string | null>;
  now: () => number;
  prepareReload?: () => Promise<void>;
  outdated?: OutdatedUiSetup;
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
      if (hostCommit === null) {
        return;
      }
      deps.storage.removeItem(MARKER_KEY);
      currentOutdatedUi(deps.doc)?.remove();
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
    case 'report': {
      const staleFor = hostCommit ?? '';
      const servedCommit = await deps.fetchServedCommit(`${uiCommit}-${deps.now()}`);
      console.error(
        `Macro Deck UI is stale: built from ${uiCommit}, host runs ${staleFor}, host serves ${servedCommit}. `
          + 'A reload did not fix it.'
      );
      const outdated = deps.outdated;
      if (!outdated) {
        return;
      }
      const variant = diagnoseOutdatedUi(staleFor, servedCommit);
      if (currentOutdatedUi(deps.doc)?.variant === 'installation' && variant === 'device') {
        outdated.refresh(staleFor);
        return;
      }
      showOutdatedUi(deps.doc, {
        variant,
        text: variant => outdated.text(variant, uiCommit, staleFor),
        onAction: () => outdated.refresh(staleFor),
        onTextChange: outdated.onTextChange,
      });
      return;
    }
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

export function outdatedUiText(
  localization: Pick<LocalizationService, 'translateKey'>,
  variant: OutdatedUiVariant,
  device: string,
  computer: string,
): OutdatedUiText {
  const t = (key: string): string => localization.translateKey(key);
  const versions = localization.translateKey(AppStrings.Shell.OutdatedUi.Versions, { device, computer });
  if (variant === 'device') {
    return {
      title: t(AppStrings.Shell.OutdatedUi.Device.Title),
      body: t(AppStrings.Shell.OutdatedUi.Device.Body),
      steps: [t(AppStrings.Shell.OutdatedUi.Device.Step.Refresh), t(AppStrings.Shell.OutdatedUi.Device.Step.Restart)],
      action: t(Strings.Common.Refresh),
      versions,
    };
  }
  return {
    title: t(AppStrings.OutdatedUi.Installation.Title),
    body: t(AppStrings.OutdatedUi.Installation.Body),
    steps: [
      t(AppStrings.Shell.OutdatedUi.Installation.Step.Reinstall),
      t(AppStrings.OutdatedUi.Installation.Step.Antivirus),
      t(AppStrings.OutdatedUi.Installation.Step.Retry),
    ],
    action: t(Strings.Common.Retry),
    versions,
  };
}

function browserDeps(prepareReload: () => Promise<void>, outdated: OutdatedUiSetup): VersionCheckDeps {
  return {
    doc: document,
    storage: sessionStorage,
    currentUrl: () => window.location.href,
    replaceUrl: (url) => window.location.replace(url),
    replaceUrlInPlace: (url) => window.history.replaceState(window.history.state, '', url),
    fetchHostCommit,
    fetchServedCommit: (nonce) => fetchServedUiCommit(document.baseURI, nonce),
    now: () => Date.now(),
    prepareReload,
    outdated,
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
      const localization = inject(LocalizationService);
      const injector = inject(Injector);
      const outdated: OutdatedUiSetup = {
        text: (variant, device, computer) => outdatedUiText(localization, variant, device, computer),
        onTextChange: (listener) => {
          const subscription = toObservable(localization.catalogVersion, { injector })
            .pipe(skip(1))
            .subscribe(() => listener());
          return () => subscription.unsubscribe();
        },
        refresh: (hostCommit) => window.location.replace(withCacheBust(window.location.href, hostCommit)),
      };
      const check = createVersionCheckRunner(resolveBaseUrl, browserDeps(prepareReload, outdated));

      check();
      connectionEstablished(api.connectionState$)
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe(check);
    }),
  ];
}
