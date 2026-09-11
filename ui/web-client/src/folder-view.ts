import {
  ClientAppStrings,
  LocalizationCatalog,
  renderUiNode,
  UiConnection,
  UiSessionStore,
  WIDGET_GRID_VIEW_ID,
  type UiNode,
  type UiNodeRenderHandle,
  type UiRenderHost,
  type UiComponentBox,
} from '@macro-deck/runtime';

const NAVIGATION_HIDDEN = 'hidden';

interface OpenResponse {
  accepted?: boolean;
  sessionId?: string;
  viewId?: string;
  navigation?: string;
}

export interface FolderViewOptions {
  connection: UiConnection;
  sessions: UiSessionStore;
  localization: LocalizationCatalog;
  host: UiRenderHost;
  canGoBack(): boolean;
  onBack(): void;
}

// The back button is deliberately not the provider's to draw or wire: it always drives Macro Deck's
// own navigation, and a view asking to hide it still gets one whenever it is the only way out.
// That is what stops an incomplete or broken plugin view trapping someone inside it.
export class FolderView {
  readonly element: HTMLElement;

  private readonly content: HTMLElement;
  private readonly chrome: HTMLElement;
  private mounted: UiNodeRenderHandle | null = null;
  private sessionId: string | null = null;
  private openedFolderId: string | null = null;
  private viewId = '';
  private navigation = '';
  private rejected = false;
  private box: UiComponentBox = { width: null, height: null };
  private observer: ResizeObserver | null = null;

  constructor(private readonly options: FolderViewOptions) {
    this.element = document.createElement('div');
    this.element.className = 'wc-folder-view';

    this.chrome = document.createElement('div');
    this.chrome.className = 'wc-folder-view-chrome';
    this.element.appendChild(this.chrome);

    this.content = document.createElement('div');
    this.content.className = 'wc-folder-view-content';
    this.element.appendChild(this.content);

    if (typeof ResizeObserver === 'function') {
      this.observer = new ResizeObserver(() => this.measure());
      this.observer.observe(this.content);
    }
  }

  open(folderId: string): void {
    if (folderId === this.openedFolderId) return;
    this.close();
    this.openedFolderId = folderId;
    if (!folderId) return;

    void this.options.connection.request<OpenResponse>('OpenFolderUiSession', [{ folderId }]).then(
      response => {
        if (this.openedFolderId !== folderId) return;
        this.viewId = response && response.viewId ? response.viewId : '';
        this.navigation = response && response.navigation ? response.navigation : '';

        if (!response || response.accepted !== true || typeof response.sessionId !== 'string') {
          // Rejected, which is not the same as no tree yet: both draw nothing, and telling someone
          // their folder is broken while it is merely loading is worse than a moment of blank.
          this.rejected = true;
          this.paint();
          return;
        }

        this.sessionId = response.sessionId;
        void this.options.connection.request('AttachUiSession', [{ sessionId: response.sessionId }]);
        this.paint();
      },
      () => {
        if (this.openedFolderId !== folderId) return;
        this.rejected = true;
        this.paint();
      });

    this.paint();
  }

  paint(): void {
    this.paintChrome();

    if (this.rejected) {
      this.dropTree();
      this.paintPlaceholder();
      return;
    }

    const tree = this.sessionId === null ? undefined : this.options.sessions.tree(this.sessionId);
    if (tree === undefined) {
      this.dropTree();
      this.paintLoading();
      return;
    }

    this.paintTree(tree);
  }

  close(): void {
    this.dropTree();
    if (this.sessionId !== null) {
      void this.options.connection.request('CloseUiSession', [{ sessionId: this.sessionId }])
        .catch(() => undefined);
    }
    this.sessionId = null;
    this.openedFolderId = null;
    this.rejected = false;
    this.viewId = '';
    this.navigation = '';
  }

  destroy(): void {
    if (this.observer !== null) {
      this.observer.disconnect();
      this.observer = null;
    }
    this.close();
    if (this.element.parentNode) this.element.parentNode.removeChild(this.element);
  }

  static isProviderView(viewId: string | null | undefined): boolean {
    return !!viewId && viewId !== WIDGET_GRID_VIEW_ID;
  }

  private paintChrome(): void {
    // Whatever the provider asked for: a view that is unavailable, or that hid the button, still
    // needs one when it is the only way out.
    const show = this.options.canGoBack() && (this.rejected || this.navigation !== NAVIGATION_HIDDEN);
    this.chrome.textContent = '';
    if (!show) return;

    const label = this.options.localization.translate('macrodeck', 'Common.Back');
    const back = document.createElement('button');
    back.type = 'button';
    back.className = 'wc-folder-view-back';
    back.setAttribute('aria-label', label);
    back.textContent = label;
    back.onclick = () => this.options.onBack();
    this.chrome.appendChild(back);
  }

  private paintPlaceholder(): void {
    const t = (key: string, args?: Record<string, unknown>) => translate(this.options.localization, key, args);

    this.content.textContent = '';
    const placeholder = document.createElement('div');
    placeholder.className = 'wc-folder-view-placeholder';
    placeholder.setAttribute('role', 'status');

    const heading = document.createElement('h2');
    heading.textContent = t(ClientAppStrings.Deck.FolderView.UnavailableHeading);
    placeholder.appendChild(heading);

    const body = document.createElement('p');
    body.textContent = t(ClientAppStrings.Deck.FolderView.UnavailableBody, { viewId: this.viewId });
    placeholder.appendChild(body);

    // No recovery actions here on purpose: a deck client can neither manage integrations nor edit a
    // folder, and offering buttons that lead nowhere is worse than explaining and stopping.
    this.content.appendChild(placeholder);
  }

  private paintLoading(): void {
    this.content.textContent = '';
    const loading = document.createElement('p');
    loading.className = 'wc-folder-view-loading';
    loading.setAttribute('role', 'status');
    loading.textContent = translate(this.options.localization, ClientAppStrings.Deck.FolderView.Loading);
    this.content.appendChild(loading);
  }

  private paintTree(tree: UiNode): void {
    if (this.content.firstChild !== null && this.mounted === null) this.content.textContent = '';
    this.measure();

    const basis = Math.min(this.box.width || 0, this.box.height || 0) || 120;
    if (this.mounted === null) {
      this.mounted = renderUiNode(this.content, tree, this.box, null, basis, this.treeHost());
    } else {
      this.mounted.update(tree, this.box, null, basis);
    }
  }

  private dropTree(): void {
    if (this.mounted !== null) {
      this.mounted.destroy();
      this.mounted = null;
    }
    this.content.textContent = '';
  }

  private measure(): void {
    const width = this.content.clientWidth;
    const height = this.content.clientHeight;
    if (width === this.box.width && height === this.box.height) return;
    this.box = { width: width || null, height: height || null };
    if (this.mounted !== null) this.paint();
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

function translate(catalog: LocalizationCatalog, qualifiedKey: string, args?: Record<string, unknown>): string {
  const separator = qualifiedKey.indexOf(':');
  return catalog.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1), args);
}
