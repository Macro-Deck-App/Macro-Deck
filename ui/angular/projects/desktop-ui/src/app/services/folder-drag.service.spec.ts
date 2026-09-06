import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Folder, FolderMoveRequest, WIDGET_GRID_VIEW_ID } from '@macro-deck/runtime';
import { ProfileService } from '@shared';
import { FolderDragService } from './folder-drag.service';

describe('FolderDragService', () => {
  let service: FolderDragService;
  let listEl: HTMLElement;
  let locked = false;

  const ROW_HEIGHT = 40;

  function folder(id: string, name = id): Folder {
    return {
      id, name, parentId: null, order: 0, isExpanded: true, isDefault: false,
      cols: 5, rows: 3, background: '', spacing: null, borderRadius: null, viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null, widgets: []
    };
  }

  function addRow(parent: HTMLElement, id: string, depth: number): HTMLElement {
    const host = document.createElement('div');
    const row = document.createElement('div');
    row.className = 'folder-item';
    row.dataset['folderId'] = id;
    row.dataset['depth'] = String(depth);
    row.style.height = `${ROW_HEIGHT}px`;
    host.appendChild(row);
    parent.appendChild(host);
    return host;
  }

  function rowRect(id: string): DOMRect {
    return listEl.querySelector<HTMLElement>(`.folder-item[data-folder-id="${id}"]`)!.getBoundingClientRect();
  }

  function yIn(id: string, ratio: number): number {
    const rect = rowRect(id);
    return rect.top + rect.height * ratio;
  }

  function pointer(type: string, y: number, x = 5): void {
    document.dispatchEvent(new PointerEvent(type, { clientX: x, clientY: y, bubbles: true }));
  }

  function press(f: Folder, hostEl: HTMLElement, y: number, targetEl?: HTMLElement): void {
    const el = targetEl ?? hostEl.querySelector<HTMLElement>('.folder-item')!;
    const event = new PointerEvent('pointerdown', { clientX: 5, clientY: y, button: 0, bubbles: true });
    el.dispatchEvent(event);
    service.press(event, f, hostEl);
  }

  beforeEach(() => {
    locked = false;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        FolderDragService,
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => locked } }
      ]
    });
    service = TestBed.inject(FolderDragService);

    listEl = document.createElement('div');
    document.body.appendChild(listEl);
    service.attach(listEl);
  });

  afterEach(() => {
    pointer('pointerup', 0);
    listEl.remove();
  });

  it('treats a sub-threshold press-release as a click, not a drag', () => {
    const hostA = addRow(listEl, 'a', 0);
    addRow(listEl, 'b', 0);
    const dropped: FolderMoveRequest[] = [];
    service.dropped.subscribe(request => dropped.push(request));

    const start = yIn('a', 0.5);
    press(folder('a'), hostA, start);
    pointer('pointermove', start + 3);
    pointer('pointerup', start + 3);

    expect(service.state().isDragging).toBeFalse();
    expect(dropped).toEqual([]);
    expect(service.consumeClickSuppression()).toBeFalse();
  });

  it('activates once the pointer travels past the threshold', () => {
    const hostA = addRow(listEl, 'a', 0);
    addRow(listEl, 'b', 0);

    const start = yIn('a', 0.5);
    press(folder('a', 'Alpha'), hostA, start);
    pointer('pointermove', start + 20);

    expect(service.state().isDragging).toBeTrue();
    expect(service.state().folderId).toBe('a');
    expect(service.ghostLabel()).toBe('Alpha');
  });

  it('maps the row bands to before, inside and after', () => {
    const hostA = addRow(listEl, 'a', 0);
    addRow(listEl, 'b', 0);

    press(folder('a'), hostA, yIn('a', 0.5));
    pointer('pointermove', yIn('b', 0.5));

    pointer('pointermove', yIn('b', 0.1));
    expect(service.state()).toEqual(jasmine.objectContaining({ targetFolderId: 'b', dropPosition: 'before' }));

    pointer('pointermove', yIn('b', 0.5));
    expect(service.state()).toEqual(jasmine.objectContaining({ targetFolderId: 'b', dropPosition: 'inside' }));

    pointer('pointermove', yIn('b', 0.9));
    expect(service.state()).toEqual(jasmine.objectContaining({ targetFolderId: 'b', dropPosition: 'after' }));
  });

  it('never targets a row inside the dragged folder own subtree', () => {
    const hostA = addRow(listEl, 'a', 0);
    const children = document.createElement('div');
    hostA.appendChild(children);
    addRow(children, 'a1', 1);
    addRow(listEl, 'b', 0);

    press(folder('a'), hostA, yIn('a', 0.5));
    pointer('pointermove', yIn('a1', 0.5));

    expect(service.state().targetFolderId).not.toBe('a1');
    expect(service.state().targetFolderId).toBe('b');
  });

  it('drops past the last row against the last root, flagged as the root end', () => {
    const hostA = addRow(listEl, 'a', 0);
    const children = document.createElement('div');
    hostA.appendChild(children);
    const hostA1 = addRow(children, 'a1', 1);
    addRow(listEl, 'b', 0);

    press(folder('a1'), hostA1, yIn('a1', 0.5));
    pointer('pointermove', rowRect('b').bottom + 200);

    expect(service.dropAtRootEnd()).toBeTrue();
    expect(service.state()).toEqual(jasmine.objectContaining({ targetFolderId: 'b', dropPosition: 'after' }));
  });

  it('targets the first row when the pointer is above it', () => {
    addRow(listEl, 'a', 0);
    const hostB = addRow(listEl, 'b', 0);

    press(folder('b'), hostB, yIn('b', 0.5));
    pointer('pointermove', rowRect('a').top - 50);

    expect(service.state()).toEqual(jasmine.objectContaining({ targetFolderId: 'a', dropPosition: 'before' }));
  });

  it('emits exactly one move when a drag is released over a valid target', () => {
    const hostA = addRow(listEl, 'a', 0);
    addRow(listEl, 'b', 0);
    const dropped: FolderMoveRequest[] = [];
    service.dropped.subscribe(request => dropped.push(request));

    press(folder('a'), hostA, yIn('a', 0.5));
    pointer('pointermove', yIn('b', 0.9));
    pointer('pointerup', yIn('b', 0.9));

    expect(dropped).toEqual([{ folderId: 'a', targetId: 'b', position: 'after' }]);
    expect(service.state().isDragging).toBeFalse();
    expect(service.consumeClickSuppression()).toBeTrue();
  });

  it('emits nothing when the drag is released over no target', () => {
    const hostA = addRow(listEl, 'a', 0);
    const dropped: FolderMoveRequest[] = [];
    service.dropped.subscribe(request => dropped.push(request));

    press(folder('a'), hostA, yIn('a', 0.5));
    pointer('pointermove', yIn('a', 0.5) + 20);
    pointer('pointerup', yIn('a', 0.5) + 20);

    expect(dropped).toEqual([]);
    expect(service.state().isDragging).toBeFalse();
  });

  it('cancels on Escape without emitting a move', () => {
    const hostA = addRow(listEl, 'a', 0);
    addRow(listEl, 'b', 0);
    const dropped: FolderMoveRequest[] = [];
    service.dropped.subscribe(request => dropped.push(request));

    press(folder('a'), hostA, yIn('a', 0.5));
    pointer('pointermove', yIn('b', 0.9));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(service.state().isDragging).toBeFalse();
    expect(service.dropAtRootEnd()).toBeFalse();

    pointer('pointerup', yIn('b', 0.9));
    expect(dropped).toEqual([]);
  });

  it('stops listening once the drag finished', () => {
    const hostA = addRow(listEl, 'a', 0);
    addRow(listEl, 'b', 0);

    press(folder('a'), hostA, yIn('a', 0.5));
    pointer('pointermove', yIn('b', 0.9));
    pointer('pointerup', yIn('b', 0.9));

    pointer('pointermove', yIn('b', 0.1));
    expect(service.state().isDragging).toBeFalse();
    expect(service.state().targetFolderId).toBeNull();
  });

  it('refuses to start a drag while the profile is locked', () => {
    locked = true;
    const hostA = addRow(listEl, 'a', 0);
    addRow(listEl, 'b', 0);

    const start = yIn('a', 0.5);
    press(folder('a'), hostA, start);
    pointer('pointermove', start + 20);

    expect(service.state().isDragging).toBeFalse();
  });

  it('ignores a press that started on the expand chevron', () => {
    const hostA = addRow(listEl, 'a', 0);
    const chevron = document.createElement('button');
    chevron.className = 'expand-btn';
    hostA.querySelector('.folder-item')!.appendChild(chevron);
    addRow(listEl, 'b', 0);

    const start = yIn('a', 0.5);
    press(folder('a'), hostA, start, chevron);
    pointer('pointermove', start + 20);

    expect(service.state().isDragging).toBeFalse();
  });
});
