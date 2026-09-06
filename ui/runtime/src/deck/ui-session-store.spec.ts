import { UiNode } from '../ui-framework/ui-node.interface';
import { UiPatch } from '../ui-framework/ui-patch';
import { UiSessionStore } from './ui-session-store';

const tree = (text: string): UiNode =>
  ({ id: 'root', type: 'ui.text', properties: { text } }) as UiNode;

const setText = (from: number, to: number, text: string): UiPatch => ({
  fromRevision: from,
  toRevision: to,
  operations: [{ op: 'set-properties', nodeId: 'root', properties: { text } }],
});

describe('UiSessionStore', () => {
  let sessions: UiSessionStore;

  beforeEach(() => { sessions = new UiSessionStore(); });

  it('holds nothing until a tree arrives', () => {
    expect(sessions.has('s1')).toBeFalse();
    expect(sessions.tree('s1')).toBeUndefined();
  });

  it('takes a whole tree and the revision it stands at', () => {
    sessions.treeUpdated('s1', 4, tree('hello'));

    expect(sessions.revision('s1')).toBe(4);
    expect(sessions.tree('s1')!.properties!['text']).toBe('hello');
  });

  it('lets a later whole tree win outright', () => {
    sessions.treeUpdated('s1', 4, tree('first'));
    sessions.treeUpdated('s1', 9, tree('second'));

    expect(sessions.revision('s1')).toBe(9);
    expect(sessions.tree('s1')!.properties!['text']).toBe('second');
  });

  it('applies a patch that starts where the client stands', () => {
    sessions.treeUpdated('s1', 4, tree('before'));

    expect(sessions.patched('s1', setText(4, 5, 'after'))).toBeTrue();
    expect(sessions.tree('s1')!.properties!['text']).toBe('after');
    expect(sessions.revision('s1')).toBe(5);
  });

  it('drops the session when a patch starts from a revision it never reached', () => {
    sessions.treeUpdated('s1', 4, tree('before'));

    expect(sessions.patched('s1', setText(7, 8, 'after'))).toBeFalse();
    // Not left showing a stale tree: the caller has to ask for a fresh one.
    expect(sessions.has('s1')).toBeFalse();
  });

  it('drops the session rather than keeping a tree it could not advance', () => {
    sessions.treeUpdated('s1', 4, tree('before'));
    sessions.patched('s1', { fromRevision: 4, toRevision: 5, operations: [] });

    expect(sessions.has('s1')).toBeFalse();
  });

  it('ignores a patch for a session it does not hold', () => {
    expect(sessions.patched('unknown', setText(1, 2, 'x'))).toBeFalse();
  });

  it('forgets an invalidated session', () => {
    sessions.treeUpdated('s1', 1, tree('x'));
    sessions.invalidated('s1');

    expect(sessions.has('s1')).toBeFalse();
  });

  it('keeps sessions apart', () => {
    sessions.treeUpdated('s1', 1, tree('one'));
    sessions.treeUpdated('s2', 1, tree('two'));
    sessions.invalidated('s1');

    expect(sessions.has('s1')).toBeFalse();
    expect(sessions.tree('s2')!.properties!['text']).toBe('two');
  });

  it('announces every accepted change so a renderer can repaint', () => {
    const seen: number[] = [];
    sessions.changed.subscribe(value => seen.push(value));

    sessions.treeUpdated('s1', 1, tree('a'));
    sessions.patched('s1', setText(1, 2, 'b'));
    sessions.invalidated('s1');

    expect(seen.length).toBe(3);
  });

  it('says nothing when asked to forget a session it never had', () => {
    const seen: number[] = [];
    sessions.changed.subscribe(value => seen.push(value));

    sessions.invalidated('never-existed');

    expect(seen).toEqual([]);
  });

  it('drops everything at once when the connection is lost', () => {
    sessions.treeUpdated('s1', 1, tree('a'));
    sessions.treeUpdated('s2', 1, tree('b'));

    sessions.clear();

    expect(sessions.has('s1')).toBeFalse();
    expect(sessions.has('s2')).toBeFalse();
  });

  describe('onChange', () => {
    it('names the session every accepted change belongs to, and null for clear', () => {
      const seen: Array<string | null> = [];
      sessions.onChange(id => seen.push(id));

      sessions.treeUpdated('s1', 1, tree('a'));
      sessions.treeUpdated('s2', 1, tree('b'));
      sessions.patched('s1', setText(1, 2, 'c'));
      sessions.invalidated('s2');
      sessions.clear();

      expect(seen).toEqual(['s1', 's2', 's1', 's2', null]);
    });

    it('says nothing for an invalidation of a session it never had', () => {
      const seen: Array<string | null> = [];
      sessions.onChange(id => seen.push(id));

      sessions.invalidated('never-existed');

      expect(seen).toEqual([]);
    });

    it('says nothing when clear() finds nothing to drop', () => {
      const seen: Array<string | null> = [];
      sessions.onChange(id => seen.push(id));

      sessions.clear();

      expect(seen).toEqual([]);
    });

    it('stops a listener once unsubscribed, without disturbing another still listening', () => {
      const seen: Array<string | null> = [];
      const other: Array<string | null> = [];
      const stop = sessions.onChange(id => seen.push(id));
      sessions.onChange(id => other.push(id));

      stop();
      stop();
      sessions.treeUpdated('s1', 1, tree('a'));

      expect(seen).toEqual([]);
      expect(other).toEqual(['s1']);
    });

    it('reports a rejected patch as one drop, leaving changed to fire once', () => {
      sessions.treeUpdated('s1', 4, tree('before'));
      const seen: Array<string | null> = [];
      let changedCount = 0;
      sessions.onChange(id => seen.push(id));
      sessions.changed.subscribe(() => { changedCount++; });

      sessions.patched('s1', setText(7, 8, 'after'));

      expect(seen).toEqual(['s1']);
      expect(changedCount).toBe(1);
      expect(sessions.has('s1')).toBeFalse();
    });
  });
});
