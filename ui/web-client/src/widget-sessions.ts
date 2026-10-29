import { GridWidget, UiSessionStore, type UiNode } from '@macro-deck/runtime';
import { MAX_BUSY_RETRIES, TOO_MANY_SESSIONS, busyRetryDelayMs } from './busy-retry';

interface SessionOpener {
  request<T>(type: string, payload?: unknown): Promise<T>;
}

export interface SessionTimers {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
}

const browserTimers: SessionTimers = {
  set: (callback, delayMs) => setTimeout(callback, delayMs),
  clear: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

function args(...values: unknown[]): unknown[] {
  return values;
}

interface OpenResponse {
  accepted?: boolean;
  sessionId?: string;
  code?: string;
}

export const WIDGET_SESSION_MEMO_CAPACITY = 120;
export const WIDGET_SESSION_CLOSE_GRACE_MS = 1000;
const MAX_REOPENS_WITHOUT_A_TREE = 3;

type HandshakeOutcome = 'live' | 'refused' | 'busy' | 'ended';

export class WidgetSessions {
  private readonly sessionByWidget: { [widgetId: string]: string } = {};
  private readonly widgetBySession: { [sessionId: string]: string } = {};
  private readonly opening: { [widgetId: string]: true } = {};
  private readonly wanted: { [widgetId: string]: true } = {};
  private readonly reopens: { [widgetId: string]: number } = {};
  private readonly closing: { [widgetId: string]: unknown } = {};
  private readonly retrying: { [widgetId: string]: unknown } = {};
  private readonly busyRetries: { [widgetId: string]: number } = {};
  private readonly outdated: { [widgetId: string]: true } = {};

  private readonly lastTree: { [widgetId: string]: UiNode } = {};
  private readonly memoOrder: string[] = [];
  private widgetListeners: Array<(widgetId: string) => void> = [];

  constructor(
    private readonly connection: SessionOpener,
    private readonly sessions: UiSessionStore,
    private readonly timers: SessionTimers = browserTimers,
  ) {
    this.sessions.onChange(sessionId => {
      if (sessionId === null || !this.sessions.has(sessionId)) return;
      const widgetId = this.widgetBySession[sessionId];
      if (widgetId === undefined) return;
      delete this.reopens[widgetId];
      const tree = this.sessions.tree(sessionId);
      if (tree !== undefined) this.remember(widgetId, tree);
    });
  }

  treeFor(widgetId: string): UiNode | undefined {
    return this.liveTree(widgetId) ?? this.lastTree[widgetId];
  }

  widgetFor(sessionId: string): string | undefined {
    return this.widgetBySession[sessionId];
  }

  isDisplayed(widgetId: string): boolean {
    return this.wanted[widgetId] === true;
  }

  onWidgetChanged(listener: (widgetId: string) => void): () => void {
    this.widgetListeners.push(listener);
    return () => {
      this.widgetListeners = this.widgetListeners.filter(candidate => candidate !== listener);
    };
  }

  sendEvent(widgetId: string, nodeId: string, name: string, data?: unknown): void {
    const sessionId = this.sessionByWidget[widgetId];
    if (sessionId === undefined) return;

    void this.connection.request('SendUiEvent', args({
      sessionId,
      nodeId,
      name,
      data,
      revision: this.sessions.revision(sessionId),
    })).catch(() => undefined);
  }

  sync(widgets: readonly GridWidget[]): void {
    for (const widgetId in this.wanted) {
      if (Object.prototype.hasOwnProperty.call(this.wanted, widgetId)) delete this.wanted[widgetId];
    }
    for (let index = 0; index < widgets.length; index++) this.wanted[widgets[index].id] = true;

    for (const widgetId in this.retrying) {
      if (Object.prototype.hasOwnProperty.call(this.retrying, widgetId) && !this.wanted[widgetId]) {
        this.cancelRetry(widgetId);
      }
    }

    for (const widgetId in this.sessionByWidget) {
      if (!Object.prototype.hasOwnProperty.call(this.sessionByWidget, widgetId)) continue;
      if (this.wanted[widgetId]) this.cancelClose(widgetId);
      else if (this.liveTree(widgetId) === undefined) this.close(widgetId);
      else if (!this.isClosing(widgetId)) this.scheduleClose(widgetId);
    }

    for (let index = 0; index < widgets.length; index++) {
      const widget = widgets[index];
      if (this.sessionByWidget[widget.id] || this.opening[widget.id] || this.isRetrying(widget.id)) continue;
      void this.open(widget);
    }
  }

  invalidate(widgetId: string): void {
    if (this.liveTree(widgetId) !== undefined) {
      if (this.isClosing(widgetId)) this.outdated[widgetId] = true;
      return;
    }
    if (!this.dropMemo(widgetId)) return;
    const notified = this.widgetListeners.slice();
    for (let index = 0; index < notified.length; index++) notified[index](widgetId);
  }

  forgetHidden(displayed: readonly GridWidget[]): void {
    const keep: { [widgetId: string]: true } = {};
    for (let index = 0; index < displayed.length; index++) keep[displayed[index].id] = true;
    const remembered = this.memoOrder.slice();
    for (let index = 0; index < remembered.length; index++) {
      if (!keep[remembered[index]]) this.dropMemo(remembered[index]);
    }
  }

  reset(): void {
    for (const widgetId in this.retrying) {
      if (Object.prototype.hasOwnProperty.call(this.retrying, widgetId)) this.cancelRetry(widgetId);
    }
    for (const widgetId in this.sessionByWidget) {
      if (Object.prototype.hasOwnProperty.call(this.sessionByWidget, widgetId)) {
        this.cancelClose(widgetId);
        delete this.sessionByWidget[widgetId];
        delete this.reopens[widgetId];
      }
    }
    for (const sessionId in this.widgetBySession) {
      if (Object.prototype.hasOwnProperty.call(this.widgetBySession, sessionId)) {
        delete this.widgetBySession[sessionId];
      }
    }
  }

  sessionClosed(sessionId: string): void {
    const widgetId = this.widgetBySession[sessionId];
    if (widgetId === undefined) return;
    const outdated = this.outdated[widgetId] === true;
    this.cancelClose(widgetId);
    delete this.widgetBySession[sessionId];
    delete this.sessionByWidget[widgetId];
    if (outdated) this.dropMemo(widgetId);
  }

  forgetAll(): void {
    for (const widgetId in this.lastTree) {
      if (Object.prototype.hasOwnProperty.call(this.lastTree, widgetId)) delete this.lastTree[widgetId];
    }
    this.memoOrder.length = 0;
  }

  private liveTree(widgetId: string): UiNode | undefined {
    const sessionId = this.sessionByWidget[widgetId];
    return sessionId === undefined ? undefined : this.sessions.tree(sessionId);
  }

  private async open(widget: GridWidget): Promise<void> {
    this.opening[widget.id] = true;
    let outcome: HandshakeOutcome = 'refused';
    try {
      while ((outcome = await this.handshake(widget)) === 'ended' && this.wanted[widget.id]) {
        const reopens = (this.reopens[widget.id] ?? 0) + 1;
        if (reopens > MAX_REOPENS_WITHOUT_A_TREE) return;
        this.reopens[widget.id] = reopens;
      }
    } finally {
      delete this.opening[widget.id];
    }
    if (outcome === 'busy') this.retryLater(widget);
    else delete this.busyRetries[widget.id];
  }

  private retryLater(widget: GridWidget): void {
    const attempt = this.busyRetries[widget.id] ?? 0;
    if (!this.wanted[widget.id] || attempt >= MAX_BUSY_RETRIES) {
      delete this.busyRetries[widget.id];
      return;
    }
    this.busyRetries[widget.id] = attempt + 1;
    this.retrying[widget.id] = this.timers.set(() => {
      delete this.retrying[widget.id];
      if (this.wanted[widget.id] && !this.sessionByWidget[widget.id] && !this.opening[widget.id]) {
        void this.open(widget);
      }
    }, busyRetryDelayMs(attempt));
  }

  private isRetrying(widgetId: string): boolean {
    return Object.prototype.hasOwnProperty.call(this.retrying, widgetId);
  }

  private cancelRetry(widgetId: string): void {
    delete this.busyRetries[widgetId];
    if (!this.isRetrying(widgetId)) return;
    this.timers.clear(this.retrying[widgetId]);
    delete this.retrying[widgetId];
  }

  // The host tells only attached connections when a session ends, so a session that dies between
  // the open and the attach shows up as a refused attach, or as ids that vanished under this handshake.
  private async handshake(widget: GridWidget): Promise<HandshakeOutcome> {
    let response: OpenResponse | undefined;
    try {
      response = await this.connection.request<OpenResponse>(
        'OpenWidgetUiSession', args({ widgetId: widget.id }));
    } catch {
      return 'refused';
    }
    if (!response || response.accepted !== true || typeof response.sessionId !== 'string') {
      return response?.code === TOO_MANY_SESSIONS ? 'busy' : 'refused';
    }

    const sessionId = response.sessionId;
    this.sessionByWidget[widget.id] = sessionId;
    this.widgetBySession[sessionId] = widget.id;
    if (!this.wanted[widget.id]) {
      this.close(widget.id);
      return 'refused';
    }

    // Opening a session does not subscribe to it. Until it is attached the host pushes nothing at
    // all, which is a widget that renders perfectly and shows nothing.
    let attached: OpenResponse | undefined;
    try {
      attached = await this.connection.request<OpenResponse>(
        'AttachUiSession', args({ sessionId }));
    } catch {
      this.forget(widget.id, sessionId);
      return 'refused';
    }

    if (this.widgetBySession[sessionId] !== widget.id) return 'ended';
    if (!attached || attached.accepted !== true) {
      this.forget(widget.id, sessionId);
      return 'ended';
    }
    return 'live';
  }

  private forget(widgetId: string, sessionId: string): void {
    if (this.widgetBySession[sessionId] === widgetId) delete this.widgetBySession[sessionId];
    if (this.sessionByWidget[widgetId] === sessionId) delete this.sessionByWidget[widgetId];
  }

  private remember(widgetId: string, tree: UiNode): void {
    delete this.outdated[widgetId];
    this.lastTree[widgetId] = tree;
    const at = this.memoOrder.indexOf(widgetId);
    if (at >= 0) this.memoOrder.splice(at, 1);
    this.memoOrder.push(widgetId);
    this.evictOverCapacity();
  }

  private dropMemo(widgetId: string): boolean {
    if (!Object.prototype.hasOwnProperty.call(this.lastTree, widgetId)) return false;
    delete this.lastTree[widgetId];
    const at = this.memoOrder.indexOf(widgetId);
    if (at >= 0) this.memoOrder.splice(at, 1);
    return true;
  }

  private evictOverCapacity(): void {
    while (this.memoOrder.length > WIDGET_SESSION_MEMO_CAPACITY) {
      let victim = -1;
      for (let index = 0; index < this.memoOrder.length; index++) {
        if (this.sessionByWidget[this.memoOrder[index]] === undefined) {
          victim = index;
          break;
        }
      }
      if (victim < 0) return;
      delete this.lastTree[this.memoOrder[victim]];
      this.memoOrder.splice(victim, 1);
    }
  }

  private isClosing(widgetId: string): boolean {
    return Object.prototype.hasOwnProperty.call(this.closing, widgetId);
  }

  // The host does not flush a session's pending patches before it closes it, so closing the moment
  // a widget leaves would lose a patch that races the folder switch.
  private scheduleClose(widgetId: string): void {
    this.closing[widgetId] = this.timers.set(() => {
      delete this.closing[widgetId];
      if (this.wanted[widgetId]) return;
      const outdated = this.outdated[widgetId] === true;
      this.close(widgetId);
      if (outdated) this.dropMemo(widgetId);
    }, WIDGET_SESSION_CLOSE_GRACE_MS);
  }

  private cancelClose(widgetId: string): void {
    delete this.outdated[widgetId];
    if (!this.isClosing(widgetId)) return;
    this.timers.clear(this.closing[widgetId]);
    delete this.closing[widgetId];
  }

  private close(widgetId: string): void {
    this.cancelClose(widgetId);
    this.cancelRetry(widgetId);
    const sessionId = this.sessionByWidget[widgetId];
    delete this.sessionByWidget[widgetId];
    delete this.reopens[widgetId];
    if (sessionId === undefined) return;
    delete this.widgetBySession[sessionId];
    this.sessions.invalidated(sessionId);
    void this.connection.request('CloseUiSession', args(sessionId)).catch(() => undefined);
  }
}
