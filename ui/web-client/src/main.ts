import {
  ClientAppStrings,
  disablePageZoom,
  randomToken,
  UiFont,
  uiResourceUrl,
  type GetSystemFontsResponse,
  type UiRenderHost,
  type WebClientTarget,
} from '@macro-deck/runtime';
import { ACTIVE_TARGET } from './targets/active-target';
import { Appearance } from './appearance';
import { Client } from './client';
import { FontLoader } from './fonts';
import { RenderingModeStore } from './rendering-mode';
import { ServerClock } from './server-clock';
import { Shell } from './shell';
import { WakeLock } from './wake-lock';
import { setupPwa } from './pwa';
import { IconPrefetch } from './icon-prefetch';
import { startNetworkWatch } from './network-watch';
import { startSessionResume } from './session-resume';
import { startVersionCheck } from './version-check';

const CLIENT_TYPE = 'web-client';

function hostBaseUrl(): string {
  return window.location.origin;
}

function clientId(): string {
  const key = 'macro-deck.client-id';
  try {
    const stored = window.localStorage.getItem(key);
    if (stored) return stored;
    const fresh = randomToken();
    window.localStorage.setItem(key, fresh);
    return fresh;
  } catch {
    // Private browsing refuses storage outright; a per-session id still lets the host tell us apart.
    return randomToken();
  }
}

export function start(root: HTMLElement, target: WebClientTarget = ACTIVE_TARGET): Client {
  disablePageZoom();

  // Before the client, which drives it: the host is authoritative for the theme and the accent, and
  // the client is what reads them off it.
  const appearance = new Appearance();
  const client = new Client(hostBaseUrl, clientId(), { appearance });
  const clock = new ServerClock(client.http);
  const fonts = new FontLoader(hostBaseUrl());
  const uiFont = new UiFont();
  appearance.useUiFont(
    uiFont,
    () => client.http.get<GetSystemFontsResponse>('/api/system/fonts').then(response => response.faces ?? []),
    faceId => `${hostBaseUrl()}/api/system/fonts/${encodeURIComponent(faceId)}/file`);
  const rendering = new RenderingModeStore();
  const wakeLock = new WakeLock(CLIENT_TYPE, target.capabilities.wakeLock ? undefined : null);
  const pwa = setupPwa(target.capabilities.serviceWorker ? {} : { devMode: true });

  const host: UiRenderHost = {
    // The host's catalogue once it has answered, and the one compiled into the package until then.
    localization: client.localization,
    resourceUrl: resource => uiResourceUrl(hostBaseUrl(), resource),
    // The host's clock, not the device's: the looping border animations are phase-locked to it, and
    // two clients an unsynchronised second apart run visibly out of step.
    now: () => clock.now(),
    // The host's language, not the browser's: a widget's dates are written in the language the deck
    // is configured for, and the two disagree about as often as they agree.
    culture: () => client.localization.culture(),
    hourCycle: () => client.localization.hourCycle(),
    simpleRendering: () => rendering.simple(),
    fontFamily: faceId => (faceId ? `MacroDeckFont_${faceId}` : null),
    fontReady: faceId => fonts.ready(faceId),
    uiFontKey: () => String(uiFont.version()),
    // Replaced per tile by the grid, which knows which widget a node belongs to.
    emit: () => undefined,
  };

  const shell = new Shell(root, client, host, {
    target,
    appearance,
    rendering,
    wakeLock,
    pwa,
    clock,
  });

  startVersionCheck({
    baseUrl: hostBaseUrl,
    connectionState: client.connection.state,
    update: pwa.update,
    deps: {
      outdatedText: {
        heading: client.translate(ClientAppStrings.WebClient.Outdated.Title),
        body: client.translate(ClientAppStrings.WebClient.Outdated.Body),
      },
    },
  });

  startSessionResume(
    {
      resume: () => client.resume(),
      isAuthenticated: () => client.app.conditions.get().authenticated,
    },
    { reconnectNow: () => client.connection.reconnectNow() });

  startNetworkWatch({ retryNow: () => client.retryNow() });

  const icons = new IconPrefetch({
    baseUrl: hostBaseUrl,
    folders: () => client.deck.folders.get(),
    currentFolderId: () => client.deck.location.get().folderId,
    resolveGrid: folder => client.gridFor(folder),
    outerMargin: 0,
  });
  client.deck.folders.subscribe(() => icons.warm());
  client.deck.location.subscribe(() => icons.warm());

  window.addEventListener('resize', () => shell.resize());
  client.localization.onChange(() => {
    shell.repaint();
    syncDocumentLanguage(client);
  });
  syncDocumentLanguage(client);
  // A face landing is the moment text held back in it can finally be drawn.
  fonts.onChange(() => shell.repaint());
  uiFont.onChange(() => shell.repaint());
  rendering.onChange(() => shell.repaint());
  appearance.onChange(() => shell.repaint());
  appearance.setPersistence((mode, accent) => void client.saveAppearance(mode, accent));

  // A tab that was in the background may have missed a whole session; the clock may also have
  // drifted while nothing was drawing.
  // The session resume above owns waking the session back up; doing it here as well would run it
  // twice per wake, uncoalesced.
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) return;
    void clock.sync();
    void pwa.update.backgroundCheck();
  });

  client.watchForRecovery();
  void client.probe();
  return client;
}

function syncDocumentLanguage(client: Client): void {
  document.documentElement.lang = client.localization.culture();
  document.title = client.translate(ClientAppStrings.WebClient.Meta.Title);
}

const mount = document.getElementById('app');
if (mount) start(mount);
