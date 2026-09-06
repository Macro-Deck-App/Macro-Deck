import {
  ClientAppStrings, DEFAULT_WEB_CLIENT_TARGET, WidgetType, type GridWidget, type UiNode, type UiRenderHost,
} from '@macro-deck/runtime';
import { Appearance } from './appearance';
import { Client } from './client';
import { RenderingModeStore } from './rendering-mode';
import { ServerClock } from './server-clock';
import { Shell, type ShellServices } from './shell';
import { dismissAllToasts } from './ui';
import { WakeLock } from './wake-lock';
import { WidgetSessions } from './widget-sessions';

const host: UiRenderHost = {
  localization: { translate: (scope, key) => `${scope}:${key}` },
  resourceUrl: () => null,
  now: () => 0,
  culture: () => 'en',
  simpleRendering: () => false,
  fontFamily: () => null,
  fontReady: () => true,
  emit: () => undefined,
};

const folder = (id: string) => ({
  id, name: id, parentId: null, order: 0, isExpanded: false, isDefault: true,
  cols: 3, rows: 2, background: '', spacing: null, borderRadius: null,
  viewId: 'macrodeck.widget-grid', viewConfiguration: null, widgets: [],
}) as never;

const gridWidget = (id: string, x: number, y: number): GridWidget =>
  ({ id, folderId: 'root', x, y, w: 1, h: 1, type: WidgetType.ActionButton, data: {} }) as GridWidget;

const folderWithWidgets = (id: string, widgetIds: readonly string[]) => ({
  ...(folder(id) as unknown as Record<string, unknown>),
  widgets: widgetIds.map((widgetId, index) => gridWidget(widgetId, index, 0)),
}) as never;

const tree = (text: string): UiNode =>
  ({ id: 'root', type: 'ui.text', properties: { text } }) as UiNode;

const iconTree = (resourceId: string): UiNode =>
  ({
    id: 'root',
    type: 'ui.image',
    properties: { size: { basis: 0.5 }, source: { resourceId, contentHash: 'h1' } },
  }) as UiNode;

function pointerEvent(type: string): Event {
  const event = new Event(type) as Event & { clientX: number; clientY: number; pointerId: number; button: number };
  event.clientX = 0;
  event.clientY = 0;
  event.pointerId = 1;
  event.button = 0;
  return event;
}

class FakeConnection {
  private next = 0;

  request<T>(type: string): Promise<T> {
    if (type === 'OpenWidgetUiSession') {
      return Promise.resolve({ accepted: true, sessionId: `s${++this.next}` } as unknown as T);
    }
    if (type === 'AttachUiSession') return Promise.resolve({ accepted: true } as unknown as T);
    return Promise.resolve(undefined as T);
  }
}

const settle = async () => { for (let turn = 0; turn < 6; turn++) await Promise.resolve(); };

class SyncScheduler {
  private callback: (() => void) | null = null;

  readonly schedule = (callback: () => void): void => {
    this.callback = callback;
  };

  flush(): void {
    const callback = this.callback;
    this.callback = null;
    if (callback) callback();
  }
}

describe('Shell', () => {
  let root: HTMLElement;
  let client: Client;

  beforeEach(() => {
    root = document.createElement('div');
    Object.defineProperty(root, 'clientWidth', { value: 600, configurable: true });
    Object.defineProperty(root, 'clientHeight', { value: 400, configurable: true });
    document.body.appendChild(root);
    client = new Client(() => 'http://host', 'client-1');
  });

  afterEach(() => root.remove());

  const services = (): ShellServices => ({
    target: DEFAULT_WEB_CLIENT_TARGET,
    appearance: new Appearance(),
    rendering: new RenderingModeStore(),
    wakeLock: new WakeLock('web-client', null),
    pwa: {
      update: {
        phase: () => 'idle',
        updatePending: () => false,
        onPhaseChange: () => undefined,
        check: () => Promise.resolve(),
        backgroundCheck: () => Promise.resolve(),
        activateNow: () => Promise.resolve(),
        destroy: () => undefined,
      },
      install: {
        displayMode: () => 'browser',
        availability: () => 'unsupported',
        onChange: () => undefined,
        promptInstall: () => Promise.resolve('unavailable'),
        destroy: () => undefined,
      },
      workerRegistered: false,
      destroy: () => undefined,
    } as unknown as ShellServices['pwa'],
    clock: new ServerClock({ get: () => new Promise(() => undefined) } as never),
  });

  const mount = (schedule?: (callback: () => void) => void) =>
    new Shell(root, client, host, { ...services(), schedule });

  const text = (key: string) => client.translate(key);

  it('says what it is doing before the host has answered', () => {
    mount();

    expect(root.textContent).toContain(text(ClientAppStrings.WebClient.Connecting));
  });

  it('tells the user where to unlock a locked key ring', () => {
    mount();
    client.app.set({ probed: true, keyRingLocked: true });

    expect(root.textContent).toContain(text(ClientAppStrings.KeyRing.Unlock.WebClient));
  });

  it('points first-time setup at the desktop app', () => {
    mount();
    client.app.set({ probed: true, setupRequired: true });

    expect(root.textContent).toContain(text(ClientAppStrings.WebClient.Setup.Body));
  });

  it('offers a sign-in form once setup is done, not a dead end', () => {
    mount();
    client.app.set({ probed: true });

    // The client could not be signed into at all until this form existed.
    expect(root.querySelector('.wc-login')).not.toBeNull();
    expect(root.querySelectorAll('input[type="password"]').length).toBe(1);
    expect(root.querySelectorAll('input:not([type="password"])').length).toBe(1);
  });

  it('does not leave a dialog standing over the screen that replaced it', () => {
    mount();
    client.app.set({ probed: true, authenticated: true, deckRendered: true });

    const trigger = root.querySelector('.wc-client-settings-trigger') as HTMLElement;
    expect(trigger).withContext('the settings gear').not.toBeNull();
    trigger.click();
    expect(document.querySelector('.wc-client-settings-backdrop')).not.toBeNull();

    // Signing out from inside that panel is the way most people reach the sign-in card, and the
    // panel's backdrop swallows every press: the form is on screen, looks ready, and cannot be
    // signed in with.
    client.app.set({ authenticated: false, deckRendered: false });

    expect(root.querySelector('.wc-login')).withContext('the sign-in card').not.toBeNull();
    expect(document.querySelector('.wc-client-settings-backdrop')).toBeNull();
  });

  it('shows one screen at a time', () => {
    mount();
    client.app.set({ probed: true });
    client.app.set({ authenticated: true });

    expect(root.querySelectorAll('.wc-panel').length).toBe(1);
    expect(root.querySelector('.wc-login')).toBeNull();
    expect(root.textContent).toContain(text(ClientAppStrings.WebClient.Connecting));
  });

  it('draws the deck once connected', () => {
    mount();
    client.deck.load([folder('root')]);
    client.app.set({ probed: true, authenticated: true, connected: true });

    expect(root.querySelector('.wc-deck')).not.toBeNull();
    expect(root.querySelectorAll('.deck-grid-cell').length).toBe(6);
  });

  it('reserves no margin between the deck and the edge of the display', () => {
    // The grid keeps 16px per side by default, which is right in the editor, where a deck sits
    // inside a page. A client that owns the whole screen spends it on the bezel twice over, and on
    // a 1024px display it is the difference between the deck reaching the edge and not.
    // jsdom measures every box as zero, and a grid solved for a zero box reserves nothing whatever
    // its margin - so the deck is given a real one to solve against before it is drawn.
    const width = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth');
    const height = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientHeight');
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', { value: 1024, configurable: true });
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', { value: 600, configurable: true });
    try {
      mount();
      client.deck.load([folder('root')]);
      client.app.set({ probed: true, authenticated: true, connected: true });

      // The grid keeps a border of its own, equal to the widget gap, so the cells do not start at
      // zero. What "no outer margin" means is that the surface reaches the edge of the box it was
      // given on whichever axis constrains it - the other one letterboxes, which is the grid's
      // shape rather than a margin. With the 16px default it would reach neither.
      const surface = root.querySelector('.deck-grid') as HTMLElement;
      const width = Math.round(parseFloat(surface.style.width));
      const height = Math.round(parseFloat(surface.style.height));

      expect(width === 1024 || height === 600).toBeTrue();
    } finally {
      if (width) Object.defineProperty(HTMLElement.prototype, 'clientWidth', width);
      if (height) Object.defineProperty(HTMLElement.prototype, 'clientHeight', height);
    }
  });

  it('keeps the very same deck element across a reconnect', () => {
    mount();
    client.deck.load([folder('root')]);
    client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });
    const deck = root.querySelector('.wc-deck');

    // A drop keeps the deck on screen, so nothing should be rebuilt.
    client.app.set({ connected: false });

    expect(root.querySelector('.wc-deck')).toBe(deck);
  });

  describe('the reconnecting notice', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('covers the deck with a reconnecting notice while the connection is down', () => {
      mount();
      client.deck.load([folder('root')]);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });
      const deck = root.querySelector('.wc-deck');

      client.app.set({ connected: false });
      // An ordinary retry reconnects well inside this delay, so nothing should show yet.
      expect(root.querySelector('.wc-reconnecting')).toBeNull();

      jasmine.clock().tick(4000);

      const notice = root.querySelector('.wc-reconnecting');
      expect(notice).withContext('the reconnecting panel').not.toBeNull();
      // The deck is still there, and it is the very same node - not rebuilt under the notice.
      expect(root.querySelector('.wc-deck')).toBe(deck);
      expect((notice as HTMLElement).getAttribute('role')).toBe('status');
      expect((notice as HTMLElement).getAttribute('aria-live')).toBe('polite');
      expect((notice as HTMLElement).querySelector('.wc-panel-title')!.textContent)
        .toBe(text(ClientAppStrings.WebClient.Reconnecting.Title));
      expect((notice as HTMLElement).querySelector('.wc-panel-detail')!.textContent)
        .toBe(text(ClientAppStrings.WebClient.Reconnecting.Body));

      client.app.set({ connected: true });

      expect(root.querySelector('.wc-reconnecting')).toBeNull();
      expect(root.querySelector('.wc-deck')).toBe(deck);
    });

    it('never covers the sign-in form of a client that signed out', () => {
      mount();
      client.deck.load([folder('root')]);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      // A sign-out drops the socket too, so `connected` falls exactly as it does on a network loss.
      client.app.set({ authenticated: false, deckRendered: false });
      client.app.set({ connected: false });
      jasmine.clock().tick(4000);

      expect(client.app.screen.get()).toBe('signedOut');
      expect(root.querySelector('.wc-reconnecting')).toBeNull();
    });

    it('never uncovers the deck of a locked host while reconnecting', () => {
      mount();
      client.deck.load([folder('root')]);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });
      client.hostLock.apply({ locked: true, lockScreenEnabled: true, supported: true });

      client.app.set({ connected: false });
      jasmine.clock().tick(4000);

      const panels = root.querySelectorAll('.wc-lock-screen, .wc-reconnecting');
      expect(panels.length).withContext('at least one explanatory panel').toBeGreaterThan(0);
      // Exactly one: the lock outranks the reconnecting notice rather than stacking with it.
      expect(root.querySelectorAll('.wc-panel').length).toBe(1);
    });
  });

  it('keeps the grid surface when the deck contents change', () => {
    mount();
    client.deck.load([folder('root')]);
    client.app.set({ probed: true, authenticated: true, connected: true });
    const surface = root.querySelector('.deck-grid');

    client.deck.widgetsUpserted('root', [
      { id: 'w1', folderId: 'root', x: 0, y: 0, w: 1, h: 1, type: 'action-button', data: {} } as never,
    ]);

    // The tiles are redrawn; the surface they sit on is not, so the deck does not flicker.
    expect(root.querySelector('.deck-grid')).toBe(surface);
    expect(root.querySelectorAll('.deck-grid-tile').length).toBe(1);
  });

  it('paints the folder background over the theme default', () => {
    mount();
    const coloured = folder('root') as unknown as { background: string };
    coloured.background = '#101820';
    client.deck.load([coloured as never]);
    client.app.set({ probed: true, authenticated: true, connected: true });

    expect((root.querySelector('.deck-grid') as HTMLElement).style.background).toBe('rgb(16, 24, 32)');
  });

  it('puts the settings entry on screen, which is the only way to sign out', () => {
    mount();

    // Without this the client is a dead end: there is no other route to sign-out, the theme, the
    // rendering mode or the update control.
    expect(root.querySelector('.wc-client-settings-trigger')).not.toBeNull();
  });

  describe('a refused press', () => {
    const refuse = (message: string) =>
      (client as unknown as { onNotification(type: string, payload: unknown): void })
        .onNotification('ActionExecutionStatusEvent', {
          executionId: 'e1', status: 'Failed', durationMs: 0, widgetId: 'w1',
          triggerType: 'onShortPress', error: { code: 'HOST_LOCKED', message }, actions: [],
        });

    // After, not before: the toast host is one module-level stack shared with every other suite, and
    // a toast left standing here turns up in theirs.
    afterEach(() => dismissAllToasts());

    it('is put in front of the person who pressed', () => {
      mount();
      client.deck.load([folder('root')]);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      refuse('The host is locked');

      const toast = root.querySelector('.wc-toast');
      expect(toast).withContext('a toast for the refusal').not.toBeNull();
      expect((toast as HTMLElement).classList).toContain('wc-toast-error');
      expect((root.querySelector('.wc-toast-message') as HTMLElement).textContent)
        .toBe('The host is locked');
    });

    it('does not outlive the session it happened in', () => {
      mount();
      client.deck.load([folder('root')]);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });
      refuse('The host is locked');
      expect(root.querySelectorAll('.wc-toast').length).toBe(1);

      client.app.set({ authenticated: false, deckRendered: false });

      // Otherwise the last session's failure hangs over the next person's sign-in card.
      expect(root.querySelectorAll('.wc-toast').length).toBe(0);
      expect(root.querySelector('.wc-login')).not.toBeNull();
    });
  });

  it('shows the Macro Deck mark above every waiting screen', () => {
    mount();
    client.app.set({ probed: true, setupRequired: true });

    const logo = root.querySelector('.wc-panel-logo') as HTMLImageElement;
    expect(logo).not.toBeNull();
    expect(logo.getAttribute('alt')).toBe('');
  });

  it('leaves the mark out of the deck, which has its own content', () => {
    mount();
    client.deck.load([folder('root')]);
    client.app.set({ probed: true, authenticated: true, connected: true });

    expect(root.querySelector('.wc-panel-logo')).toBeNull();
  });

  describe('per-widget session repaints', () => {
    let scheduler: SyncScheduler;
    let widgetSessions: WidgetSessions;

    beforeEach(() => {
      scheduler = new SyncScheduler();
      widgetSessions = new WidgetSessions(new FakeConnection(), client.sessions);
      (client as unknown as { widgetSessions: WidgetSessions }).widgetSessions = widgetSessions;
    });

    const tileText = (widgetId: string): string | null => {
      const tile = root.querySelector(`[data-widget-id="${widgetId}"]`);
      return tile ? tile.querySelector('.widget-text')!.textContent : null;
    };

    it('repaints only the widget whose session changed', async () => {
      client.deck.load([folderWithWidgets('root', ['w1', 'w2'])]);
      mount(scheduler.schedule);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      widgetSessions.sync([gridWidget('w1', 0, 0), gridWidget('w2', 1, 0)]);
      await settle();
      client.sessions.treeUpdated('s1', 1, tree('A1'));
      client.sessions.treeUpdated('s2', 1, tree('B1'));
      scheduler.flush();

      const w2Tile = root.querySelector('[data-widget-id="w2"]');

      // s2's tree changes in the store without ever being announced, so the only thing that can
      // make w2 show it is a full deck repaint - which announcing s1 alone must not trigger.
      (client.sessions as unknown as { entries: { [id: string]: { tree: UiNode; revision: number } } })
        .entries['s2'] = { tree: tree('B2'), revision: 2 };
      client.sessions.treeUpdated('s1', 2, tree('A2'));
      scheduler.flush();

      expect(tileText('w1')).toBe('A2');
      expect(tileText('w2')).toBe('B1');
      expect(root.querySelector('[data-widget-id="w2"]')).toBe(w2Tile);
    });

    it('repaints the deck when a session change cannot be mapped to any widget', async () => {
      client.deck.load([folderWithWidgets('root', ['w1', 'w2'])]);
      mount(scheduler.schedule);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      widgetSessions.sync([gridWidget('w1', 0, 0), gridWidget('w2', 1, 0)]);
      await settle();
      client.sessions.treeUpdated('s1', 1, tree('A1'));
      client.sessions.treeUpdated('s2', 1, tree('B1'));
      scheduler.flush();
      expect(tileText('w1')).toBe('A1');

      // w1's tree changes in the store without being announced, so only a full deck repaint - the
      // fallback for a session id no widget owns, a modal's or a provider folder view's - picks it up.
      (client.sessions as unknown as { entries: { [id: string]: { tree: UiNode; revision: number } } })
        .entries['s1'] = { tree: tree('A2'), revision: 2 };
      client.sessions.treeUpdated('modal-session', 1, tree('X'));
      scheduler.flush();

      expect(tileText('w1')).toBe('A2');
    });

    it('never drops a tree that arrived for a widget with no tile yet', async () => {
      client.deck.load([folderWithWidgets('root', ['w1'])]);
      mount(scheduler.schedule);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      // A session is open for w2 before it is on this deck at all - the client only needs the
      // mapping to exist for the race this covers, not the exact sequence that produces it.
      widgetSessions.sync([gridWidget('w1', 0, 0), gridWidget('w2', 1, 0)]);
      await settle();

      client.sessions.treeUpdated('s2', 1, tree('B1'));
      scheduler.flush();
      expect(root.querySelector('[data-widget-id="w2"]')).toBeNull();

      client.deck.widgetsUpserted('root', [gridWidget('w2', 1, 0)]);
      scheduler.flush();

      expect(tileText('w2')).toBe('B1');
    });

    it('does not blank the deck when the socket drops', async () => {
      client.deck.load([folderWithWidgets('root', ['w1', 'w2'])]);
      mount(scheduler.schedule);
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      widgetSessions.sync([gridWidget('w1', 0, 0), gridWidget('w2', 1, 0)]);
      await settle();
      client.sessions.treeUpdated('s1', 1, tree('A1'));
      client.sessions.treeUpdated('s2', 1, tree('B1'));
      scheduler.flush();
      const deckHost = root.querySelector('.wc-deck');

      client.sessions.clear();
      widgetSessions.reset();
      scheduler.flush();

      expect(tileText('w1')).toBe('A1');
      expect(tileText('w2')).toBe('B1');
      expect(root.querySelector('.wc-deck')).toBe(deckHost);
    });

    it('asks again for an icon whose request the outage refused', async () => {
      const iconHost: UiRenderHost = {
        ...host,
        resourceUrl: resource => (resource ? `/api/ui/resources/${resource.resourceId}` : null),
      };
      client.deck.load([folderWithWidgets('root', ['w1'])]);
      new Shell(root, client, iconHost, { ...services(), schedule: scheduler.schedule });
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      widgetSessions.sync([gridWidget('w1', 0, 0)]);
      await settle();
      client.sessions.treeUpdated('s1', 1, iconTree('icon-1'));
      scheduler.flush();

      const image = root.querySelector('img') as HTMLImageElement;
      expect(image).withContext('the widget icon').not.toBeNull();
      // An icon requested while the host was unreachable keeps the URL it failed on, so the deck that
      // comes back would otherwise stay blank until the whole client is loaded again.
      image.dispatchEvent(new Event('error'));
      client.app.set({ connected: false });
      const assigned = spyOn(image, 'setAttribute').and.callThrough();

      client.app.set({ connected: true });

      expect(assigned.calls.all().filter(call => call.args[0] === 'src').map(call => call.args[1]))
        .toEqual(['/api/ui/resources/icon-1']);
    });

    it('keeps interaction feedback synchronous, with no scheduler flush', () => {
      client.deck.load([folderWithWidgets('root', ['w1', 'w2']), folder('other')]);
      new Shell(root, client, host, {
        ...services(), target: { ...DEFAULT_WEB_CLIENT_TARGET, hardwareInput: { keys: [] } },
        schedule: scheduler.schedule,
      });
      client.app.set({ probed: true, authenticated: true, connected: true, deckRendered: true });

      const w1Tile = () => root.querySelector('[data-widget-id="w1"]') as HTMLElement;
      const surface = w1Tile().querySelector('.deck-grid-tile-surface') as HTMLElement;
      surface.dispatchEvent(pointerEvent('pointerdown'));

      expect(w1Tile().classList.contains('deck-grid-tile-pressed')).toBeTrue();
      surface.dispatchEvent(pointerEvent('pointerup'));

      // A folder switch and back moves the input cursor, which paints the focus ring directly on
      // the grid - nothing here ever touches `scheduler`.
      client.deck.openFolder('other');
      client.deck.openFolder('root');

      expect(root.querySelectorAll('.deck-grid-tile-focused').length).toBe(1);
      expect(w1Tile().classList.contains('deck-grid-tile-focused')).toBeTrue();
    });
  });
});
