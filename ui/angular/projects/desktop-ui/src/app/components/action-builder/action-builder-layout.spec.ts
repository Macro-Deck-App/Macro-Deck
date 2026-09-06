import { isToolbarCompact, measureToolbarNaturalWidth } from './action-builder-layout';

describe('isToolbarCompact', () => {
  it('stays a single row while the content fits', () => {
    expect(isToolbarCompact(700, 800, false)).toBeFalse();
  });

  it('splits into two rows once the content no longer fits', () => {
    expect(isToolbarCompact(900, 800, false)).toBeTrue();
  });

  it('treats an exact fit as fitting', () => {
    expect(isToolbarCompact(800, 800, false)).toBeFalse();
  });

  it('holds the two-row layout until there is room to spare, so it cannot flip on one pixel', () => {
    expect(isToolbarCompact(799, 800, true)).toBeTrue();
    expect(isToolbarCompact(790, 800, true)).toBeFalse();
  });

  it('does not apply that slack on the way into the two-row layout', () => {
    expect(isToolbarCompact(799, 800, false)).toBeFalse();
  });
});

describe('measureToolbarNaturalWidth', () => {
  let row: HTMLElement;

  const group = (widths: number[]): HTMLElement => {
    const el = document.createElement('div');
    el.className = 'tab-group';
    el.style.cssText = 'display: flex; column-gap: 10px;';
    for (const width of widths) {
      const child = document.createElement('div');
      child.style.cssText = `width: ${width}px; height: 10px; flex: 0 0 auto;`;
      el.append(child);
    }
    return el;
  };

  beforeEach(() => {
    row = document.createElement('div');
    row.className = 'tab-row';
    document.body.append(row);
  });

  afterEach(() => row.remove());

  it('is zero when the row has no groups yet', () => {
    expect(measureToolbarNaturalWidth(row)).toBe(0);
  });

  it('sums the groups and the gaps between and inside them', () => {
    row.append(group([100, 50]), group([40]));

    expect(measureToolbarNaturalWidth(row)).toBe(210);
  });

  it('measures a tab bar by its scroll content, not by the width it was squeezed into', () => {
    const bar = document.createElement('shared-tab-bar');
    bar.style.cssText = 'display: inline-flex; width: 60px; overflow: hidden;';
    const inner = document.createElement('div');
    inner.className = 'tab-bar';
    inner.style.cssText = 'display: inline-flex; width: 60px; overflow-x: auto;';
    const content = document.createElement('div');
    content.style.cssText = 'width: 300px; height: 10px; flex: 0 0 auto;';
    inner.append(content);
    bar.append(inner);

    const holder = document.createElement('div');
    holder.className = 'tab-group';
    holder.style.cssText = 'display: flex; column-gap: 10px;';
    holder.append(bar);
    row.append(holder);

    expect(measureToolbarNaturalWidth(row)).toBeGreaterThanOrEqual(300);
  });
});
