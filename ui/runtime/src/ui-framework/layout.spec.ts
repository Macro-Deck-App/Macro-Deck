import { UiNode } from './ui-node.interface';
import { UI_COMPONENT_CELL } from './length';
import {
  intrinsicMainPx,
  layoutStackChildren,
  UiComponentBox,
  UiIntrinsicMetrics,
  WIDGET_FIELD_BORDER_PX,
  WIDGET_FIELD_LINE_HEIGHT,
  WIDGET_FIELD_PADDING_EM,
} from './layout';
import { DEFAULT_UI_COMPONENT_REGISTRY } from './component-registry';

let nextId = 0;
function node(type: string, properties: Record<string, unknown> = {}, children?: UiNode[]): UiNode {
  const built: UiNode = { id: `n${++nextId}`, type, properties } as UiNode;
  if (children) (built as { children?: UiNode[] }).children = children;
  return built;
}

const box = (width: number | null, height: number | null): UiComponentBox => ({ width, height });
const len = (basis: number, extra: Record<string, number> = {}) => ({ basis, ...extra });

function metrics(basis: number, crossExtent: number | null, horizontal: boolean): UiIntrinsicMetrics {
  const m: UiIntrinsicMetrics = {
    basis, crossExtent, horizontal,
    ofChild: child => intrinsicMainPx(child, m, DEFAULT_UI_COMPONENT_REGISTRY),
  };
  return m;
}

describe('stack layout', () => {
  const BASIS = 100;

  it('resolves a declared main size as a fraction of the basis', () => {
    const child = node('ui.text', { mainSize: len(0.25) });
    const [entry] = layoutStackChildren(
      node('ui.stack', {}, [child]), box(200, 400), BASIS, 0, 0, false, DEFAULT_UI_COMPONENT_REGISTRY);

    expect(entry.box.height).toBe(25);
  });

  it('hands a child the stack cross extent, not the stack width', () => {
    const child = node('ui.text', { mainSize: len(0.1) });
    const [entry] = layoutStackChildren(
      node('ui.stack', {}, [child]), box(200, 400), BASIS, 20, 0, false, DEFAULT_UI_COMPONENT_REGISTRY);

    // Padding is removed from both axes before anything is divided.
    expect(entry.crossExtent).toBe(160);
    expect(entry.box.width).toBe(160);
  });

  it('clamps a length against the cross extent when maxOfCross is declared', () => {
    const child = node('ui.text', { mainSize: len(1, { maxOfCross: 0.5 }) });
    const [entry] = layoutStackChildren(
      node('ui.stack', {}, [child]), box(80, 400), BASIS, 0, 0, false, DEFAULT_UI_COMPONENT_REGISTRY);

    // basis alone would be 100; half of the 80px cross extent wins.
    expect(entry.box.height).toBe(40);
  });

  it('clamps a length against one deck cell when maxOfCell is declared', () => {
    const child = node('ui.text', { mainSize: len(4, { maxOfCell: 0.5 }) });
    const [entry] = layoutStackChildren(
      node('ui.stack', {}, [child]), box(400, 800), BASIS, 0, 0, false, DEFAULT_UI_COMPONENT_REGISTRY);

    expect(entry.box.height).toBe(UI_COMPONENT_CELL * 0.5);
  });

  it('gives a fill child what the gaps and the fixed children leave', () => {
    const fixed = node('ui.text', { mainSize: len(0.5) });
    const filler = node('ui.text', { fill: true });
    const [, entry] = layoutStackChildren(
      node('ui.stack', {}, [fixed, filler]), box(200, 300), BASIS, 0, 20, false, DEFAULT_UI_COMPONENT_REGISTRY);

    // 300 content - 20 gap - 50 fixed
    expect(entry.box.height).toBe(230);
  });

  it('splits the remainder evenly between several fill children', () => {
    const children = [
      node('ui.text', { fill: true }),
      node('ui.text', { fill: true }),
      node('ui.text', { fill: true }),
    ];
    const entries = layoutStackChildren(
      node('ui.stack', {}, children), box(200, 260), BASIS, 0, 10, false, DEFAULT_UI_COMPONENT_REGISTRY);

    // 260 - 2 gaps of 10 = 240, three ways
    expect(entries.map(entry => entry.box.height)).toEqual([80, 80, 80]);
  });

  it('never hands a fill child a negative extent when the fixed children overflow', () => {
    const fixed = node('ui.text', { mainSize: len(3) });
    const filler = node('ui.text', { fill: true });
    const [, entry] = layoutStackChildren(
      node('ui.stack', {}, [fixed, filler]), box(200, 100), BASIS, 0, 0, false, DEFAULT_UI_COMPONENT_REGISTRY);

    expect(entry.box.height).toBe(0);
  });

  it('swaps which axis is the main one for a horizontal stack', () => {
    const child = node('ui.text', { mainSize: len(0.3) });
    const [entry] = layoutStackChildren(
      node('ui.stack', {}, [child]), box(400, 200), BASIS, 0, 0, true, DEFAULT_UI_COMPONENT_REGISTRY);

    expect(entry.box.width).toBe(30);
    expect(entry.box.height).toBe(200);
    expect(entry.crossExtent).toBe(200);
  });

  it('leaves the main extent undetermined when the parent box is open on that axis', () => {
    const filler = node('ui.text', { fill: true });
    const [entry] = layoutStackChildren(
      node('ui.stack', {}, [filler]), box(200, null), BASIS, 0, 0, false, DEFAULT_UI_COMPONENT_REGISTRY);

    expect(entry.box.height).toBeNull();
  });

  it('lays out nothing for a stack with no children', () => {
    expect(layoutStackChildren(node('ui.stack'), box(200, 200), BASIS, 0, 0, false, DEFAULT_UI_COMPONENT_REGISTRY))
      .toEqual([]);
  });
});

describe('intrinsic main extent', () => {
  const BASIS = 100;

  it('is the declared main size when there is one', () => {
    expect(intrinsicMainPx(node('ui.text', { mainSize: len(0.4) }), metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(40);
  });

  it('gives a text node room for every line it is allowed', () => {
    const text = node('ui.text', { size: len(0.2), maxLines: 3 });

    expect(intrinsicMainPx(text, metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(60);
  });

  it('treats a text node as contributing nothing along a row', () => {
    // Its width comes from the row, so counting its font size as width would squeeze its siblings.
    const text = node('ui.text', { size: len(0.2), maxLines: 3 });

    expect(intrinsicMainPx(text, metrics(BASIS, null, true), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(0);
  });

  it('sizes an image and a clock dial by their declared size on either axis', () => {
    for (const type of ['ui.image', 'macrodeck.clock-dial']) {
      const built = node(type, { size: len(0.6) });

      expect(intrinsicMainPx(built, metrics(BASIS, null, true), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(60);
      expect(intrinsicMainPx(built, metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(60);
    }
  });

  it('sizes a bar by its thickness across the axis it runs along', () => {
    const bar = node('ui.range-bar', { thickness: len(0.08) });

    expect(intrinsicMainPx(bar, metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(8);
    expect(intrinsicMainPx(bar, metrics(BASIS, null, true), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(0);
  });

  it('makes a layer as large as its largest child', () => {
    const layer = node('ui.layer', {}, [
      node('ui.image', { size: len(0.2) }),
      node('ui.image', { size: len(0.7) }),
    ]);

    expect(intrinsicMainPx(layer, metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(70);
  });

  it('adds up a nested stack along its own axis, including gaps and padding', () => {
    const inner = node('ui.stack', { padding: len(0.05), gap: len(0.1) }, [
      node('ui.image', { size: len(0.2) }),
      node('ui.image', { size: len(0.3) }),
    ]);

    // 20 + 30 children, one 10px gap, 5px padding on each side
    expect(intrinsicMainPx(inner, metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(70);
  });

  it('takes the widest child of a nested stack that runs across the measured axis', () => {
    const inner = node('ui.stack', { direction: 'horizontal', padding: len(0.05) }, [
      node('ui.image', { size: len(0.2) }),
      node('ui.image', { size: len(0.3) }),
    ]);

    expect(intrinsicMainPx(inner, metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(40);
  });

  it('contributes nothing for a type it does not know', () => {
    expect(intrinsicMainPx(node('ui.something-new', { size: len(0.5) }), metrics(BASIS, null, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(0);
  });
});

describe('a stack holding a field and a filling list', () => {
  const field = (size: number) =>
    ({ id: 'search', type: 'ui.text-field', properties: { size: { basis: size } } }) as UiNode;

  const list = () =>
    ({ id: 'results', type: 'ui.list', properties: { fill: true }, children: [] }) as UiNode;

  const stack = () =>
    ({ id: 'root', type: 'ui.stack', properties: {}, children: [field(0.1), list()] }) as UiNode;

  it('leaves the list only what the field did not take', () => {
    const laid = layoutStackChildren(
      stack(), { width: 300, height: 300 }, 100, 10, 6, false, DEFAULT_UI_COMPONENT_REGISTRY);

    // The field sizes itself - a child with neither a main size nor `fill` is told nothing about that
    // axis - but the stack still has to know what it will take, or the filling sibling is handed the
    // whole box and overflows the stack by exactly the field's height.
    const fieldHeight = 10 * (WIDGET_FIELD_LINE_HEIGHT + 2 * WIDGET_FIELD_PADDING_EM)
      + 2 * WIDGET_FIELD_BORDER_PX;

    expect(laid[0].box.height).toBeNull();
    expect(laid[1].box.height).toBeCloseTo(300 - 2 * 10 - fieldHeight - 6, 5);
  });

  it('gives a list no intrinsic height of its own', () => {
    // Its main axis has no bound, so it is only ever sized from outside.
    expect(intrinsicMainPx(list(), metrics(100, 300, false), DEFAULT_UI_COMPONENT_REGISTRY)).toBe(0);
  });
});
