import { WIDGET_GRID_VIEW_ID } from '../domain/folder.interface';
import { IpcFolder } from './messages/folder';
import { IpcWidget } from './messages/widget';
import { folderFromWire, foldersFromWire, widgetFromWire } from './folder-mapping';

const wireFolder = (overrides: Partial<IpcFolder> = {}): IpcFolder => ({
  id: 'f1',
  name: 'Living room',
  order: 2,
  ...overrides,
} as IpcFolder);

const wireWidget = (overrides: Partial<IpcWidget> = {}): IpcWidget => ({
  id: 'w1',
  type: 'action-button',
  positionX: 3,
  positionY: 1,
  width: 2,
  height: 1,
  ...overrides,
} as IpcWidget);

describe('folder mapping', () => {
  it('renames the wire fields to the ones the client uses', () => {
    const folder = folderFromWire(wireFolder({ columns: 6, rows: 4 }));

    expect(folder.cols).toBe(6);
    expect(folder.rows).toBe(4);
  });

  it('leaves an omitted grid size unanswered rather than guessing one', () => {
    const folder = folderFromWire(wireFolder());

    expect(folder.cols).toBeNull();
    expect(folder.rows).toBeNull();
  });

  it('reads the very first versions\' background as no background at all', () => {
    // It was written as an opaque default rather than as absence, and painting it would put a panel
    // behind every deck that has never set one.
    expect(folderFromWire(wireFolder({ backgroundColor: 'rgba(18, 18, 18, 0.6)' })).background).toBe('');
  });

  it('keeps a background someone actually chose', () => {
    expect(folderFromWire(wireFolder({ backgroundColor: '#101820' })).background).toBe('#101820');
  });

  it('falls an absent or empty view back to the built-in grid', () => {
    expect(folderFromWire(wireFolder()).viewId).toBe(WIDGET_GRID_VIEW_ID);
    expect(folderFromWire(wireFolder({ viewId: '' })).viewId).toBe(WIDGET_GRID_VIEW_ID);
  });

  it('keeps a view a provider registered', () => {
    expect(folderFromWire(wireFolder({ viewId: 'acme.wheel' })).viewId).toBe('acme.wheel');
  });

  it('treats an omitted parent and default flag as top-level and not default', () => {
    const folder = folderFromWire(wireFolder());

    expect(folder.parentId).toBeNull();
    expect(folder.isDefault).toBeFalse();
  });

  it('maps the widgets it carries and tells each one which folder it is in', () => {
    const folder = folderFromWire(wireFolder({ widgets: [wireWidget()] }));

    expect(folder.widgets.length).toBe(1);
    expect(folder.widgets[0].folderId).toBe('f1');
  });

  it('reads a folder with no widgets as empty rather than undefined', () => {
    expect(folderFromWire(wireFolder()).widgets).toEqual([]);
  });

  it('maps a whole list, and an absent one as empty', () => {
    expect(foldersFromWire([wireFolder(), wireFolder({ id: 'f2' })]).map(f => f.id)).toEqual(['f1', 'f2']);
    expect(foldersFromWire(undefined)).toEqual([]);
  });
});

describe('widget mapping', () => {
  it('renames the position and size fields', () => {
    const widget = widgetFromWire(wireWidget(), 'f1');

    expect(widget.x).toBe(3);
    expect(widget.y).toBe(1);
    expect(widget.w).toBe(2);
    expect(widget.h).toBe(1);
  });

  it('parses the data the host sent as a JSON string', () => {
    const widget = widgetFromWire(wireWidget({ data: '{"label":{"text":"Lights"}}' }), 'f1');

    expect(widget.data).toBeDefined();
  });

  it('still produces usable data when the host sent none', () => {
    expect(widgetFromWire(wireWidget(), 'f1').data).toBeDefined();
  });

  it('treats an unpinned widget as unpinned and scopes a pin to the profile by default', () => {
    expect(widgetFromWire(wireWidget(), 'f1').isPinned).toBeFalse();
    expect(widgetFromWire(wireWidget({ isPinned: true }), 'f1').pinScope).toBe('Profile');
  });

  it('keeps a pin scope the host stated', () => {
    expect(widgetFromWire(wireWidget({ isPinned: true, pinScope: 'Subtree' }), 'f1').pinScope)
      .toBe('Subtree');
  });
});
