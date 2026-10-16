import {
  applyNotificationDraftEvent,
  ClientAppStrings,
  createNotificationDraft,
  overlayNotificationDraft,
  renderUiNode,
  resolveLocalizedText,
  type ActiveNotification,
  type NotificationDraft,
  type UiComponentBox,
  type UiNode,
  type UiNodeRenderHandle,
  type UiRenderHost,
} from '@macro-deck/runtime';
import type { NotificationFeed } from './notification-feed';
import { sourceOf, translateKey } from './notification-text';
import { createButton, createProgressBar, severityIconPaths, type ButtonHandle, type ButtonVariant } from './ui';
import { dismissButton, element, strokeIcon } from './ui/dom';

function variantOf(style: string): ButtonVariant {
  if (style === 'primary') return 'primary';
  if (style === 'danger') return 'danger';
  return 'secondary';
}

function has(draft: NotificationDraft, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(draft, key);
}

export class NotificationDialogs {
  private readonly backdrop: HTMLElement;
  private readonly dialog: HTMLElement;
  private readonly icon: HTMLElement;
  private readonly heading: HTMLElement;
  private readonly source: HTMLElement;
  private readonly message: HTMLElement;
  private readonly content: HTMLElement;
  private readonly progress: HTMLElement;
  private readonly problem: HTMLElement;
  private readonly actions: HTMLElement;
  private readonly closeButton: HTMLButtonElement;

  private currentId: string | null = null;
  private revision = -1;
  private draft: NotificationDraft = {};
  private busy = false;
  private actionSignature = '';
  private buttons: ButtonHandle[] = [];
  private mounted: UiNodeRenderHandle | null = null;
  private mountedType: string | null = null;
  private box: UiComponentBox = { width: null, height: null };
  private basis = 0;
  private observer: ResizeObserver | null = null;
  private previousFocus: Element | null = null;

  constructor(
    root: HTMLElement,
    private readonly feed: NotificationFeed,
    private readonly host: UiRenderHost,
  ) {
    this.backdrop = element('div', 'wc-notification-backdrop');
    this.backdrop.setAttribute('hidden', '');

    this.dialog = element('div', 'wc-notification-dialog');
    this.dialog.setAttribute('role', 'dialog');
    this.dialog.setAttribute('aria-modal', 'true');
    this.dialog.setAttribute('tabindex', '-1');
    this.backdrop.appendChild(this.dialog);

    const header = element('div', 'wc-notification-header');
    this.dialog.appendChild(header);

    this.icon = element('span', 'wc-notification-icon');
    header.appendChild(this.icon);

    const titles = element('div', 'wc-notification-titles');
    this.source = element('span', 'wc-notification-source');
    titles.appendChild(this.source);
    this.heading = element('h2', 'wc-notification-title');
    this.heading.id = 'wc-notification-title';
    this.dialog.setAttribute('aria-labelledby', this.heading.id);
    titles.appendChild(this.heading);
    header.appendChild(titles);

    this.closeButton = dismissButton('wc-notification-close', 'wc-notification-close-icon', '');
    this.closeButton.addEventListener('click', () => this.press(null));
    header.appendChild(this.closeButton);

    const body = element('div', 'wc-notification-body');
    this.dialog.appendChild(body);

    this.message = element('p', 'wc-notification-message');
    body.appendChild(this.message);

    this.content = element('div', 'wc-notification-content widget-dialog-content');
    body.appendChild(this.content);

    this.progress = element('div', 'wc-notification-progress');
    body.appendChild(this.progress);

    this.problem = element('p', 'wc-notification-problem');
    this.problem.setAttribute('role', 'alert');
    this.problem.hidden = true;
    body.appendChild(this.problem);

    this.actions = element('div', 'wc-notification-actions');
    this.dialog.appendChild(this.actions);

    // Capture phase, ahead of the action dialog's own Escape handling: while a notification is up it
    // is the topmost thing, so it takes the key whether or not it can be dismissed.
    document.addEventListener('keydown', event => this.onKey(event), true);

    root.appendChild(this.backdrop);

    feed.store.subscribe(() => this.paint());
    this.paint();
  }

  isOpen(): boolean {
    return this.currentId !== null;
  }

  repaint(): void {
    const current = this.current();
    if (current === undefined) return;
    this.revision = -1;
    this.paint();
  }

  private current(): ActiveNotification | undefined {
    const dialogs = this.feed.store.list().filter(notification => notification.popup === 'dialog');
    return dialogs[0];
  }

  private paint(): void {
    const current = this.current();
    if (current === undefined) {
      this.hide();
      return;
    }

    if (current.id !== this.currentId) this.open(current);
    else if (current.revision === this.revision) return;
    else this.reseed(current);

    this.revision = current.revision;
    this.render(current);
  }

  private open(notification: ActiveNotification): void {
    this.reset();
    this.currentId = notification.id;
    this.draft = notification.content === undefined ? {} : createNotificationDraft(notification.content);
    this.previousFocus = document.activeElement;
    this.backdrop.removeAttribute('hidden');
    this.observe();
    this.dialog.focus();
  }

  private reseed(notification: ActiveNotification): void {
    if (notification.content === undefined) {
      this.draft = {};
      return;
    }
    const seeded = createNotificationDraft(notification.content);
    const merged: NotificationDraft = {};
    for (const key of Object.keys(seeded)) merged[key] = has(this.draft, key) ? this.draft[key] : seeded[key];
    this.draft = merged;
  }

  private hide(): void {
    if (this.currentId === null) return;
    const restore = this.previousFocus;
    this.reset();
    this.backdrop.setAttribute('hidden', '');
    if (restore !== null && typeof (restore as HTMLElement).focus === 'function' && document.contains(restore)) {
      (restore as HTMLElement).focus();
    }
  }

  private reset(): void {
    this.currentId = null;
    this.revision = -1;
    this.draft = {};
    this.busy = false;
    this.actionSignature = '';
    this.buttons = [];
    this.actions.textContent = '';
    this.showProblem('');
    this.previousFocus = null;
    this.unmount();
    this.content.textContent = '';
    if (this.observer !== null) {
      this.observer.disconnect();
      this.observer = null;
    }
  }

  private render(notification: ActiveNotification): void {
    const text = (value: ActiveNotification['title'] | undefined) => resolveLocalizedText(value, this.host.localization);

    this.dialog.className = 'wc-notification-dialog wc-notification-' + notification.severity;
    this.icon.textContent = '';
    this.icon.appendChild(strokeIcon('wc-notification-icon-svg', severityIconPaths(notification.severity)));

    const source = sourceOf(notification, this.host.localization);
    this.source.textContent = '';
    const kind = element('span', 'wc-notification-source-kind');
    kind.textContent = source.kind;
    this.source.appendChild(kind);
    if (source.name !== '') {
      const name = element('span', 'wc-notification-source-name');
      name.textContent = source.name;
      this.source.appendChild(name);
    }

    this.heading.textContent = text(notification.title);

    const message = notification.message === undefined ? '' : text(notification.message);
    this.message.textContent = message;
    this.message.hidden = message === '';

    this.paintProgress(notification, text(notification.title));
    this.paintClose(notification);
    this.paintActions(notification);
    this.paintContent(notification);
  }

  private paintProgress(notification: ActiveNotification, label: string): void {
    this.progress.textContent = '';
    const progress = notification.progress;
    this.progress.hidden = progress === undefined;
    if (progress === undefined) return;
    this.progress.appendChild(createProgressBar(
      { processed: progress.processed, total: progress.total === undefined ? null : progress.total },
      label));
  }

  private paintClose(notification: ActiveNotification): void {
    this.closeButton.hidden = !notification.dismissible;
    this.closeButton.setAttribute(
      'aria-label', translateKey(this.host.localization, ClientAppStrings.Feedback.Dismiss));
  }

  private paintActions(notification: ActiveNotification): void {
    const text = (value: ActiveNotification['title'] | undefined) => resolveLocalizedText(value, this.host.localization);
    const labels = notification.actions.map(action => ({
      id: action.id,
      label: text(action.label),
      style: action.style,
    }));

    if (labels.length === 0 && notification.dismissible) {
      labels.push({
        id: '',
        label: translateKey(this.host.localization, ClientAppStrings.Feedback.Notification.Ok),
        style: 'primary',
      });
    }

    const signature = JSON.stringify(labels);
    if (signature === this.actionSignature) return;
    this.actionSignature = signature;

    this.actions.textContent = '';
    this.buttons = [];
    this.actions.hidden = labels.length === 0;

    for (const action of labels) {
      const button = createButton({
        label: action.label,
        variant: variantOf(action.style),
        disabled: this.busy,
        onClick: () => this.press(action.id === '' ? null : action.id),
      });
      this.buttons.push(button);
      this.actions.appendChild(button.element);
    }
  }

  private paintContent(notification: ActiveNotification): void {
    const content = notification.content;
    this.content.hidden = content === undefined;
    if (content === undefined) {
      this.unmount();
      this.content.textContent = '';
      return;
    }

    const tree = overlayNotificationDraft(content, this.draft);
    if (this.mounted === null || this.mountedType !== tree.type) {
      this.unmount();
      this.content.textContent = '';
      this.mounted = renderUiNode(this.content, tree, this.box, null, this.basis, this.treeHost());
      this.mountedType = tree.type;
      return;
    }
    this.mounted.update(tree, this.box, null, this.basis);
  }

  private unmount(): void {
    if (this.mounted === null) return;
    this.mounted.destroy();
    this.mounted = null;
    this.mountedType = null;
  }

  private press(actionId: string | null): void {
    const current = this.current();
    if (current === undefined || current.id !== this.currentId || this.busy) return;

    this.busy = true;
    this.showProblem('');
    this.setButtonsDisabled(true);

    const values = actionId !== null && current.content !== undefined ? this.draft : undefined;
    void this.feed.respond(current.id, actionId, values).then(response => {
      if (this.currentId !== current.id) return;
      this.busy = false;
      this.setButtonsDisabled(false);
      if (response !== null && response.status === 'invalid') {
        this.showProblem(translateKey(this.host.localization, ClientAppStrings.Feedback.Notification.Rejected));
      }
    });
  }

  private showProblem(message: string): void {
    this.problem.textContent = message;
    this.problem.hidden = message === '';
  }

  private setButtonsDisabled(disabled: boolean): void {
    for (const button of this.buttons) button.setDisabled(disabled);
  }

  private onKey(event: KeyboardEvent): void {
    if (this.currentId === null || event.key !== 'Escape') return;
    event.stopImmediatePropagation();
    const current = this.current();
    if (current !== undefined && current.dismissible) this.press(null);
  }

  private onContentEvent(node: UiNode, name: string, data?: unknown): void {
    const current = this.current();
    if (current === undefined || current.content === undefined || current.id !== this.currentId) return;

    const next = applyNotificationDraftEvent(current.content, this.draft, { nodeId: node.id, name, data });
    if (next === this.draft) return;
    this.draft = next;
    this.showProblem('');
    this.paintContent(current);
  }

  private treeHost(): UiRenderHost {
    const base = this.host;
    return {
      localization: base.localization,
      resourceUrl: (resource, hint) => base.resourceUrl(resource, hint),
      now: () => base.now(),
      culture: () => base.culture(),
      hourCycle: () => base.hourCycle?.(),
      hourPadded: () => base.hourPadded?.(),
      simpleRendering: () => base.simpleRendering(),
      fontFamily: faceId => base.fontFamily(faceId),
      fontReady: faceId => base.fontReady(faceId),
      uiFontKey: () => base.uiFontKey?.() ?? '',
      emit: (node: UiNode, name: string, data?: unknown) => this.onContentEvent(node, name, data),
    };
  }

  private measure(width: number, height: number): void {
    this.basis = Math.min(width, height);
    this.box = { width: width || null, height: height || null };
  }

  private observe(): void {
    this.measure(this.content.clientWidth, this.content.clientHeight);
    if (typeof ResizeObserver === 'undefined') return;

    this.observer = new ResizeObserver(entries => {
      const rect = entries[0] ? entries[0].contentRect : null;
      if (rect === null) return;
      this.measure(rect.width, rect.height);
      const current = this.current();
      if (current !== undefined) this.paintContent(current);
    });
    this.observer.observe(this.content);
  }
}
