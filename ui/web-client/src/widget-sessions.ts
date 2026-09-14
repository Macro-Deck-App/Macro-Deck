import { GridWidget, UiSessionStore, type UiNode } from '@macro-deck/runtime';

interface SessionOpener {
  request<T>(type: string, payload?: unknown): Promise<T>;
}

function args(...values: unknown[]): unknown[] {
  return values;
}

interface OpenResponse {
  accepted?: boolean;
  sessionId?: string;
}

export const WIDGET_SESSION_MEMO_CAPACITY = 120;
const MAX_REOPENS_WITHOUT_A_TREE = 3;

type HandshakeOutcome = 'live' | 'refused' | 'ended';

export class WidgetSessions {
  private readonly sessionByWidget: { [widgetId: string]: string } = {};
  private readonly widgetBySession: { [sessionId: string]: string } = {};
  private readonly opening: { [widgetId: string]: true } = {};
  private readonly wanted: { [widgetId: string]: true } = {};
  private readonly reopens: { [widgetId: string]: number } = {};

  private readonly lastTree: { [widgetId: string]: UiNode } = {};
  private readonly memoOrder: string[] = [];

  constructor(
    private readonly connection: SessionOpener,
    private readonly sessions: UiSessionStore,
  ) {
    this.sessions.onChange(sessionId => {
      if (sessionId === null || !this.sessions.has(sessionId)) return;
      const widgetId = this.widgetBySession[sessionId];
      if (widgetId !== undefined) delete this.reopens[widgetId];
    });
  }

  treeFor(widgetId: string): UiNode | undefined {
    const sessionId = this.sessionByWidget[widgetId];
    const live = sessionId === undefined ? undefined : this.sessions.tree(sessionId);
    if (live !== undefined) {
      this.remember(widgetId, live);
      return live;
    }
    return this.lastTree[widgetId];
  }

  widgetFor(sessionId: string): string | undefined {
    return this.widgetBySession[sessionId];
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

    for (const widgetId in this.sessionByWidget) {
      if (!Object.prototype.hasOwnProperty.call(this.sessionByWidget, widgetId)) continue;
      if (this.wanted[widgetId]) continue;
      this.close(widgetId);
    }

    for (let index = 0; index < widgets.length; index++) {
      const widget = widgets[index];
      if (this.sessionByWidget[widget.id] || this.opening[widget.id]) continue;
      void this.open(widget);
    }
  }

  reset(): void {
    for (const widgetId in this.sessionByWidget) {
      if (Object.prototype.hasOwnProperty.call(this.sessionByWidget, widgetId)) {
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
    delete this.widgetBySession[sessionId];
    delete this.sessionByWidget[widgetId];
  }

  forgetAll(): void {
    for (const widgetId in this.lastTree) {
      if (Object.prototype.hasOwnProperty.call(this.lastTree, widgetId)) delete this.lastTree[widgetId];
    }
    this.memoOrder.length = 0;
  }

  private async open(widget: GridWidget): Promise<void> {
    this.opening[widget.id] = true;
    try {
      while (await this.handshake(widget) === 'ended' && this.wanted[widget.id]) {
        const reopens = (this.reopens[widget.id] ?? 0) + 1;
        if (reopens > MAX_REOPENS_WITHOUT_A_TREE) return;
        this.reopens[widget.id] = reopens;
      }
    } finally {
      delete this.opening[widget.id];
    }
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
    if (!response || response.accepted !== true || typeof response.sessionId !== 'string') return 'refused';

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
    this.lastTree[widgetId] = tree;
    const at = this.memoOrder.indexOf(widgetId);
    if (at >= 0) this.memoOrder.splice(at, 1);
    this.memoOrder.push(widgetId);
    this.evictOverCapacity();
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

  private close(widgetId: string): void {
    const sessionId = this.sessionByWidget[widgetId];
    delete this.sessionByWidget[widgetId];
    delete this.reopens[widgetId];
    if (sessionId === undefined) return;
    delete this.widgetBySession[sessionId];
    this.sessions.invalidated(sessionId);
    void this.connection.request('CloseUiSession', args(sessionId)).catch(() => undefined);
  }
}
