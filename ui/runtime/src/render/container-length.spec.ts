import { UiNode } from '../ui-framework/ui-node.interface';
import { renderUiNode } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';

function host(): UiRenderHost {
  return {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: () => null,
    now: () => 0,
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
  };
}

const text = (id: string, size: Record<string, number>, extra: Record<string, unknown> = {}): UiNode =>
  ({ id, type: 'ui.text', properties: { text: id, size, ...extra } });

const stack = (id: string, children: UiNode[], properties: Record<string, unknown> = {}): UiNode =>
  ({ id, type: 'ui.stack', properties, children });

describe('lengths relative to the containing box, rendered', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const element = (id: string) => container.querySelector(`[data-node-id="${id}"]`) as HTMLElement;
  const fontSize = (id: string) => parseFloat(element(id).style.fontSize);
  const height = (id: string) => parseFloat(element(id).style.height);

  const half = { basis: 0.05, ofParent: 0.5 };

  it('sizes a layer child from the layer, not from the widget', () => {
    const root = stack('root', [{ id: 'layer', type: 'ui.layer', properties: { fill: true }, children: [text('label', half)] }]);

    renderUiNode(container, root, { width: 200, height: 100 }, null, 100, host());

    expect(fontSize('label')).toBeCloseTo(50, 6);
  });

  it('sizes a stack child from the stack content box', () => {
    const root = stack('root', [stack('inner', [text('label', { basis: 0.05, ofParent: 0.25 })], { fill: true, padding: { basis: 0.1 } })]);

    renderUiNode(container, root, { width: 200, height: 100 }, null, 100, host());

    expect(fontSize('label')).toBeCloseTo(0.25 * 80, 6);
  });

  it('falls back to the basis where the containing box has no definite height', () => {
    const root = stack('root', [stack('natural', [text('label', half)])]);

    renderUiNode(container, root, { width: 200, height: 100 }, null, 100, host());

    expect(fontSize('label')).toBeCloseTo(5, 6);
  });

  it('measures and paints a nested intrinsic stack with the same fallback so its sibling gets the rest', () => {
    const root = stack('root', [
      stack('outer', [stack('nested', [text('label', half)])]),
      stack('rest', [], { fill: true }),
    ]);

    renderUiNode(container, root, { width: 200, height: 100 }, null, 100, host());

    expect(fontSize('label')).toBeCloseTo(5, 6);
    expect(height('rest')).toBeCloseTo(95, 6);
  });

  it('measures and paints a layer child of an intrinsic stack child with the same fallback', () => {
    const root = stack('root', [
      stack('outer', [{ id: 'layer', type: 'ui.layer', properties: {}, children: [text('label', half)] }]),
      stack('rest', [], { fill: true }),
    ]);

    renderUiNode(container, root, { width: 200, height: 100 }, null, 100, host());

    expect(fontSize('label')).toBeCloseTo(5, 6);
    expect(height('rest')).toBeCloseTo(95, 6);
  });

  it('gives a filling layer inside a stack a definite box for its children', () => {
    const root = stack('root', [{ id: 'layer', type: 'ui.layer', properties: { fill: true }, children: [text('label', half)] }]);

    renderUiNode(container, root, { width: 200, height: 40 }, null, 100, host());

    expect(fontSize('label')).toBeCloseTo(20, 6);
  });

  it('gives a list child no definite box along its scroll axis', () => {
    const list: UiNode = { id: 'list', type: 'ui.list', properties: { fill: true }, children: [text('row', half)] };

    renderUiNode(container, stack('root', [list]), { width: 200, height: 100 }, null, 100, host());

    expect(fontSize('row')).toBeCloseTo(5, 6);
  });

  it('sizes a modifier child from the box left after the modifier padding', () => {
    const modifier: UiNode = {
      id: 'wrapper', type: 'ui.modifier', properties: { fill: true, padding: { basis: 0.1 } }, children: [text('label', half)],
    };

    renderUiNode(container, stack('root', [modifier]), { width: 200, height: 100 }, null, 100, host());

    expect(fontSize('label')).toBeCloseTo(0.5 * 80, 6);
  });

  it('repaints from the new box when the container is resized', () => {
    const root = stack('root', [{ id: 'layer', type: 'ui.layer', properties: { fill: true }, children: [text('label', half)] }]);
    const handle = renderUiNode(container, root, { width: 200, height: 100 }, null, 100, host());

    handle.update(root, { width: 60, height: 40 }, null);

    expect(fontSize('label')).toBeCloseTo(20, 6);
  });

  it('refreshes a length on the root node itself when the widget is resized', () => {
    const root = stack('root', [], { padding: { basis: 0.01, ofParent: 0.1 } });
    const handle = renderUiNode(container, root, { width: 200, height: 100 }, null, 100, host());

    expect(parseFloat(element('root').style.padding)).toBeCloseTo(10, 6);

    handle.update(root, { width: 400, height: 300 }, null, 300);

    expect(parseFloat(element('root').style.padding)).toBeCloseTo(30, 6);
  });

  it('lets a grid pick its own arrangement and size each cell child from its cell', () => {
    const cells = Array.from({ length: 6 }, (_, index) => ({
      id: `c${index}`, type: 'ui.layer', properties: {}, children: [text(`t${index}`, { basis: 0.02, ofParent: 0.5 })],
    }) as UiNode);
    const grid: UiNode = { id: 'grid', type: 'ui.grid', properties: { fill: true, columns: 1, minCellSize: { basis: 0.25 } }, children: cells };

    renderUiNode(container, stack('root', [grid]), { width: 240, height: 108 }, null, 240, host());

    expect(parseFloat(element('c0').style.width)).toBeCloseTo(80, 6);
    expect(parseFloat(element('c0').style.height)).toBeCloseTo(54, 6);
    expect(fontSize('t0')).toBeCloseTo(27, 6);
  });

  it('keeps a grid in a stack consistent between measuring and painting with and without fill', () => {
    const cells = Array.from({ length: 6 }, (_, index) => text(`t${index}`, { basis: 0.05 }));
    const open: UiNode = { id: 'open', type: 'ui.grid', properties: { columns: 1, minCellSize: { basis: 0.25 } }, children: cells };
    const root = stack('root', [open, stack('rest', [], { fill: true })]);

    renderUiNode(container, root, { width: 240, height: 240 }, null, 240, host());

    expect(height('open')).toBeCloseTo(120, 6);
    expect(height('rest')).toBeCloseTo(120, 6);
  });
});
