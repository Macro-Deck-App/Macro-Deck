import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CellDimensions, GridRect, GridWidget, WidgetType } from '@macro-deck/runtime';
import { DeckDragConfig, DeckDragService } from './deck-drag.service';

describe('DeckDragService', () => {
  let service: DeckDragService;
  let gridEl: HTMLElement;
  let sourceEl: HTMLElement;

  const cell: CellDimensions = { cellWidth: 100, cellHeight: 100, gap: 0, padding: 0 };
  const step = 100;

  function widget(id: string, x: number, y: number, w = 1, h = 1, isPinned = false): GridWidget {
    return { id, folderId: 'f1', x, y, w, h, type: WidgetType.ActionButton, data: {}, isPinned };
  }

  function config(widgets: GridWidget[], cols = 4, rows = 2): DeckDragConfig {
    return { widgets, cols, rows, cell, gridEl, sourceEl };
  }

  function pointer(type: string, x: number, y: number): void {
    document.dispatchEvent(new PointerEvent(type, { clientX: x, clientY: y, bubbles: true }));
  }

  function pressAt(w: GridWidget, cfg: DeckDragConfig, x = 0, y = 0, mode: 'DRAG' | 'RESIZE' = 'DRAG'): void {
    service.press(new PointerEvent('pointerdown', { clientX: x, clientY: y }), w, mode, cfg);
  }

  function pressGroupAt(primary: GridWidget, members: GridWidget[], cfg: DeckDragConfig, x = 0, y = 0): void {
    service.pressGroup(new PointerEvent('pointerdown', { clientX: x, clientY: y }), primary, members, cfg);
  }

  function frame(): Promise<void> {
    return new Promise(resolve => requestAnimationFrame(() => resolve()));
  }

  async function moveTo(x: number, y: number): Promise<void> {
    pointer('pointermove', x, y);
    await frame();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), DeckDragService] });
    service = TestBed.inject(DeckDragService);
    gridEl = document.createElement('div');
    sourceEl = document.createElement('div');
    document.body.appendChild(gridEl);
    document.body.appendChild(sourceEl);
  });

  afterEach(() => {
    pointer('pointerup', 0, 0);
    gridEl.remove();
    sourceEl.remove();
  });

  it('treats a sub-threshold press-release as a click, not a drag', async () => {
    const dropped: ReadonlyMap<string, GridRect>[] = [];
    service.dropped.subscribe(d => dropped.push(d));
    pressAt(widget('a', 0, 0), config([widget('a', 0, 0)]));

    await moveTo(3, 0);
    pointer('pointerup', 3, 0);

    expect(service.session()).toBeNull();
    expect(dropped.length).toBe(0);
    expect(service.consumeClickSuppression()).toBeFalse();
  });

  it('activates after the threshold and snaps the placeholder per cell', async () => {
    const a = widget('a', 0, 0);
    pressAt(a, config([a]));

    await moveTo(step * 0.6, 0);

    expect(service.session()).toEqual({ widgetId: 'a', mode: 'DRAG', memberIds: new Set(['a']) });
    expect(service.ghostWidgets()).toEqual([{ widget: a, offsetX: 0, offsetY: 0 }]);
    expect(service.placeholders()).toEqual([{ x: 1, y: 0, w: 1, h: 1 }]);
  });

  it('clamps the placeholder to the grid bounds', async () => {
    const a = widget('a', 0, 0);
    pressAt(a, config([a], 4, 2));

    await moveTo(step * 99, step * 99);

    expect(service.placeholders()).toEqual([{ x: 3, y: 1, w: 1, h: 1 }]);
  });

  it('displaces an overlapped neighbour and emits the full diff on drop (single-widget reflow)', async () => {
    // This is the regression guard for issue #213: a single-widget drag - even one dragging a widget
    // that happens to be part of no (or a size-1) selection - must keep reflowing unselected widgets
    // out of the way, exactly like before the multi-selection group path existed.
    const a = widget('a', 0, 0);
    const b = widget('b', 1, 0);
    const dropped: ReadonlyMap<string, GridRect>[] = [];
    service.dropped.subscribe(d => dropped.push(d));
    pressAt(a, config([a, b], 4, 1));

    await moveTo(step, 0);
    expect(service.placeholders()).toEqual([{ x: 1, y: 0, w: 1, h: 1 }]);
    expect(service.displacements().get('b')).toEqual({ x: 0, y: 0, w: 1, h: 1 });

    pointer('pointerup', step, 0);

    expect(dropped.length).toBe(1);
    expect(dropped[0].get('a')).toEqual({ x: 1, y: 0, w: 1, h: 1 });
    expect(dropped[0].get('b')).toEqual({ x: 0, y: 0, w: 1, h: 1 });
    expect(service.session()).toBeNull();
    expect(service.placeholders()).toEqual([]);
    expect(service.displacements().size).toBe(0);
    expect(service.justDropped()).toEqual(new Set(['a']));
    expect(service.consumeClickSuppression()).toBeTrue();
  });

  it('keeps the last valid layout when the candidate cannot be resolved', async () => {
    const widgets = [widget('a', 0, 0), widget('b', 1, 0), widget('c', 0, 1), widget('d', 1, 1)];
    pressAt(widgets[0], config(widgets, 2, 2), 0, 0, 'RESIZE');

    await moveTo(step, 0);

    expect(service.placeholders()).toEqual([{ x: 0, y: 0, w: 1, h: 1 }]);
    expect(service.displacements().size).toBe(0);
  });

  it('commits the final pointer position when released before the next frame (fast flick)', () => {
    const a = widget('a', 0, 0);
    const dropped: ReadonlyMap<string, GridRect>[] = [];
    service.dropped.subscribe(d => dropped.push(d));
    pressAt(a, config([a]));

    pointer('pointermove', step * 2, 0);
    pointer('pointerup', step * 2, 0);

    expect(dropped.length).toBe(1);
    expect(dropped[0].get('a')).toEqual({ x: 2, y: 0, w: 1, h: 1 });
  });

  it('does not emit when dropped back on the original cell', async () => {
    const dropped: ReadonlyMap<string, GridRect>[] = [];
    service.dropped.subscribe(d => dropped.push(d));
    const a = widget('a', 0, 0);
    pressAt(a, config([a]));

    await moveTo(step, 0);
    await moveTo(0, 0);
    pointer('pointerup', 0, 0);

    expect(dropped.length).toBe(0);
    expect(service.session()).toBeNull();
  });

  it('cancels on Escape without emitting', async () => {
    const dropped: ReadonlyMap<string, GridRect>[] = [];
    service.dropped.subscribe(d => dropped.push(d));
    const a = widget('a', 0, 0);
    const b = widget('b', 1, 0);
    pressAt(a, config([a, b], 4, 1));

    await moveTo(step, 0);
    expect(service.displacements().size).toBe(1);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(dropped.length).toBe(0);
    expect(service.session()).toBeNull();
    expect(service.placeholders()).toEqual([]);
    expect(service.displacements().size).toBe(0);
    expect(service.consumeClickSuppression()).toBeTrue();
  });

  it('cancels on pointercancel without emitting', async () => {
    const dropped: ReadonlyMap<string, GridRect>[] = [];
    service.dropped.subscribe(d => dropped.push(d));
    const a = widget('a', 0, 0);
    pressAt(a, config([a]));

    await moveTo(step, 0);
    document.dispatchEvent(new PointerEvent('pointercancel'));

    expect(dropped.length).toBe(0);
    expect(service.session()).toBeNull();
  });

  it('resizes through the same engine, pushing neighbours', async () => {
    const a = widget('a', 0, 0);
    const b = widget('b', 1, 0);
    const dropped: ReadonlyMap<string, GridRect>[] = [];
    service.dropped.subscribe(d => dropped.push(d));
    pressAt(a, config([a, b], 3, 2), 0, 0, 'RESIZE');

    await moveTo(step, 0);
    expect(service.placeholders()).toEqual([{ x: 0, y: 0, w: 2, h: 1 }]);
    expect(service.ghostWidgets()).toEqual([]);
    const displacedB = service.displacements().get('b');
    expect(displacedB).toBeDefined();

    pointer('pointerup', step, 0);

    expect(dropped.length).toBe(1);
    expect(dropped[0].get('a')).toEqual({ x: 0, y: 0, w: 2, h: 1 });
    expect(dropped[0].get('b')).toEqual(displacedB!);
  });

  it('never resizes below 1x1', async () => {
    const a = widget('a', 1, 1, 2, 1);
    pressAt(a, config([a], 4, 2), 0, 0, 'RESIZE');

    await moveTo(-step * 5, -step * 5);

    expect(service.placeholders()).toEqual([{ x: 1, y: 1, w: 1, h: 1 }]);
  });

  it('includes the resized widget itself in the displacements map', async () => {
    const a = widget('a', 0, 0);
    pressAt(a, config([a], 3, 2), 0, 0, 'RESIZE');

    await moveTo(step, 0);

    expect(service.displacements().get('a')).toEqual({ x: 0, y: 0, w: 2, h: 1 });
  });

  it('clears stale click suppression when a new press starts', async () => {
    const a = widget('a', 0, 0);
    pressAt(a, config([a]));
    await moveTo(step, 0);
    pointer('pointerup', step, 0);

    pressAt(a, config([a]));
    pointer('pointerup', 0, 0);

    expect(service.consumeClickSuppression()).toBeFalse();
  });

  it('applies the pending transform when the ghost attaches late', async () => {
    const a = widget('a', 0, 0);
    pressAt(a, config([a]));
    await moveTo(step, 0);

    const ghost = document.createElement('div');
    service.attachGhost(ghost);

    expect(ghost.style.transform).toContain('translate3d');
    service.detachGhost(ghost);
  });

  describe('group drag (issue #213)', () => {
    it('moves a rigid 2+ selection preserving relative offsets and touches no unselected widget', async () => {
      const a = widget('a', 0, 0);
      const b = widget('b', 1, 0);
      const c = widget('c', 3, 0);
      const dropped: ReadonlyMap<string, GridRect>[] = [];
      service.dropped.subscribe(d => dropped.push(d));
      pressGroupAt(a, [a, b], config([a, b, c], 5, 1));

      await moveTo(step, 0);

      expect(service.session()).toEqual({ widgetId: 'a', mode: 'DRAG', memberIds: new Set(['a', 'b']) });
      expect(service.placeholders()).toEqual([{ x: 1, y: 0, w: 1, h: 1 }, { x: 2, y: 0, w: 1, h: 1 }]);
      expect(service.displacements().size).toBe(0);

      pointer('pointerup', step, 0);

      expect(dropped.length).toBe(1);
      expect(dropped[0].size).toBe(2);
      expect(dropped[0].get('a')).toEqual({ x: 1, y: 0, w: 1, h: 1 });
      expect(dropped[0].get('b')).toEqual({ x: 2, y: 0, w: 1, h: 1 });
      expect(dropped[0].has('c')).toBeFalse();
    });

    it('snaps back to the last valid offset (identity) when the current offset is invalid', async () => {
      const a = widget('a', 0, 0);
      const b = widget('b', 1, 0);
      const c = widget('c', 2, 0);
      const dropped: ReadonlyMap<string, GridRect>[] = [];
      service.dropped.subscribe(d => dropped.push(d));
      pressGroupAt(a, [a, b], config([a, b, c], 3, 1));

      await moveTo(step, 0);

      expect(service.placeholders()).toEqual([{ x: 0, y: 0, w: 1, h: 1 }, { x: 1, y: 0, w: 1, h: 1 }]);

      pointer('pointerup', step, 0);

      expect(dropped.length).toBe(0);
    });

    it('recovers a later valid offset after an invalid one was rejected', async () => {
      const a = widget('a', 0, 0);
      const b = widget('b', 1, 0);
      const c = widget('c', 3, 0);
      const dropped: ReadonlyMap<string, GridRect>[] = [];
      service.dropped.subscribe(d => dropped.push(d));
      pressGroupAt(a, [a, b], config([a, b, c], 5, 1));

      await moveTo(step * 2, 0); // invalid: 'b' (1+2=3,0) would land on 'c'
      expect(service.placeholders()).toEqual([{ x: 0, y: 0, w: 1, h: 1 }, { x: 1, y: 0, w: 1, h: 1 }]);

      await moveTo(step, 0); // valid: shifts the pair by one cell into free space
      expect(service.placeholders()).toEqual([{ x: 1, y: 0, w: 1, h: 1 }, { x: 2, y: 0, w: 1, h: 1 }]);

      pointer('pointerup', step, 0);

      expect(dropped.length).toBe(1);
      expect(dropped[0].get('a')).toEqual({ x: 1, y: 0, w: 1, h: 1 });
      expect(dropped[0].get('b')).toEqual({ x: 2, y: 0, w: 1, h: 1 });
    });
  });
});
