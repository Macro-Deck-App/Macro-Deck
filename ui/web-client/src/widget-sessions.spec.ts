import { GridWidget, UiSessionStore, type UiNode } from '@macro-deck/runtime';
import { WIDGET_SESSION_MEMO_CAPACITY, WidgetSessions } from './widget-sessions';

const widget = (id: string): GridWidget =>
  ({ id, folderId: 'f', x: 0, y: 0, w: 1, h: 1, type: 'action-button', data: {} }) as GridWidget;

const tree = (text: string): UiNode =>
  ({ id: 'root', type: 'ui.text', properties: { text } }) as UiNode;

class FakeConnection {
  readonly calls: Array<{ type: string; payload: unknown }> = [];
  accept = true;
  attach = true;
  private next = 0;

  request<T>(type: string, payload?: unknown): Promise<T> {
    this.calls.push({ type, payload });
    if (type === 'OpenWidgetUiSession') {
      return Promise.resolve({ accepted: this.accept, sessionId: `s${++this.next}` } as unknown as T);
    }
    if (type === 'AttachUiSession') {
      return Promise.resolve({ accepted: this.attach } as unknown as T);
    }
    return Promise.resolve(undefined as T);
  }
}

describe('WidgetSessions', () => {
  let connection: FakeConnection;
  let store: UiSessionStore;
  let sessions: WidgetSessions;

  beforeEach(() => {
    connection = new FakeConnection();
    store = new UiSessionStore();
    sessions = new WidgetSessions(connection, store);
  });

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
    // The grid drawing it before the widget leaves the deck - what the memo remembers.
    expect(sessions.treeFor('w1')!.properties!['text']).toBe('Lights');

    sessions.sync([]);
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
    await settle();

    const closed = connection.calls.filter(call => call.type === 'CloseUiSession');
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

  it('keeps no session it could not attach to, so the next change asks again', async () => {
    connection.attach = false;
    sessions.sync([widget('w1')]);
    await settle();

    expect(sessions.treeFor('w1')).toBeUndefined();
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
        // The grid actually drawing it - what the memo remembers.
        sessions.treeFor(`w${index}`);
      }
      sessions.sync([]);
      await settle();
    };

    it('evicts the least recently drawn widget once the bound is passed', async () => {
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
