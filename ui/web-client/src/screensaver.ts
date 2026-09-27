import {
  ClientAppStrings,
  LocalizationCatalog,
  nodeClaimsGesture,
  nodeClaimsValue,
  renderUiNode,
  UiConnection,
  UiSessionStore,
  type OpenScreenSaverUiSessionResponse,
  type UiComponentBox,
  type UiNode,
  type UiNodeRenderHandle,
  type UiRenderHost,
} from '@macro-deck/runtime';

const SWALLOWED_EVENTS = ['pointerdown', 'pointerup', 'click', 'touchstart', 'touchend'];

// How long after the dismissing input the overlay keeps covering the deck, transparent: a second tap
// or key that quickly is still part of waking the device up, not a press on whatever was underneath.
export const DISMISS_COOLDOWN_MS = 300;

export interface ScreenSaverOptions {
  connection: UiConnection;
  sessions: UiSessionStore;
  localization: LocalizationCatalog;
  host: UiRenderHost;
  onDismiss(): void;
}

// Listeners run in the capture phase: a press-claiming node stops the bubble phase before it would
// reach the overlay, and a screensaver that declared no interactivity must still be dismissed by it.
export class ScreenSaver {
  readonly element: HTMLElement;

  private mounted: UiNodeRenderHandle | null = null;
  private sessionId: string | null = null;
  private interactive = false;
  private showing = false;
  private reopened = false;
  private box: UiComponentBox = { width: null, height: null };
  private observer: ResizeObserver | null = null;
  private cooldown: ReturnType<typeof setTimeout> | null = null;
  private readonly unsubscribe: () => void;

  constructor(private readonly options: ScreenSaverOptions) {
    this.element = document.createElement('div');
    this.element.className = 'wc-screensaver';
    this.element.setAttribute('role', 'status');
    this.element.setAttribute('aria-label',
      translate(options.localization, ClientAppStrings.Deck.ScreenSaver.Announcement));
    this.element.hidden = true;

    for (let index = 0; index < SWALLOWED_EVENTS.length; index++) {
      this.element.addEventListener(SWALLOWED_EVENTS[index], this.onPointer, { capture: true });
    }

    if (typeof ResizeObserver === 'function') {
      this.observer = new ResizeObserver(() => this.measure());
      this.observer.observe(this.element);
    }

    this.unsubscribe = options.sessions.onChange(sessionId => this.onSessionChanged(sessionId));
  }

  isShowing(): boolean {
    return this.showing;
  }

  show(): void {
    if (this.showing) return;
    this.showing = true;
    this.reopened = false;
    this.endCooldown();
    this.element.hidden = false;
    document.addEventListener('keydown', this.onKey, true);
    this.open();
  }

  hide(): void {
    this.endCooldown();
    this.element.hidden = true;
    if (!this.showing) return;
    this.showing = false;
    this.close();
  }

  destroy(): void {
    this.unsubscribe();
    if (this.observer !== null) {
      this.observer.disconnect();
      this.observer = null;
    }
    this.hide();
    if (this.element.parentNode) this.element.parentNode.removeChild(this.element);
  }

  private open(): void {
    void this.options.connection.request<OpenScreenSaverUiSessionResponse>('OpenScreenSaverUiSession', [])
      .then(
        response => {
          if (!this.showing) return;
          if (!response || response.accepted !== true || typeof response.sessionId !== 'string') {
            this.giveBack();
            return;
          }
          this.sessionId = response.sessionId;
          this.interactive = response.interactive === true;
          void this.options.connection.request('AttachUiSession', [{ sessionId: response.sessionId }]);
          this.paint();
        },
        () => {
          if (this.showing) this.giveBack();
        });
  }

  private close(): void {
    this.dropTree();
    if (this.sessionId !== null) {
      void this.options.connection.request('CloseUiSession', [{ sessionId: this.sessionId }])
        .catch(() => undefined);
    }
    this.sessionId = null;
    this.interactive = false;
  }

  private dismiss(): void {
    this.showing = false;
    this.close();
    this.element.classList.add('wc-screensaver-deaf');
    this.cooldown = setTimeout(() => {
      this.endCooldown();
      this.element.hidden = true;
    }, DISMISS_COOLDOWN_MS);
    this.options.onDismiss();
  }

  // Nothing was touched, so nothing under the overlay needs shielding: the deck comes back at once.
  private giveBack(): void {
    this.hide();
    this.options.onDismiss();
  }

  private endCooldown(): void {
    if (this.cooldown !== null) {
      clearTimeout(this.cooldown);
      this.cooldown = null;
    }
    document.removeEventListener('keydown', this.onKey, true);
    this.element.classList.remove('wc-screensaver-deaf');
  }

  isDismissing(): boolean {
    return this.cooldown !== null;
  }

  private onSessionChanged(sessionId: string | null): void {
    if (!this.showing || this.sessionId === null) return;
    if (sessionId !== null && sessionId !== this.sessionId) return;

    if (this.options.sessions.has(this.sessionId)) {
      this.reopened = false;
      this.paint();
      return;
    }

    // Opened again at most once until a tree arrives: the host answers with the clock when the selection
    // is gone, and a second loss without a tree in between leaves only hiding to show something.
    this.dropTree();
    this.sessionId = null;
    if (this.reopened) {
      this.giveBack();
      return;
    }
    this.reopened = true;
    this.open();
  }

  private paint(): void {
    const tree = this.sessionId === null ? undefined : this.options.sessions.tree(this.sessionId);
    if (tree === undefined) {
      this.dropTree();
      return;
    }
    this.measure();
    const basis = Math.min(this.box.width || 0, this.box.height || 0) || 120;
    if (this.mounted === null) {
      this.mounted = renderUiNode(this.element, tree, this.box, null, basis, this.treeHost());
    } else {
      this.mounted.update(tree, this.box, null, basis);
    }
  }

  private dropTree(): void {
    if (this.mounted !== null) {
      this.mounted.destroy();
      this.mounted = null;
    }
    this.element.textContent = '';
  }

  private measure(): void {
    const width = this.element.clientWidth;
    const height = this.element.clientHeight;
    if (width === this.box.width && height === this.box.height) return;
    this.box = { width: width || null, height: height || null };
    if (this.mounted !== null) this.paint();
  }

  private readonly onPointer = (event: Event): void => {
    if (this.showing && this.interactive && this.targetsClaimingNode(event.target)) return;
    event.stopPropagation();
    event.preventDefault();
    if (this.showing && (event.type === 'pointerdown' || event.type === 'touchstart')) this.dismiss();
  };

  private readonly onKey = (event: Event): void => {
    if (!this.showing) {
      event.stopPropagation();
      return;
    }
    const key = (event as KeyboardEvent).key;
    if (this.interactive && key !== 'Escape') return;
    event.stopPropagation();
    this.dismiss();
  };

  private targetsClaimingNode(target: EventTarget | null): boolean {
    const tree = this.sessionId === null ? undefined : this.options.sessions.tree(this.sessionId);
    if (tree === undefined) return false;
    const byId: { [id: string]: UiNode } = {};
    collect(tree, byId);
    let current = target as Element | null;
    while (current !== null && current !== this.element) {
      const id = current.getAttribute ? current.getAttribute('data-node-id') : null;
      const node = id === null ? undefined : byId[id];
      if (node !== undefined && (nodeClaimsGesture(node) || nodeClaimsValue(node))) return true;
      current = current.parentNode as Element | null;
    }
    return false;
  }

  private treeHost(): UiRenderHost {
    const base = this.options.host;
    const sessionId = this.sessionId;
    const connection = this.options.connection;
    const sessions = this.options.sessions;

    return {
      localization: base.localization,
      resourceUrl: resource => base.resourceUrl(resource),
      now: () => base.now(),
      culture: () => base.culture(),
      hourCycle: () => base.hourCycle?.(),
      simpleRendering: () => base.simpleRendering(),
      fontFamily: faceId => base.fontFamily(faceId),
      fontReady: faceId => base.fontReady(faceId),
      uiFontKey: () => base.uiFontKey?.() ?? '',
      emit: (node, name, data) => {
        if (sessionId === null) return;
        void connection.request('SendUiEvent', [{
          sessionId,
          nodeId: node.id,
          name,
          data,
          revision: sessions.revision(sessionId),
        }]).catch(() => undefined);
      },
    };
  }
}

function collect(node: UiNode, into: { [id: string]: UiNode }): void {
  into[node.id] = node;
  const children = node.children ?? [];
  for (let index = 0; index < children.length; index++) collect(children[index], into);
  if (node.fallback) collect(node.fallback, into);
}

function translate(catalog: LocalizationCatalog, qualifiedKey: string): string {
  const separator = qualifiedKey.indexOf(':');
  return catalog.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1));
}
