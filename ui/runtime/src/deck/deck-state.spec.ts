import { Folder } from '../domain/folder.interface';
import { GridWidget } from '../domain/widget.interface';
import { DeckState } from './deck-state';

const widget = (id: string, folderId: string, x = 0, y = 0): GridWidget =>
  ({ id, folderId, x, y, w: 1, h: 1, type: 'action-button', data: {} }) as GridWidget;

const folder = (id: string, overrides: Partial<Folder> = {}): Folder => ({
  id,
  name: id,
  parentId: null,
  order: 0,
  isExpanded: false,
  isDefault: false,
  cols: null,
  rows: null,
  background: '',
  spacing: null,
  borderRadius: null,
  viewId: 'macrodeck.widget-grid',
  viewConfiguration: null,
  widgets: [],
  ...overrides,
} as Folder);

describe('DeckState', () => {
  let deck: DeckState;

  beforeEach(() => { deck = new DeckState(); });

  describe('loading', () => {
    it('opens the default folder rather than the first one', () => {
      deck.load([folder('a'), folder('b', { isDefault: true })]);

      expect(deck.location.get().folderId).toBe('b');
    });

    it('falls back to the first folder when none is marked default', () => {
      deck.load([folder('a'), folder('b')]);

      expect(deck.location.get().folderId).toBe('a');
    });

    it('opens nothing at all for an empty deck', () => {
      deck.load([]);

      expect(deck.location.get().folderId).toBeNull();
      expect(deck.displayedWidgets).toEqual([]);
    });

    it('stays where it is when a reload still has that folder', () => {
      deck.load([folder('a', { isDefault: true }), folder('b')]);
      deck.openFolder('b');

      deck.load([folder('a', { isDefault: true }), folder('b')]);

      expect(deck.location.get().folderId).toBe('b');
    });

    it('leaves a folder that a reload no longer has', () => {
      deck.load([folder('a', { isDefault: true }), folder('b')]);
      deck.openFolder('b');

      deck.load([folder('a', { isDefault: true })]);

      expect(deck.location.get().folderId).toBe('a');
    });
  });

  describe('loading into a folder', () => {
    it('opens the folder the load names, not the deck\'s default one', () => {
      deck.load([folder('a-root', { isDefault: true })]);

      deck.load([folder('b-root', { isDefault: true }), folder('b-child')], 'b-child');

      expect(deck.location.get().folderId).toBe('b-child');
    });

    it('passes through nowhere on the way', () => {
      deck.load([folder('a-root', { isDefault: true })]);
      const visited: (string | null)[] = [];
      deck.location.subscribe(location => visited.push(location.folderId));

      deck.load([folder('b-root', { isDefault: true }), folder('b-child')], 'b-child');

      expect(visited).toEqual(['b-child']);
    });

    it('leaves no way back into the deck it replaced', () => {
      deck.load([folder('a-root', { isDefault: true }), folder('a-child')]);
      deck.openFolder('a-child');

      deck.load([folder('b-root', { isDefault: true }), folder('b-child')], 'b-child');
      deck.back();

      expect(deck.canGoBack).toBeFalse();
      expect(deck.location.get().folderId).toBe('b-child');
    });

    it('falls back to the default folder when the named one is not in the new deck', () => {
      deck.load([folder('a-root', { isDefault: true })]);

      deck.load([folder('b-root', { isDefault: true }), folder('b-child')], 'gone');

      expect(deck.location.get().folderId).toBe('b-root');
    });
  });

  describe('the folders it holds', () => {
    it('answers with one it holds and with nothing for one it does not', () => {
      deck.load([folder('a', { isDefault: true })]);

      expect(deck.folder('a')?.id).toBe('a');
      expect(deck.folder('b')).toBeUndefined();
    });
  });

  describe('moving through folders', () => {
    beforeEach(() => deck.load([
      folder('root', { isDefault: true }),
      folder('child', { parentId: 'root' }),
      folder('grandchild', { parentId: 'child' }),
      folder('elsewhere'),
    ]));

    it('remembers the trail as it walks in', () => {
      deck.openFolder('child');
      deck.openFolder('grandchild');

      expect(deck.location.get().history).toEqual(['root', 'child']);
    });

    it('walks back the way it came', () => {
      deck.openFolder('child');
      deck.openFolder('elsewhere');
      deck.back();

      expect(deck.location.get().folderId).toBe('child');
      expect(deck.location.get().history).toEqual(['root']);
    });

    it('does nothing going back from the start of the trail', () => {
      deck.back();

      expect(deck.location.get().folderId).toBe('root');
    });

    it('goes up the tree rather than back through the trail', () => {
      // Reached grandchild from elsewhere, so "back" and "up" are different folders.
      deck.openFolder('elsewhere');
      deck.openFolder('grandchild');

      deck.parent();

      expect(deck.location.get().folderId).toBe('child');
    });

    it('clears the trail on the way up, because it no longer leads anywhere visited', () => {
      deck.openFolder('grandchild');
      deck.parent();

      expect(deck.location.get().history).toEqual([]);
    });

    it('does nothing going up from a folder with no parent', () => {
      deck.parent();

      expect(deck.location.get().folderId).toBe('root');
    });

    it('ignores opening the folder that is already open', () => {
      deck.openFolder('child');
      deck.openFolder('child');

      expect(deck.location.get().history).toEqual(['root']);
    });
  });

  describe('when the host changes the deck underfoot', () => {
    beforeEach(() => deck.load([
      folder('root', { isDefault: true }),
      folder('child', { parentId: 'root' }),
    ]));

    it('adds a folder it has not seen', () => {
      deck.folderUpserted(folder('fresh'));

      expect(deck.folders.get().map(f => f.id)).toEqual(['root', 'child', 'fresh']);
    });

    it('replaces a folder it already had, rather than merging into it', () => {
      deck.folderUpserted(folder('child', { name: 'Renamed', parentId: 'root' }));

      expect(deck.folders.get().find(f => f.id === 'child')!.name).toBe('Renamed');
      expect(deck.folders.get().length).toBe(2);
    });

    it('retreats along the trail when the folder underfoot is deleted', () => {
      deck.openFolder('child');

      deck.folderDeleted('child');

      expect(deck.location.get().folderId).toBe('root');
    });

    it('falls back to the default when the folder underfoot is deleted with no trail', () => {
      deck.folderDeleted('root');

      expect(deck.location.get().folderId).toBe('child');
    });

    it('drops a deleted folder out of the trail without moving the user', () => {
      deck.openFolder('child');
      deck.folderDeleted('root');

      expect(deck.location.get().folderId).toBe('child');
      expect(deck.location.get().history).toEqual([]);
    });

    it('adds a widget the host reports', () => {
      deck.widgetsUpserted('root', [widget('w1', 'root')]);

      expect(deck.displayedWidgets.map(w => w.id)).toEqual(['w1']);
    });

    it('replaces a widget that moved instead of showing it twice', () => {
      deck.widgetsUpserted('root', [widget('w1', 'root', 0, 0)]);
      deck.widgetsUpserted('root', [widget('w1', 'root', 3, 1)]);

      expect(deck.displayedWidgets.length).toBe(1);
      expect(deck.displayedWidgets[0].x).toBe(3);
    });

    it('applies a batch of widgets in one go', () => {
      deck.widgetsUpserted('root', [widget('w1', 'root'), widget('w2', 'root', 1)]);

      expect(deck.displayedWidgets.map(w => w.id)).toEqual(['w1', 'w2']);
    });

    it('removes a deleted widget', () => {
      deck.widgetsUpserted('root', [widget('w1', 'root'), widget('w2', 'root', 1)]);
      deck.widgetDeleted('root', 'w1');

      expect(deck.displayedWidgets.map(w => w.id)).toEqual(['w2']);
    });

    it('ignores a change for a folder it does not have', () => {
      deck.widgetsUpserted('gone', [widget('w1', 'gone')]);

      expect(deck.folders.get().length).toBe(2);
    });

    it('hands subscribers a new list, so a repaint can be decided by identity', () => {
      const before = deck.folders.get();
      const seen: Array<readonly Folder[]> = [];
      deck.folders.subscribe(folders => seen.push(folders));

      deck.widgetsUpserted('root', [widget('w1', 'root')]);

      expect(seen.length).toBe(1);
      expect(seen[0]).not.toBe(before);
      expect(seen[0]).toBe(deck.folders.get());
    });
  });

  it('shows widgets pinned in from another folder alongside its own', () => {
    deck.load([
      folder('root', { isDefault: true, widgets: [widget('own', 'root')] }),
      folder('other', { widgets: [{ ...widget('pinned', 'other', 2), isPinned: true }] }),
    ]);

    expect(deck.displayedWidgets.map(w => w.id).sort()).toEqual(['own', 'pinned']);
  });
});
