import { GridMetrics, isCellOccupied } from './grid-metrics';
import { GridRect } from './grid-layout.util';

function metrics(cols = 3, rows = 2): GridMetrics {
  const m = new GridMetrics();
  m.configure({ cols, rows, spacing: 12, outerMargin: 10 });
  // 3 columns of 60 + 2 gaps of 6 + 2 paddings of 6 = 204, plus 2x10 outer margin.
  m.measure(224, 20 + 2 * 60 + 6 + 2 * 6);
  return m;
}

describe('GridMetrics', () => {
  it('solves a square cell that fits the box after the outer margin is taken off', () => {
    const m = metrics();

    expect(m.cell.cellWidth).toBeCloseTo(60, 9);
    expect(m.cell.cellHeight).toBeCloseTo(60, 9);
    expect(m.gap).toBeCloseTo(6, 9);
    expect(m.padding).toBeCloseTo(6, 9);
  });

  it('reports the grid box as cells plus the gaps and padding between them', () => {
    const m = metrics();

    expect(m.width).toBeCloseTo(3 * 60 + 2 * 6 + 2 * 6, 9);
    expect(m.height).toBeCloseTo(2 * 60 + 1 * 6 + 2 * 6, 9);
  });

  it('places a cell at padding plus its share of cell-and-gap strides', () => {
    const m = metrics();

    expect(m.left({ x: 0, y: 0, w: 1, h: 1 })).toBeCloseTo(6, 9);
    expect(m.left({ x: 1, y: 0, w: 1, h: 1 })).toBeCloseTo(6 + 66, 9);
    expect(m.left({ x: 2, y: 0, w: 1, h: 1 })).toBeCloseTo(6 + 132, 9);
    expect(m.top({ x: 0, y: 1, w: 1, h: 1 })).toBeCloseTo(6 + 66, 9);
  });

  it('sizes a spanning rect to cover the gaps it straddles', () => {
    const m = metrics();

    expect(m.widthOf({ x: 0, y: 0, w: 1, h: 1 })).toBeCloseTo(60, 9);
    expect(m.widthOf({ x: 0, y: 0, w: 2, h: 1 })).toBeCloseTo(126, 9);
    expect(m.heightOf({ x: 0, y: 0, w: 1, h: 2 })).toBeCloseTo(126, 9);
  });

  it('round-trips a cell through its own placement', () => {
    const m = metrics();

    for (let index = 0; index < m.cellCount; index++) {
      const rect = m.cellRect(index);
      const hit = m.cellAt(m.left(rect) + 1, m.top(rect) + 1);

      expect(hit).withContext(`cell ${index}`).toEqual({ x: rect.x, y: rect.y });
      expect(m.cellIndex(rect.x, rect.y)).toBe(index);
    }
  });

  it('reports no cell for a point in a gap or outside the grid', () => {
    const m = metrics();
    const firstGapX = 6 + 60 + 3;

    expect(m.cellAt(firstGapX, 10)).toBeNull();
    expect(m.cellAt(2, 10)).toBeNull();
    expect(m.cellAt(10_000, 10)).toBeNull();
    expect(m.cellAt(10, -5)).toBeNull();
  });

  it('scales content by the cell size against the 120px reference, and by 1 before it has one', () => {
    const m = metrics();

    expect(m.contentScale).toBeCloseTo(0.5, 9);
    expect(new GridMetrics().contentScale).toBe(1);
  });

  it('re-solves after the grid shape changes', () => {
    const m = metrics();
    const wide = m.cell.cellWidth;

    m.configure({ cols: 6 });

    expect(m.measure(224, 20 + 2 * 60 + 6 + 2 * 6)).toBeTrue();
    expect(m.cell.cellWidth).toBeLessThan(wide);
    expect(m.cols).toBe(6);
  });

  it('reports an unchanged box as no change, so a caller can skip re-laying out', () => {
    const m = new GridMetrics();
    m.configure({ cols: 3, rows: 2, spacing: 12, outerMargin: 10 });

    expect(m.measure(300, 200)).toBeTrue();
    expect(m.measure(300, 200)).toBeFalse();
    expect(m.measure(301, 200)).toBeTrue();
  });

  it('never solves a negative cell for a box smaller than its own margins', () => {
    const m = new GridMetrics();
    m.configure({ cols: 3, rows: 2, spacing: 12, outerMargin: 40 });
    m.measure(10, 10);

    expect(m.cell.cellWidth).toBeCloseTo(0, 9);
    expect(m.width).toBeCloseTo(0, 9);
  });
});

describe('isCellOccupied', () => {
  const rects: GridRect[] = [{ x: 1, y: 0, w: 2, h: 1 }, { x: 0, y: 2, w: 1, h: 2 }];

  it('reports every cell a rect covers, not just its origin', () => {
    expect(isCellOccupied(rects, 1, 0)).toBeTrue();
    expect(isCellOccupied(rects, 2, 0)).toBeTrue();
    expect(isCellOccupied(rects, 0, 3)).toBeTrue();
  });

  it('reports free cells next to and past a rect as free', () => {
    expect(isCellOccupied(rects, 0, 0)).toBeFalse();
    expect(isCellOccupied(rects, 3, 0)).toBeFalse();
    expect(isCellOccupied(rects, 1, 1)).toBeFalse();
    expect(isCellOccupied([], 0, 0)).toBeFalse();
  });
});
