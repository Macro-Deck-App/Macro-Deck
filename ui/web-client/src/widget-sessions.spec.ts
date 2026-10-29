import { GridWidget, UiSessionStore, type UiNode } from '@macro-deck/runtime';
import {
  type SessionTimers,
  WIDGET_SESSION_CLOSE_GRACE_MS,
  WIDGET_SESSION_MEMO_CAPACITY,
  WidgetSessions,
} from './widget-sessions';

const widget = (id: string): GridWidget =>
  ({ id, folderId: 'f', x: 0, y: 0, w: 1, h: 1, type: 'action-button', data: {} }) as GridWidget;

const tree = (text: string): UiNode =>
  ({ id: 'root', type: 'ui.text', properties: { text } }) as UiNode;

class FakeConnection {
  readonly calls: Array<{ type: string; payload: unknown }> = [];
  accept = true;
  refusalCode: string | undefined = undefined;
  attach = true;
  holdAttach = false;
  private heldAttaches: Array<{ resolve: (value: unknown) => void; reject: (reason: unknown) => void }> = [];
  private next = 0;

  request<T>(type: string, payload?: unknown): Promise<T> {
    this.calls.push({ type, payload });
    if (type === 'OpenWidgetUiSession') {
      return Promise.resolve({
        accepted: this.accept,
        sessionId: `s${++this.next}`,
        code: this.accept ? undefined : this.refusalCode,
      } as unknown as T);
    }
    if (type === 'AttachUiSession') {
      if (this.holdAttach) {
        return new Promise<T>((resolve, reject) => this.heldAttaches.push({ resolve: resolve as (value: unknown) => void, reject }));
      }
      return Promise.resolve({ accepted: this.attach } as unknown as T);
    }
    return Promise.resolve(undefined as T);
  }

  answerHeldAttach(accepted: boolean): void {
    this.heldAttaches.shift()!.resolve({ accepted });
  }

  dropHeldAttach(): void {
    this.heldAttaches.shift()!.reject(new Error('connection closed'));
  }

  opens(): number {
    return this.calls.filter(call => call.type === 'OpenWidgetUiSession').length;
  }
}

class FakeClock implements SessionTimers {
  private now = 0;
  private next = 0;
  private readonly due = new Map<number, { at: number; callback: () => void }>();

  set(callback: () => void, delayMs: number): unknown {
    const handle = ++this.next;
    this.due.set(handle, { at: this.now + delayMs, callback });
    return handle;
  }

  clear(handle: unknown): void {
    this.due.delete(handle as number);
  }

  advance(ms: number): void {
    this.now += ms;
    for (const [handle, timer] of [...this.due]) {
      if (timer.at > this.now) continue;
      this.due.delete(handle);
      timer.callback();
    }
  }
}

describe('WidgetSessions', () => {
  let connection: FakeConnection;
  let store: UiSessionStore;
  let sessions: WidgetSessions;
  let clock: FakeClock;

  beforeEach(() => {
    connection = new FakeConnection();
    store = new UiSessionStore();
    clock = new FakeClock();
    sessions = new WidgetSessions(connection, store, clock);
  });

  const closes = () => connection.calls.filter(call => call.type === 'CloseUiSession');

  const settle = async () => { for (let turn = 0; turn < 6; turn++) await Promise.resolve(); };

  it('sends the arguments positionally, the way the host reads them', async () => {
    sessions.sync([widget('w1')]);
    await settle();

    // A bare object is accepted by the transport and ignored by the host: no answer, empty tile.
    expect(Array.isArray(connection.calls[0].payload)).toBeTrue();
  });

  it('opens a session for every widget on the deck', async () => {
    sessions.sync([widget('w1'), widget('w2')]);
    await settle();

    const opened = connection.calls.filter(call => call.type === 'OpenWidgetUiSession');
    expect(opened.length).toBe(2);
    expect(opened.map(call => (call.payload as Array<{ widgetId: string }>)[0].widgetId))
      .toEqual(['w1', 'w2']);
  });

  it('hands the grid the tree that arrived for that widget', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Lights'));

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');
  });

  it('keeps drawing what a widget last drew across a reconnect', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Lights'));
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');

    // What a dropped connection does: the host keeps no sessions, so neither does this.
    store.clear();
    sessions.reset();

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');
  });

  it('replaces it as soon as the reopened session answers', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Lights'));
    store.clear();
    sessions.reset();

    sessions.sync([widget('w1')]);
    await settle();
    // The reopened session is a new one: the host mints an id per open, it does not resume the old.
    store.treeUpdated('s2', 1, tree('Lights on'));

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights on');
  });

  it('keeps what a widget drew after it leaves the deck, and paints it at once on return', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Lights'));
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');

    sessions.sync([]);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');

    sessions.sync([widget('w1')]);
    await settle();
    // Before the reopened session has answered: still the memo.
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');

    // The load-bearing half - the existing `:92` contract (a reopened session replaces the memo)
    // restated across a folder switch rather than a reconnect.
    store.treeUpdated('s2', 1, tree('Lights on'));
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights on');
  });

  it('paints on return the tree that arrived before the widget left, drawn or not', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Off'));
    store.treeUpdated('s1', 2, tree('On'));

    sessions.sync([]);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    sessions.sync([widget('w1')]);

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('On');
  });

  it('keeps the tree that arrives just after its widget left, as a press that switches folders sends it', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Off'));

    sessions.sync([]);
    store.treeUpdated('s1', 2, tree('On'));
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();
    sessions.sync([widget('w1')]);

    expect(closes().length).toBe(1);
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('On');
  });

  it('keeps the session of a widget that comes back before its close was due', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Lights'));

    sessions.sync([]);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS / 2);
    sessions.sync([widget('w1')]);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();

    expect(closes().length).toBe(0);
    expect(connection.opens()).toBe(1);
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');
  });

  it('paints nothing outdated for a widget whose state moved while it was hidden', async () => {
    const changed: string[] = [];
    sessions.onWidgetChanged(widgetId => changed.push(widgetId));
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Off'));
    sessions.sync([]);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();

    sessions.invalidate('w1');
    sessions.sync([widget('w1')]);
    await settle();

    expect(changed).toEqual(['w1']);
    expect(sessions.treeFor('w1')).toBeUndefined();
    store.treeUpdated('s2', 1, tree('On'));
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('On');
  });

  it('keeps painting at once on return a widget whose state moved while it was shown', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Off'));
    store.treeUpdated('s1', 2, tree('On'));
    sessions.invalidate('w1');

    sessions.sync([]);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();
    sessions.sync([widget('w1')]);

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('On');
  });

  it('keeps the tree that follows a state change of a widget still closing', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Off'));
    sessions.sync([]);

    sessions.invalidate('w1');
    store.treeUpdated('s1', 2, tree('On'));
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();
    sessions.sync([widget('w1')]);

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('On');
  });

  it('paints nothing outdated when the change of a widget still closing never arrived before it closed', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Off'));
    sessions.sync([]);

    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS - 10);
    sessions.invalidate('w1');
    clock.advance(10);
    await settle();
    sessions.sync([widget('w1')]);

    expect(closes().length).toBe(1);
    expect(sessions.treeFor('w1')).toBeUndefined();
  });

  it('closes a hidden widget on time however often its state moves', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('0'));
    sessions.sync([]);

    for (let step = 1; step <= 4; step++) {
      clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS / 4);
      sessions.invalidate('w1');
    }
    await settle();

    expect(closes().length).toBe(1);
  });

  it('sends no close for a session the host already ended while its widget was hidden', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Lights'));
    sessions.sync([]);

    sessions.sessionClosed('s1');
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();

    expect(closes().length).toBe(0);
  });

  it('forgets after a reconnect what hidden widgets drew, keeping what the deck shows', async () => {
    sessions.sync([widget('w1'), widget('w2')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Shown'));
    store.treeUpdated('s2', 1, tree('Hidden'));
    sessions.sync([widget('w1')]);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();
    store.clear();
    sessions.reset();

    sessions.forgetHidden([widget('w1')]);

    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Shown');
    expect(sessions.treeFor('w2')).toBeUndefined();
  });

  it('has no tree for a widget whose session has not answered yet', async () => {
    sessions.sync([widget('w1')]);

    expect(sessions.treeFor('w1')).toBeUndefined();
  });

  it('does not open a second session for a widget it already has one for', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    sessions.sync([widget('w1')]);
    await settle();

    expect(connection.calls.filter(c => c.type === 'OpenWidgetUiSession').length).toBe(1);
  });

  it('does not open twice while the first request is still in flight', async () => {
    sessions.sync([widget('w1')]);
    sessions.sync([widget('w1')]);
    await settle();

    expect(connection.calls.filter(c => c.type === 'OpenWidgetUiSession').length).toBe(1);
  });

  it('closes the session of a widget that has left the deck', async () => {
    sessions.sync([widget('w1'), widget('w2')]);
    await settle();
    store.treeUpdated('s2', 1, tree('Lights'));
    sessions.treeFor('w2');

    sessions.sync([widget('w1')]);
    expect(closes().length).toBe(0);
    clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
    await settle();

    const closed = closes();
    expect(closed.length).toBe(1);
    expect(closed[0].payload).toEqual(['s2']);
    // The session closes, but what it last drew stays in the memo (issue #856) - see the folder
    // switch test above.
    expect(sessions.treeFor('w2')).toBeDefined();
  });

  it('attaches the session it opened, because opening alone subscribes to nothing', async () => {
    sessions.sync([widget('w1')]);
    await settle();

    const attached = connection.calls.filter(call => call.type === 'AttachUiSession');
    expect(attached.length).toBe(1);
    expect((attached[0].payload as Array<{ sessionId: string }>)[0].sessionId).toBe('s1');
  });

  it('opens again when the attach is refused, because the session ended before it could attach', async () => {
    connection.attach = false;
    sessions.sync([widget('w1')]);
    for (let round = 0; round < 4; round++) await settle();

    expect(connection.opens()).toBe(4);
    expect(sessions.treeFor('w1')).toBeUndefined();
    for (let index = 1; index <= 4; index++) expect(sessions.widgetFor(`s${index}`)).toBeUndefined();

    connection.attach = true;
    sessions.sync([widget('w1')]);
    await settle();
    expect(connection.opens()).toBe(5);
    expect(sessions.widgetFor('s5')).toBe('w1');
  });

  it('opens again when the session ends between the open answer and the attach answer', async () => {
    connection.holdAttach = true;
    sessions.sync([widget('w1')]);
    await settle();

    expect(sessions.widgetFor('s1')).toBe('w1');
    store.invalidated('s1');
    sessions.sessionClosed('s1');
    sessions.sync([widget('w1')]);
    connection.holdAttach = false;
    connection.answerHeldAttach(true);
    await settle();

    expect(connection.opens()).toBe(2);
    expect(sessions.widgetFor('s1')).toBeUndefined();
    expect(sessions.widgetFor('s2')).toBe('w1');
    store.treeUpdated('s2', 1, tree('Lights'));
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');
    sessions.sendEvent('w1', 'root', 'press');
    const sent = connection.calls.filter(call => call.type === 'SendUiEvent');
    expect((sent[0].payload as Array<{ sessionId: string }>)[0].sessionId).toBe('s2');
  });

  it('counts the retries afresh once a session delivered a tree', async () => {
    connection.holdAttach = true;
    sessions.sync([widget('w1')]);
    for (let refusal = 0; refusal < 3; refusal++) {
      await settle();
      connection.answerHeldAttach(false);
    }
    await settle();
    connection.answerHeldAttach(true);
    await settle();
    expect(connection.opens()).toBe(4);
    store.treeUpdated('s4', 1, tree('Lights'));

    store.invalidated('s4');
    sessions.sessionClosed('s4');
    sessions.sync([widget('w1')]);
    await settle();
    connection.answerHeldAttach(false);
    await settle();

    expect(connection.opens()).toBe(6);
  });

  it('does not reopen a widget that left the deck during the handshake', async () => {
    connection.holdAttach = true;
    sessions.sync([widget('w1')]);
    await settle();

    sessions.sync([]);
    connection.answerHeldAttach(true);
    await settle();

    expect(connection.opens()).toBe(1);
    expect(sessions.widgetFor('s1')).toBeUndefined();
    expect(connection.calls.filter(call => call.type === 'CloseUiSession').length).toBe(1);
  });

  it('does not reopen on its own when the connection drops during the handshake', async () => {
    connection.holdAttach = true;
    sessions.sync([widget('w1')]);
    await settle();

    sessions.reset();
    connection.dropHeldAttach();
    await settle();

    expect(connection.opens()).toBe(1);
    expect(sessions.widgetFor('s1')).toBeUndefined();
  });

  it('leaves the tile empty when the host refuses the session', async () => {
    connection.accept = false;
    sessions.sync([widget('w1')]);
    await settle();

    expect(sessions.treeFor('w1')).toBeUndefined();
  });

  it('asks again on the next deck change after a refusal', async () => {
    connection.accept = false;
    sessions.sync([widget('w1')]);
    await settle();

    connection.accept = true;
    sessions.sync([widget('w1')]);
    await settle();

    expect(connection.calls.filter(c => c.type === 'OpenWidgetUiSession').length).toBe(2);
    store.treeUpdated('s2', 1, tree('Lights'));
    expect(sessions.treeFor('w1')).toBeDefined();
  });

  describe('when the provider has no session slot free', () => {
    beforeEach(() => {
      connection.accept = false;
      connection.refusalCode = 'TOO_MANY_SESSIONS';
    });

    it('asks again after a backoff and shows the tile once a slot frees up', async () => {
      sessions.sync([widget('w1')]);
      await settle();
      clock.advance(999);
      await settle();
      expect(connection.opens()).toBe(1);

      connection.accept = true;
      clock.advance(1);
      await settle();

      expect(connection.opens()).toBe(2);
      store.treeUpdated('s2', 1, tree('Lights'));
      expect(sessions.treeFor('w1')).toBeDefined();
    });

    it('waits longer before each further attempt and gives up after the last one', async () => {
      sessions.sync([widget('w1')]);
      await settle();
      for (const delay of [1000, 2000, 4000, 8000, 16000, 16000]) {
        clock.advance(delay - 1);
        await settle();
        const before = connection.opens();
        clock.advance(1);
        await settle();
        expect(connection.opens()).toBe(before + 1);
      }

      clock.advance(60000);
      await settle();
      expect(connection.opens()).toBe(7);
    });

    it('neither opens early nor twice when the deck changes while it waits', async () => {
      sessions.sync([widget('w1')]);
      await settle();
      sessions.sync([widget('w1')]);
      await settle();
      expect(connection.opens()).toBe(1);

      clock.advance(1000);
      await settle();
      expect(connection.opens()).toBe(2);
    });

    it('stops asking once the widget left the deck', async () => {
      sessions.sync([widget('w1')]);
      await settle();
      sessions.sync([]);
      clock.advance(60000);
      await settle();

      expect(connection.opens()).toBe(1);
    });

    it('stops asking when the connection drops', async () => {
      sessions.sync([widget('w1')]);
      await settle();
      sessions.reset();
      clock.advance(60000);
      await settle();

      expect(connection.opens()).toBe(1);
    });

    it('does not ask again on its own for any other refusal', async () => {
      connection.refusalCode = 'PROVIDER_UNAVAILABLE';
      sessions.sync([widget('w1')]);
      await settle();
      clock.advance(60000);
      await settle();

      expect(connection.opens()).toBe(1);
    });
  });

  it('forgets everything when the connection drops, because the host does too', async () => {
    sessions.sync([widget('w1')]);
    await settle();

    sessions.reset();

    expect(sessions.treeFor('w1')).toBeUndefined();
    // And asks for a fresh one rather than reusing an id the host has dropped.
    sessions.sync([widget('w1')]);
    await settle();
    expect(connection.calls.filter(c => c.type === 'OpenWidgetUiSession').length).toBe(2);
  });

  it('re-opens a session the host closed under it', async () => {
    sessions.sync([widget('w1')]);
    await settle();

    sessions.sessionClosed('s1');
    sessions.sync([widget('w1')]);
    await settle();

    expect(connection.calls.filter(c => c.type === 'OpenWidgetUiSession').length).toBe(2);
  });

  it('maps a session back to the widget it belongs to', async () => {
    sessions.sync([widget('w1')]);
    await settle();

    expect(sessions.widgetFor('s1')).toBe('w1');
    expect(sessions.widgetFor('unknown')).toBeUndefined();
  });

  describe('the memo bound', () => {
    const fillToCapacity = async () => {
      for (let index = 0; index <= WIDGET_SESSION_MEMO_CAPACITY; index++) {
        sessions.sync([widget(`w${index}`)]);
        await settle();
        store.treeUpdated(`s${index + 1}`, 1, tree(`text-${index}`));
        clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
      }
      sessions.sync([]);
      clock.advance(WIDGET_SESSION_CLOSE_GRACE_MS);
      await settle();
    };

    it('evicts the widget whose tree arrived longest ago once the bound is passed', async () => {
      await fillToCapacity();

      expect(sessions.treeFor('w0')).toBeUndefined();
      for (let index = 1; index <= WIDGET_SESSION_MEMO_CAPACITY; index++) {
        expect(sessions.treeFor(`w${index}`)).withContext(`w${index}`).toBeDefined();
      }
    });

    it('answers correctly for a widget revisited after eviction, just not instantly', async () => {
      await fillToCapacity();
      expect(sessions.treeFor('w0')).toBeUndefined();

      sessions.sync([widget('w0')]);
      await settle();
      expect(sessions.treeFor('w0')).toBeUndefined();

      // The (capacity + 1)th session already opened by fillToCapacity, plus this reopened one.
      store.treeUpdated(`s${WIDGET_SESSION_MEMO_CAPACITY + 2}`, 1, tree('fresh'));

      expect(sessions.treeFor('w0')!.properties!['text']).toBe('fresh');
    });
  });

  it('forgetAll() clears what every widget drew, as signing out does', async () => {
    sessions.sync([widget('w1')]);
    await settle();
    store.treeUpdated('s1', 1, tree('Lights'));
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');

    // Drop the session too, as a real sign-out does (client.ts's endSession disconnects) - the
    // memo is what treeFor falls back to once it does, and that is what forgetAll has to clear.
    store.clear();
    sessions.reset();
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');

    sessions.forgetAll();

    expect(sessions.treeFor('w1')).toBeUndefined();
  });
});
