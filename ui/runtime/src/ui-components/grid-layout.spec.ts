import { UiNode } from '../ui-framework/ui-node.interface';
import { chooseColumns, placeGridChildren } from './grid-layout';

function rings(count: number, spans: Record<number, number> = {}): UiNode {
  const children: UiNode[] = [];
  for (let index = 0; index < count; index++) {
    children.push({
      id: `r${index}`,
      type: 'ui.layer',
      properties: spans[index] === undefined ? {} : { columnSpan: spans[index] },
    });
  }
  return { id: 'grid', type: 'ui.grid', properties: { columns: 1 }, children };
}

const choose = (node: UiNode, width: number | null, height: number | null, minCell = 30, gap = 0, padding = 0) =>
  chooseColumns(node, width, height, padding, gap, minCell);

describe('choosing the column count', () => {
  describe('with a definite height the cells are made as large as possible', () => {
    it('arranges fifteen rings per the largest smaller cell edge', () => {
      expect(choose(rings(15), 120, 120)).toBe(4);
      expect(choose(rings(15), 240, 120)).toBe(5);
      expect(choose(rings(15), 120, 240)).toBe(3);
      expect(choose(rings(15), 360, 240)).toBe(5);
    });

    it('arranges two rings per the largest smaller cell edge, fewer columns on a tie', () => {
      expect(choose(rings(2), 120, 120)).toBe(1);
      expect(choose(rings(2), 120, 240)).toBe(1);
      expect(choose(rings(2), 240, 120)).toBe(2);
    });

    it('is not moved by the minimum, which only the open height uses', () => {
      for (const minCell of [1, 30, 42, 500]) expect(choose(rings(15), 240, 120, minCell)).toBe(5);
    });

    it('subtracts the gap between cells', () => {
      expect(choose(rings(2), 120, 120, 30, 20)).toBe(1);
    });

    it('counts a spanning child in the rows a column count needs', () => {
      const node = rings(4, { 0: 2 });

      expect(choose(node, 120, 120)).toBe(2);
      expect(placeGridChildren(node, 2).rows).toBe(3);
    });
  });

  describe('with an open height rows are as tall as a column is wide', () => {
    it('takes the largest count whose cells are at least the minimum', () => {
      expect(choose(rings(6), 240, null, 60)).toBe(4);
      expect(choose(rings(6), 240, null, 100)).toBe(2);
      expect(choose(rings(6), 480, null, 60)).toBe(6);
    });

    it('takes one column when nothing is wide enough', () => {
      expect(choose(rings(6), 240, null, 1000)).toBe(1);
    });

    it('never uses more columns than there are children', () => {
      expect(choose(rings(3), 240, null, 10)).toBe(3);
    });
  });

  describe('when it does not apply', () => {
    it('leaves the arrangement to Columns and Rows without a usable minimum or width', () => {
      expect(chooseColumns(rings(6), 240, 240, 0, 0, undefined)).toBeUndefined();
      expect(chooseColumns(rings(6), 240, 240, 0, 0, 0)).toBeUndefined();
      expect(chooseColumns(rings(6), 240, 240, 0, 0, -5)).toBeUndefined();
      expect(chooseColumns(rings(6), 240, 240, 0, 0, Number.NaN)).toBeUndefined();
      expect(chooseColumns(rings(6), null, 240, 0, 0, 30)).toBeUndefined();
    });

    it('places every child, ignoring Rows, once the count is chosen', () => {
      const node = rings(6);
      node.properties = { columns: 1, rows: 1 };

      expect(placeGridChildren(node, 3).placements.length).toBe(6);
      expect(placeGridChildren(node).placements.length).toBe(1);
    });
  });

  it('stays cheap for hundreds of children with spans', () => {
    const spans: Record<number, number> = {};
    for (let index = 0; index < 500; index += 7) spans[index] = 2;
    const started = Date.now();

    const columns = choose(rings(500, spans), 960, 540);

    expect(columns).toBeGreaterThanOrEqual(1);
    expect(columns).toBeLessThanOrEqual(64);
    expect(Date.now() - started).toBeLessThan(5000);
  });
});
