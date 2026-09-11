import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponentBox } from '../ui-framework/layout';
import { renderUiNode, UiRenderTimers } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';

let nextId = 0;
function node(type: string, properties: Record<string, unknown> = {}, children?: UiNode[]): UiNode {
  const built = { id: `n${++nextId}`, type, properties } as UiNode;
  if (children) (built as { children?: UiNode[] }).children = children;
  return built;
}

function testHost(overrides: Partial<UiRenderHost> = {}): UiRenderHost {
  return {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: resource => (resource ? `/api/ui/resources/${resource.resourceId}` : null),
    now: () => Date.parse('2026-01-02T03:04:05Z'),
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
    ...overrides,
  };
}

function pointer(type: string, fields: { clientX?: number; clientY?: number; pointerId?: number } = {}): Event {
  const event = new Event(type) as Event & { clientX: number; clientY: number; pointerId: number; button: number };
  event.clientX = fields.clientX ?? 0;
  event.clientY = fields.clientY ?? 0;
  event.pointerId = fields.pointerId ?? 1;
  event.button = 0;
  return event;
}

function press(element: HTMLElement): void {
  element.dispatchEvent(pointer('pointerdown'));
  element.dispatchEvent(pointer('pointerup'));
}

function node2(type: string, properties: Record<string, unknown> = {}, children?: UiNode[]): UiNode {
  const built = { id: 'stable', type, properties } as UiNode;
  if (children) (built as { children?: UiNode[] }).children = children;
  return built;
}

describe('widget node renderer', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const mount = (tree: UiNode, width = 120, height = 120, host = testHost()) =>
    renderUiNode(container, tree, { width, height }, null, 120, host);

  const dragSurface = (width = 200, height = 40) => {
    const slider = container.querySelector('.widget-slider') as HTMLElement;
    slider.getBoundingClientRect = () => ({ left: 0, top: 0, width, height }) as DOMRect;
    return slider;
  };

  it('renders a stack as a flex column sized to its box', () => {
    mount(node('ui.stack'), 200, 100);
    const stack = container.querySelector('.widget-stack') as HTMLElement;

    expect(stack).not.toBeNull();
    expect(stack.style.flexDirection).toBe('column');
    expect(stack.style.width).toBe('200px');
    expect(stack.style.height).toBe('100px');
  });

  it('turns a horizontal stack into a row', () => {
    mount(node('ui.stack', { direction: 'horizontal' }));

    expect((container.querySelector('.widget-stack') as HTMLElement).style.flexDirection).toBe('row');
  });

  it('gives every child the box the layout resolved for it', () => {
    const tree = node('ui.stack', {}, [
      node('ui.text', { mainSize: { basis: 0.25 } }),
      node('ui.text', { mainSize: { basis: 0.5 } }),
    ]);
    mount(tree, 120, 120);

    const texts = Array.from(container.querySelectorAll('.widget-text')) as HTMLElement[];

    expect(texts.length).toBe(2);
    // 0.25 and 0.5 of the 120px basis, down the column
    expect(texts[0].style.width).toBe('120px');
    expect(texts[1].style.width).toBe('120px');
  });

  it('resolves a localized text reference through the host', () => {
    mount(node('ui.text', { text: { $localized: { scope: 'macrodeck.app', key: 'Deck.Empty' } } }));

    expect(container.querySelector('.widget-text')!.textContent).toBe('macrodeck.app:Deck.Empty');
  });

  it('renders literal text as it stands', () => {
    mount(node('ui.text', { text: 'Living room' }));

    expect(container.querySelector('.widget-text')!.textContent).toBe('Living room');
  });

  it('keeps single-line text on one line with an ellipsis', () => {
    mount(node('ui.text', { text: 'x' }));
    const text = container.querySelector('.widget-text') as HTMLElement;

    expect(text.style.whiteSpace).toBe('nowrap');
    expect(text.style.textOverflow).toBe('ellipsis');
  });

  it('lets wrapping text wrap freely when no line cap was given', () => {
    mount(node('ui.text', { text: 'x', wrap: true }));
    const text = container.querySelector('.widget-text') as HTMLElement;

    expect(text.style.whiteSpace).toBe('pre-wrap');
    expect(text.classList.contains('widget-text-clamp')).toBeFalse();
  });

  it('clamps text to the line count it was given', () => {
    mount(node('ui.text', { text: 'x', maxLines: 3 }));

    expect(container.querySelector('.widget-text')!.classList.contains('widget-text-clamp')).toBeTrue();
  });

  it('hides text whose font has not loaded yet, rather than showing a fallback that swaps', () => {
    mount(node('ui.text', { text: 'x', fontFace: 'face-1' }), 120, 120,
      testHost({ fontReady: () => false, fontFamily: () => 'Face One' }));

    expect((container.querySelector('.widget-text') as HTMLElement).style.visibility).toBe('hidden');
  });

  it('paints a button with the accent when the producer sent no background', () => {
    mount(node('ui.button'));
    const button = container.querySelector('.widget-button') as HTMLElement;

    expect(button).not.toBeNull();
    expect(button.style.background).toContain('--color-accent');
  });

  it('hangs the artwork off the host resource url', () => {
    mount(node('ui.button', { source: { resourceId: 'abc' } }));
    const image = container.querySelector('.widget-button-artwork') as HTMLImageElement;

    expect(image.getAttribute('src')).toBe('/api/ui/resources/abc');
  });

  it('answers a long press itself rather than letting the browser open its context menu', () => {
    // A tree mounted outside a deck - in a modal, in a folder view - owns its long press the same way
    // a tile does.
    mount(node('ui.button', { source: { resourceId: 'abc' } }));
    const image = container.querySelector('.widget-button-artwork') as HTMLImageElement;

    const event = new Event('contextmenu', { cancelable: true, bubbles: true });
    image.dispatchEvent(event);

    expect(event.defaultPrevented).toBeTrue();
  });

  it('leaves artwork undraggable, so a press-and-move over it stays a widget gesture', () => {
    mount(node('ui.button', { source: { resourceId: 'abc' } }));
    const image = container.querySelector('.widget-button-artwork') as HTMLImageElement;

    expect(image.getAttribute('draggable')).toBe('false');
  });

  it('leaves a ui.image undraggable for the same reason', () => {
    mount(node('ui.image', { source: { resourceId: 'abc' } }));
    const image = container.querySelector('.widget-image img') as HTMLImageElement;

    expect(image.getAttribute('draggable')).toBe('false');
  });

  it('draws a ring only for a border style it knows', () => {
    mount(node('ui.button', { borderStyle: 'heartbeat' }));
    expect(container.querySelector('.widget-button-ring')).not.toBeNull();

    container.textContent = '';
    mount(node('ui.button', { borderStyle: 'dashed-neon' }));
    expect(container.querySelector('.widget-button-ring')).toBeNull();
  });

  it('draws a chart as an area under a line', () => {
    mount(node('ui.chart', { points: [0, 1] }), 100, 50);

    expect(container.querySelector('.widget-chart-area')!.getAttribute('d')).toContain('Z');
    expect(container.querySelector('.widget-chart-line')!.getAttribute('d')).toBe('M0 50 L100 0');
  });

  it('draws no chart at all in simple rendering mode', () => {
    mount(node('ui.chart', { points: [0, 1] }), 100, 50, testHost({ simpleRendering: () => true }));

    expect(container.querySelector('.widget-chart-line')).toBeNull();
  });

  it('draws a clock face, twelve marks and two hands', () => {
    mount(node('macrodeck.clock-dial'), 100, 100);

    expect(container.querySelector('.widget-clock-dial-face')).not.toBeNull();
    expect(container.querySelectorAll('.widget-clock-dial-tick').length).toBe(12);
    expect(container.querySelector('.widget-clock-dial-hand-hour')).not.toBeNull();
    expect(container.querySelector('.widget-clock-dial-hand-minute')).not.toBeNull();
  });

  it('adds a second hand only when the producer asked for one', () => {
    mount(node('macrodeck.clock-dial'), 100, 100);
    expect(container.querySelector('.widget-clock-dial-hand-second')).toBeNull();

    container.textContent = '';
    mount(node('macrodeck.clock-dial', { seconds: true }), 100, 100);
    expect(container.querySelector('.widget-clock-dial-hand-second')).not.toBeNull();
  });

  it('points the hands at the time in the zone the reference names', () => {
    const hands = (instant: string, zone?: string): string => {
      container.textContent = '';
      const properties = zone === undefined ? {} : { value: { $time: { zone } } };
      mount(node('macrodeck.clock-dial', properties), 100, 100, testHost({ now: () => Date.parse(instant) }));
      return ['hour', 'minute'].map(name => {
        const hand = container.querySelector(`.widget-clock-dial-hand-${name}`) as SVGElement;
        return `${hand.getAttribute('x2')},${hand.getAttribute('y2')}`;
      }).join(' ');
    };

    // Noon UTC is 21:00 in Tokyo, so a Tokyo dial then must point exactly where a UTC dial points
    // at 21:00 - the zone moves the hands, and moves them by the right amount. Both sides name a
    // zone, so the reading does not depend on where the machine running this happens to be.
    expect(hands('2026-01-02T12:00:00Z', 'Asia/Tokyo')).toBe(hands('2026-01-02T21:00:00Z', 'UTC'));
    expect(hands('2026-01-02T12:00:00Z', 'Asia/Tokyo'))
      .not.toBe(hands('2026-01-02T12:00:00Z', 'America/New_York'));
  });

  it('falls back to the device zone for a zone this runtime does not know', () => {
    const hourHand = (zone?: string): string => {
      container.textContent = '';
      const properties = zone === undefined ? {} : { value: { $time: { zone } } };
      mount(node('macrodeck.clock-dial', properties), 100, 100);
      return (container.querySelector('.widget-clock-dial-hand-hour') as SVGElement).getAttribute('x2') ?? '';
    };

    // An unknown id makes `Intl` throw; a dial that threw would take the whole tree down with it.
    expect(hourHand('Mars/Olympus_Mons')).toBe(hourHand(undefined));
  });

  it('marks a type it does not know instead of rendering nothing at all', () => {
    mount(node('ui.hologram'));
    const unsupported = container.querySelector('.widget-node-unsupported');

    expect(unsupported).not.toBeNull();
    expect(unsupported!.getAttribute('data-unsupported-type')).toBe('ui.hologram');
  });

  it('emits a press only for a node that declared it', () => {
    const pressed: string[] = [];
    const host = testHost({ emit: (target, event) => pressed.push(`${target.type}:${event}`) });

    mount(node('ui.button'), 120, 120, host);
    press(container.querySelector('.widget-button') as HTMLElement);

    expect(pressed).toEqual([]);

    container.textContent = '';
    mount(node('ui.button', { events: ['press'] }), 120, 120, host);
    press(container.querySelector('.widget-button') as HTMLElement);

    expect(pressed).toEqual(['ui.button:press']);
  });

  it('runs the press lifecycle in the order the profile states', () => {
    const emitted: string[] = [];
    const host = testHost({ emit: (_, event) => emitted.push(event) });
    mount(node('ui.button', { events: ['press', 'press-start', 'press-end'] }), 120, 120, host);

    press(container.querySelector('.widget-button') as HTMLElement);

    expect(emitted).toEqual(['press-start', 'press-end', 'press']);
  });

  it('claims the gesture from any node that declared a press, not just a button', () => {
    const emitted: string[] = [];
    const host = testHost({ emit: (_, event) => emitted.push(event) });
    mount(node('ui.stack', { events: ['press'] }), 120, 120, host);

    press(container.querySelector('.widget-stack') as HTMLElement);

    expect(emitted).toEqual(['press']);
    expect(container.querySelector('.widget-press-tint')).not.toBeNull();
  });

  it('does not emit a press when the pointer leaves before it is released', () => {
    const emitted: string[] = [];
    const host = testHost({ emit: (_, event) => emitted.push(event) });
    mount(node('ui.button', { events: ['press', 'press-start', 'press-end'] }), 120, 120, host);

    const button = container.querySelector('.widget-button') as HTMLElement;
    button.dispatchEvent(pointer('pointerdown'));
    button.dispatchEvent(pointer('pointerleave'));

    // The press began and ended - what a leave withholds is the completed press itself.
    expect(emitted).toEqual(['press-start', 'press-end']);
  });

  it('reports its press state so the tile around it can paint one', () => {
    const states: boolean[] = [];
    const host = testHost({ setPressed: (_, pressed) => states.push(pressed) });
    mount(node('ui.button', { events: ['press'] }), 120, 120, host);

    const button = container.querySelector('.widget-button') as HTMLElement;
    button.dispatchEvent(pointer('pointerdown'));

    expect(states).toEqual([true]);
    expect(container.querySelector('.widget-press-tint-active')).not.toBeNull();
  });

  it('shows the new value on update without rebuilding the element', () => {
    const handle = mount(node('ui.text', { text: 'before' }));
    const before = container.querySelector('.widget-text');

    handle.update(node('ui.text', { text: 'after' }), { width: 120, height: 120 }, null);

    expect(container.querySelector('.widget-text')).toBe(before);
    expect(before!.textContent).toBe('after');
  });

  it('replaces the element when the node changes type', () => {
    const handle = mount(node('ui.text', { text: 'x' }));

    handle.update(node('ui.stack'), { width: 120, height: 120 }, null);

    expect(container.querySelector('.widget-text')).toBeNull();
    expect(container.querySelector('.widget-stack')).not.toBeNull();
  });

  it('relays out its children when the box changes', () => {
    const tree = node('ui.stack', {}, [node('ui.text', { text: 'x' })]);
    const handle = mount(tree, 100, 100);

    handle.update(tree, { width: 300, height: 300 }, null);

    expect((container.querySelector('.widget-stack') as HTMLElement).style.width).toBe('300px');
  });

  it('leaves nothing in the container after destroy', () => {
    const handle = mount(node('ui.stack', {}, [node('ui.text', { text: 'x' })]));

    handle.destroy();

    expect(container.childNodes.length).toBe(0);
  });

  it('stops emitting after destroy', () => {
    const pressed: string[] = [];
    const host = testHost({ emit: (_, event) => pressed.push(event) });
    const handle = mount(node('ui.button', { events: ['press'] }), 120, 120, host);
    const button = container.querySelector('.widget-button') as HTMLElement;

    handle.destroy();
    button.dispatchEvent(new Event('pointerup'));

    expect(pressed).toEqual([]);
  });

  it('renders a range bar fill only when the producer sent both colours', () => {
    mount(node('ui.range-bar', { thickness: { basis: 0.1 }, start: 0.25, end: 0.75 }), 200, 40);
    expect(container.querySelector('.widget-range-bar-fill')).toBeNull();

    container.textContent = '';
    mount(node('ui.range-bar', {
      thickness: { basis: 0.1 }, start: 0.25, end: 0.75,
      startColor: '#001122', endColor: '#334455',
    }), 200, 40);
    const fill = container.querySelector('.widget-range-bar-fill') as HTMLElement;

    expect(fill.style.left).toBe('25%');
    expect(fill.style.width).toBe('50%');
  });

  it('adds a marker only when it has both a position and an end colour', () => {
    mount(node('ui.range-bar', { thickness: { basis: 0.1 }, marker: 0.5 }), 200, 40);
    expect(container.querySelector('.widget-range-bar-marker')).toBeNull();

    container.textContent = '';
    mount(node('ui.range-bar', {
      thickness: { basis: 0.1 }, marker: 0.5, startColor: '#001122', endColor: '#334455',
    }), 200, 40);
    expect(container.querySelector('.widget-range-bar-marker')).not.toBeNull();
  });

  it('drops a range bar in simple rendering mode but keeps a progress bar', () => {
    const simple = testHost({ simpleRendering: () => true });
    mount(node('ui.range-bar', { thickness: { basis: 0.1 }, startColor: '#001122', endColor: '#334455' }),
      200, 40, simple);
    expect(container.querySelector('.widget-range-bar-track')).toBeNull();

    container.textContent = '';
    mount(node('macrodeck.progress-bar', { thickness: { basis: 0.1 } }), 200, 40, simple);
    expect(container.querySelector('.widget-range-bar-track')).not.toBeNull();
  });

  it('splits a dynamic time into a smaller seconds run', () => {
    mount(node('macrodeck.dynamic-text', {
      size: { basis: 0.2 }, format: 'time', seconds: true, value: { $time: { zone: 'UTC' } },
    }));
    const seconds = container.querySelector('.widget-dynamic-text-seconds') as HTMLElement;

    expect(seconds).not.toBeNull();
    expect(seconds.textContent!.length).toBeGreaterThan(0);
    // The seconds run is deliberately smaller than the rest of the clock.
    expect(parseFloat(seconds.style.fontSize)).toBeLessThan(24);
  });

  it('renders a slider track, fill and thumb along its axis', () => {
    mount(node('ui.slider', { thickness: { basis: 0.1 }, level: 0.5 }), 200, 40);
    const slider = container.querySelector('.widget-slider') as HTMLElement;

    expect(slider.classList.contains('widget-slider-vertical')).toBeFalse();
    expect((container.querySelector('.widget-slider-fill') as HTMLElement).style.width).toBe('50%');
    expect(container.querySelector('.widget-slider-thumb')).not.toBeNull();
  });

  it('leaves no stale cross-axis style behind when a slider changes orientation', () => {
    // The renderer patches a node in place unless its type changes, so the fill and the thumb outlive
    // an orientation change. Whatever the old axis wrote has to go, or it keeps overriding the
    // stylesheet that spans the fill across the track and centres the thumb on it.
    const properties = { thickness: { basis: 0.1 }, level: 0.3 };
    const handle = mount(node2('ui.slider', properties), 200, 40);
    const fill = () => container.querySelector('.widget-slider-fill') as HTMLElement;
    const thumb = () => container.querySelector('.widget-slider-thumb') as HTMLElement;

    expect(fill().style.width).toBe('30%');
    expect(thumb().style.left).not.toBe('');

    handle.update(node2('ui.slider', { ...properties, direction: 'vertical' }), { width: 40, height: 200 }, null);

    expect(fill().style.height).toBe('30%');
    expect(fill().style.width).toBe('');
    expect(thumb().style.bottom).not.toBe('');
    expect(thumb().style.left).toBe('');

    // Symmetric: back the other way, the vertical axis has to be given up just as readily.
    handle.update(node2('ui.slider', properties), { width: 200, height: 40 }, null);

    expect(fill().style.width).toBe('30%');
    expect(fill().style.height).toBe('');
    expect(thumb().style.left).not.toBe('');
    expect(thumb().style.bottom).toBe('');
  });

  it('reports the level a drag ended on, once, when it ends', () => {
    const events: Array<{ event: string; payload: unknown }> = [];
    const host = testHost({ emit: (_, event, payload) => events.push({ event, payload }) });
    mount(node('ui.slider', { thickness: { basis: 0.1 }, events: ['change'] }), 200, 40, host);

    const slider = dragSurface();
    slider.dispatchEvent(pointer('pointerdown', { clientX: 50 }));
    slider.dispatchEvent(pointer('pointermove', { clientX: 100 }));
    // Nothing yet: this node offered no `adjust`, so the drag is silent until it lands.
    expect(events).toEqual([]);

    slider.dispatchEvent(pointer('pointerup', { clientX: 100 }));

    expect(events.length).toBe(1);
    expect(events[0].event).toBe('change');
    // The bare level, not an object around it: that is what the producer reads on the other side.
    expect(events[0].payload).toBeCloseTo(0.5, 5);
  });

  it('ignores a drag on a slider that declared no level events at all', () => {
    const events: string[] = [];
    const host = testHost({ emit: (_, event) => events.push(event) });
    mount(node('ui.slider', { thickness: { basis: 0.1 }, level: 0.25 }), 200, 40, host);

    const slider = dragSurface();
    slider.dispatchEvent(pointer('pointerdown', { clientX: 150 }));
    slider.dispatchEvent(pointer('pointerup', { clientX: 150 }));

    expect(events).toEqual([]);
    // And it keeps painting the producer's level rather than the one the finger was at.
    expect((container.querySelector('.widget-slider-fill') as HTMLElement).style.width).toBe('25%');
  });

  it('follows the pointer while it is dragged, reporting as it goes', () => {
    const events: Array<{ event: string; payload: unknown }> = [];
    const host = testHost({ emit: (_, event, payload) => events.push({ event, payload }) });
    mount(node('ui.slider', { thickness: { basis: 0.1 }, events: ['adjust', 'change'] }), 200, 40, host);

    const slider = dragSurface();
    slider.dispatchEvent(pointer('pointerdown', { clientX: 50 }));
    const fill = container.querySelector('.widget-slider-fill') as HTMLElement;

    expect(fill.style.width).toBe('25%');
    expect(fill.classList.contains('widget-slider-fill-dragging')).toBeTrue();
    expect(events.map(entry => entry.event)).toEqual(['adjust']);
    expect(events[0].payload).toBeCloseTo(0.25, 5);
  });

  it('streams no more than the ten adjusts a second the profile allows', () => {
    // Normative on `UiComponentEvents.Adjust` and on the slider's own profile entry: a reader sends
    // `adjust` no more than ten times a second, so a producer can size its work to that number.
    const adjusts: Array<{ at: number; payload: unknown }> = [];
    const host = testHost({
      emit: (_, event, payload) => {
        if (event === 'adjust') adjusts.push({ at: Date.now(), payload });
      },
    });
    jasmine.clock().install();
    jasmine.clock().mockDate();
    try {
      mount(node('ui.slider', { thickness: { basis: 0.1 }, events: ['adjust', 'change'] }), 200, 40, host);

      const slider = dragSurface();
      slider.dispatchEvent(pointer('pointerdown', { clientX: 0 }));
      // A finger dragged across the whole track over a second, reported as often as a browser would
      // report it - far faster than the rate the profile allows anything to leave the client at.
      for (let elapsed = 25; elapsed <= 1000; elapsed += 25) {
        jasmine.clock().tick(25);
        slider.dispatchEvent(pointer('pointermove', { clientX: elapsed / 5 }));
      }
      jasmine.clock().tick(1000);

      const gaps = adjusts.slice(1).map((entry, index) => entry.at - adjusts[index].at);

      expect(adjusts.length).toBeGreaterThan(1);
      expect(Math.min(...gaps)).toBeGreaterThanOrEqual(100);
      // And the throttle coalesces rather than discards: where the finger ended still arrives.
      expect(adjusts[adjusts.length - 1].payload).toBeCloseTo(1, 5);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('reads the level off the box it is really drawn at, not the box it was laid out for', () => {
    const events: Array<{ event: string; payload: unknown }> = [];
    const host = testHost({ emit: (_, event, payload) => events.push({ event, payload }) });
    // Laid out for 200 reference px but drawn at half that, the way a scaled tile draws it. A level
    // read against the layout box instead of the measured one comes out short by exactly the scale.
    mount(node('ui.slider', { thickness: { basis: 0.1 }, events: ['change'] }), 200, 40, host);

    const slider = container.querySelector('.widget-slider') as HTMLElement;
    slider.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 20 }) as DOMRect;

    slider.dispatchEvent(pointer('pointerdown', { clientX: 100 }));
    slider.dispatchEvent(pointer('pointerup', { clientX: 100 }));

    expect(events[0].payload).toBeCloseTo(1, 5);
  });

  it('reports no landing level when a drag is cancelled', () => {
    const events: string[] = [];
    const host = testHost({ emit: (_, event) => events.push(event) });
    mount(node('ui.slider', { thickness: { basis: 0.1 }, events: ['adjust', 'change'] }), 200, 40, host);

    const slider = dragSurface();
    slider.dispatchEvent(pointer('pointerdown', { clientX: 50 }));
    slider.dispatchEvent(pointer('pointercancel'));

    // A gesture the system took over is not a value the user chose, so only what was already
    // reported mid-drag stands - never a `change`.
    expect(events).not.toContain('change');
  });

  it('lets a child take its natural size when the parent leaves an axis open', () => {
    // A box with null extents is a parent saying "size yourself on that axis". Answering it with the
    // whole widget is how a column of children ends up drawn on top of one another - which is what a
    // real deck looked like before this was distinguished from "no box at all".
    const tree = node('ui.stack', {}, [
      node('ui.image', { size: { basis: 0.2 } }),
      node('ui.text', { text: 'below' }),
    ]);
    mount(tree, 240, 240);

    const image = container.querySelector('.widget-image') as HTMLElement;

    // Across the column it spans the cross extent; along it, nothing is imposed.
    expect(image.style.width).toBe('240px');
    expect(image.style.height).toBe('');
  });

  it('still falls back to the basis for a node mounted with no box at all', () => {
    renderUiNode(container, node('ui.stack'), null, null, 120, testHost());

    const stack = container.querySelector('.widget-stack') as HTMLElement;
    expect(stack.style.width).toBe('120px');
    expect(stack.style.height).toBe('120px');
  });

  describe('ui.text-field', () => {
    const field = (properties: Record<string, unknown> = {}) =>
      mount(node('ui.text-field', properties), 200, 40, testHost({
        emit: (_, event, payload) => emitted.push({ event, payload }),
      }));

    let emitted: Array<{ event: string; payload: unknown }>;
    const input = () => container.querySelector('.widget-text-field') as HTMLInputElement;

    beforeEach(() => { emitted = []; });

    it('draws the value the producer put in it', () => {
      field({ text: 'panzer' });

      expect(input().value).toBe('panzer');
    });

    it('resolves its placeholder through the reader catalogue, unlike its value', () => {
      field({ placeholder: { $localized: { scope: 'macrodeck.app', key: 'Deck.Search' } } });

      expect(input().getAttribute('placeholder')).toBe('macrodeck.app:Deck.Search');
    });

    it('keeps its own context menu, which is how a pointer-only user pastes into it', () => {
      field({ text: 'x' });

      const event = new Event('contextmenu', { cancelable: true, bubbles: true });
      input().dispatchEvent(event);

      expect(event.defaultPrevented).toBeFalse();
    });

    it('accepts no typing at all from a node that declared no events', () => {
      field({ text: 'x' });

      // The same rule a slider and a button follow: interaction is offered only where declared.
      expect(input().readOnly).toBeTrue();
    });

    it('reports what the user typed while they are still typing', () => {
      field({ text: '', events: ['adjust', 'change'] });

      input().value = 'pan';
      input().dispatchEvent(new Event('input'));

      expect(emitted).toEqual([{ event: 'adjust', payload: 'pan' }]);
    });

    it('reports the value they settled on when the field is left', () => {
      field({ text: '', events: ['change'] });

      input().value = 'panzer';
      input().dispatchEvent(new Event('blur'));

      expect(emitted).toEqual([{ event: 'change', payload: 'panzer' }]);
    });

    it('settles on Enter without waiting for the field to be left', () => {
      field({ text: '', events: ['change'] });

      input().value = 'panzer';
      const enter = new Event('keydown') as Event & { key: string };
      enter.key = 'Enter';
      input().dispatchEvent(enter);

      expect(emitted).toEqual([{ event: 'change', payload: 'panzer' }]);
    });

    it('does not overwrite what the user is typing when the producer echoes it back', () => {
      const handle = field({ text: '', events: ['adjust'] });
      input().dispatchEvent(new Event('focus'));
      input().value = 'panz';

      // A filtering producer echoes the query on every keystroke; applying that would move the caret.
      handle.update(node('ui.text-field', { text: 'pan', events: ['adjust'] }),
        { width: 200, height: 40 }, null);

      expect(input().value).toBe('panz');
    });

    it('takes the producer value again once the user has left', () => {
      const handle = field({ text: '', events: ['adjust'] });
      input().dispatchEvent(new Event('focus'));
      input().value = 'panz';
      input().dispatchEvent(new Event('blur'));

      handle.update(node('ui.text-field', { text: 'cleared', events: ['adjust'] }),
        { width: 200, height: 40 }, null);

      expect(input().value).toBe('cleared');
    });
  });

  describe('ui.list', () => {
    const PAST_THROTTLE_MS = 600;

    let emitted: Array<{ event: string; payload: unknown }>;

    // Stable ids, so a repaint patches the same elements rather than replacing them - which is also
    // what the real list does, and the only way the stubbed geometry below survives an update.
    const rows = (count: number) => {
      const children: UiNode[] = [];
      for (let index = 0; index < count; index++) {
        children.push({ id: `row-${index}`, type: 'ui.text', properties: { text: `row ${index}` } } as UiNode);
      }
      return children;
    };

    const list = (count: number, properties: Record<string, unknown> = {}) =>
      mount(node2('ui.list', properties, rows(count)), 200, 100, testHost({
        emit: (_, event, payload) => emitted.push({ event, payload }),
      }));

    const repaint = (
      handle: { update(node: UiNode, box: UiComponentBox, cross: null): void },
      count: number,
      properties: Record<string, unknown>,
    ) => handle.update(node2('ui.list', properties, rows(count)), { width: 200, height: 100 }, null);

    const surface = () => container.querySelector('.widget-list') as HTMLElement;

    const layOut = (visibleRows: number, rowHeight = 20) => {
      const element = surface();
      Object.defineProperty(element, 'clientHeight',
        { value: visibleRows * rowHeight, configurable: true });
      Object.defineProperty(element, 'scrollTop', { value: 0, configurable: true, writable: true });
      for (let index = 0; index < element.children.length; index++) {
        Object.defineProperty(element.children[index], 'offsetTop',
          { value: index * rowHeight, configurable: true });
      }
      return element;
    };

    beforeEach(() => {
      emitted = [];
      jasmine.clock().install();
      jasmine.clock().mockDate();
    });

    afterEach(() => jasmine.clock().uninstall());

    it('lays its children out one under the other rather than dividing its box between them', () => {
      list(3);

      expect(surface().children.length).toBe(3);
    });

    it('asks for more as soon as it is drawn, when its content does not fill it', () => {
      // Nothing would ever scroll to say so, and a list that came up half empty would stay that way.
      const handle = list(3, { events: ['reveal'] });
      layOut(10);
      emitted.length = 0;
      jasmine.clock().tick(PAST_THROTTLE_MS);

      repaint(handle, 3, { events: ['reveal'] });

      expect(emitted).toEqual([{ event: 'reveal', payload: 2 }]);
    });

    it('reports how far down its own children the user has come', () => {
      const handle = list(20, { events: ['reveal'] });
      const element = layOut(3);
      emitted.length = 0;
      jasmine.clock().tick(PAST_THROTTLE_MS);

      repaint(handle, 20, { events: ['reveal'] });

      // Three rows fit, and the fourth one's top sits exactly on the fold - it has come into view,
      // which is what the index means.
      expect(emitted).toEqual([{ event: 'reveal', payload: 3 }]);
      expect(element.children.length).toBe(20);
    });

    it('reports nothing while it has no box to have been seen in', () => {
      // Painted before it is laid out, every child sits at offset zero and reads as visible. A list
      // that reported that would claim its last index at once and never ask for anything again.
      list(20, { events: ['reveal'] });

      expect(emitted).toEqual([]);
    });

    it('says nothing at all for a list that declared no reveal', () => {
      const handle = list(3);
      layOut(10);
      jasmine.clock().tick(PAST_THROTTLE_MS);

      repaint(handle, 3, {});

      expect(emitted).toEqual([]);
    });

    it('does not ask again for a position it has already asked about', () => {
      const handle = list(20, { events: ['reveal'] });
      const element = layOut(3);
      jasmine.clock().tick(PAST_THROTTLE_MS);
      repaint(handle, 20, { events: ['reveal'] });
      emitted.length = 0;
      jasmine.clock().tick(PAST_THROTTLE_MS);

      // Scrolling back up is not new ground, so it asks for nothing - and the throttle is not what
      // is holding it back here, because the tick above already cleared it.
      (element as unknown as { scrollTop: number }).scrollTop = 0;
      element.dispatchEvent(new Event('scroll'));

      expect(emitted).toEqual([]);
    });

    it('keeps the rows it already had when more arrive', () => {
      const handle = list(3, { events: ['reveal'] });
      const before = Array.from(surface().children);

      repaint(handle, 6, { events: ['reveal'] });

      // Identity, not count: a row torn out of the document and put back takes the container's scroll
      // position with it, which is a list that jumps to the top every time it loads more.
      const after = Array.from(surface().children);
      expect(after.length).toBe(6);
      expect(after[0]).toBe(before[0]);
      expect(after[1]).toBe(before[1]);
      expect(after[2]).toBe(before[2]);
    });

    it('puts a row that appeared in the middle where it belongs', () => {
      const handle = list(3, {});
      const kept = surface().children[2];

      const inserted = rows(3);
      inserted.splice(1, 0, { id: 'row-new', type: 'ui.text', properties: { text: 'new' } } as UiNode);
      handle.update(node2('ui.list', {}, inserted), { width: 200, height: 100 }, null);

      const order = Array.from(surface().children)
        .map(child => child.getAttribute('data-node-id'));
      expect(order).toEqual(['row-0', 'row-new', 'row-1', 'row-2']);
      expect(surface().children[3]).toBe(kept);
    });

    it('lays its children out inside the width a scroll bar leaves, not the width it was given', () => {
      const handle = list(20);
      const surfaceElement = surface();
      const widthOfFirstRow = () => (surfaceElement.children[0] as HTMLElement).style.width;

      expect(widthOfFirstRow()).toBe('200px');

      // What a vertical scroll bar does to the box: the element keeps its width and loses 15px inside.
      Object.defineProperty(surfaceElement, 'clientWidth', { value: 185, configurable: true });
      repaint(handle, 20, {});

      expect(widthOfFirstRow()).toBe('185px');
    });

    it('lays a horizontal list out in a row that scrolls along x', () => {
      const columns = [0, 1, 2].map(index => ({ id: `col-${index}`, type: 'ui.stack', properties: {} }) as UiNode);
      mount(node2('ui.list', { direction: 'horizontal' }, columns), 200, 100, testHost({}));

      const element = surface();
      expect(element.classList.contains('widget-list-horizontal')).toBeTrue();
      const first = element.children[0] as HTMLElement;
      expect(first.style.height).toBe('100px');
      expect(first.style.width).toBe('');
    });

    it('keeps a list without a direction vertical', () => {
      list(3);

      const element = surface();
      expect(element.classList.contains('widget-list-horizontal')).toBeFalse();
      expect((element.children[0] as HTMLElement).style.width).toBe('200px');
    });

    it('reports how far along a horizontal list the user has come', () => {
      const node = (count: number) => node2('ui.list', { direction: 'horizontal', events: ['reveal'] }, rows(count));
      const handle = mount(node(20), 200, 100, testHost({
        emit: (_, event, payload) => emitted.push({ event, payload }),
      }));
      const element = surface();
      Object.defineProperty(element, 'clientWidth', { value: 60, configurable: true });
      Object.defineProperty(element, 'clientHeight', { value: 100, configurable: true });
      Object.defineProperty(element, 'scrollLeft', { value: 0, configurable: true, writable: true });
      for (let index = 0; index < element.children.length; index++) {
        Object.defineProperty(element.children[index], 'offsetLeft', { value: index * 20, configurable: true });
        Object.defineProperty(element.children[index], 'offsetTop', { value: 0, configurable: true });
      }
      jasmine.clock().tick(PAST_THROTTLE_MS);
      handle.update(node(20), { width: 200, height: 100 }, null);
      emitted.length = 0;
      jasmine.clock().tick(PAST_THROTTLE_MS);

      (element as unknown as { scrollLeft: number }).scrollLeft = 100;
      element.dispatchEvent(new Event('scroll'));

      expect(emitted).toEqual([{ event: 'reveal', payload: 8 }]);
    });

    it('asks again once the user goes further than they have been', () => {
      const handle = list(20, { events: ['reveal'] });
      const element = layOut(3);
      jasmine.clock().tick(PAST_THROTTLE_MS);
      repaint(handle, 20, { events: ['reveal'] });
      emitted.length = 0;
      jasmine.clock().tick(PAST_THROTTLE_MS);

      (element as unknown as { scrollTop: number }).scrollTop = 100;
      element.dispatchEvent(new Event('scroll'));

      expect(emitted).toEqual([{ event: 'reveal', payload: 8 }]);
    });
  });
});

describe('widget button corner', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const num = (value: string) => Number.parseFloat(value);

  const nestedCornerOf = (height: number) => {
    container.textContent = '';
    const child = node('ui.button', { mainSize: { basis: height / 240 } });
    renderUiNode(container,
      node('ui.stack', {}, [child]),
      { width: 200, height: 240 },
      null,
      240,
      testHost());

    const button = container.querySelector('.widget-button') as HTMLElement;
    return num(button.style.borderRadius);
  };

  it('rounds a nested button by the share of its height the profile states', () => {
    expect(nestedCornerOf(60)).toBeCloseTo(7.2, 2);
    expect(nestedCornerOf(44)).toBeCloseTo(5.28, 2);
  });

  it('halves that corner when the button does', () => {
    expect(nestedCornerOf(30)).toBeCloseTo(nestedCornerOf(60) / 2, 2);
  });

  it('leaves the corner to the reader when the button is the whole tree', () => {
    container.textContent = '';
    renderUiNode(container, node('ui.button'), { width: 120, height: 120 }, null, 120, testHost());

    const button = container.querySelector('.widget-button') as HTMLElement;
    expect(button.style.borderRadius).toBe('');
    expect(button.classList.contains('widget-node-root')).toBeTrue();
  });

  it('does not mark a nested button as the tree', () => {
    nestedCornerOf(60);
    const button = container.querySelector('.widget-button') as HTMLElement;
    expect(button.classList.contains('widget-node-root')).toBeFalse();
  });

  it('leaves the corner to the reader when a nested button asks for the tile corner', () => {
    container.textContent = '';
    const child = node('ui.button', { mainSize: { basis: 0.25 }, corner: 'tile' });
    renderUiNode(container, node('ui.stack', {}, [child]), { width: 200, height: 240 }, null, 240, testHost());

    const button = container.querySelector('.widget-button') as HTMLElement;
    expect(button.style.borderRadius).toBe('');
    expect(button.classList.contains('widget-tile-corner')).toBeTrue();
    expect(button.classList.contains('widget-node-root')).toBeFalse();
  });

  it('drops the tile corner again when the node stops asking for it', () => {
    container.textContent = '';
    const handle = renderUiNode(container,
      node('ui.stack', {}, [node('ui.button', { mainSize: { basis: 0.25 }, corner: 'tile' })]),
      { width: 200, height: 240 },
      null,
      240,
      testHost());
    handle.update(node('ui.stack', {}, [node('ui.button', { mainSize: { basis: 0.25 } })]),
      { width: 200, height: 240 },
      null);

    const button = container.querySelector('.widget-button') as HTMLElement;
    expect(button.classList.contains('widget-tile-corner')).toBeFalse();
    expect(num(button.style.borderRadius)).toBeCloseTo(7.2, 2);
  });

  // Issue #895: a ring painted inside the tile's transform-scaled content is resampled with it and
  // comes out visibly thinner than one the tile draws beside that content. A surface that draws the
  // root's ring itself says so, and the button then paints none - but only for the root, and only
  // when asked: everywhere else (a modal, a standalone mount, a conformance fixture) it still owns it.
  it('leaves the root button ring to a surface that says it draws it itself', () => {
    container.textContent = '';
    renderUiNode(container,
      node('ui.button', { borderStyle: 'static', borderColor: '#ff0000' }),
      { width: 200, height: 60 },
      null,
      120,
      testHost({ ownsRootWidgetBorder: () => true }));

    expect(container.querySelector('.widget-button-ring')).toBeNull();
  });

  it('still paints a nested button ring for a surface that draws the root one', () => {
    container.textContent = '';
    renderUiNode(container,
      {
        id: 'stack',
        type: 'ui.stack',
        children: [node('ui.button', { borderStyle: 'static', borderColor: '#ff0000' })],
      },
      { width: 200, height: 60 },
      null,
      120,
      testHost({ ownsRootWidgetBorder: () => true }));

    expect(container.querySelector('.widget-button-ring')).not.toBeNull();
  });

  it('leaves the ring to inherit that corner rather than stating one of its own', () => {
    container.textContent = '';
    renderUiNode(container,
      node('ui.button', { borderStyle: 'static', borderColor: '#ff0000' }),
      { width: 200, height: 60 },
      null,
      120,
      testHost());

    const ring = container.querySelector('.widget-button-ring') as HTMLElement;
    expect(ring).not.toBeNull();
    expect(ring.style.borderRadius).toBe('');
  });
});

describe('widget text fitting', () => {
  let container: HTMLElement;
  let realRect: () => DOMRect;

  const widthOf = (text: string, fontSize: number) => text.length * fontSize * 0.61;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);

    // jsdom measures no text at all. The stand-in only has to be proportional to the size and to
    // land off a whole pixel, which is what the rounding the fit trips over needs.
    realRect = Element.prototype.getBoundingClientRect;
    Element.prototype.getBoundingClientRect = function (this: Element) {
      const element = this as HTMLElement;
      const size = parseFloat(element.style.fontSize || '0');
      return { width: widthOf(element.textContent ?? '', size), height: size } as DOMRect;
    };
  });

  afterEach(() => {
    Element.prototype.getBoundingClientRect = realRect;
    container.remove();
  });

  const timesRow = (elapsed: string): UiNode => ({
    id: 'row',
    type: 'ui.stack',
    properties: { direction: 'horizontal', justify: 'space-between' },
    children: [
      // No mainSize and no fill: the layout hands it no width, so it is sized by its own text.
      { id: 'elapsed', type: 'ui.text', properties: { text: elapsed, size: { basis: 0.25 }, minSize: { basis: 0.1 } } },
      { id: 'duration', type: 'ui.text', properties: { text: '4:30', size: { basis: 0.25 }, minSize: { basis: 0.1 } } },
    ],
  }) as UiNode;

  function sizeAsBrowser(): void {
    const row = container.querySelector('.widget-stack') as HTMLElement;
    Object.defineProperty(row, 'clientWidth', { value: 200, configurable: true });
    for (const text of Array.from(container.querySelectorAll('.widget-text'))) {
      const element = text as HTMLElement;
      Object.defineProperty(element, 'clientWidth', {
        configurable: true,
        get: () => Math.floor(widthOf(element.textContent ?? '', parseFloat(element.style.fontSize || '0'))),
      });
    }
  }

  const elapsedSize = () =>
    (container.querySelector('[data-node-id="elapsed"]') as HTMLElement).style.fontSize;

  it('holds one size for text that already fits, however often it is repainted', () => {
    const handle = renderUiNode(container, timesRow('1:07'), { width: 200, height: 40 }, null, 120, testHost());
    sizeAsBrowser();
    handle.update(timesRow('1:07'), { width: 200, height: 40 }, null);
    const settled = elapsedSize();

    for (let second = 0; second < 5; second++) {
      handle.update(timesRow('1:07'), { width: 200, height: 40 }, null);
    }

    expect(settled).toBe('30px');
    expect(elapsedSize()).toBe(settled);
  });

  it('holds that size as the digits under it change width', () => {
    const handle = renderUiNode(container, timesRow('1:07'), { width: 200, height: 40 }, null, 120, testHost());
    sizeAsBrowser();

    const sizes: string[] = [];
    for (const elapsed of ['1:08', '1:09', '1:10', '1:11', '1:12']) {
      handle.update(timesRow(elapsed), { width: 200, height: 40 }, null);
      sizes.push(elapsedSize());
    }

    expect(sizes).toEqual(['30px', '30px', '30px', '30px', '30px']);
  });
});

describe('widget clock ticking', () => {
  interface Scheduled {
    handle: string;
    at: number;
    callback: () => void;
  }

  function fakeClock(startedAt: string) {
    let now = Date.parse(startedAt);
    let handles = 0;
    let pending: Scheduled[] = [];

    const timers: UiRenderTimers = {
      set(callback: () => void, delayMs: number): unknown {
        const handle = `t${++handles}`;
        pending.push({ handle, at: now + delayMs, callback });
        return handle;
      },
      clear(handle: unknown): void {
        pending = pending.filter(entry => entry.handle !== handle);
      },
    };

    return {
      timers,
      now: () => now,
      pendingCount: () => pending.length,

      dueIn(): number | null {
        return pending.length === 0 ? null : pending[0].at - now;
      },

      advanceTo(instant: string): void {
        const target = Date.parse(instant);
        for (;;) {
          const due = pending.filter(entry => entry.at <= target)
            .sort((left, right) => left.at - right.at)[0];
          if (due === undefined) break;

          now = due.at;
          pending = pending.filter(entry => entry !== due);
          due.callback();
        }
        now = target;
      },
    };
  }

  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  function mountAt(clock: ReturnType<typeof fakeClock>, tree: UiNode, width = 100, height = 100) {
    const host = testHost({ now: () => clock.now() });
    return renderUiNode(container, tree, { width, height }, null, 120, host, { timers: clock.timers });
  }

  const digital = (properties: Record<string, unknown>) =>
    node('macrodeck.dynamic-text', { format: 'time', value: { $time: { zone: 'UTC' } }, ...properties });

  const shown = () => (container.querySelector('.widget-dynamic-text') as HTMLElement).textContent;

  it('advances a clock showing seconds on every second, with nothing else updating it', () => {
    const clock = fakeClock('2026-01-02T03:04:05.400Z');
    mountAt(clock, digital({ seconds: true }));

    expect(shown()).toBe('3:04:05');
    expect(clock.dueIn()).toBe(600);

    clock.advanceTo('2026-01-02T03:04:06.400Z');
    expect(shown()).toBe('3:04:06');

    clock.advanceTo('2026-01-02T03:04:09.400Z');
    expect(shown()).toBe('3:04:09');
  });

  it('waits for the minute boundary when the display carries no seconds', () => {
    const clock = fakeClock('2026-01-02T03:04:05.400Z');
    mountAt(clock, digital({}));

    expect(shown()).toBe('3:04');
    expect(clock.dueIn()).toBe(54600);

    clock.advanceTo('2026-01-02T03:04:59.900Z');
    expect(shown()).toBe('3:04');

    clock.advanceTo('2026-01-02T03:05:00.100Z');
    expect(shown()).toBe('3:05');
  });

  it('moves an analog second hand on its own', () => {
    const clock = fakeClock('2026-01-02T03:04:05.000Z');
    mountAt(clock, node('macrodeck.clock-dial', { seconds: true, value: { $time: { zone: 'UTC' } } }));

    const secondHand = () =>
      (container.querySelector('.widget-clock-dial-hand-second') as SVGElement).getAttribute('x2');
    const atMount = secondHand();

    clock.advanceTo('2026-01-02T03:04:06.000Z');

    expect(clock.dueIn()).toBe(1000);
    expect(secondHand()).not.toBe(atMount);
  });

  it('schedules nothing for a node that draws no instant', () => {
    const clock = fakeClock('2026-01-02T03:04:05.400Z');

    mountAt(clock, node('ui.text', { text: 'not a clock' }));
    expect(clock.pendingCount()).toBe(0);

    mountAt(clock, node('macrodeck.dynamic-text', { format: 'zone-name', value: { $time: { zone: 'UTC' } } }));
    expect(clock.pendingCount()).toBe(0);
  });

  it('stops ticking once the clock is gone', () => {
    const clock = fakeClock('2026-01-02T03:04:05.400Z');
    const handle = mountAt(clock, digital({ seconds: true }));
    expect(clock.pendingCount()).toBe(1);

    handle.update(node('ui.text', { text: 'no longer a clock' }), { width: 100, height: 100 }, null);
    expect(clock.pendingCount()).toBe(0);

    const stillTicking = mountAt(clock, digital({ seconds: true }));
    expect(clock.pendingCount()).toBe(1);

    stillTicking.destroy();
    expect(clock.pendingCount()).toBe(0);
  });

  it('ticks a clock nested inside a tree the same way', () => {
    const clock = fakeClock('2026-01-02T03:04:05.400Z');
    mountAt(clock, node('ui.stack', {}, [digital({ seconds: true })]), 200, 100);

    expect(shown()).toBe('3:04:05');

    clock.advanceTo('2026-01-02T03:04:07.400Z');
    expect(shown()).toBe('3:04:07');
  });
});

describe('widget text inside the box its stack has', () => {
  let roomPx: number;

  const ADVANCE = 0.6;

  let container: HTMLElement;
  let realRect: () => DOMRect;
  let realClientWidth: PropertyDescriptor | undefined;

  const naturalWidth = (element: HTMLElement) =>
    (element.textContent ?? '').length * parseFloat(element.style.fontSize || '0') * ADVANCE;

  beforeEach(() => {
    roomPx = 100;
    container = document.createElement('div');
    document.body.appendChild(container);
    realRect = Element.prototype.getBoundingClientRect;
    realClientWidth = Object.getOwnPropertyDescriptor(Element.prototype, 'clientWidth');

    // The same box, reported the way a browser reports it: a whole number of pixels. This is what
    // the reader watches for a width that moved, and jsdom answers zero to every element.
    Object.defineProperty(Element.prototype, 'clientWidth', {
      configurable: true,
      get(this: Element) { return Math.floor(this.getBoundingClientRect().width); },
    });

    // jsdom lays nothing out. A row divides what it has among its children: each is as wide as its
    // own text, and when together they ask for more than the row holds they are all cut back by the
    // same share, which is what leaves each of them inside the row rather than beyond it.
    Element.prototype.getBoundingClientRect = function (this: Element) {
      const element = this as HTMLElement;
      const natural = naturalWidth(element);
      if (!element.classList.contains('widget-text')) return { width: natural, height: 0 } as DOMRect;

      const row = Array.from(container.querySelectorAll('.widget-text')) as HTMLElement[];
      const asked = row.reduce((total, sibling) => total + naturalWidth(sibling), 0);
      return { width: asked > roomPx ? natural * roomPx / asked : natural, height: 0 } as DOMRect;
    };
  });

  afterEach(() => {
    Element.prototype.getBoundingClientRect = realRect;
    if (realClientWidth) Object.defineProperty(Element.prototype, 'clientWidth', realClientWidth);
    else delete (Element.prototype as unknown as Record<string, unknown>)['clientWidth'];
    container.remove();
  });

  const header = (label: string): UiNode => ({
    id: 'header',
    type: 'ui.stack',
    properties: { direction: 'horizontal', align: 'center', justify: 'space-between' },
    children: [
      { id: 'label', type: 'ui.text', properties: { text: label, size: { basis: 0.11 }, minSize: { basis: 0.075 } } },
      { id: 'value', type: 'ui.text', properties: { text: '34%', size: { basis: 0.11 }, minSize: { basis: 0.075 } } },
    ],
  }) as UiNode;

  const texts = () => Array.from(container.querySelectorAll('.widget-text')) as HTMLElement[];
  const sizeOf = (element: HTMLElement) => parseFloat(element.style.fontSize);

  it('leaves no text wider than the room its row has for it', () => {
    renderUiNode(container, header('System Volume'), { width: 120, height: 30 }, null, 120, testHost());

    for (const element of texts()) {
      expect(naturalWidth(element))
        .withContext(`${element.getAttribute('data-node-id')} overflows the room it was given`)
        .toBeLessThanOrEqual(element.getBoundingClientRect().width + 0.01);
    }
  });

  it('shrinks no further than the size the node declares it will stay whole down to', () => {
    renderUiNode(
      container,
      header('An extremely long label that could never fit'),
      { width: 120, height: 30 },
      null,
      120,
      testHost());

    for (const element of texts()) {
      expect(sizeOf(element))
        .withContext(`${element.getAttribute('data-node-id')} shrank past its minimum`)
        .toBeGreaterThanOrEqual(0.075 * 120);
    }
  });

  it('leaves text that already fits at the size the producer asked for', () => {
    renderUiNode(container, header('Vol'), { width: 120, height: 30 }, null, 120, testHost());

    for (const element of texts()) expect(sizeOf(element)).toBe(0.11 * 120);
  });

  it('holds the fit it settled on however often the tree is repainted', () => {
    const handle = renderUiNode(
      container, header('System Volume'), { width: 120, height: 30 }, null, 120, testHost());
    const settled = texts().map(sizeOf);

    for (let repaint = 0; repaint < 4; repaint++) {
      handle.update(header('System Volume'), { width: 120, height: 30 }, null);
    }

    expect(texts().map(sizeOf)).toEqual(settled);
  });

  describe('when the room comes back', () => {
    const declared = 0.11 * 120;
    const box = { width: 120, height: 30 };

    it('leaves nothing shrunk when the text itself gets shorter in the same box', () => {
      const handle = renderUiNode(
        container, header('An extremely long label indeed'), box, null, 120, testHost());
      expect(sizeOf(texts()[0])).toBeLessThan(declared);

      handle.update(header('Vol'), box, null);

      expect(sizeOf(texts()[0])).toBe(declared);
      for (const element of texts()) {
        expect(naturalWidth(element))
          .withContext(`${element.getAttribute('data-node-id')} is cut off with room to spare`)
          .toBeLessThanOrEqual(element.getBoundingClientRect().width + 0.01);
      }
    });
  });

  describe('when a width moves underneath the tree', () => {
    let realObserver: unknown;
    let fire: () => void;

    beforeEach(() => {
      const observers: Array<{ callback: () => void; watching: number }> = [];
      realObserver = (globalThis as Record<string, unknown>)['ResizeObserver'];
      (globalThis as Record<string, unknown>)['ResizeObserver'] = function (callback: () => void) {
        const entry = { callback, watching: 0 };
        observers.push(entry);
        return {
          observe: () => { entry.watching++; },
          unobserve: () => { entry.watching = Math.max(0, entry.watching - 1); },
          disconnect: () => { entry.watching = 0; },
        };
      };
      fire = () => {
        for (const entry of observers.slice()) if (entry.watching > 0) entry.callback();
      };
    });

    afterEach(() => {
      (globalThis as Record<string, unknown>)['ResizeObserver'] = realObserver;
    });

    it('keeps the sizes the tree settled on, however often the reader reports back', () => {
      renderUiNode(container, header('System Volume'), { width: 120, height: 30 }, null, 120, testHost());
      const settled = texts().map(sizeOf);

      for (let round = 0; round < 4; round++) fire();

      expect(texts().map(sizeOf)).toEqual(settled);
    });

    it('leaves no text cut off after the reader reports back', () => {
      renderUiNode(container, header('System Volume'), { width: 120, height: 30 }, null, 120, testHost());

      for (let round = 0; round < 4; round++) fire();

      for (const element of texts()) {
        expect(naturalWidth(element))
          .withContext(`${element.getAttribute('data-node-id')} was cut off by a re-fit`)
          .toBeLessThanOrEqual(element.getBoundingClientRect().width + 0.01);
      }
    });

    it('returns to the declared size after the room it lost is given back', () => {
      roomPx = 1000;
      const declared = 0.11 * 120;
      renderUiNode(container, header('System Volume'), { width: 120, height: 30 }, null, 120, testHost());
      expect(texts().map(sizeOf)).toEqual([declared, declared]);

      roomPx = 40;
      fire();
      for (const element of texts()) expect(sizeOf(element)).toBeLessThan(declared);

      roomPx = 1000;
      fire();
      expect(texts().map(sizeOf)).toEqual([declared, declared]);
    });
  });

  describe('what a repaint costs', () => {
    let reads = 0;
    let readTargets: Element[] = [];
    let writes = 0;
    let realRepaintCostRect: () => DOMRect;
    let realRepaintCostClientWidth: PropertyDescriptor | undefined;
    let realOffsetWidth: PropertyDescriptor | undefined;
    let realSetProperty: (name: string, value: string | null, priority?: string) => void;

    function rowTexts(element: HTMLElement): HTMLElement[] {
      const parent = element.parentElement;
      if (parent === null) return [element];
      const found: HTMLElement[] = [];
      for (let index = 0; index < parent.children.length; index++) {
        const child = parent.children[index] as HTMLElement;
        if (child.classList.contains('widget-text') || child.classList.contains('widget-dynamic-text')) {
          found.push(child);
        }
      }
      return found;
    }

    beforeEach(() => {
      reads = 0;
      readTargets = [];
      writes = 0;

      realRepaintCostRect = Element.prototype.getBoundingClientRect;
      Element.prototype.getBoundingClientRect = function (this: Element): DOMRect {
        reads++;
        readTargets.push(this);
        const element = this as HTMLElement;
        const natural = naturalWidth(element);
        const isText = element.classList
          && (element.classList.contains('widget-text') || element.classList.contains('widget-dynamic-text'));
        if (!isText) return { width: natural, height: 0 } as DOMRect;

        const row = rowTexts(element);
        const asked = row.reduce((total, sibling) => total + naturalWidth(sibling), 0);
        return { width: asked > roomPx ? natural * roomPx / asked : natural, height: 0 } as DOMRect;
      };

      realRepaintCostClientWidth = Object.getOwnPropertyDescriptor(Element.prototype, 'clientWidth');
      Object.defineProperty(Element.prototype, 'clientWidth', {
        configurable: true,
        get(this: Element) {
          reads++;
          readTargets.push(this);
          return Math.floor(this.getBoundingClientRect().width);
        },
      });

      realOffsetWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth');
      Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
        configurable: true,
        get(this: HTMLElement) {
          reads++;
          readTargets.push(this);
          return Math.floor(this.getBoundingClientRect().width);
        },
      });

      realSetProperty = CSSStyleDeclaration.prototype.setProperty;
      CSSStyleDeclaration.prototype.setProperty = function (
        this: CSSStyleDeclaration, name: string, value: string | null, priority?: string,
      ): void {
        writes++;
        realSetProperty.call(this, name, value as string, priority as string);
      };
    });

    afterEach(() => {
      Element.prototype.getBoundingClientRect = realRepaintCostRect;
      if (realRepaintCostClientWidth) {
        Object.defineProperty(Element.prototype, 'clientWidth', realRepaintCostClientWidth);
      }
      if (realOffsetWidth) Object.defineProperty(HTMLElement.prototype, 'offsetWidth', realOffsetWidth);
      CSSStyleDeclaration.prototype.setProperty = realSetProperty;
    });

    function styleMap(styleAttr: string | null): { [prop: string]: string } {
      const map: { [prop: string]: string } = {};
      if (!styleAttr) return map;
      const parts = styleAttr.split(';');
      for (let index = 0; index < parts.length; index++) {
        const at = parts[index].indexOf(':');
        if (at < 0) continue;
        const name = parts[index].slice(0, at).trim();
        if (name) map[name] = parts[index].slice(at + 1).trim();
      }
      return map;
    }

    function changedStyleKeys(before: string | null, after: string | null): string[] {
      const beforeMap = styleMap(before);
      const afterMap = styleMap(after);
      const keys: { [key: string]: true } = {};
      for (const key in beforeMap) if (Object.prototype.hasOwnProperty.call(beforeMap, key)) keys[key] = true;
      for (const key in afterMap) if (Object.prototype.hasOwnProperty.call(afterMap, key)) keys[key] = true;
      const changed: string[] = [];
      for (const key in keys) if (beforeMap[key] !== afterMap[key]) changed.push(key);
      return changed;
    }

    const stackOf = (count: number): UiNode => {
      const children: UiNode[] = [];
      for (let index = 0; index < count; index++) {
        children.push({
          id: `t${index}`,
          type: 'ui.text',
          properties: { text: `label number ${index}`, size: { basis: 0.11 }, minSize: { basis: 0.075 } },
        } as UiNode);
      }
      return {
        id: 'row', type: 'ui.stack', properties: { direction: 'horizontal' }, children,
      } as UiNode;
    };

    const freshContainer = (): HTMLElement => {
      const element = document.createElement('div');
      document.body.appendChild(element);
      return element;
    };

    it('P1: an unchanged model costs no measurement and no style write', () => {
      roomPx = 1000;
      const host = testHost();
      const box = { width: 120, height: 30 };
      const handle = renderUiNode(container, header('System Volume'), box, null, 120, host);

      const before = texts().map(element => element.getAttribute('style'));
      const identities = texts();
      reads = 0;
      writes = 0;

      handle.update(header('System Volume'), { width: 120, height: 30 }, null);

      expect(reads).toBe(0);
      expect(writes).toBe(0);
      const after = texts();
      expect(after.length).toBe(identities.length);
      for (let index = 0; index < identities.length; index++) {
        expect(after[index]).toBe(identities[index]);
        expect(after[index].getAttribute('style')).toBe(before[index]);
      }
    });

    it('P2: mounting sibling trees does not re-measure the ones already mounted', () => {
      roomPx = 40;

      const solo = freshContainer();
      reads = 0;
      const soloHandle = renderUiNode(solo, stackOf(3), { width: 400, height: 30 }, null, 120, testHost());
      const unit = reads;
      soloHandle.destroy();
      solo.remove();

      const tiles: HTMLElement[] = [];
      for (let index = 0; index < 8; index++) tiles.push(freshContainer());
      reads = 0;
      for (let index = 0; index < tiles.length; index++) {
        renderUiNode(tiles[index], stackOf(3), { width: 400, height: 30 }, null, 120, testHost());
      }
      const total = reads;
      for (let index = 0; index < tiles.length; index++) tiles[index].remove();

      expect(total).toBeLessThanOrEqual(12 * unit);
    });

    it('P4: a text-only change touches only the fitted font-size', () => {
      roomPx = 100;
      const box = { width: 120, height: 30 };
      const handle = renderUiNode(container, header('An extremely long label indeed'), box, null, 120, testHost());
      const before = texts().map(element => element.getAttribute('style'));
      const identities = texts();

      handle.update(header('Vol'), box, null);

      const after = texts();
      expect(after.length).toBe(identities.length);
      for (let index = 0; index < identities.length; index++) {
        expect(after[index]).toBe(identities[index]);
        const changed = changedStyleKeys(before[index], after[index].getAttribute('style'));
        expect(changed.every(key => key === 'font-size'))
          .withContext(`unexpected style change(s): ${changed.join(', ')}`)
          .toBeTrue();
      }
    });

    it('P7: text re-fits when a font face finishes loading, even with an unchanged model', () => {
      roomPx = 1000;
      let ready = false;
      const faceHost = testHost({ fontFamily: () => 'Face One', fontReady: () => ready });
      const labelNode: UiNode = {
        id: 'label', type: 'ui.text',
        properties: { text: 'Vol', fontFace: 'face-1', size: { basis: 0.11 }, minSize: { basis: 0.075 } },
      } as UiNode;

      const first = freshContainer();
      const handle = renderUiNode(first, labelNode, { width: 120, height: 30 }, null, 120, faceHost);
      const element = first.querySelector('.widget-text') as HTMLElement;
      expect(element.style.visibility).toBe('hidden');

      ready = true;
      reads = 0;
      handle.update(labelNode, { width: 120, height: 30 }, null);

      expect(element.style.visibility).not.toBe('hidden');
      expect(reads).toBeGreaterThan(0);

      const second = freshContainer();
      const readyHost = testHost({ fontFamily: () => 'Face One', fontReady: () => true });
      renderUiNode(second, labelNode, { width: 120, height: 30 }, null, 120, readyHost);
      const freshElement = second.querySelector('.widget-text') as HTMLElement;

      expect(element.style.fontSize).toBe(freshElement.style.fontSize);
      first.remove();
      second.remove();
    });

    it('P7: text re-fits when the language changes, unchanged model', () => {
      roomPx = 1000;
      let locale = 'en-GB';
      const cultureHost = testHost({ culture: () => locale });
      const clockNode: UiNode = {
        id: 'clock', type: 'macrodeck.dynamic-text',
        properties: {
          format: 'date', value: { $time: { zone: 'UTC' } }, size: { basis: 0.2 }, minSize: { basis: 0.1 },
        },
      } as UiNode;

      const handle = renderUiNode(container, clockNode, { width: 120, height: 30 }, null, 120, cultureHost);
      const element = container.querySelector('.widget-dynamic-text') as HTMLElement;
      const before = element.textContent;

      locale = 'de-DE';
      reads = 0;
      handle.update(clockNode, { width: 120, height: 30 }, null);

      expect(element.textContent).not.toBe(before);
      expect(reads).toBeGreaterThan(0);
    });

    it('P7: text without a face of its own re-fits when the global UI font changes', () => {
      roomPx = 1000;
      let uiFont = 'a';
      const fontHost = testHost({ uiFontKey: () => uiFont });
      const handle = renderUiNode(container, header('Vol'), { width: 120, height: 30 }, null, 120, fontHost);

      reads = 0;
      handle.update(header('Vol'), { width: 120, height: 30 }, null);
      expect(reads).toBe(0);

      uiFont = 'b';
      handle.update(header('Vol'), { width: 120, height: 30 }, null);
      expect(reads).toBeGreaterThan(0);
    });

    it('P7: a clock re-fits when the global UI font changes', () => {
      roomPx = 1000;
      let uiFont = 'a';
      const fontHost = testHost({ uiFontKey: () => uiFont });
      const clockNode: UiNode = {
        id: 'clock', type: 'macrodeck.dynamic-text',
        properties: {
          format: 'date', value: { $time: { zone: 'UTC' } }, size: { basis: 0.2 }, minSize: { basis: 0.1 },
        },
      } as UiNode;
      const handle = renderUiNode(container, clockNode, { width: 120, height: 30 }, null, 120, fontHost);

      uiFont = 'b';
      reads = 0;
      handle.update(clockNode, { width: 120, height: 30 }, null);

      expect(reads).toBeGreaterThan(0);
    });

    it('P7: text re-fits when only the box or basis changes', () => {
      roomPx = 1000;
      const handle = renderUiNode(
        container, header('System Volume'), { width: 120, height: 30 }, null, 120, testHost());
      expect(texts().map(sizeOf)).toEqual([0.11 * 120, 0.11 * 120]);

      reads = 0;
      handle.update(header('System Volume'), { width: 240, height: 60 }, null, 240);

      expect(reads).toBeGreaterThan(0);
      const declared = 0.11 * 240;
      for (const element of texts()) {
        expect(sizeOf(element)).toBeCloseTo(declared, 5);
        expect(naturalWidth(element)).toBeLessThanOrEqual(element.getBoundingClientRect().width + 0.01);
      }
    });

    describe('via the width observer', () => {
      let realObserver: unknown;
      let fire: () => void;

      beforeEach(() => {
        const observers: Array<{ callback: () => void; watching: number }> = [];
        realObserver = (globalThis as Record<string, unknown>)['ResizeObserver'];
        (globalThis as Record<string, unknown>)['ResizeObserver'] = function (callback: () => void) {
          const entry = { callback, watching: 0 };
          observers.push(entry);
          return {
            observe: () => { entry.watching++; },
            unobserve: () => { entry.watching = Math.max(0, entry.watching - 1); },
            disconnect: () => { entry.watching = 0; },
          };
        };
        fire = () => {
          for (const entry of observers.slice()) if (entry.watching > 0) entry.callback();
        };
      });

      afterEach(() => {
        (globalThis as Record<string, unknown>)['ResizeObserver'] = realObserver;
      });

      it('P2: mount-and-first-report cost is linear in text nodes, not its square', () => {
        roomPx = 40;

        const small = freshContainer();
        reads = 0;
        const smallHandle = renderUiNode(small, stackOf(2), { width: 400, height: 30 }, null, 120, testHost());
        fire();
        const smallReads = reads;
        smallHandle.destroy();
        small.remove();

        const large = freshContainer();
        reads = 0;
        const largeHandle = renderUiNode(large, stackOf(20), { width: 400, height: 30 }, null, 120, testHost());
        fire();
        const largeReads = reads;
        largeHandle.destroy();
        large.remove();

        expect(largeReads).toBeLessThanOrEqual(15 * smallReads);
      });

      it('P3: one width notification settles the tree once, not once per text node', () => {
        roomPx = 1000;

        const small = freshContainer();
        renderUiNode(small, stackOf(2), { width: 400, height: 30 }, null, 120, testHost());
        reads = 0;
        fire();
        const smallReads = reads;
        small.remove();

        const large = freshContainer();
        renderUiNode(large, stackOf(12), { width: 400, height: 30 }, null, 120, testHost());
        reads = 0;
        fire();
        const largeReads = reads;
        large.remove();

        expect(largeReads).toBeLessThanOrEqual(12 * smallReads);
      });

      it('P5: a destroyed tree is never measured again, and siblings keep fitting', () => {
        roomPx = 1000;
        const containerA = freshContainer();
        const containerB = freshContainer();

        const handleA = renderUiNode(
          containerA, header('System Volume'), { width: 120, height: 30 }, null, 120, testHost());
        const handleB = renderUiNode(
          containerB, header('Battery Level'), { width: 120, height: 30 }, null, 120, testHost());
        const capturedA = Array.from(containerA.querySelectorAll('.widget-text')) as HTMLElement[];

        handleA.destroy();
        reads = 0;
        readTargets = [];
        fire();
        handleB.update(header('Battery Level'), { width: 120, height: 30 }, null);

        for (const element of capturedA) expect(readTargets.indexOf(element)).toBe(-1);

        const textsB = Array.from(containerB.querySelectorAll('.widget-text')) as HTMLElement[];
        for (const element of textsB) {
          expect(naturalWidth(element)).toBeLessThanOrEqual(element.getBoundingClientRect().width + 0.01);
        }

        containerA.remove();
        containerB.remove();
      });
    });
  });
});

describe('macrodeck.dynamic-text time with a host hour cycle', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  function shown(format: string, overrides: Partial<UiRenderHost>): string {
    const tree = {
      id: 'clock', type: 'macrodeck.dynamic-text', properties: { format, value: { $time: { zone: 'UTC' } } },
    } as UiNode;
    renderUiNode(container, tree, { width: 100, height: 100 }, null, 120, testHost(overrides));
    return (container.querySelector('.widget-dynamic-text') as HTMLElement).textContent ?? '';
  }

  it('writes a 24-hour time in the language\'s own padding when the host prefers h23', () => {
    expect(shown('time', { culture: () => 'es', hourCycle: () => 'h23' })).toBe('3:04');
  });

  it('adds a day period when the host prefers h12, even in a 24-hour language', () => {
    const german = shown('time', { culture: () => 'de', hourCycle: () => 'h12' });

    expect(german).toContain('3:04');
    expect(german.length).toBeGreaterThan('3:04'.length);
  });

  it('renders exactly as before when the host has no preference', () => {
    const withoutMember = shown('time', {});
    container.innerHTML = '';
    const undecided = shown('time', { hourCycle: () => undefined });

    expect(withoutMember).toBe('3:04');
    expect(undecided).toBe(withoutMember);
  });

  it('keeps a pinned 24-hour face when the host prefers h12', () => {
    expect(shown('time-24h', { hourCycle: () => 'h12' })).toBe('03:04');
  });
});
