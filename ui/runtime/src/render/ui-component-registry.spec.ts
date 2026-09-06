import { UiNode } from '../ui-framework/ui-node.interface';
import { containsTickingNode } from '../ui-framework/node-gestures';
import {
  createUiComponentRegistry,
  DEFAULT_UI_COMPONENT_REGISTRY,
  UI_CORE_COMPONENTS,
  UiComponentDefinition,
} from '../ui-framework/component-registry';
import { renderUiNode, UiNodeRenderHandle, UiRenderTimers } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';

const SVG_NS = 'http://www.w3.org/2000/svg';

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

function freshContainer(): HTMLElement {
  const element = document.createElement('div');
  document.body.appendChild(element);
  return element;
}

describe('the component registry', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  it('renders a component the core does not ship', () => {
    let created: HTMLElement | null = null;
    const gauge: UiComponentDefinition = {
      type: 'acme.gauge',
      create(doc) {
        created = doc.createElement('div');
        return created;
      },
      paint(componentNode, ctx) {
        ctx.setAttribute(ctx.element, 'data-gauge-value', '42');
        (ctx.element as HTMLElement).textContent = '42%';
      },
    };
    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, gauge);
    const tree = node('ui.stack', {}, [node('acme.gauge')]);

    renderUiNode(container, tree, { width: 100, height: 100 }, null, 100, testHost(), { registry });

    const element = container.querySelector('[data-node-type="acme.gauge"]') as HTMLElement;
    expect(element).not.toBeNull();
    expect(element).toBe(created!);
    expect(element.tagName).toBe('DIV');
    expect(element.getAttribute('data-gauge-value')).toBe('42');
    expect(element.textContent).toBe('42%');
    expect(container.querySelector('.widget-node-unsupported')).toBeNull();
  });

  it('lets a definition create an SVG element instead of building one from a tag name', () => {
    const svgGauge: UiComponentDefinition = {
      type: 'acme.svg-gauge',
      create(doc) {
        return doc.createElementNS(SVG_NS, 'svg');
      },
      paint(componentNode, ctx) {
        ctx.setAttribute(ctx.element, 'data-painted', 'yes');
      },
    };
    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, svgGauge);

    renderUiNode(container, node('acme.svg-gauge'), { width: 50, height: 50 }, null, 50, testHost(), { registry });

    const element = container.querySelector('[data-node-type="acme.svg-gauge"]')!;
    expect(element instanceof SVGElement).toBeTrue();
    expect(element.namespaceURI).toBe(SVG_NS);
    expect(element.getAttribute('data-painted')).toBe('yes');
  });

  describe('the fallback chain', () => {
    it('walks to the first supported link, case A: unsupported -> supported -> deeper supported', () => {
      const last = node('ui.text', { text: 'LAST' });
      const first: UiNode = { ...node('ui.text', { text: 'FIRST' }), fallback: last };
      const root: UiNode = { ...node('acme.unregistered'), fallback: first };

      renderUiNode(container, root, { width: 200, height: 30 }, null, 100, testHost());

      const texts = container.querySelectorAll('.widget-text');
      expect(texts.length).toBe(1);
      expect((texts[0] as HTMLElement).textContent).toBe('FIRST');
      expect(container.querySelector(`[data-node-id="${root.id}"]`)).toBeNull();
      expect(container.querySelector(`[data-node-id="${last.id}"]`)).toBeNull();
      expect(container.querySelector('.widget-node-unsupported')).toBeNull();
    });

    it('walks to the first supported link, case B: unsupported -> unsupported -> supported', () => {
      const deepest = node('ui.text', { text: 'DEEPEST' });
      const mid: UiNode = { ...node('acme.also-unregistered'), fallback: deepest };
      const root: UiNode = { ...node('acme.unregistered'), fallback: mid };

      renderUiNode(container, root, { width: 200, height: 30 }, null, 100, testHost());

      const texts = container.querySelectorAll('.widget-text');
      expect(texts.length).toBe(1);
      expect((texts[0] as HTMLElement).textContent).toBe('DEEPEST');
      expect(container.querySelector('.widget-node-unsupported')).toBeNull();
    });

    it('degrades a chain that bottoms out without taking its siblings with it', () => {
      const before = node('ui.text', { text: 'before' });
      const badFallback = node('acme.also-unregistered');
      const bad: UiNode = { ...node('acme.unregistered'), fallback: badFallback };
      const after = node('ui.text', { text: 'after' });
      const stack = node('ui.stack', {}, [before, bad, after]);

      expect(() => {
        renderUiNode(container, stack, { width: 300, height: 100 }, null, 100, testHost());
      }).not.toThrow();

      const stackElement = container.querySelector('.widget-stack')!;
      expect(stackElement.children.length).toBe(3);

      const middle = stackElement.children[1] as HTMLElement;
      expect(middle.classList.contains('widget-node-unsupported')).toBeTrue();
      expect(middle.getAttribute('data-unsupported-type')).toBe('acme.also-unregistered');

      expect(stackElement.children[0].textContent).toBe('before');
      expect(stackElement.children[2].textContent).toBe('after');
    });
  });

  describe('version negotiation', () => {
    function versionedDefinition(range?: { minimum: number; maximum: number }): UiComponentDefinition {
      return {
        type: 'acme.versioned',
        version: range,
        create: doc => doc.createElement('div'),
        paint: (componentNode, ctx) => { (ctx.element as HTMLElement).textContent = 'acme.versioned'; },
      };
    }

    it('supports the declared version range rather than the fact of registration', () => {
      const registry = createUiComponentRegistry(
        ...UI_CORE_COMPONENTS, versionedDefinition({ minimum: 1, maximum: 2 }));

      const v1 = freshContainer();
      renderUiNode(v1, { ...node('acme.versioned'), requiredComponentVersion: 1 },
        { width: 20, height: 20 }, null, 20, testHost(), { registry });
      expect(v1.querySelector('[data-node-type="acme.versioned"]')).not.toBeNull();
      v1.remove();

      const v2 = freshContainer();
      renderUiNode(v2, { ...node('acme.versioned'), requiredComponentVersion: 2 },
        { width: 20, height: 20 }, null, 20, testHost(), { registry });
      expect(v2.querySelector('[data-node-type="acme.versioned"]')).not.toBeNull();
      v2.remove();

      const v3fallback = node('ui.text', { text: 'fallback' });
      const v3 = freshContainer();
      renderUiNode(v3, { ...node('acme.versioned'), requiredComponentVersion: 3, fallback: v3fallback },
        { width: 20, height: 20 }, null, 20, testHost(), { registry });
      expect(v3.querySelector('[data-node-type="acme.versioned"]')).toBeNull();
      expect((v3.querySelector('.widget-text') as HTMLElement).textContent).toBe('fallback');
      v3.remove();
    });

    it('defaults an unversioned definition to {minimum: 1, maximum: 1}', () => {
      const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, versionedDefinition(undefined));

      const req1 = freshContainer();
      renderUiNode(req1, { ...node('acme.versioned'), requiredComponentVersion: 1 },
        { width: 20, height: 20 }, null, 20, testHost(), { registry });
      expect(req1.querySelector('[data-node-type="acme.versioned"]')).not.toBeNull();
      req1.remove();

      const fallback = node('ui.text', { text: 'fallback' });
      const req2 = freshContainer();
      renderUiNode(req2, { ...node('acme.versioned'), requiredComponentVersion: 2, fallback },
        { width: 20, height: 20 }, null, 20, testHost(), { registry });
      expect(req2.querySelector('[data-node-type="acme.versioned"]')).toBeNull();
      expect((req2.querySelector('.widget-text') as HTMLElement).textContent).toBe('fallback');
      req2.remove();
    });
  });

  describe('capabilities()', () => {
    it('comes from exactly what was registered', () => {
      const custom: UiComponentDefinition = {
        type: 'acme.custom',
        version: { minimum: 2, maximum: 5 },
        create: doc => doc.createElement('div'),
        paint: () => undefined,
      };
      const registry = createUiComponentRegistry(custom);

      expect(Object.keys(registry.capabilities())).toEqual(['acme.custom']);
      expect(registry.capabilities()['acme.custom']).toEqual({ minimum: 2, maximum: 5 });
    });

    it('never claims the configuration vocabulary on the default registry', () => {
      const caps = DEFAULT_UI_COMPONENT_REGISTRY.capabilities();
      expect(caps['stack']).toBeUndefined();
      expect(caps['string']).toBeUndefined();
      expect(caps['ui.stack']).toBeDefined();
    });
  });

  it('replaces a type on register, and registries are independent', () => {
    const originalTextDefinition = DEFAULT_UI_COMPONENT_REGISTRY.get('ui.text');
    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS);

    const replacement: UiComponentDefinition = {
      type: 'ui.text',
      create: doc => doc.createElement('span'),
      paint: (componentNode, ctx) => { (ctx.element as HTMLElement).textContent = 'REPLACED'; },
    };
    const displaced = registry.register(replacement);

    expect(displaced).toBe(originalTextDefinition);

    renderUiNode(container, node('ui.text', { text: 'original text' }),
      { width: 60, height: 20 }, null, 60, testHost(), { registry });
    const element = container.querySelector('[data-node-type="ui.text"]') as HTMLElement;
    expect(element.textContent).toBe('REPLACED');

    expect(DEFAULT_UI_COMPONENT_REGISTRY.get('ui.text')).toBe(originalTextDefinition);
  });

  it('rebuilds a mounted node when its type is registered over', () => {
    // Registration normally happens before anything mounts, but the seam allows the other order, and
    // painting a new definition onto the element and state the old one made would half-swap the
    // instance - the failure would surface as a listener bound to a component that no longer exists.
    const released: string[] = [];
    const first: UiComponentDefinition = {
      type: 'acme.swap',
      create: doc => doc.createElement('div'),
      paint: (componentNode, ctx) => { (ctx.element as HTMLElement).textContent = 'FIRST'; },
      release: () => released.push('first'),
    };
    const second: UiComponentDefinition = {
      type: 'acme.swap',
      create: doc => doc.createElement('section'),
      paint: (componentNode, ctx) => { (ctx.element as HTMLElement).textContent = 'SECOND'; },
    };

    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, first);
    const tree = node('acme.swap');
    const handle = renderUiNode(container, tree, { width: 60, height: 20 }, null, 60, testHost(),
      { registry });
    const before = container.querySelector('[data-node-type="acme.swap"]') as HTMLElement;
    expect(before.tagName).toBe('DIV');

    registry.register(second);
    handle.update(tree, { width: 60, height: 20 }, null);

    const after = container.querySelector('[data-node-type="acme.swap"]') as HTMLElement;
    expect(after).not.toBe(before);
    expect(after.tagName).toBe('SECTION');
    expect(after.textContent).toBe('SECOND');
    expect(released).toEqual(['first']);
  });

  it('keeps the same element instance across a property-only update', () => {
    let createCount = 0;
    const definition: UiComponentDefinition = {
      type: 'acme.counter',
      create(doc) {
        createCount++;
        return doc.createElement('div');
      },
      paint(componentNode, ctx) {
        (ctx.element as HTMLElement).textContent = String(componentNode.properties?.['value'] ?? '');
      },
    };
    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, definition);
    const treeNode = { id: 'counter', type: 'acme.counter', properties: { value: 1 } } as UiNode;

    const handle = renderUiNode(container, treeNode, { width: 10, height: 10 }, null, 10, testHost(), { registry });
    const element = container.querySelector('[data-node-type="acme.counter"]') as HTMLElement;
    element.setAttribute('data-sentinel', 'kept');
    expect(createCount).toBe(1);

    handle.update({ id: 'counter', type: 'acme.counter', properties: { value: 2 } } as UiNode,
      { width: 10, height: 10 }, null);

    const after = container.querySelector('[data-node-type="acme.counter"]') as HTMLElement;
    expect(after).toBe(element);
    expect(after.getAttribute('data-sentinel')).toBe('kept');
    expect(after.textContent).toBe('2');
    expect(createCount).toBe(1);
  });

  it('rebuilds and releases when a node id changes type, handing the new definition a fresh state', () => {
    const released: unknown[] = [];
    const paintedStates: unknown[] = [];
    const stateObjectsB: unknown[] = [];

    const definitionA: UiComponentDefinition<{ tag: string }> = {
      type: 'acme.state-a',
      create: doc => doc.createElement('div'),
      createState: () => ({ tag: 'A' }),
      paint: (componentNode, ctx) => { paintedStates.push(ctx.state); },
      release: ctx => { released.push(ctx.state); },
    };
    const definitionB: UiComponentDefinition<{ tag: string }> = {
      type: 'acme.state-b',
      create: doc => doc.createElement('span'),
      createState: () => {
        const state = { tag: 'B' };
        stateObjectsB.push(state);
        return state;
      },
      paint: (componentNode, ctx) => { paintedStates.push(ctx.state); },
    };
    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, definitionA, definitionB);

    const handle = renderUiNode(container, { id: 'n', type: 'acme.state-a' } as UiNode,
      { width: 10, height: 10 }, null, 10, testHost(), { registry });
    const elementA = container.querySelector('[data-node-type="acme.state-a"]') as HTMLElement;
    expect(elementA).not.toBeNull();

    handle.update({ id: 'n', type: 'acme.state-b' } as UiNode, { width: 10, height: 10 }, null);

    expect(released.length).toBe(1);
    expect(released[0]).toEqual({ tag: 'A' });
    expect(elementA.isConnected).toBeFalse();

    const elementB = container.querySelector('[data-node-type="acme.state-b"]') as HTMLElement;
    expect(elementB).not.toBeNull();
    expect(elementB).not.toBe(elementA);
    expect(elementB.tagName).toBe('SPAN');

    const lastPaintedState = paintedStates[paintedStates.length - 1];
    expect(lastPaintedState).toBe(stateObjectsB[0]);
    expect(lastPaintedState).not.toBe(released[0]);
  });

  describe('ticking', () => {
    function livePeriodDefinition(): UiComponentDefinition {
      return {
        type: 'acme.live',
        create: doc => doc.createElement('div'),
        paint: (componentNode, ctx) => { (ctx.element as HTMLElement).textContent = String(ctx.host.now()); },
        tickPeriodMs: componentNode => (componentNode.properties?.['live'] === true ? 500 : null),
      };
    }

    it('is a per-definition declaration, read by containsTickingNode through the registry', () => {
      const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, livePeriodDefinition());

      const liveLeaf = node('acme.live', { live: true });
      const liveTree = node('ui.stack', {}, [node('ui.stack', {}, [liveLeaf])]);
      expect(containsTickingNode(liveTree, registry)).toBeTrue();

      const staticLeaf = node('acme.live', {});
      const staticTree = node('ui.stack', {}, [staticLeaf]);
      expect(containsTickingNode(staticTree, registry)).toBeFalse();

      const unregisteredTree = node('ui.stack', {}, [node('acme.never-registered')]);
      expect(containsTickingNode(unregisteredTree, registry)).toBeFalse();
    });

    it('asks for no repaint for a progress reference that is not advancing', () => {
      // A halted reference resolves to the same position at every instant. Ticking for one would cost a
      // timer per node on every client to redraw a value that cannot have changed - which is the whole
      // reason the period is the definition's answer about a specific node rather than about its type.
      const reference = (rate: number) => ({
        $progress: { positionMs: 30_000, durationMs: 210_000, rate, anchor: '2026-01-02T03:04:05.000Z' },
      });

      const playing = node('ui.stack', {}, [node('macrodeck.progress-bar', { value: reference(1) })]);
      expect(containsTickingNode(playing, DEFAULT_UI_COMPONENT_REGISTRY)).toBeTrue();

      const paused = node('ui.stack', {}, [node('macrodeck.progress-bar', { value: reference(0) })]);
      expect(containsTickingNode(paused, DEFAULT_UI_COMPONENT_REGISTRY)).toBeFalse();

      const pausedText = node('ui.stack', {}, [node('macrodeck.progress-text', { value: reference(0) })]);
      expect(containsTickingNode(pausedText, DEFAULT_UI_COMPONENT_REGISTRY)).toBeFalse();

      const withoutReference = node('ui.stack', {}, [node('macrodeck.progress-bar', {})]);
      expect(containsTickingNode(withoutReference, DEFAULT_UI_COMPONENT_REGISTRY)).toBeFalse();
    });

    it('repaints a ticking node on its own schedule, with no update() call', () => {
      let now = Date.parse('2026-01-02T03:04:05.000Z');
      let scheduled: { at: number; callback: () => void } | null = null;
      const timers: UiRenderTimers = {
        set(callback, delayMs) {
          scheduled = { at: now + delayMs, callback };
          return 'handle';
        },
        clear() { scheduled = null; },
      };
      const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, livePeriodDefinition());
      const host = testHost({ now: () => now });

      renderUiNode(container, node('acme.live', { live: true }), { width: 10, height: 10 }, null, 10, host,
        { registry, timers });

      const element = container.querySelector('[data-node-type="acme.live"]') as HTMLElement;
      const before = element.textContent;
      expect(scheduled).not.toBeNull();

      const due = scheduled!;
      now = due.at;
      scheduled = null;
      due.callback();

      expect(element.textContent).not.toBe(before);
      expect(element.textContent).toBe(String(now));
    });
  });

  it('grants no interaction the node did not itself declare, regardless of a definition\'s own events metadata', () => {
    const emitted: string[] = [];
    const host = testHost({
      emit: (target, name) => { emitted.push(`${target.id}:${name}`); },
    });

    const withMetadata: UiComponentDefinition = {
      type: 'acme.emitter-with-metadata',
      events: ['ping'],
      create: doc => doc.createElement('div'),
      bind: ctx => ctx.emit(ctx.current(), 'ping'),
      paint: () => undefined,
    };
    const withoutMetadata: UiComponentDefinition = {
      type: 'acme.emitter-no-metadata',
      create: doc => doc.createElement('div'),
      bind: ctx => ctx.emit(ctx.current(), 'ping'),
      paint: () => undefined,
    };
    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS, withMetadata, withoutMetadata);

    // Declares the very event the definition emits - fires.
    const declares = { id: 'declares', type: 'acme.emitter-with-metadata', properties: { events: ['ping'] } } as UiNode;
    // No declared events at all, despite the definition listing 'ping' - silent.
    const silentUndeclared = { id: 'silent-undeclared', type: 'acme.emitter-with-metadata', properties: {} } as UiNode;
    // Declares a different event than the one emitted - silent.
    const silentWrongEvent =
      { id: 'silent-wrong', type: 'acme.emitter-with-metadata', properties: { events: ['other'] } } as UiNode;
    // The decisive node: its definition declares NO `events` metadata at all, but the node itself
    // declares the event the definition emits - it still fires.
    const decisiveFires =
      { id: 'decisive', type: 'acme.emitter-no-metadata', properties: { events: ['ping'] } } as UiNode;

    const tree = node('ui.stack', {}, [declares, silentUndeclared, silentWrongEvent, decisiveFires]);
    renderUiNode(container, tree, { width: 400, height: 100 }, null, 100, host, { registry });

    expect(emitted).toEqual(['declares:ping', 'decisive:ping']);
  });

  it('destroy() releases every component in the tree, nested ones included, and empties the container', () => {
    const released: string[] = [];
    const trackingDefinition = (type: string): UiComponentDefinition => ({
      type,
      create: doc => doc.createElement('div'),
      paint: () => undefined,
      release: () => { released.push(type); },
    });
    const registry = createUiComponentRegistry(
      ...UI_CORE_COMPONENTS, trackingDefinition('acme.leaf-a'), trackingDefinition('acme.leaf-b'));

    const tree = node('ui.stack', {}, [
      node('acme.leaf-a'),
      node('ui.stack', {}, [node('acme.leaf-b')]),
    ]);
    const handle: UiNodeRenderHandle =
      renderUiNode(container, tree, { width: 200, height: 100 }, null, 100, testHost(), { registry });
    expect(container.children.length).toBeGreaterThan(0);

    handle.destroy();

    expect(released.slice().sort()).toEqual(['acme.leaf-a', 'acme.leaf-b']);
    expect(container.children.length).toBe(0);
  });
});
