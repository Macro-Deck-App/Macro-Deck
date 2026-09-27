import { UiNode } from '../ui-framework/ui-node.interface';
import { DEFAULT_UI_COMPONENT_REGISTRY } from '../ui-framework/component-registry';
import { renderUiNode } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';

let nextId = 0;
function node(type: string, properties: Record<string, unknown> = {}, children?: UiNode[], id?: string): UiNode {
  const built = { id: id ?? `n${++nextId}`, type, properties } as UiNode;
  if (children) built.children = children;
  return built;
}

function pointer(type: string, fields: { clientX?: number; clientY?: number; pointerId?: number } = {}): Event {
  const event = new Event(type, { bubbles: true, cancelable: true }) as Event & {
    clientX: number; clientY: number; pointerId: number; button: number;
  };
  event.clientX = fields.clientX ?? 0;
  event.clientY = fields.clientY ?? 0;
  event.pointerId = fields.pointerId ?? 1;
  event.button = 0;
  return event;
}

interface Emitted {
  node: string;
  name: string;
  payload: unknown;
}

describe('pointer family', () => {
  let container: HTMLElement;
  let emitted: Emitted[];

  const host = (): UiRenderHost => ({
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: () => null,
    now: () => 0,
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: (target, name, payload) => emitted.push({ node: target.id, name, payload }),
  });

  beforeEach(() => {
    emitted = [];
    container = document.createElement('div');
    document.body.appendChild(container);
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(0));
    spyOn(performance, 'now').and.callFake(() => Date.now());
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    container.remove();
  });

  const mount = (tree: UiNode) =>
    renderUiNode(container, tree, { width: 120, height: 120 }, null, 120, host(), { registry: DEFAULT_UI_COMPONENT_REGISTRY });

  const byId = (id: string) => {
    const element = container.querySelector(`[data-node-id="${id}"]`) as HTMLElement;
    element.getBoundingClientRect = () => ({ left: 0, top: 0, width: 120, height: 60 }) as DOMRect;
    return element;
  };

  const pad = (events: string[], children: UiNode[] = [node('ui.stack')], id = 'pad') =>
    node('ui.modifier', { events }, children, id);

  const stream = ['pointer-down', 'pointer-move', 'pointer-up', 'tap'];

  const names = () => emitted.map(entry => entry.name);
  const payloads = (name: string) => emitted.filter(entry => entry.name === name).map(entry => entry.payload as any);

  it('reports a pointer-down with its position, time and the node size in basis fractions', () => {
    mount(pad(stream));

    byId('pad').dispatchEvent(pointer('pointerdown', { clientX: 60, clientY: 30 }));

    const [down] = payloads('pointer-down');
    expect(down).toEqual({ id: jasmine.any(Number), x: 0.5, y: 0.25, t: 0, width: 1, height: 0.5 });
    expect(Number.isInteger(down.id) && down.id >= 0 && down.id < 2 ** 31).toBeTrue();
  });

  it('gives every finger its own id, kept from down to up', () => {
    mount(pad(stream));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown', { pointerId: 7 }));
    element.dispatchEvent(pointer('pointerdown', { pointerId: 8, clientX: 60 }));
    element.dispatchEvent(pointer('pointerup', { pointerId: 8, clientX: 60 }));
    element.dispatchEvent(pointer('pointerup', { pointerId: 7 }));

    const downs = payloads('pointer-down').map(down => down.id);
    const ups = payloads('pointer-up').map(up => up.id);
    expect(downs[0]).not.toBe(downs[1]);
    expect(ups).toEqual([downs[1], downs[0]]);
  });

  it('batches moves and sends them no more often than every 16 ms, all samples in order', () => {
    mount(pad(['pointer-move']));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown'));
    element.dispatchEvent(pointer('pointermove', { clientX: 12 }));
    jasmine.clock().tick(5);
    element.dispatchEvent(pointer('pointermove', { clientX: 24 }));
    jasmine.clock().tick(5);
    element.dispatchEvent(pointer('pointermove', { clientX: 36 }));
    expect(payloads('pointer-move').length).toBe(1);

    jasmine.clock().tick(6);

    const moves = payloads('pointer-move');
    expect(moves.length).toBe(2);
    expect(moves[0].samples.map((sample: any) => sample.x)).toEqual([0.1]);
    expect(moves[1].samples.map((sample: any) => [sample.x, sample.t])).toEqual([[0.2, 5], [0.3, 10]]);
  });

  it('flushes pending samples before the pointer-up, and sends the tap after the last pointer-up', () => {
    mount(pad(stream));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown', { pointerId: 1 }));
    element.dispatchEvent(pointer('pointerdown', { pointerId: 2, clientX: 60 }));
    element.dispatchEvent(pointer('pointermove', { pointerId: 1, clientX: 1 }));
    element.dispatchEvent(pointer('pointermove', { pointerId: 1, clientX: 2 }));
    element.dispatchEvent(pointer('pointerup', { pointerId: 1, clientX: 2 }));
    element.dispatchEvent(pointer('pointerup', { pointerId: 2, clientX: 60 }));

    expect(names()).toEqual([
      'pointer-down', 'pointer-down', 'pointer-move', 'pointer-move', 'pointer-up', 'pointer-up', 'tap',
    ]);
    expect(payloads('tap')).toEqual([{ pointers: 2 }]);
  });

  it('reports a quick one- or three-finger touch as a tap with that many pointers', () => {
    mount(pad(['tap']));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown'));
    element.dispatchEvent(pointer('pointerup'));
    for (const pointerId of [1, 2, 3]) element.dispatchEvent(pointer('pointerdown', { pointerId }));
    for (const pointerId of [3, 2, 1]) element.dispatchEvent(pointer('pointerup', { pointerId }));

    expect(emitted).toEqual([
      { node: 'pad', name: 'tap', payload: { pointers: 1 } },
      { node: 'pad', name: 'tap', payload: { pointers: 3 } },
    ]);
  });

  it('sends no tap for a touch that travelled past the slop, took too long or was cancelled', () => {
    mount(pad(stream));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown'));
    element.dispatchEvent(pointer('pointerup', { clientX: 12 }));

    element.dispatchEvent(pointer('pointerdown'));
    jasmine.clock().tick(401);
    element.dispatchEvent(pointer('pointerup'));

    element.dispatchEvent(pointer('pointerdown'));
    element.dispatchEvent(pointer('pointercancel', { clientX: 99 }));

    expect(names()).not.toContain('tap');
    expect(payloads('pointer-up')[2]).toEqual(jasmine.objectContaining({ x: 0, cancelled: true }));
    expect(payloads('pointer-up')[0].cancelled).toBeUndefined();
  });

  it('streams nothing for a node that declares only drag', () => {
    mount(pad(['drag', 'drag-end']));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown'));
    element.dispatchEvent(pointer('pointermove', { clientX: 24 }));
    element.dispatchEvent(pointer('pointerup', { clientX: 24 }));

    expect(names()).toEqual(['drag', 'drag-end']);
  });

  it('drags nothing on a node that declares only the pointer family', () => {
    mount(pad(stream));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown'));
    element.dispatchEvent(pointer('pointermove', { clientX: 60 }));
    element.dispatchEvent(pointer('pointerup', { clientX: 60 }));

    expect(names().filter(name => /drag|swipe|pinch/.test(name))).toEqual([]);
  });

  it('keeps drag on its own 100 ms cadence while the stream runs on the same node', () => {
    mount(pad(['drag', 'pointer-move', 'pointer-up']));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown'));
    for (let step = 1; step <= 10; step++) {
      element.dispatchEvent(pointer('pointermove', { clientX: step * 6 }));
      jasmine.clock().tick(16);
    }
    element.dispatchEvent(pointer('pointerup', { clientX: 60 }));

    const samples = payloads('pointer-move').reduce((all: any[], move) => all.concat(move.samples), []);
    expect(samples.length).toBe(10);
    expect(payloads('drag').length).toBe(2);
    expect(payloads('pointer-up').length).toBe(1);
  });

  it('leaves a pointer that starts on a slider inside the pad to the slider', () => {
    mount(pad(stream, [node('ui.slider', { level: 0.5, events: ['change'] }, [], 'slider')]));

    byId('slider').dispatchEvent(pointer('pointerdown'));
    byId('slider').dispatchEvent(pointer('pointerup'));

    expect(names().filter(name => name.startsWith('pointer') || name === 'tap')).toEqual([]);
  });

  it('lets the innermost pointer-family node own the finger, so one touch taps once', () => {
    mount(pad(['tap'], [pad(['tap'], [node('ui.stack')], 'inner')]));

    byId('inner').dispatchEvent(pointer('pointerdown'));
    byId('inner').dispatchEvent(pointer('pointerup'));

    expect(emitted).toEqual([{ node: 'inner', name: 'tap', payload: { pointers: 1 } }]);
  });

  it('keeps a drag wrapper inert for a finger that starts on a streaming child', () => {
    mount(pad(['drag', 'drag-end'], [pad(stream, [node('ui.stack')], 'child')], 'wrapper'));

    byId('child').dispatchEvent(pointer('pointerdown'));
    byId('child').dispatchEvent(pointer('pointermove', { clientX: 60 }));
    byId('child').dispatchEvent(pointer('pointerup', { clientX: 60 }));

    expect(emitted.filter(entry => entry.node === 'wrapper')).toEqual([]);
    expect(emitted.filter(entry => entry.node === 'child').map(entry => entry.name))
      .toEqual(['pointer-down', 'pointer-move', 'pointer-up']);
  });

  it('keeps a press ancestor from taking a streamed finger', () => {
    mount(node('ui.button', { events: ['press', 'press-start', 'press-end'] }, [pad(stream, [node('ui.stack')], 'child')], 'button'));

    byId('child').dispatchEvent(pointer('pointerdown'));
    byId('child').dispatchEvent(pointer('pointerup'));

    expect(emitted.filter(entry => entry.node === 'button')).toEqual([]);
    expect(names()).toEqual(['pointer-down', 'pointer-up', 'tap']);
  });

  it('streams a finger that starts on a button in the pad, lets the button press, and sends no tap', () => {
    mount(pad(stream, [node('ui.button', { events: ['press'] }, [], 'button')]));

    byId('button').dispatchEvent(pointer('pointerdown'));
    byId('button').dispatchEvent(pointer('pointerup'));

    expect(emitted.map(entry => `${entry.node}:${entry.name}`)).toEqual(['pad:pointer-down', 'pad:pointer-up', 'button:press']);
  });

  it('leaves a second finger on a streaming child out of an ancestor pinch', () => {
    mount(pad(['pinch', 'pinch-end'], [pad(stream, [node('ui.stack')], 'child')], 'wrapper'));
    const wrapper = byId('wrapper');

    wrapper.dispatchEvent(pointer('pointerdown', { pointerId: 1 }));
    byId('child').dispatchEvent(pointer('pointerdown', { pointerId: 2, clientX: 60 }));
    byId('child').dispatchEvent(pointer('pointermove', { pointerId: 2, clientX: 120 }));
    byId('child').dispatchEvent(pointer('pointerup', { pointerId: 2, clientX: 120 }));
    wrapper.dispatchEvent(pointer('pointerup', { pointerId: 1 }));

    expect(emitted.filter(entry => entry.node === 'wrapper')).toEqual([]);
    expect(emitted.filter(entry => entry.node === 'child').map(entry => entry.name))
      .toEqual(['pointer-down', 'pointer-move', 'pointer-up']);
  });

  it('still pinches with a second finger on a slider inside an older pinch wrapper', () => {
    mount(pad(['pinch', 'pinch-end'], [node('ui.slider', { level: 0.5, events: ['change'] }, [], 'slider')], 'wrapper'));
    const wrapper = byId('wrapper');

    wrapper.dispatchEvent(pointer('pointerdown', { pointerId: 1 }));
    byId('slider').dispatchEvent(pointer('pointerdown', { pointerId: 2, clientX: 60 }));
    wrapper.dispatchEvent(pointer('pointermove', { pointerId: 2, clientX: 120 }));
    wrapper.dispatchEvent(pointer('pointerup', { pointerId: 2, clientX: 120 }));
    wrapper.dispatchEvent(pointer('pointerup', { pointerId: 1 }));

    expect(emitted.filter(entry => entry.node === 'wrapper').map(entry => entry.name)).toEqual(['pinch', 'pinch-end']);
  });

  it('ends a held finger with a cancelled pointer-up when the node is repainted as another type', () => {
    const handle = mount(pad(stream));
    byId('pad').dispatchEvent(pointer('pointerdown', { clientX: 12 }));

    handle.update(node('ui.stack', { events: stream }, [], 'pad'), { width: 120, height: 120 }, null);

    expect(payloads('pointer-up')).toEqual([jasmine.objectContaining({ x: 0.1, cancelled: true })]);
  });

  it('sends nothing after the node stops declaring the family, and nothing leaves a disabled node', () => {
    const handle = mount(pad(stream));
    handle.update(pad([]), { width: 120, height: 120 }, null);
    byId('pad').dispatchEvent(pointer('pointerdown'));
    byId('pad').dispatchEvent(pointer('pointerup'));

    handle.update(node('ui.modifier', { events: stream, modifiers: { disabled: true } }, [node('ui.stack')], 'pad'),
      { width: 120, height: 120 }, null);
    byId('pad').dispatchEvent(pointer('pointerdown'));
    byId('pad').dispatchEvent(pointer('pointerup'));

    expect(emitted).toEqual([]);
  });

  it('turns off platform panning for a node that declares only tap', () => {
    mount(pad(['tap']));

    expect(byId('pad').style.getPropertyValue('touch-action')).toBe('none');
  });

  it('ends a finger whose pointer-up never arrived when the same pointer goes down again', () => {
    mount(pad(stream));
    const element = byId('pad');

    element.dispatchEvent(pointer('pointerdown', { clientX: 12 }));
    element.dispatchEvent(pointer('pointerdown', { clientX: 24 }));
    element.dispatchEvent(pointer('pointerup', { clientX: 24 }));

    const downs = payloads('pointer-down').map(down => down.id);
    expect(names()).toEqual(['pointer-down', 'pointer-up', 'pointer-down', 'pointer-up', 'tap']);
    expect(payloads('pointer-up')).toEqual([
      jasmine.objectContaining({ id: downs[0], x: 0.1, cancelled: true }),
      jasmine.objectContaining({ id: downs[1], x: 0.2 }),
    ]);
  });
});
