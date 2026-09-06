import { UiComponentBox } from '../ui-framework/layout';
import { nodeClaimsGesture } from '../ui-framework/node-gestures';
import { emitsEvent } from '../ui-framework/node-properties.util';
import { isUnsupportedResolution, resolveRenderableNode } from '../ui-framework/node-resolution.util';
import { UiNode } from '../ui-framework/ui-node.interface';
import {
  DEFAULT_UI_COMPONENT_REGISTRY,
  UiComponentContext,
  UiComponentDefinition,
  UiComponentRegistry,
} from '../ui-framework/component-registry';
import { UiRenderHost } from './ui-render-host';
import { disableContextMenu } from '../util/disable-context-menu';
import {
  setAttribute as writeAttribute,
  setClass as writeClass,
  setClassName as writeClassName,
  setStyle as writeStyle,
} from './dom-writes';
import { createTextFitScope, MountedFit, TextFit, TextFitScope } from './text-fit';

export interface UiNodeRenderHandle {
  element(): Element | null;

  update(node: UiNode, box: UiComponentBox | null, crossExtent: number | null, basis?: number): void;
  destroy(): void;
}

export interface UiRenderTimers {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
}

const REAL_TIMERS: UiRenderTimers = {
  set: (callback: () => void, delayMs: number) => setTimeout(callback, delayMs),
  clear: (handle: unknown) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export interface UiNodeRenderOptions {
  registry?: UiComponentRegistry;

  timers?: UiRenderTimers;
}

function px(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : `${value}px`;
}

export function renderUiNode(
  container: Element,
  node: UiNode,
  box: UiComponentBox | null,
  crossExtent: number | null,
  initialBasis: number,
  host: UiRenderHost,
  options?: UiNodeRenderOptions,
): UiNodeRenderHandle {
  const registry = options?.registry ?? DEFAULT_UI_COMPONENT_REGISTRY;
  const timers = options?.timers ?? REAL_TIMERS;
  return renderInScope(container, node, box, crossExtent, initialBasis, host, registry, timers, createTextFitScope());
}

function renderInScope(
  container: Element,
  node: UiNode,
  box: UiComponentBox | null,
  crossExtent: number | null,
  initialBasis: number,
  host: UiRenderHost,
  registry: UiComponentRegistry,
  timers: UiRenderTimers,
  scope: TextFitScope,
): UiNodeRenderHandle {
  function setStyle(element: HTMLElement | SVGElement, name: string, value: string | null): void {
    if (writeStyle(element, name, value)) scope.markDirty();
  }

  function setClass(element: Element, name: string, on: boolean): void {
    if (writeClass(element, name, on)) scope.markDirty();
  }

  function setClassName(element: Element, value: string): void {
    if (writeClassName(element, value)) scope.markDirty();
  }

  function setAttribute(element: Element, name: string, value: string | null): void {
    if (writeAttribute(element, name, value)) scope.markDirty();
  }

  let basis = initialBasis;
  let currentType: string | null = null;
  let activeDefinition: UiComponentDefinition<unknown> | undefined;
  let componentState: unknown;

  const isTreeRoot = typeof container.closest !== 'function'
    || container.closest('[data-node-id]') === null;
  let root: HTMLElement | SVGElement | null = null;
  let children: UiNodeRenderHandle[] = [];
  let childKeys: string[] = [];
  const parts: { [name: string]: HTMLElement | SVGElement } = {};

  let renderNode: UiNode = node;
  let lastCross: number | null = crossExtent;
  let lastBox: UiComponentBox | null = box;
  let resolved: UiComponentBox = { width: basis, height: basis };

  let lastNode: UiNode = node;

  let timeTimer: unknown = null;

  function emit(target: UiNode, name: string, payload?: unknown): void {
    if (!emitsEvent(target, name)) return;
    host.emit(target, name, payload);
  }

  let mountedFit: MountedFit | null = null;

  function releaseChildren(): void {
    for (let index = 0; index < children.length; index++) children[index].destroy();
    children = [];
    childKeys = [];
  }

  function clearTimeTick(): void {
    if (timeTimer !== null) {
      timers.clear(timeTimer);
      timeTimer = null;
    }
  }

  function dropFit(): void {
    if (mountedFit === null) return;
    scope.drop(mountedFit);
    mountedFit = null;
  }

  function discardRoot(): void {
    releaseChildren();
    clearTimeTick();
    dropFit();
    if (activeDefinition?.release) activeDefinition.release(ctx);
    for (const name in parts) {
      if (Object.prototype.hasOwnProperty.call(parts, name)) delete parts[name];
    }
    if (root !== null && root.parentNode) root.parentNode.removeChild(root);
    root = null;
    activeDefinition = undefined;
    componentState = undefined;
  }

  function effectiveBox(given: UiComponentBox | null): UiComponentBox {
    return given === null ? { width: basis, height: basis } : given;
  }

  function sizeTo(element: HTMLElement | SVGElement, size: UiComponentBox): void {
    setStyle(element, 'width', px(size.width));
    setStyle(element, 'height', px(size.height));
  }

  function part(name: string, tag: string, namespace?: string, parent?: Element): HTMLElement | SVGElement {
    let element = parts[name];
    if (element === undefined) {
      element = (namespace === undefined
        ? document.createElement(tag)
        : document.createElementNS(namespace, tag)) as HTMLElement | SVGElement;
      parts[name] = element;
    }
    const target = parent ?? root!;
    if (element.parentNode !== target) target.appendChild(element);
    return element;
  }

  function dropPart(name: string): void {
    const element = parts[name];
    if (element === undefined) return;
    if (element.parentNode) element.parentNode.removeChild(element);
    delete parts[name];
  }

  function syncChildren(
    parent: Element,
    entries: Array<{ child: UiNode; box: UiComponentBox; crossExtent: number | null }>,
  ): void {
    const keys: string[] = [];
    for (let index = 0; index < entries.length; index++) {
      keys.push(`${entries[index].child.id}:${entries[index].child.type}`);
    }

    if (keys.length !== childKeys.length) {
      scope.markDirty();
    } else {
      for (let index = 0; index < keys.length; index++) {
        if (keys[index] !== childKeys[index]) {
          scope.markDirty();
          break;
        }
      }
    }

    // Where this run of children starts, so new ones land among them rather than after whatever the
    // node paints around them - a button's artwork sits before its children, its tint after them.
    const first = children.length > 0 ? children[0].element() : null;
    const before = first === null ? null : first.previousSibling;

    const reusable: { [key: string]: UiNodeRenderHandle } = {};
    for (let index = 0; index < children.length; index++) {
      // A duplicate key would shadow the first holder, which then never gets destroyed below.
      if (!Object.prototype.hasOwnProperty.call(reusable, childKeys[index])) {
        reusable[childKeys[index]] = children[index];
      }
    }

    const wanted: { [key: string]: true } = {};
    for (let index = 0; index < keys.length; index++) wanted[keys[index]] = true;

    for (let index = 0; index < children.length; index++) {
      if (!wanted[childKeys[index]] || reusable[childKeys[index]] !== children[index]) {
        children[index].destroy();
      }
    }

    const next: UiNodeRenderHandle[] = [];
    for (let index = 0; index < entries.length; index++) {
      const entry = entries[index];
      const existing = reusable[keys[index]];

      if (existing !== undefined) {
        delete reusable[keys[index]];
        existing.update(entry.child, entry.box, entry.crossExtent, basis);
        next.push(existing);
        continue;
      }

      next.push(renderInScope(
        parent, entry.child, entry.box, entry.crossExtent, basis, host, registry, timers, scope));
    }

    let cursor: Node | null = before;
    for (let index = 0; index < next.length; index++) {
      const element = next[index].element();
      if (element === null) continue;

      const expected = cursor === null ? parent.firstChild : cursor.nextSibling;
      // Moving an element that is already where it belongs would take it out of the document and put it
      // back, which is exactly what this exists to avoid.
      if (element !== expected) parent.insertBefore(element, expected);
      cursor = element;
    }

    children = next;
    childKeys = keys;
  }

  function paintWithFits(current: UiNode, definition: UiComponentDefinition<unknown> | undefined, type: string): void {
    const outermost = scope.enter();
    try {
      if (definition) {
        definition.paint(current, ctx);
      } else {
        setClassName(root!, 'widget-node-unsupported');
        setAttribute(root!, 'data-unsupported-type', type);
        setAttribute(root!, 'title', type);
        sizeTo(root!, resolved);
      }
    } finally {
      scope.leave();
    }
    if (outermost) scope.settle();
  }

  function keepFit(element: Element, fit: TextFit, signature: string): void {
    mountedFit = scope.keep(mountedFit, element, fit, signature);
  }

  function pressTint(target: UiNode): void {
    const claims = nodeClaimsGesture(target);
    setClass(root!, 'widget-pressable', claims);
    if (!claims) {
      dropPart('tint');
      return;
    }
    const tint = part('tint', 'div');
    // A press is a pointer state, not a model state, so a repaint that happens mid-press must leave it
    // alone. `setClassName` rewrites the whole attribute, which would drop the active class the gesture
    // owns - and every tree holding a clock or a playing track is repainted once a second, so the tint
    // would vanish under the finger.
    const held = tint.classList.contains('widget-press-tint-active');
    setClassName(tint, 'widget-press-tint');
    setClass(tint, 'widget-press-tint-active', held);
  }

  const ctx: UiComponentContext<unknown> = {
    get element() { return root!; },
    host,
    get basis() { return basis; },
    get crossExtent() { return lastCross; },
    get box() { return resolved; },
    isTreeRoot,
    registry,
    current: () => renderNode,
    setStyle,
    setClass,
    setClassName,
    setAttribute,
    sizeTo,
    part,
    dropPart,
    syncChildren,
    keepFit,
    emit,
    pressTint,
    repaint(): void {
      if (root !== null) render(lastNode, lastBox, lastCross);
    },
    get state() { return componentState; },
    set state(value: unknown) { componentState = value; },
  };

  function scheduleTimeTick(current: UiNode, definition: UiComponentDefinition<unknown> | undefined): void {
    clearTimeTick();

    const period = definition?.tickPeriodMs ? definition.tickPeriodMs(current) : null;
    if (period === null || period === undefined) return;

    timeTimer = timers.set(() => {
      timeTimer = null;
      if (root === null) return;
      render(lastNode, lastBox, lastCross);
    }, period - (host.now() % period));
  }

  function render(next: UiNode, nextBox: UiComponentBox | null, nextCross: number | null): void {
    const capabilities = registry.capabilities();
    const resolution = resolveRenderableNode(next, capabilities);
    const unsupported = isUnsupportedResolution(resolution);
    const current = unsupported ? next : resolution;
    const type = unsupported ? resolution.unsupportedType : current.type;
    const definition = unsupported ? undefined : registry.get(type);

    renderNode = current;
    lastNode = next;
    lastCross = nextCross;
    lastBox = nextBox;
    resolved = effectiveBox(nextBox);

    // A definition swap is a rebuild too: `register` may replace a type that is already mounted, and
    // painting the new definition onto an element the old one created - over state the old one made,
    // releasing through the old one - would half-swap the instance. Registration normally happens
    // before anything mounts, so in practice this only ever fires on the type change.
    if (type !== currentType || root === null || definition !== activeDefinition) {
      discardRoot();
      currentType = type;
      activeDefinition = definition;
      componentState = definition?.createState ? definition.createState() : undefined;
      root = definition ? definition.create(document) : document.createElement('div');
      container.appendChild(root);
      // Once per tree rather than once per node: the listener catches every node under it, and a widget
      // answers a long press itself wherever it is mounted - a deck tile, a modal, a folder view.
      if (isTreeRoot) disableContextMenu(root);
      if (definition?.bind) definition.bind(ctx);
    }

    setAttribute(root, 'data-node-id', current.id);
    setAttribute(root, 'data-node-type', type);

    paintWithFits(current, definition, type);
    scheduleTimeTick(current, definition);
  }

  render(node, box, crossExtent);

  return {
    element(): Element | null {
      return root;
    },

    update(
      nextNode: UiNode,
      nextBox: UiComponentBox | null,
      nextCross: number | null,
      nextBasis?: number,
    ): void {
      if (nextBasis !== undefined && nextBasis !== basis) basis = nextBasis;
      render(nextNode, nextBox, nextCross);
    },

    destroy(): void {
      discardRoot();
      currentType = null;
    },
  };
}
