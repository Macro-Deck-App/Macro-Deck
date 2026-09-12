import {
  activationClaim,
  ClientAppStrings,
  reloadFailedImages,
  renderWidgetGrid,
  type GridWidget,
  type UiRenderHost,
  type WebClientTarget,
  type WidgetGridHandle,
} from '@macro-deck/runtime';
import { AppScreen } from './app-state';
import { Appearance } from './appearance';
import { Client } from './client';
import { DeckInput } from './deck-input';
import { DeckRepaintQueue } from './deck-repaint';
import { FolderView } from './folder-view';
import { ModalHost } from './modal';
import { RenderingModeStore } from './rendering-mode';
import { ServerClock } from './server-clock';
import { WakeLock } from './wake-lock';
import { createLoginForm } from './login';
import { createClientSettings, type ClientSettingsHandle } from './settings';
import { createDeviceSetupWizard, createSetupBanner, DeviceSetupService, DismissibleHints } from './setup';
import { dismissAllToasts, mountToastHost, showToast } from './ui';
import type { Pwa } from './pwa';

type ScreenView = { screen: AppScreen; element: HTMLElement };

const RECONNECTING_OVERLAY_DELAY_MS = 4000;

export interface ShellServices {
  target: WebClientTarget;
  appearance: Appearance;
  rendering: RenderingModeStore;
  wakeLock: WakeLock;
  pwa: Pwa;
  clock: ServerClock;
  schedule?(callback: () => void): void;
}

// The deck is built once and kept rather than rebuilt per screen change: a deck that has painted
// survives a dropped connection (see screenFor), and re-mounting would undo exactly that.
export class Shell {
  private readonly root: HTMLElement;
  private current: ScreenView | null = null;
  private grid: WidgetGridHandle | null = null;
  private deckHost: HTMLElement | null = null;
  private readonly modals: ModalHost;
  private readonly deckInput: DeckInput;
  private readonly deviceSetup: DeviceSetupService;
  private folderView: FolderView | null = null;
  private readonly settings: ClientSettingsHandle | null;
  private lockScreen: HTMLElement | null = null;
  private reconnectingPanel: HTMLElement | null = null;
  private reconnectingTimer: ReturnType<typeof setTimeout> | null = null;
  private wasConnected = false;
  private reconnectingDue = false;
  private readonly repaintQueue: DeckRepaintQueue;

  constructor(
    root: HTMLElement,
    private readonly client: Client,
    private readonly host: UiRenderHost,
    private readonly services: ShellServices,
  ) {
    this.root = root;
    this.root.className = 'wc-root';
    this.modals = new ModalHost(this.root, client, host);

    mountToastHost(this.root, {
      dismissLabel: client.translate(ClientAppStrings.Feedback.Dismiss),
    });

    this.deckInput = new DeckInput(
      {
        widgets: () => this.client.deck.displayedWidgets,
        activateWidget: widgetId => activationClaim(this.client.widgetSessions.treeFor(widgetId)) === 'absorbed',
        triggerWidget: (widget: GridWidget) => {
          void this.client.executeTrigger(widget.id, 'onShortPress');
        },
        goBack: () => this.client.deck.back(),
        modalOpen: () => this.modals.isOpen(),
        focusChanged: widgetId => {
          if (this.grid) this.grid.setFocusedWidget(widgetId);
        },
      },
      services.target.hardwareInput);

    this.repaintQueue = new DeckRepaintQueue({
      paintDeck: () => this.paintDeck(),
      paintWidget: widgetId => this.paintWidgetTile(widgetId),
    }, services.schedule);

    this.client.app.screen.subscribe(screen => { this.show(screen); this.paintOverlays(); });
    this.client.deck.folders.subscribe(() => this.paintDeck());
    this.client.deck.location.subscribe(() => {
      // A folder switch replaces the deck, so the cursor goes back to the start rather than landing
      // on whatever tile happens to be in the old position.
      this.deckInput.resetFocus();
      this.paintDeck();
    });
    this.client.sessions.onChange(sessionId => {
      if (sessionId === null) {
        this.repaintQueue.deck();
        return;
      }
      const widgetId = this.client.widgetSessions.widgetFor(sessionId);
      if (widgetId === undefined || !this.grid) {
        this.repaintQueue.deck();
        return;
      }
      this.repaintQueue.widget(widgetId);
    });
    this.client.hostLock.state.subscribe(() => this.paintOverlays());
    this.wasConnected = this.client.app.conditions.get().connected;
    this.client.app.conditions.subscribe(conditions => this.onConnectedChanged(conditions.connected));
    this.client.executionFeedback.failures.subscribe(failure => {
      if (failure !== null) showToast(failure.message, { kind: 'error' });
    });

    this.deviceSetup = new DeviceSetupService(client.http);
    this.settings = createClientSettings({
      client,
      appearance: services.appearance,
      renderingMode: services.rendering,
      wakeLock: services.wakeLock,
      update: services.pwa.update,
      install: services.pwa.install,
      capabilities: services.target.capabilities,
      openDeviceSetup: () => this.openDeviceSetup(),
    });
    if (this.settings !== null) this.root.appendChild(this.settings.element);

    if (services.target.capabilities.deviceSetupPrompts) {
      this.root.appendChild(createSetupBanner({
        deviceSetup: this.deviceSetup,
        localization: client.localization,
        hints: new DismissibleHints(),
        onOpen: () => this.openDeviceSetup(),
      }).element);
      // Asked for once the client is signed in, which is the first moment the answer is meaningful.
      this.client.app.screen.subscribe(screen => {
        if (screen === 'deck') void this.deviceSetup.load();
      });
    }

    this.show(this.client.app.screen.get());
  }

  private openDeviceSetup(): void {
    const wizard = createDeviceSetupWizard({
      deviceSetup: this.deviceSetup,
      localization: this.client.localization,
      pwaInstall: this.services.pwa.install,
    });
    this.root.appendChild(wizard.element);
  }

  resize(): void {
    if (this.grid) this.grid.resize();
  }

  repaint(): void {
    this.paintDeck();
    this.paintOverlays();
    this.modals.repaint();
  }

  private onConnectedChanged(connected: boolean): void {
    if (connected === this.wasConnected) return;
    this.wasConnected = connected;

    if (connected) {
      this.clearReconnectingTimer();
      this.reconnectingDue = false;
      // An icon whose request was refused while the socket was down keeps the same URL, so a repaint
      // alone never asks for it again.
      reloadFailedImages(this.root);
      this.paintOverlays();
      return;
    }

    this.reconnectingTimer = setTimeout(() => {
      this.reconnectingTimer = null;
      this.reconnectingDue = true;
      this.paintOverlays();
      // An ordinary retry reconnects well inside this delay, so only a disconnection that outlives
      // it is worth telling anyone about at all.
    }, RECONNECTING_OVERLAY_DELAY_MS);
  }

  private clearReconnectingTimer(): void {
    if (this.reconnectingTimer === null) return;
    clearTimeout(this.reconnectingTimer);
    this.reconnectingTimer = null;
  }

  private paintOverlays(): void {
    if (this.client.hostLock.showLockScreen()) {
      this.removeReconnectingPanel();
      this.paintLockScreen();
      return;
    }
    this.removeLockScreen();

    // Only over a deck: a sign-out disconnects the socket too, and a "reconnecting" panel over the
    // sign-in form would be both wrong and impossible to get past.
    const overDeck = this.client.app.screen.get() === 'deck';
    if (overDeck && this.reconnectingDue && !this.client.app.conditions.get().connected) {
      this.paintReconnectingPanel();
    } else {
      this.removeReconnectingPanel();
    }
  }

  private paintLockScreen(): void {
    if (!this.lockScreen) {
      this.lockScreen = document.createElement('div');
      this.lockScreen.className = 'wc-lock-screen';
      // Announced rather than merely drawn: someone using a screen reader has no other way to know
      // the deck under it stopped being reachable.
      this.lockScreen.setAttribute('role', 'status');
      this.lockScreen.setAttribute('aria-live', 'polite');
      this.root.appendChild(this.lockScreen);
    }

    this.lockScreen.textContent = '';
    this.lockScreen.appendChild(panel(
      this.client.translate(ClientAppStrings.WebClient.Lock.Title),
      this.client.translate(ClientAppStrings.WebClient.Lock.Body)));
  }

  private removeLockScreen(): void {
    if (!this.lockScreen) return;
    this.lockScreen.remove();
    this.lockScreen = null;
  }

  private paintReconnectingPanel(): void {
    if (!this.reconnectingPanel) {
      this.reconnectingPanel = document.createElement('div');
      this.reconnectingPanel.className = 'wc-reconnecting';
      // Same reason as the lock screen: the deck under it goes quiet with no other signal at all.
      this.reconnectingPanel.setAttribute('role', 'status');
      this.reconnectingPanel.setAttribute('aria-live', 'polite');
      this.root.appendChild(this.reconnectingPanel);
    }

    this.reconnectingPanel.textContent = '';
    this.reconnectingPanel.appendChild(panel(
      this.client.translate(ClientAppStrings.WebClient.Reconnecting.Title),
      this.client.translate(ClientAppStrings.WebClient.Reconnecting.Body)));
  }

  private removeReconnectingPanel(): void {
    if (!this.reconnectingPanel) return;
    this.reconnectingPanel.remove();
    this.reconnectingPanel = null;
  }

  private show(screen: AppScreen): void {
    if (this.current && this.current.screen === screen) return;

    if (this.current) this.current.element.remove();
    const element = screen === 'deck' ? this.deckElement() : this.messageFor(screen);
    this.root.appendChild(element);
    this.current = { screen, element };
    if (screen !== 'deck') this.root.style.background = '';

    // The lock is held only while a deck is actually on screen; the preference outlives the screen.
    this.services.wakeLock.setGate(screen === 'deck');

    // Every standing confirmation is about the session that just ended, and leaving one over the
    // sign-in card shows the next person what the last one was doing.
    if (screen === 'signedOut') dismissAllToasts();

    // A dialog belongs to the screen it was opened over. Signing out from the settings panel used to
    // leave it standing on top of the sign-in card, where its backdrop swallowed every press: the
    // form was on screen, looked ready, and could not be signed in with.
    if (this.settings !== null) this.settings.close();

    if (screen === 'deck') this.paintDeck();
  }

  private messageFor(screen: AppScreen): HTMLElement {
    const t = (key: string) => this.client.translate(key);

    switch (screen) {
      case 'keyRingLocked':
        return panel('Macro Deck', t(ClientAppStrings.KeyRing.Unlock.WebClient));
      case 'setupRequired':
        return panel('Macro Deck', t(ClientAppStrings.WebClient.Setup.Body));
      case 'signedOut':
        return createLoginForm(this.client).element;
      default:
        return panel('Macro Deck', t(ClientAppStrings.WebClient.Connecting));
    }
  }

  private deckElement(): HTMLElement {
    if (!this.deckHost) {
      this.deckHost = document.createElement('div');
      this.deckHost.className = 'wc-deck';
    }
    return this.deckHost;
  }

  private paintDeck(): void {
    // `document.contains` rather than `node.isConnected`, which is Safari 10: on iOS 9 the
    // property is simply undefined, so the guard read as "not connected" and returned before
    // the grid was ever created. A blank deck with no error anywhere (issue #829).
    if (!this.deckHost || !document.contains(this.deckHost)) return;

    const folder = this.client.deck.currentFolder;

    // A folder a provider draws is not a grid, and drawing it as one showed an empty deck: the grid
    // is simply the only view this client knew how to render.
    if (folder && FolderView.isProviderView(folder.viewId)) {
      this.showFolderView(folder.id);
      return;
    }
    this.hideFolderView();

    // Resolved rather than read off the folder: a folder usually states none of this, and its profile
    // is where the deck's shape actually lives.
    const grid = this.client.gridFor(folder);
    // No outer margin. The grid already reserves a border of its own - `computeCellDimensions`
    // gives it a padding equal to the gap between widgets, so the deck is framed as evenly as it is
    // spaced - and the default 16px sat on top of that as a second, unscaled border. A client that
    // owns the whole display wants the first one only.
    const geometry = { cols: grid.cols, rows: grid.rows, spacing: grid.spacing, outerMargin: 0 };
    if (!this.grid) {
      this.grid = renderWidgetGrid(this.deckHost, {
        host: this.host,
        onWidgetEvent: (widgetId, node, name, data) =>
          this.client.widgetSessions.sendEvent(widgetId, node.id, name, data),
        onWidgetTrigger: (widgetId, triggerType) =>
          void this.client.executeTrigger(widgetId, triggerType),
        geometry,
        borderRadius: grid.borderRadius,
      });
    }

    // Re-applied on every paint, not only at creation: a folder carries its own grid size, corner
    // radius and background, so walking into one keeps the previous folder's otherwise.
    this.grid.configure(geometry, grid.borderRadius);
    // Never on the grid as well: a translucent colour would stack there.
    this.root.style.background = folder && folder.background ? folder.background : '';
    this.grid.setFocusedWidget(this.deckInput.focusedWidgetId());

    this.grid.update(
      this.client.deck.displayedWidgets,
      widgetId => this.client.widgetSessions.treeFor(widgetId));
  }

  private paintWidgetTile(widgetId: string): boolean {
    if (!this.grid) return false;
    return this.grid.updateWidget(widgetId);
  }

  private showFolderView(folderId: string): void {
    this.root.style.background = '';
    if (this.grid) {
      this.grid.destroy();
      this.grid = null;
    }

    if (this.folderView === null) {
      this.folderView = new FolderView({
        connection: this.client.connection,
        sessions: this.client.sessions,
        localization: this.client.localization,
        host: this.host,
        canGoBack: () => this.client.deck.canGoBack,
        onBack: () => this.client.deck.back(),
      });
      (this.deckHost as HTMLElement).appendChild(this.folderView.element);
    }

    this.folderView.open(folderId);
    this.folderView.paint();
  }

  private hideFolderView(): void {
    if (this.folderView === null) return;
    this.folderView.destroy();
    this.folderView = null;
  }

}

function panel(title: string, detail: string): HTMLElement {
  const element = document.createElement('div');
  element.className = 'wc-panel';

  const logo = document.createElement('img');
  logo.className = 'wc-panel-logo';
  logo.src = './logo.png';
  logo.alt = '';
  element.appendChild(logo);

  const heading = document.createElement('h1');
  heading.className = 'wc-panel-title';
  heading.textContent = title;
  element.appendChild(heading);

  const body = document.createElement('p');
  body.className = 'wc-panel-detail';
  body.textContent = detail;
  element.appendChild(body);

  return element;
}
