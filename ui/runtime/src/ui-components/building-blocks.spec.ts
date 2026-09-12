import { UiNode } from '../ui-framework/ui-node.interface';
import { createUiComponentRegistry, UI_CORE_COMPONENTS } from '../ui-framework/component-registry';
import { renderUiNode } from '../render/ui-node-renderer';
import { UiRenderHost } from '../render/ui-render-host';
import { activationFor, findInteractiveNode } from '../ui-framework/node-gestures';
import { arcSweep, dialTravelOnMove, dialTravelOnPress, pointerAngle, arcMetrics } from './arc';
import { placeGridChildren } from './grid-layout';
import { UI_ICON_VERSIONS, UiComponents } from './ui-component-types';

interface Emitted {
  id: string;
  name: string;
  payload: unknown;
}

function node(id: string, type: string, properties: Record<string, unknown> = {}, children?: UiNode[]): UiNode {
  return { id, type, properties, children } as UiNode;
}

function pointer(type: string, fields: { clientX?: number; clientY?: number; pointerId?: number } = {}): Event {
  const event = new Event(type) as Event & { clientX: number; clientY: number; pointerId: number; button: number };
  event.clientX = fields.clientX ?? 0;
  event.clientY = fields.clientY ?? 0;
  event.pointerId = fields.pointerId ?? 1;
  event.button = 0;
  return event;
}

function sized(element: Element, width: number, height: number): void {
  (element as HTMLElement).getBoundingClientRect = () =>
    ({ left: 0, top: 0, width, height, right: width, bottom: height, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect;
}

describe('the building-block components', () => {
  let container: HTMLElement;
  let emitted: Emitted[];

  const host = (): UiRenderHost => ({
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: () => null,
    now: () => Date.parse('2026-01-02T03:04:05Z'),
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: (target, name, payload) => emitted.push({ id: target.id, name, payload }),
  });

  const mount = (tree: UiNode, box = { width: 120, height: 80 }, registry?: ReturnType<typeof createUiComponentRegistry>) =>
    renderUiNode(container, tree, box, null, 120, host(), registry ? { registry } : undefined);

  const find = (selector: string) => container.querySelector(selector) as HTMLElement | null;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    emitted = [];
    jasmine.clock().install();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    container.remove();
  });

  describe('ui.shape', () => {
    it('inscribes a circle in the smaller side, centred', () => {
      mount(node('s', 'ui.shape', { shape: 'circle', color: '#2b6cee' }));

      const path = find('.widget-shape path')!;
      expect(path.getAttribute('d')).toBe('M 20 40 A 40 40 0 1 1 100 40 A 40 40 0 1 1 20 40 Z');
      expect(path.getAttribute('fill')).toBe('#2b6cee');
      expect(path.getAttribute('stroke')).toBeNull();
    });

    it('clamps a rounded corner to half the smaller side', () => {
      mount(node('s', 'ui.shape', { shape: 'rounded-rectangle', cornerRadius: { basis: 1 } }));

      expect(find('.widget-shape path')!.getAttribute('d')).toContain('A 40 40 0 0 1 120 40');
    });

    it('stretches a path over the box while keeping its stroke width', () => {
      mount(node('s', 'ui.shape', {
        shape: 'path', path: 'M0 0 L1 0 L0.5 1 Z', strokeColor: '#ffffff', strokeWidth: { basis: 0.05 },
      }));

      const path = find('.widget-shape path')!;
      expect(path.getAttribute('transform')).toBe('scale(120 80)');
      expect(path.getAttribute('vector-effect')).toBe('non-scaling-stroke');
      expect(path.getAttribute('stroke-width')).toBe('6');
      expect(path.getAttribute('fill')).toBe('none');
    });

    for (const data of ['M0 0 E 1', 'M0 0 L1', 'L1 1', 'M0 0 Z 1']) {
      it(`draws nothing for the malformed path "${data}"`, () => {
        mount(node('s', 'ui.shape', { shape: 'path', path: data }));
        expect(find('.widget-shape path')).toBeNull();
      });
    }

    it('accepts implicit repetition, exponents and commas', () => {
      mount(node('s', 'ui.shape', { shape: 'path', path: 'M0,0 L1 0 1 1 0.5e0 1 Z' }));
      expect(find('.widget-shape path')).not.toBeNull();
    });

    it('draws nothing for a path outside the restricted grammar or a shape it does not know', () => {
      mount(node('s', 'ui.shape', { shape: 'path', path: 'M0 0 l1 1 Z' }));
      expect(find('.widget-shape path')).toBeNull();

      mount(node('t', 'ui.shape', { shape: 'star' }));
      expect(container.querySelectorAll('.widget-shape path').length).toBe(0);
    });
  });

  describe('ui.icon', () => {
    it('draws a published glyph centred at its size in the resolved colour', () => {
      mount(node('i', 'ui.icon', { icon: 'play', size: { basis: 0.25 }, role: 'secondary' }));

      const glyph = find('.widget-icon-glyph')!;
      expect(glyph.className).toBe('widget-icon-glyph icon icon-play');
      expect(glyph.style.width).toBe('30px');
      expect(glyph.style.left).toBe('45px');
      expect(glyph.style.top).toBe('25px');
      expect(glyph.style.color).toBe('var(--color-text-secondary)');
    });

    for (const name of ['xs', 'not-an-icon', 'wifi-solid-full']) {
      it(`draws nothing for "${name}", never the filled square a bare icon class paints`, () => {
        mount(node('i', 'ui.icon', { icon: name }));

        expect(container.querySelector('.icon')).toBeNull();
      });
    }

    it('advertises one component version per published group', () => {
      const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS);
      expect(registry.capabilities()['ui.icon']).toEqual({ minimum: 1, maximum: UI_ICON_VERSIONS.length });
    });
  });

  describe('ui.grid', () => {
    const cells = (spans: Array<[number, number]>) => spans.map(([columnSpan, rowSpan], index) =>
      node(`c${index}`, 'ui.text', { columnSpan, rowSpan }));

    it('places each child at the first free block, row by row', () => {
      const grid = node('g', 'ui.grid', { columns: 3 }, cells([[2, 1], [1, 2], [1, 1], [1, 1], [5, 1]]));

      const { placements, rows } = placeGridChildren(grid);
      expect(placements.map(p => [p.row, p.column, p.columnSpan])).toEqual([
        [0, 0, 2], [0, 2, 1], [1, 0, 1], [1, 1, 1], [2, 0, 3],
      ]);
      expect(rows).toBe(3);
    });

    it('drops children that do not fit a declared row count, but still fills gaps', () => {
      const grid = node('g', 'ui.grid', { columns: 2, rows: 1 }, cells([[1, 2], [1, 1], [1, 1]]));

      expect(placeGridChildren(grid).placements.map(p => p.child.id)).toEqual(['c1', 'c2']);
    });

    it('holds every count to the documented ceiling, so one integer cannot stall the reader', () => {
      const grid = node('g', 'ui.grid', { columns: 1e9, rows: 1e9 }, [node('c', 'ui.text', { rowSpan: 1e9, columnSpan: 1e9 })]);

      const { placements, columns, rows } = placeGridChildren(grid);
      expect([columns, rows, placements[0].rowSpan, placements[0].columnSpan]).toEqual([64, 64, 64, 64]);
    });

    it('draws each child across its block', () => {
      const stacks = cells([[1, 1], [1, 1], [2, 1]]).map(cell => ({ ...cell, type: 'ui.stack' }));
      mount(node('g', 'ui.grid', { columns: 2, gap: { basis: 0.1 } }, stacks),
        { width: 120, height: 80 });

      const children = Array.from(find('.widget-grid')!.children) as HTMLElement[];
      expect(children.map(c => [c.style.left, c.style.top, c.style.width, c.style.height])).toEqual([
        ['0px', '0px', '54px', '34px'],
        ['66px', '0px', '54px', '34px'],
        ['0px', '46px', '120px', '34px'],
      ]);
    });
  });

  describe('ui.grid inside a list', () => {
    it('makes rows as tall as a column is wide, and the grid as tall as its rows', () => {
      const grid = node('g', 'ui.grid', { columns: 2 }, [0, 1, 2].map(i => node(`c${i}`, 'ui.stack')));
      mount(node('l', 'ui.list', {}, [grid]), { width: 120, height: 80 });

      const cells = Array.from(find('.widget-grid')!.children) as HTMLElement[];
      expect(cells.map(c => [c.style.top, c.style.height])).toEqual([['0px', '60px'], ['0px', '60px'], ['60px', '60px']]);
      expect(find('.widget-grid')!.style.height).toBe('120px');
    });
  });

  describe('ui.gauge', () => {
    it('draws the track over the sweep and no fill at level 0', () => {
      mount(node('g', 'ui.gauge', { thickness: { basis: 0.1 } }));

      expect(find('.widget-gauge-track')).not.toBeNull();
      expect(find('.widget-gauge-fill')).toBeNull();
    });

    it('draws a full ring as two half arcs, filled in the accent by default', () => {
      mount(node('g', 'ui.gauge', { startAngle: 0, endAngle: 360, level: 1, thickness: { basis: 0.1 } }));

      const fill = find('.widget-gauge-fill')!;
      expect(fill.getAttribute('d')!.match(/A /g)!.length).toBe(2);
      expect(fill.style.stroke).toBe('var(--color-accent)');
    });
  });

  describe('ui.toggle', () => {
    const toggle = (on: boolean, events: string[] = ['change']) =>
      node('t', 'ui.toggle', { on, events, levelColor: '#34c759' });

    it('flips at once on a completed press, sends the new state, and holds it until the producer answers', () => {
      const handle = mount(toggle(false));
      const element = find('.widget-toggle')!;

      element.dispatchEvent(pointer('pointerdown'));
      expect(find('.widget-press-tint')!.classList).toContain('widget-press-tint-active');
      element.dispatchEvent(pointer('pointerup'));

      expect(emitted).toEqual([{ id: 't', name: 'change', payload: true }]);
      expect(find('.widget-toggle-track')!.style.background).toBe('rgb(52, 199, 89)');

      handle.update(toggle(false), { width: 120, height: 80 }, null);
      expect(find('.widget-toggle-track')!.style.background).toBe('rgb(52, 199, 89)');

      jasmine.clock().tick(1000);
      expect(find('.widget-toggle-track')!.style.background).toBe('var(--color-bg-tertiary)');
    });

    it('sends nothing for a cancelled press', () => {
      mount(toggle(false));
      const element = find('.widget-toggle')!;

      element.dispatchEvent(pointer('pointerdown'));
      element.dispatchEvent(pointer('pointercancel'));

      expect(emitted).toEqual([]);
    });

    it('is drawn but offers nothing without a declared change', () => {
      mount(toggle(true, []));
      const element = find('.widget-toggle')!;

      element.dispatchEvent(pointer('pointerdown'));
      element.dispatchEvent(pointer('pointerup'));

      expect(emitted).toEqual([]);
      expect(find('.widget-press-tint')).toBeNull();
      expect(element.classList).not.toContain('widget-pressable');
    });
  });

  describe('ui.segmented', () => {
    const segmented = (events: string[] = ['change']) => node('s', 'ui.segmented', { selected: 0, events }, [
      node('a', 'ui.text', { text: 'A' }),
      node('b', 'ui.button', { events: ['press'] }),
      node('c', 'ui.text', { text: 'C' }),
    ]);

    it('selects the pressed segment and sends its index', () => {
      mount(segmented());
      const element = find('.widget-segmented')!;
      sized(element, 120, 80);

      element.dispatchEvent(pointer('pointerdown', { clientX: 60 }));
      element.dispatchEvent(pointer('pointerup', { clientX: 60 }));

      expect(emitted).toEqual([{ id: 's', name: 'change', payload: 1 }]);
      expect(find('.widget-segmented-face')!.style.left).toBe(`${40 + 0.08 * 80}px`);
    });

    it('never offers a child segment its own events', () => {
      mount(segmented());
      const child = container.querySelector('[data-node-id="b"]') as HTMLElement;
      sized(find('.widget-segmented')!, 120, 80);

      child.dispatchEvent(pointer('pointerdown', { clientX: 60 }));
      child.dispatchEvent(pointer('pointerup', { clientX: 60 }));

      expect(emitted.map(e => e.name)).toEqual(['change']);
      expect(findInteractiveNode(segmented([]))).toBeNull();
    });
  });

  describe('ui.dial', () => {
    const sweep = arcSweep(node('d', 'ui.dial', { startAngle: 0, endAngle: 360 }));

    it('never jumps across the seam of a full turn', () => {
      expect(dialTravelOnMove(sweep, 350, 10, 355)).toBe(360);
      expect(dialTravelOnMove(sweep, 10, 350, 5)).toBe(0);
      expect(dialTravelOnMove(sweep, 10, 30, 5)).toBe(25);
    });

    it('takes the nearer end for a press in the gap, and ignores the centre', () => {
      const gapped = arcSweep(node('d', 'ui.dial', {}));
      expect(dialTravelOnPress(gapped, 150)).toBe(270);
      expect(dialTravelOnPress(gapped, 200)).toBe(0);
      expect(pointerAngle(arcMetrics(100, 100, 10), 52, 52)).toBeNull();
    });

    it('ends a drag when the pointer leaves an element that could not capture it', () => {
      mount(node('d', 'ui.dial', { startAngle: 0, endAngle: 360, thickness: { basis: 0.1 }, events: ['change'] }),
        { width: 100, height: 100 });
      const element = container.querySelector('.widget-dial') as unknown as HTMLElement;
      sized(element, 100, 100);
      element.setPointerCapture = () => { throw new Error('no capture'); };

      element.dispatchEvent(pointer('pointerdown', { clientX: 95, clientY: 50 }));
      element.dispatchEvent(pointer('pointerleave', { clientX: 120, clientY: 50 }));
      element.dispatchEvent(pointer('pointerdown', { clientX: 50, clientY: 95, pointerId: 2 }));
      element.dispatchEvent(pointer('pointerup', { clientX: 50, clientY: 95, pointerId: 2 }));

      expect(emitted.map(e => e.payload)).toEqual([0.25, 0.5]);
    });

    it('follows the pointer, adjusts and changes like a slider', () => {
      mount(node('d', 'ui.dial', { startAngle: 0, endAngle: 360, thickness: { basis: 0.1 }, events: ['adjust', 'change'] }),
        { width: 100, height: 100 });
      const element = container.querySelector('.widget-dial') as unknown as HTMLElement;
      sized(element, 100, 100);

      element.dispatchEvent(pointer('pointerdown', { clientX: 95, clientY: 50 }));
      element.dispatchEvent(pointer('pointerup', { clientX: 95, clientY: 50 }));

      expect(emitted).toEqual([
        { id: 'd', name: 'adjust', payload: 0.25 },
        { id: 'd', name: 'change', payload: 0.25 },
      ]);
    });
  });

  describe('keyboard and hardware activation', () => {
    it('flips a toggle, steps a segmented, and leaves a slider alone', () => {
      expect(activationFor(node('t', 'ui.toggle', { on: true, events: ['change'] })))
        .toEqual({ name: 'change', payload: false });
      expect(activationFor(node('s', 'ui.segmented', { selected: 1, events: ['change'] },
        [node('a', 'ui.text'), node('b', 'ui.text')]))).toEqual({ name: 'change', payload: 0 });
      expect(activationFor(node('l', 'ui.slider', { events: ['change'] }))).toBeNull();
    });
  });

  it('keeps a slider and a text field free of any press tint', () => {
    mount(node('root', 'ui.stack', {}, [
      node('sl', 'ui.slider', { events: ['change'] }),
      node('tf', 'ui.text-field', { events: ['change'] }),
    ]));

    expect(find('.widget-press-tint')).toBeNull();
    expect(container.querySelector('[data-node-id="tf"]')!.classList).not.toContain('widget-pressable');
  });

  it('draws each recommended fallback for a reader that predates these types', () => {
    const building = new Set<string>([
      UiComponents.Shape, UiComponents.Icon, UiComponents.Grid, UiComponents.Gauge,
      UiComponents.Toggle, UiComponents.Segmented, UiComponents.Dial,
    ]);
    const older = createUiComponentRegistry(...UI_CORE_COMPONENTS.filter(d => !building.has(d.type)));
    const pairs: Array<[string, string]> = [
      ['ui.shape', 'ui.stack'], ['ui.icon', 'ui.text'], ['ui.grid', 'ui.stack'], ['ui.gauge', 'ui.range-bar'],
      ['ui.toggle', 'ui.button'], ['ui.segmented', 'ui.stack'], ['ui.dial', 'ui.slider'],
    ];

    mount(node('root', 'ui.stack', {}, pairs.map(([type, fallback], index) =>
      ({ ...node(`n${index}`, type), fallback: node(`f${index}`, fallback) }))), undefined, older);

    for (let index = 0; index < pairs.length; index++) {
      const drawn = container.querySelector(`[data-node-id="f${index}"]`);
      expect(drawn?.getAttribute('data-node-type')).withContext(pairs[index][0]).toBe(pairs[index][1]);
    }
  });
});
