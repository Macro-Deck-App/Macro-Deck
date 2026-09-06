import { Folder } from './folder.interface';
import { GridWidget, PinScope, WidgetType } from './widget.interface';
import { collectDisplayedWidgets, foreignPinnedWidgets } from './pinned-widget.util';
import { WIDGET_GRID_VIEW_ID } from './index';

function widget(id: string, folderId: string, isPinned = false, pinScope?: PinScope): GridWidget {
  return { id, folderId, x: 0, y: 0, w: 1, h: 1, type: WidgetType.ActionButton, data: {}, isPinned, pinScope };
}

function folder(id: string, widgets: GridWidget[], parentId: string | null = null): Folder {
  return {
    id,
    name: id,
    parentId,
    order: 0,
    isExpanded: false,
    isDefault: false,
    cols: 5,
    rows: 3,
    background: '',
    spacing: null,
    borderRadius: null,
    viewId: WIDGET_GRID_VIEW_ID,
    viewConfiguration: null,
    widgets
  };
}

describe('pinned-widget util', () => {
  it('shows a pinned widget of another folder in the selected one', () => {
    const folders = [
      folder('a', [widget('w1', 'a')]),
      folder('b', [widget('w2', 'b', true)])
    ];

    expect(collectDisplayedWidgets(folders, 'a').map(w => w.id)).toEqual(['w1', 'w2']);
  });

  it('does not duplicate a pinned widget in its own folder', () => {
    const folders = [
      folder('a', [widget('w1', 'a', true)]),
      folder('b', [])
    ];

    expect(collectDisplayedWidgets(folders, 'a').map(w => w.id)).toEqual(['w1']);
    expect(collectDisplayedWidgets(folders, 'b').map(w => w.id)).toEqual(['w1']);
  });

  it('leaves unpinned widgets of other folders out', () => {
    const folders = [
      folder('a', [widget('w1', 'a')]),
      folder('b', [widget('w2', 'b')])
    ];

    expect(collectDisplayedWidgets(folders, 'a').map(w => w.id)).toEqual(['w1']);
  });

  it('returns the folder list unchanged when nothing is pinned elsewhere', () => {
    const own = [widget('w1', 'a')];
    const folders = [folder('a', own)];

    expect(collectDisplayedWidgets(folders, 'a')).toBe(own);
  });

  it('returns nothing for an unknown folder', () => {
    expect(collectDisplayedWidgets([folder('a', [])], 'missing')).toEqual([]);
  });

  it('collects pinned widgets across every other folder', () => {
    const folders = [
      folder('a', [widget('w1', 'a', true)]),
      folder('b', [widget('w2', 'b', true)]),
      folder('c', [widget('w3', 'c')])
    ];

    expect(foreignPinnedWidgets(folders, 'a').map(w => w.id)).toEqual(['w2']);
  });
});

describe('pinned-widget util - scope reach', () => {
  function tree(widgetsByFolder: Record<string, GridWidget[]> = {}): Folder[] {
    return [
      folder('main', widgetsByFolder['main'] ?? []),
      folder('games', widgetsByFolder['games'] ?? [], 'main'),
      folder('retro', widgetsByFolder['retro'] ?? [], 'games'),
      folder('media', widgetsByFolder['media'] ?? [], 'main'),
      folder('work', widgetsByFolder['work'] ?? []),
      folder('mail', widgetsByFolder['mail'] ?? [], 'work'),
    ];
  }

  it('H1: a subtree pin reaches its home, a child and a grandchild', () => {
    const w1 = widget('w1', 'main', true, 'Subtree');
    const folders = tree({ main: [w1] });

    for (const target of ['main', 'games', 'retro', 'media']) {
      expect(collectDisplayedWidgets(folders, target).map(w => w.id))
        .withContext(target).toContain('w1');
      expect(collectDisplayedWidgets(folders, target).filter(w => w.id === 'w1').length)
        .withContext(target).toBe(1);
    }
  });

  it('H2: a subtree pin does not reach its parent, a sibling or an unrelated subtree', () => {
    const w2 = widget('w2', 'games', true, 'Subtree');
    const folders = tree({ games: [w2] });

    for (const target of ['main', 'media', 'work', 'mail']) {
      expect(collectDisplayedWidgets(folders, target).map(w => w.id)).withContext(target).not.toContain('w2');
    }
  });

  it('H3: a profile pin reaches every folder', () => {
    const w3 = widget('w3', 'mail', true, 'Profile');
    const folders = tree({ mail: [w3] });

    for (const target of ['main', 'games', 'retro', 'media', 'work']) {
      expect(collectDisplayedWidgets(folders, target).map(w => w.id)).withContext(target).toContain('w3');
    }
  });

  it('H4: an undefined scope reaches every folder, same as Profile', () => {
    const w4 = widget('w4', 'mail', true, undefined);
    const folders = tree({ mail: [w4] });

    expect(collectDisplayedWidgets(folders, 'main').map(w => w.id)).toContain('w4');
  });

  it('H5: a folder\'s own subtree pin is not duplicated, and other folders get it once', () => {
    const a = widget('a', 'main');
    const w1 = widget('w1', 'main', true, 'Subtree');
    const b = widget('b', 'games');
    const folders = tree({ main: [a, w1], games: [b] });

    expect(collectDisplayedWidgets(folders, 'main').map(w => w.id)).toEqual(['a', 'w1']);
    expect(collectDisplayedWidgets(folders, 'games').map(w => w.id)).toEqual(['b', 'w1']);
  });

  it('H6: an orphan folder (parentId not in the list) returns only its own widgets and does not throw or hang', () => {
    const w1 = widget('w1', 'main', true, 'Subtree');
    const folders = [
      ...tree({ main: [w1] }),
      folder('orphan', [widget('own', 'orphan')], 'gone'),
    ];

    let result: GridWidget[] | undefined;
    expect(() => { result = collectDisplayedWidgets(folders, 'orphan'); }).not.toThrow();
    expect(result!.map(w => w.id)).toEqual(['own']);
  });

  it('does not treat every pin as Subtree - a profile-wide pin still reaches an unrelated folder', () => {
    const w = widget('w', 'main', true, 'Profile');
    const folders = tree({ main: [w] });

    expect(collectDisplayedWidgets(folders, 'mail').map(x => x.id)).toContain('w');
  });

  it('does not treat every pin as Profile - a subtree pin does not leak outside its reach', () => {
    const w = widget('w', 'games', true, 'Subtree');
    const folders = tree({ games: [w] });

    expect(collectDisplayedWidgets(folders, 'work').map(x => x.id)).not.toContain('w');
  });
});
