import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Folder } from '@macro-deck/runtime';
import { FolderItemComponent } from './folder-item.component';
import { WIDGET_GRID_VIEW_ID } from '@macro-deck/runtime';

describe('FolderItemComponent', () => {
  let fixture: ComponentFixture<FolderItemComponent>;

  const folder: Folder = {
    id: 'f1',
    name: 'Lights',
    parentId: null,
    order: 0,
    isExpanded: false,
    isDefault: false,
    cols: 5,
    rows: 3,
    background: '',
    spacing: null,
    borderRadius: null,
    viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null,
    widgets: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FolderItemComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(FolderItemComponent);
    fixture.componentRef.setInput('folder', folder);
    fixture.detectChanges();
  });

  function row(): HTMLElement {
    return fixture.nativeElement.querySelector('.folder-item') as HTMLElement;
  }

  function keydown(key: string, options: KeyboardEventInit = {}): boolean {
    return row().dispatchEvent(new KeyboardEvent('keydown', { key, cancelable: true, ...options }));
  }

  it('selects the folder on Enter and Space', () => {
    const selected: string[] = [];
    fixture.componentInstance.select.subscribe(id => selected.push(id));

    keydown('Enter');
    keydown(' ');

    expect(selected).toEqual(['f1', 'f1']);
  });

  it('emits move with the resolved outliner direction on Alt+Arrow, and prevents default', () => {
    const moves: Array<{ folderId: string; direction: string }> = [];
    fixture.componentInstance.move.subscribe(move => moves.push(move));

    const notPrevented = keydown('ArrowUp', { altKey: true });

    expect(moves).toEqual([{ folderId: 'f1', direction: 'up' }]);
    expect(notPrevented).toBeFalse();
  });

  it('maps every Alt+Arrow to its outliner move direction', () => {
    const moves: Array<{ direction: string }> = [];
    fixture.componentInstance.move.subscribe(move => moves.push(move));

    keydown('ArrowDown', { altKey: true });
    keydown('ArrowRight', { altKey: true });
    keydown('ArrowLeft', { altKey: true });

    expect(moves.map(move => move.direction)).toEqual(['down', 'into', 'out']);
  });

  it('emits navigate, not move, for a plain arrow key', () => {
    const moves: unknown[] = [];
    const navigated: Array<{ folderId: string; direction: string }> = [];
    fixture.componentInstance.move.subscribe(move => moves.push(move));
    fixture.componentInstance.navigate.subscribe(nav => navigated.push(nav));

    keydown('ArrowUp');
    keydown('Home');

    expect(moves).toEqual([]);
    expect(navigated).toEqual([
      { folderId: 'f1', direction: 'up' },
      { folderId: 'f1', direction: 'home' },
    ]);
  });

  it('opens the context menu at the row rect on Shift+F10 and the ContextMenu key', () => {
    const opened: Array<{ folderId: string; x: number; y: number }> = [];
    fixture.componentInstance.contextMenu.subscribe(event => opened.push(event));

    keydown('F10', { shiftKey: true });
    keydown('ContextMenu');

    expect(opened.length).toBe(2);
    expect(opened[0].folderId).toBe('f1');
    expect(opened[1].folderId).toBe('f1');
  });

  // Direct regression guard for issue #281: the row must be a real treeitem driven entirely by
  // pointer/keyboard events, with no HTML5 draggable attribute left for the Tauri shell to swallow.
  it('#281 regression guard: the row is a treeitem with no draggable attribute', () => {
    expect(row().getAttribute('role')).toBe('treeitem');
    expect(row().hasAttribute('draggable')).toBeFalse();
  });
});
