import {
  canPlaceGroup,
  cellFromPoint,
  clampRectToGrid,
  DEFAULT_CELL_GAP_RATIO,
  clampSizeToGrid,
  computeCellDimensions,
  computePasteRect,
  GridPlacement,
  GridRect,
  rectsOverlap,
  reflowLayout,
  snapDrag,
  snapGroupDelta,
  snapResize,
  validateGroupMove,
  widgetAspectRatio,
} from './grid-layout.util';
import { CellDimensions, WIDGET_REFERENCE_CELL_SIZE, WIDGET_REFERENCE_GAP } from '../domain/widget.interface';

const cell: CellDimensions = { cellWidth: 88, cellHeight: 88, gap: 12, padding: 12 };
const step = cell.cellWidth + cell.gap;

function placement(id: string, x: number, y: number, w = 1, h = 1): GridPlacement {
  return { id, x, y, w, h };
}

function locked(id: string, x: number, y: number, w = 1, h = 1): GridPlacement {
  return { id, x, y, w, h, locked: true };
}

function rect(x: number, y: number, w = 1, h = 1): GridRect {
  return { x, y, w, h };
}

describe('computeCellDimensions', () => {
  it('yields the reference gap and padding when the cell lands at the reference size', () => {
    const cols = 5;
    const rows = 3;
    const ratio = WIDGET_REFERENCE_GAP / WIDGET_REFERENCE_CELL_SIZE;
    const width = WIDGET_REFERENCE_CELL_SIZE * (cols + (cols - 1) * ratio + 2 * ratio);
    const dims = computeCellDimensions(width, 10_000, cols, rows);

    expect(dims.cellWidth).toBeCloseTo(WIDGET_REFERENCE_CELL_SIZE, 6);
    expect(dims.gap).toBeCloseTo(WIDGET_REFERENCE_GAP, 6);
    expect(dims.padding).toBeCloseTo(WIDGET_REFERENCE_GAP, 6);
  });

  it('scales gap and padding proportionally with the cell size', () => {
    const large = computeCellDimensions(1000, 600, 5, 3);
    const small = computeCellDimensions(500, 300, 5, 3);

    expect(small.cellWidth).toBeCloseTo(large.cellWidth / 2, 6);
    expect(small.gap).toBeCloseTo(large.gap / 2, 6);
    expect(small.padding).toBeCloseTo(large.padding / 2, 6);
    expect(large.gap / large.cellWidth).toBeCloseTo(WIDGET_REFERENCE_GAP / WIDGET_REFERENCE_CELL_SIZE, 6);
  });

  it('fills the available space exactly on the constrained axis', () => {
    const cols = 4;
    const rows = 2;
    const dims = computeCellDimensions(800, 5000, cols, rows);
    const total = dims.cellWidth * cols + (cols - 1) * dims.gap + 2 * dims.padding;

    expect(total).toBeCloseTo(800, 6);
  });

  it('never returns negative dimensions', () => {
    const dims = computeCellDimensions(-50, 100, 5, 3);

    expect(dims.cellWidth).toBe(0);
    expect(dims.gap).toBe(0);
    expect(dims.padding).toBe(0);
  });

  it('honors a custom gap reference and still fills the constrained axis exactly', () => {
    const cols = 4;
    const rows = 2;
    for (const gapReference of [0, 40]) {
      const dims = computeCellDimensions(800, 5000, cols, rows, gapReference);
      const total = dims.cellWidth * cols + (cols - 1) * dims.gap + 2 * dims.padding;

      expect(total).toBeCloseTo(800, 6);
      expect(dims.gap / (dims.cellWidth || 1)).toBeCloseTo(gapReference / WIDGET_REFERENCE_CELL_SIZE, 6);
    }
  });

  it('produces touching cells at gap reference 0', () => {
    const dims = computeCellDimensions(800, 5000, 4, 2, 0);

    expect(dims.gap).toBe(0);
    expect(dims.padding).toBe(0);
    expect(dims.cellWidth).toBeCloseTo(200, 6);
  });
});

describe('rectsOverlap', () => {
  it('detects overlapping rects', () => {
    expect(rectsOverlap(rect(0, 0, 2, 2), rect(1, 1, 2, 2))).toBeTrue();
  });

  it('treats edge-adjacent rects as non-overlapping', () => {
    expect(rectsOverlap(rect(0, 0, 2, 2), rect(2, 0, 1, 1))).toBeFalse();
    expect(rectsOverlap(rect(0, 0, 2, 2), rect(0, 2, 1, 1))).toBeFalse();
  });
});

describe('clampRectToGrid', () => {
  it('shifts negative origins to zero', () => {
    expect(clampRectToGrid(rect(-2, -1, 2, 2), 5, 5)).toEqual(rect(0, 0, 2, 2));
  });

  it('shifts rects past the far edges back in without shrinking', () => {
    expect(clampRectToGrid(rect(4, 4, 2, 2), 5, 5)).toEqual(rect(3, 3, 2, 2));
  });

  it('leaves in-bounds rects untouched', () => {
    expect(clampRectToGrid(rect(1, 2, 2, 1), 5, 5)).toEqual(rect(1, 2, 2, 1));
  });
});

describe('clampSizeToGrid', () => {
  it('caps width and height at the grid edge', () => {
    expect(clampSizeToGrid(rect(3, 3, 4, 4), 5, 5)).toEqual(rect(3, 3, 2, 2));
  });

  it('enforces a 1x1 minimum', () => {
    expect(clampSizeToGrid(rect(2, 2, 0, -3), 5, 5)).toEqual(rect(2, 2, 1, 1));
  });
});

describe('snapDrag', () => {
  it('does not move below half a cell of travel', () => {
    expect(snapDrag(rect(1, 1), step * 0.49, 0, cell, 5, 5)).toEqual(rect(1, 1));
  });

  it('moves one cell at half a cell of travel', () => {
    expect(snapDrag(rect(1, 1), step * 0.5, 0, cell, 5, 5)).toEqual(rect(2, 1));
  });

  it('snaps both axes independently', () => {
    expect(snapDrag(rect(1, 1), step * 1.2, -step * 0.8, cell, 5, 5)).toEqual(rect(2, 0));
  });

  it('clamps at every edge', () => {
    expect(snapDrag(rect(0, 0, 2, 2), -step * 5, -step * 5, cell, 5, 5)).toEqual(rect(0, 0, 2, 2));
    expect(snapDrag(rect(3, 3, 2, 2), step * 5, step * 5, cell, 5, 5)).toEqual(rect(3, 3, 2, 2));
  });
});

describe('snapResize', () => {
  it('grows by whole cells', () => {
    expect(snapResize(rect(1, 1, 1, 1), step, step * 2, cell, 5, 5)).toEqual(rect(1, 1, 2, 3));
  });

  it('never shrinks below 1x1', () => {
    expect(snapResize(rect(1, 1, 2, 2), -step * 4, -step * 4, cell, 5, 5)).toEqual(rect(1, 1, 1, 1));
  });

  it('caps at the grid edge', () => {
    expect(snapResize(rect(3, 3, 1, 1), step * 9, step * 9, cell, 5, 5)).toEqual(rect(3, 3, 2, 2));
  });
});

describe('snapGroupDelta', () => {
  it('rounds like snapDrag but clamps the whole bounding box, not a single rect', () => {
    const bounds = rect(0, 0, 2, 1);
    expect(snapGroupDelta(bounds, -step * 5, -step * 5, cell, 3, 3)).toEqual({ dx: 0, dy: 0 });
  });

  it('clamps so the bounding box never leaves the grid on the far edge', () => {
    const bounds = rect(0, 0, 2, 1);
    expect(snapGroupDelta(bounds, step * 9, 0, cell, 3, 3)).toEqual({ dx: 1, dy: 0 });
  });

  it('leaves an in-bounds delta untouched', () => {
    const bounds = rect(1, 1, 2, 1);
    expect(snapGroupDelta(bounds, step, 0, cell, 5, 5)).toEqual({ dx: 1, dy: 0 });
  });
});

describe('validateGroupMove', () => {
  it('accepts a shift into cells the group itself vacates', () => {
    const layout = [placement('a', 0, 0), placement('b', 1, 0), placement('c', 2, 0)];
    const forward = validateGroupMove(layout, new Set(['a', 'b', 'c']), 1, 0, 5, 5);
    expect(forward?.get('a')).toEqual(rect(1, 0));
    expect(forward?.get('b')).toEqual(rect(2, 0));
    expect(forward?.get('c')).toEqual(rect(3, 0));

    // Mirror direction with only a subset selected (b, c) - proves the obstacle exclusion (and the
    // clamp/overlap check behind it) is not direction-dependent. 'a' sits well out of the way at x=3
    // so only the move's own validity is exercised, and being a non-member it must not appear in the
    // result even though it is part of the layout.
    const mirrorLayout = [placement('a', 3, 0), placement('b', 1, 0), placement('c', 2, 0)];
    const backward = validateGroupMove(mirrorLayout, new Set(['b', 'c']), -1, 0, 5, 5);
    expect(backward?.get('b')).toEqual(rect(0, 0));
    expect(backward?.get('c')).toEqual(rect(1, 0));
    expect(backward?.has('a')).toBeFalse();
  });

  it('rejects a shift where any member leaves the grid, with no partial map', () => {
    // 'a' alone would have a perfectly legal target at (1,0) - the whole batch must still fail
    // because 'b' would leave the 4-wide grid.
    const layout = [placement('a', 0, 0), placement('b', 3, 0)];
    const result = validateGroupMove(layout, new Set(['a', 'b']), 1, 0, 4, 1);
    expect(result).toBeNull();
  });

  it('rejects a shift where any member lands on an unselected widget, displacing nothing', () => {
    const layout = [placement('a', 0, 0), placement('obstacle', 2, 0)];
    const result = validateGroupMove(layout, new Set(['a']), 2, 0, 5, 5);
    expect(result).toBeNull();
  });

  it('preserves relative offsets for differently-sized members moved diagonally', () => {
    const layout = [placement('wide', 0, 0, 2, 1), placement('tall', 0, 1, 1, 2)];
    const result = validateGroupMove(layout, new Set(['wide', 'tall']), 1, 1, 5, 5);
    expect(result?.get('wide')).toEqual(rect(1, 1, 2, 1));
    expect(result?.get('tall')).toEqual(rect(1, 2, 1, 2));
  });
});

describe('canPlaceGroup', () => {
  it('accepts a group that fits with no overlap', () => {
    const rects = [rect(0, 0, 2, 1), rect(2, 1, 1, 2)];
    expect(canPlaceGroup(rects, 5, 5, [])).toBeTrue();
  });

  it('rejects a group that does not fit at the anchor rather than relocating or shrinking it', () => {
    const rects = [rect(0, 0, 2, 1), rect(4, 1, 2, 1)];
    expect(canPlaceGroup(rects, 5, 5, [])).toBeFalse();
  });

  it('rejects when any single member leaves the grid, even if the rest fit', () => {
    const rects = [rect(0, 0, 1, 1), rect(4, 4, 2, 1)];
    expect(canPlaceGroup(rects, 5, 5, [])).toBeFalse();
  });

  it('rejects when any member overlaps an occupied rect', () => {
    const rects = [rect(0, 0, 1, 1), rect(1, 0, 1, 1)];
    expect(canPlaceGroup(rects, 5, 5, [rect(1, 0, 1, 1)])).toBeFalse();
  });

  it('does not reject on an occupied rect that is excluded (e.g. a same-folder cut source)', () => {
    const rects = [rect(1, 0, 1, 1)];
    expect(canPlaceGroup(rects, 5, 5, [])).toBeTrue();
  });

  it('accepts an empty group vacuously', () => {
    expect(canPlaceGroup([], 5, 5, [rect(0, 0, 5, 5)])).toBeTrue();
  });
});

describe('reflowLayout', () => {
  it('keeps everything in place when the candidate hits nothing', () => {
    const widgets = [placement('a', 0, 0), placement('b', 3, 3)];
    const result = reflowLayout(widgets, 'a', rect(1, 1), 5, 5);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('a')).toEqual(rect(1, 1));
    expect(result.placements.get('b')).toEqual(rect(3, 3));
  });

  it('returns the original layout when the candidate is the original rect', () => {
    const widgets = [placement('a', 0, 0, 2, 2), placement('b', 2, 0), placement('c', 0, 2)];
    const result = reflowLayout(widgets, 'a', rect(0, 0, 2, 2), 5, 5);
    expect(result.ok).toBeTrue();
    for (const widget of widgets) {
      expect(result.placements.get(widget.id)).toEqual(rect(widget.x, widget.y, widget.w, widget.h));
    }
  });

  it('swaps a 1x1 pair when dragging onto a direct neighbour', () => {
    const widgets = [placement('a', 0, 0), placement('b', 1, 0)];
    const result = reflowLayout(widgets, 'a', rect(1, 0), 5, 5);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('a')).toEqual(rect(1, 0));
    expect(result.placements.get('b')).toEqual(rect(0, 0));
  });

  it('pushes the displaced widget to the nearest adjacent cell rather than the vacated one', () => {
    const widgets = [placement('a', 0, 0), placement('b', 2, 0)];
    const result = reflowLayout(widgets, 'a', rect(2, 0), 4, 1);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('a')).toEqual(rect(2, 0));
    expect(result.placements.get('b')).toEqual(rect(1, 0));
  });

  it('cascades displacement when the nearest spot is another widget\'s cell', () => {
    const widgets = [placement('a', 0, 0, 2, 1), placement('b', 2, 0), placement('c', 3, 0)];
    const result = reflowLayout(widgets, 'a', rect(1, 0, 2, 1), 4, 1);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('a')).toEqual(rect(1, 0, 2, 1));
    expect(result.placements.get('b')).toEqual(rect(3, 0));
    expect(result.placements.get('c')).toEqual(rect(0, 0));
    expectNoOverlaps(result.placements);
  });

  it('resolves a full grid drag as a swap', () => {
    const widgets = [placement('a', 0, 0), placement('b', 1, 0), placement('c', 0, 1), placement('d', 1, 1)];
    const result = reflowLayout(widgets, 'a', rect(1, 0), 2, 2);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('a')).toEqual(rect(1, 0));
    expect(result.placements.get('b')).toEqual(rect(0, 0));
    expect(result.placements.get('c')).toEqual(rect(0, 1));
    expect(result.placements.get('d')).toEqual(rect(1, 1));
    expectNoOverlaps(result.placements);
  });

  it('reports failure for an unknown moved id', () => {
    expect(reflowLayout([placement('a', 0, 0)], 'nope', rect(1, 1), 5, 5).ok).toBeFalse();
  });

  it('handles multi-cell widgets pushing multi-cell widgets', () => {
    const widgets = [placement('big', 0, 0, 2, 2), placement('wide', 2, 0, 2, 1)];
    const result = reflowLayout(widgets, 'big', rect(2, 0, 2, 2), 5, 5);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('big')).toEqual(rect(2, 0, 2, 2));
    expectNoOverlaps(result.placements);
    const wide = result.placements.get('wide')!;
    expect(wide.w).toBe(2);
    expect(wide.h).toBe(1);
  });

  it('is deterministic', () => {
    const widgets = [
      placement('a', 0, 0), placement('b', 1, 0), placement('c', 2, 0),
      placement('d', 0, 1), placement('e', 1, 1, 2, 1),
    ];
    const first = reflowLayout(widgets, 'a', rect(1, 0), 4, 4);
    const second = reflowLayout(widgets, 'a', rect(1, 0), 4, 4);
    expect([...first.placements.entries()]).toEqual([...second.placements.entries()]);
  });

  it('supports resize candidates pushing neighbours', () => {
    const widgets = [placement('a', 0, 0), placement('b', 1, 0)];
    const result = reflowLayout(widgets, 'a', rect(0, 0, 2, 1), 3, 3);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('a')).toEqual(rect(0, 0, 2, 1));
    expectNoOverlaps(result.placements);
  });

  it('reports failure for a resize that cannot be accommodated', () => {
    const widgets = [placement('a', 0, 0), placement('b', 1, 0), placement('c', 0, 1), placement('d', 1, 1)];
    const result = reflowLayout(widgets, 'a', rect(0, 0, 2, 1), 2, 2);
    expect(result.ok).toBeFalse();
  });
});

describe('reflowLayout with locked (pinned) widgets', () => {
  it('rejects a candidate that would cover a locked widget', () => {
    const widgets = [placement('a', 0, 0), locked('p', 2, 0)];
    const result = reflowLayout(widgets, 'a', rect(2, 0), 5, 5);
    expect(result.ok).toBeFalse();
  });

  it('never displaces a locked widget, pushing the loser elsewhere instead', () => {
    const widgets = [placement('a', 0, 0, 2, 1), placement('b', 2, 0), locked('p', 3, 0)];
    const result = reflowLayout(widgets, 'a', rect(1, 0, 2, 1), 4, 1);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('p')).toEqual(rect(3, 0));
    expect(result.placements.get('b')).toEqual(rect(0, 0));
    expectNoOverlaps(result.placements);
  });

  it('fails when the only free spot for a displaced widget is locked', () => {
    const widgets = [placement('a', 0, 0), placement('b', 1, 0), locked('p', 2, 0)];
    const result = reflowLayout(widgets, 'a', rect(1, 0), 3, 1);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('b')).toEqual(rect(0, 0));

    const noRoom = [placement('a', 0, 0), locked('p', 1, 0), locked('q', 2, 0)];
    expect(reflowLayout(noRoom, 'a', rect(1, 0), 3, 1).ok).toBeFalse();
  });

  it('lets a locked widget itself move when it is the dragged one', () => {
    const widgets = [locked('p', 0, 0), placement('a', 2, 0)];
    const result = reflowLayout(widgets, 'p', rect(1, 0), 4, 1);
    expect(result.ok).toBeTrue();
    expect(result.placements.get('p')).toEqual(rect(1, 0));
  });
});

describe('computePasteRect', () => {
  it('keeps the desired size when it fits at the anchor', () => {
    expect(computePasteRect(0, 0, 2, 2, 5, 3, [])).toEqual(rect(0, 0, 2, 2));
  });

  it('keeps the desired size around other widgets when there is room', () => {
    const occupied = [rect(3, 0, 2, 1)];
    expect(computePasteRect(0, 0, 2, 2, 5, 3, occupied)).toEqual(rect(0, 0, 2, 2));
  });

  it('shrinks to the grid bounds when the desired size would overflow the edge', () => {
    expect(computePasteRect(4, 0, 2, 1, 5, 3, [])).toEqual(rect(4, 0, 1, 1));
    expect(computePasteRect(0, 2, 1, 2, 5, 3, [])).toEqual(rect(0, 2, 1, 1));
  });

  it('shrinks the width to avoid an occupied column while keeping the height', () => {
    const occupied = [rect(1, 0, 1, 3)];
    expect(computePasteRect(0, 0, 3, 2, 5, 3, occupied)).toEqual(rect(0, 0, 1, 2));
  });

  it('chooses the largest-area rectangle that fits, not just the first row run', () => {
    const occupied = [rect(2, 2, 1, 1)];
    expect(computePasteRect(0, 0, 3, 3, 3, 3, occupied)).toEqual(rect(0, 0, 3, 2));
  });

  it('always fits a 1x1 at a free anchor even when fully boxed in', () => {
    const occupied = [rect(1, 0, 1, 1), rect(0, 1, 1, 1)];
    expect(computePasteRect(0, 0, 3, 3, 5, 3, occupied)).toEqual(rect(0, 0, 1, 1));
  });

  it('returns null when the anchor cell itself is occupied', () => {
    expect(computePasteRect(0, 0, 1, 1, 5, 3, [rect(0, 0, 2, 2)])).toBeNull();
  });

  it('returns null when the anchor is out of bounds', () => {
    expect(computePasteRect(5, 0, 1, 1, 5, 3, [])).toBeNull();
    expect(computePasteRect(0, 3, 1, 1, 5, 3, [])).toBeNull();
    expect(computePasteRect(-1, 0, 1, 1, 5, 3, [])).toBeNull();
  });
});

function expectNoOverlaps(placements: ReadonlyMap<string, GridRect>): void {
  const entries = [...placements.entries()];
  for (let i = 0; i < entries.length; i++) {
    for (let j = i + 1; j < entries.length; j++) {
      expect(rectsOverlap(entries[i][1], entries[j][1]))
        .withContext(`${entries[i][0]} overlaps ${entries[j][0]}`)
        .toBeFalse();
    }
  }
}

describe('cellFromPoint', () => {
  it('round-trips against the positions the grid renders at', () => {
    for (let y = 0; y < 3; y++) {
      for (let x = 0; x < 5; x++) {
        const left = cell.padding + x * step;
        const top = cell.padding + y * step;

        expect(cellFromPoint(left, top, cell, 5, 3)).toEqual({ x, y });
        expect(cellFromPoint(left + cell.cellWidth - 1, top + cell.cellHeight - 1, cell, 5, 3)).toEqual({ x, y });
      }
    }
  });

  it('rejects the gap between two cells', () => {
    const inTheGap = cell.padding + cell.cellWidth + cell.gap / 2;

    expect(cellFromPoint(inTheGap, cell.padding, cell, 5, 3)).toBeNull();
  });

  it('rejects the padding and anything past the last cell', () => {
    expect(cellFromPoint(cell.padding - 1, cell.padding, cell, 5, 3)).toBeNull();
    expect(cellFromPoint(cell.padding, cell.padding - 1, cell, 5, 3)).toBeNull();
    expect(cellFromPoint(cell.padding + 5 * step, cell.padding, cell, 5, 3)).toBeNull();
    expect(cellFromPoint(cell.padding, cell.padding + 3 * step, cell, 5, 3)).toBeNull();
  });

  // Before the first layout pass every dimension is zero; asking then must not divide by zero.
  it('answers null for an unmeasured grid', () => {
    const unmeasured: CellDimensions = { cellWidth: 0, cellHeight: 0, gap: 0, padding: 0 };

    expect(cellFromPoint(0, 0, unmeasured, 5, 3)).toBeNull();
  });
});

describe('widgetAspectRatio', () => {
  it('is square for a single cell', () => {
    expect(widgetAspectRatio(1, 1)).toBe(1);
  });

  it('counts the gap between cells, so a 2x1 is wider than 2:1', () => {
    expect(widgetAspectRatio(2, 1)).toBeCloseTo(2 + DEFAULT_CELL_GAP_RATIO, 5);
  });

  it('honors a folder spacing that differs from the default', () => {
    expect(widgetAspectRatio(3, 2, 0)).toBeCloseTo(1.5, 5);
  });

  it('treats a missing span as a single cell', () => {
    expect(widgetAspectRatio(0, 0)).toBe(1);
  });
});
