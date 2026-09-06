import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ActionButtonData, GridWidget, WidgetClipboardEntry, WidgetType } from '@macro-deck/runtime';
import { WidgetClipboardService } from './widget-clipboard.service';

function widget(overrides: Partial<GridWidget> = {}): GridWidget {
  return {
    id: 'w1',
    folderId: 'f1',
    x: 0,
    y: 0,
    w: 2,
    h: 3,
    type: WidgetType.ActionButton,
    data: { label: 'Hello' } as ActionButtonData,
    ...overrides,
  };
}

describe('WidgetClipboardService', () => {
  let service: WidgetClipboardService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    service = TestBed.inject(WidgetClipboardService);
  });

  it('starts empty', () => {
    expect(service.entries()).toEqual([]);
    expect(service.hasContent()).toBeFalse();
    expect(service.cutWidgetIds().size).toBe(0);
    expect(service.originAnchor()).toBeNull();
  });

  it('copies size, type and a snapshot of the data, with a non-cut origin', () => {
    service.copy(widget({ id: 'src' }), 'folder-1');

    const entries = service.entries();
    expect(entries.length).toBe(1);
    expect(entries[0].type).toBe(WidgetType.ActionButton);
    expect(entries[0].w).toBe(2);
    expect(entries[0].h).toBe(3);
    expect(entries[0].isCut).toBeFalse();
    expect(entries[0].origin).toEqual({ widgetId: 'src', folderId: 'folder-1' });
    expect(service.hasContent()).toBeTrue();
    expect(service.cutWidgetIds().size).toBe(0);
  });

  it('copying a single widget yields one entry at offset (0, 0)', () => {
    service.copy(widget({ x: 5, y: 7 }), 'folder-1');

    expect(service.entries()[0].dx).toBe(0);
    expect(service.entries()[0].dy).toBe(0);
  });

  it('deep-copies the data so later source edits do not leak into the clipboard', () => {
    const source = widget();
    service.copy(source, 'folder-1');

    (source.data as ActionButtonData).label = 'Changed';

    expect((service.entries()[0].data as ActionButtonData).label).toBe('Hello');
  });

  it('marks a cut with its origin and exposes the cut widget id', () => {
    service.cut(widget({ id: 'src' }), 'folder-1');

    const entries = service.entries();
    expect(entries[0].isCut).toBeTrue();
    expect(entries[0].origin).toEqual({ widgetId: 'src', folderId: 'folder-1' });
    expect(service.cutWidgetIds().has('src')).toBeTrue();
  });

  it('does not report a cut id for a copy, even though the copy carries an origin', () => {
    service.copy(widget({ id: 'src' }), 'folder-1');
    expect(service.entries()[0].origin).not.toBeNull();
    expect(service.cutWidgetIds().size).toBe(0);
  });

  it('clears the clipboard', () => {
    service.cut(widget(), 'folder-1');
    service.clear();

    expect(service.entries()).toEqual([]);
    expect(service.hasContent()).toBeFalse();
    expect(service.cutWidgetIds().size).toBe(0);
    expect(service.originAnchor()).toBeNull();
  });

  describe('group copy/cut (issue #213)', () => {
    function groupWidgets(): { a: GridWidget; b: GridWidget; c: GridWidget } {
      return {
        a: widget({ id: 'a', x: 2, y: 5, w: 2, h: 1 }),
        b: widget({ id: 'b', x: 4, y: 6, w: 1, h: 2 }),
        c: widget({ id: 'c', x: 3, y: 8, w: 3, h: 1 }),
      };
    }

    function bySize(): Map<string, WidgetClipboardEntry> {
      return new Map(service.entries().map(e => [`${e.w}x${e.h}`, e]));
    }

    it('records every member offset from the group top-left (min x, min y across the group)', () => {
      const { a, b, c } = groupWidgets();
      service.copyMany([a, b, c], 'folder-1');

      const entries = bySize();
      expect(entries.get('2x1')).toEqual(jasmine.objectContaining({ dx: 0, dy: 0 })); // a
      expect(entries.get('1x2')).toEqual(jasmine.objectContaining({ dx: 2, dy: 1 })); // b
      expect(entries.get('3x1')).toEqual(jasmine.objectContaining({ dx: 1, dy: 3 })); // c
    });

    it('exposes the group top-left as the origin anchor', () => {
      const { a, b, c } = groupWidgets();
      service.copyMany([a, b, c], 'folder-1');
      expect(service.originAnchor()).toEqual({ x: 2, y: 5 });
    });

    it('mutating a source widget after copyMany does not change the clipboard payload (deep copy)', () => {
      const { a, b, c } = groupWidgets();
      service.copyMany([a, b, c], 'folder-1');

      (a.data as ActionButtonData).label = 'Changed';

      const entries = service.entries();
      expect(entries.every(e => (e.data as ActionButtonData).label === 'Hello')).toBeTrue();
    });

    it('cutMany marks every member cut, with the shared folder as each origin', () => {
      const { a, b } = groupWidgets();
      service.cutMany([a, b], 'folder-1');

      const entries = service.entries();
      expect(entries.every(e => e.isCut)).toBeTrue();
      expect(service.cutWidgetIds()).toEqual(new Set(['a', 'b']));
    });

    it('a later copy/cut replaces the whole group, not just appends to it', () => {
      const { a, b, c } = groupWidgets();
      service.copyMany([a, b], 'folder-1');
      service.copy(c, 'folder-1');

      expect(service.entries().length).toBe(1);
      expect(service.entries()[0].dx).toBe(0);
      expect(service.entries()[0].dy).toBe(0);
    });
  });
});
