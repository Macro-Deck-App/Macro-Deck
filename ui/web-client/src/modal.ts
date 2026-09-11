import {
  nodeString,
  renderUiNode,
  resolveLocalizedText,
  UiComponentEvents,
  UiComponentProperties,
  type UiModalOpenedEvent,
  type UiNode,
  type UiNodeRenderHandle,
  type UiRenderHost,
} from '@macro-deck/runtime';
import { Client } from './client';

const COMPLETE_EVENT = 'modal.complete';

export class ModalHost {
  private readonly backdrop: HTMLElement;
  private readonly heading: HTMLElement;
  private readonly content: HTMLElement;

  private modalId: string | null = null;
  private sessionId: string | null = null;

  isOpen(): boolean {
    return this.sessionId !== null;
  }

  private mounted: UiNodeRenderHandle | null = null;
  private mountedType: string | null = null;
  private observer: ResizeObserver | null = null;
  private box: { width: number | null; height: number | null } = { width: null, height: null };
  private basis = 0;

  constructor(
    private readonly root: HTMLElement,
    private readonly client: Client,
    private readonly host: UiRenderHost,
  ) {
    this.backdrop = document.createElement('div');
    this.backdrop.className = 'wc-modal-backdrop';
    this.backdrop.setAttribute('hidden', '');

    const dialog = document.createElement('div');
    dialog.className = 'wc-modal';
    this.backdrop.appendChild(dialog);

    const header = document.createElement('div');
    header.className = 'wc-modal-header';
    dialog.appendChild(header);

    this.heading = document.createElement('h2');
    this.heading.className = 'wc-modal-title';
    header.appendChild(this.heading);

    const close = document.createElement('button');
    close.className = 'wc-modal-close';
    close.type = 'button';
    close.textContent = '×';
    close.addEventListener('click', () => this.cancel());
    header.appendChild(close);

    this.content = document.createElement('div');
    this.content.className = 'wc-modal-content';
    dialog.appendChild(this.content);

    this.backdrop.addEventListener('pointerdown', event => {
      if (event.target === this.backdrop) this.cancel();
    });
    document.addEventListener('keydown', event => {
      if (this.modalId !== null && (event as KeyboardEvent).key === 'Escape') this.cancel();
    });

    this.root.appendChild(this.backdrop);

    this.client.modal.subscribe(event => this.onModal(event));
    this.client.sessions.onChange(sessionId => {
      if (sessionId === null || sessionId === this.sessionId) this.paint();
    });
  }

  private onModal(event: UiModalOpenedEvent | null): void {
    if (event === null) {
      this.close();
      return;
    }

    // One modal at a time: a previous one is cancelled rather than stacked, so its action settles
    // instead of waiting on a dialog the user can no longer see.
    if (this.modalId !== null && this.modalId !== event.modalId) {
      this.client.completeModal(this.modalId, true);
    }

    this.modalId = event.modalId;
    this.heading.textContent = event.title === undefined
      ? ''
      : resolveLocalizedText(event.title, this.host.localization) ?? '';
    this.backdrop.removeAttribute('hidden');
    this.observe();
    void this.openSession(event.modalId);
  }

  private async openSession(modalId: string): Promise<void> {
    try {
      const opened = await this.client.connection.request<{ accepted?: boolean; sessionId?: string }>(
        'OpenModalUiSession', [{ modalId }]);
      if (!opened || opened.accepted !== true || typeof opened.sessionId !== 'string') return;
      if (this.modalId !== modalId) return;

      // Opening a session does not subscribe to it: until it is attached the host pushes nothing,
      // which is a dialog that opens empty and stays empty.
      const attached = await this.client.connection.request<{ accepted?: boolean }>(
        'AttachUiSession', [{ sessionId: opened.sessionId }]);
      if (!attached || attached.accepted !== true || this.modalId !== modalId) return;

      this.sessionId = opened.sessionId;
      this.paint();
    } catch {
      // A refused session leaves the dialog empty; closing it still settles the waiting action.
    }
  }

  repaint(): void {
    this.paint();
  }

  private paint(): void {
    if (this.modalId === null || this.sessionId === null) return;

    const tree = this.client.sessions.tree(this.sessionId);
    if (tree === undefined) return;

    if (this.mounted === null || this.mountedType !== tree.type) {
      if (this.mounted) this.mounted.destroy();
      this.mounted = renderUiNode(this.content, tree, this.box, null, this.basis, this.treeHost());
      this.mountedType = tree.type;
      return;
    }
    this.mounted.update(tree, this.box, null, this.basis);
  }

  private treeHost(): UiRenderHost {
    const base = this.host;
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
      emit: (node: UiNode, name: string, data?: unknown) => this.onTreeEvent(node, name, data),
    };
  }

  private onTreeEvent(node: UiNode, name: string, data?: unknown): void {
    if (this.modalId === null) return;

    if (name === COMPLETE_EVENT) {
      this.client.completeModal(this.modalId, false, data);
      return;
    }

    // A button carrying an answer settles the dialog with it. The client is the side that knows the
    // press happened, has to take the dialog down and owns the principal the modal is bound to, so
    // it is the side that settles - a producer doing it would need to be told all three.
    const answer = name === UiComponentEvents.Press
      ? nodeString(node, UiComponentProperties.Answer)
      : undefined;
    if (answer !== undefined) {
      this.client.completeModal(this.modalId, false, answer);
      return;
    }

    if (this.sessionId === null) return;
    void this.client.connection.request('SendUiEvent', [{
      sessionId: this.sessionId,
      nodeId: node.id,
      name,
      data,
      revision: this.client.sessions.revision(this.sessionId),
    }]).catch(() => undefined);
  }

  private measure(width: number, height: number): void {
    this.basis = Math.min(width, height);
    this.box = { width: width || null, height: height || null };
  }

  private cancel(): void {
    if (this.modalId === null) return;
    this.client.completeModal(this.modalId, true);
  }

  private close(): void {
    if (this.sessionId !== null) {
      void this.client.connection.request('CloseUiSession', [this.sessionId]).catch(() => undefined);
      this.client.sessions.invalidated(this.sessionId);
    }
    this.modalId = null;
    this.sessionId = null;
    if (this.mounted) {
      this.mounted.destroy();
      this.mounted = null;
      this.mountedType = null;
    }
    if (this.observer !== null) {
      this.observer.disconnect();
      this.observer = null;
    }
    this.backdrop.setAttribute('hidden', '');
  }

  private observe(): void {
    // Measured straight away, not only when the observer first fires: the dialog has a size from its
    // own stylesheet the moment it is shown, and a tree that arrives before the first callback would
    // otherwise resolve every length against a basis of zero.
    this.measure(this.content.clientWidth, this.content.clientHeight);

    if (typeof ResizeObserver === 'undefined') return;

    if (this.observer !== null) this.observer.disconnect();
    this.observer = new ResizeObserver(entries => {
      const rect = entries[0] ? entries[0].contentRect : null;
      if (rect === null) return;
      this.measure(rect.width, rect.height);
      this.paint();
    });
    this.observer.observe(this.content);
  }
}
