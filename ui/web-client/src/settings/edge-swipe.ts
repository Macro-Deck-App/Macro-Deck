import { ownsPointerAt } from '@macro-deck/runtime';

export const EDGE_SWIPE_ZONE_PX = 56;
export const EDGE_SWIPE_MIN_TRAVEL_PX = 48;
export const EDGE_SWIPE_MAX_DURATION_MS = 500;

const TILE_SURFACE_CLASS = 'deck-grid-tile-surface';

export interface EdgeSwipeOptions {
  root: HTMLElement;
  blocked(): boolean;
  onSwipe(): void;
  now?(): number;
}

export interface EdgeSwipeHandle {
  destroy(): void;
}

interface PointerDown {
  pointerId: number;
  target: EventTarget | null;
  surface: Element | null;
}

interface Gesture {
  touchId: number | null;
  pointerId: number | null;
  startX: number;
  startY: number;
  startedAt: number;
  down: PointerDown | null;
}

interface TouchPoint {
  identifier: number;
  clientX: number;
  clientY: number;
  target: EventTarget | null;
}

function tileSurfaceOf(target: EventTarget | null): Element | null {
  let node = target as Node | null;
  while (node !== null && node !== undefined) {
    if (node.nodeType === 1 && hasClass(node as Element, TILE_SURFACE_CLASS)) return node as Element;
    node = node.parentNode;
  }
  return null;
}

function hasClass(element: Element, name: string): boolean {
  const value = element.getAttribute('class');
  return value !== null && (' ' + value + ' ').indexOf(' ' + name + ' ') >= 0;
}

function touchesOf(event: Event): TouchPoint[] {
  const list = (event as TouchEvent).changedTouches as unknown as ArrayLike<TouchPoint> | undefined;
  const points: TouchPoint[] = [];
  if (list === undefined || list === null) return points;
  for (let index = 0; index < list.length; index++) points.push(list[index]);
  return points;
}

function pointerCancelEvent(pointerId: number): Event {
  if (typeof PointerEvent === 'function') {
    return new PointerEvent('pointercancel', { bubbles: true, pointerId: pointerId });
  }
  const event = document.createEvent('Event');
  event.initEvent('pointercancel', true, false);
  Object.defineProperty(event, 'pointerId', { value: pointerId });
  return event;
}

function connected(node: EventTarget | null): node is Node {
  return node !== null && document.documentElement.contains(node as Node);
}

export function installEdgeSwipe(options: EdgeSwipeOptions): EdgeSwipeHandle {
  const now = options.now === undefined ? () => Date.now() : options.now;
  let lastDown: PointerDown | null = null;
  let gesture: Gesture | null = null;

  function zone(): number {
    const inset = parseFloat(getComputedStyle(options.root).paddingLeft);
    return EDGE_SWIPE_ZONE_PX + (inset > 0 ? inset : 0);
  }

  function canStart(x: number, target: EventTarget | null): boolean {
    if (!connected(options.root) || options.blocked()) return false;
    if (x > zone()) return false;
    return !ownsPointerAt(target, null);
  }

  function begin(x: number, y: number, touchId: number | null, pointerId: number | null): void {
    gesture = { touchId, pointerId, startX: x, startY: y, startedAt: now(), down: lastDown };
  }

  function follow(x: number, y: number): void {
    const current = gesture;
    if (current === null) return;
    if (now() - current.startedAt > EDGE_SWIPE_MAX_DURATION_MS) {
      gesture = null;
      return;
    }
    const dx = x - current.startX;
    const dy = y - current.startY;
    if (dx < EDGE_SWIPE_MIN_TRAVEL_PX || Math.abs(dy) >= dx / 2) return;

    gesture = null;
    cancelPress(current.down);
    options.onSwipe();
  }

  // The press under the finger commits on a pointerup of its own pointer id, so the cancel carries it.
  function cancelPress(down: PointerDown | null): void {
    if (down === null) return;
    const at = connected(down.target) ? down.target : connected(down.surface) ? down.surface : null;
    if (at !== null) at.dispatchEvent(pointerCancelEvent(down.pointerId));
  }

  function onPointerDown(event: Event): void {
    const pointer = event as PointerEvent;
    const current = gesture;
    if (current !== null && (current.touchId !== null || current.pointerId !== pointer.pointerId)) {
      gesture = null;
      return;
    }
    lastDown = { pointerId: pointer.pointerId, target: event.target, surface: tileSurfaceOf(event.target) };
    if (current !== null || pointer.pointerType === 'touch') return;
    if (pointer.button > 0 || !canStart(pointer.clientX, event.target)) return;
    begin(pointer.clientX, pointer.clientY, null, pointer.pointerId);
  }

  function onPointerMove(event: Event): void {
    const pointer = event as PointerEvent;
    if (gesture === null || gesture.pointerId === null || gesture.pointerId !== pointer.pointerId) return;
    follow(pointer.clientX, pointer.clientY);
  }

  function onPointerEnd(event: Event): void {
    const pointer = event as PointerEvent;
    if (gesture !== null && gesture.pointerId !== null && gesture.pointerId === pointer.pointerId) gesture = null;
  }

  function onTouchStart(event: Event): void {
    const touches = touchesOf(event);
    if (gesture !== null) {
      gesture = null;
      return;
    }
    const all = (event as TouchEvent).touches as unknown as ArrayLike<TouchPoint> | undefined;
    if (touches.length !== 1 || (all !== undefined && all !== null && all.length > 1)) return;
    const touch = touches[0];
    if (!canStart(touch.clientX, touch.target)) return;
    begin(touch.clientX, touch.clientY, touch.identifier, null);
  }

  function trackedTouch(event: Event): TouchPoint | null {
    if (gesture === null || gesture.touchId === null) return null;
    const touches = touchesOf(event);
    for (let index = 0; index < touches.length; index++) {
      if (touches[index].identifier === gesture.touchId) return touches[index];
    }
    return null;
  }

  function onTouchMove(event: Event): void {
    const touch = trackedTouch(event);
    if (touch !== null) follow(touch.clientX, touch.clientY);
  }

  function onTouchEnd(event: Event): void {
    if (trackedTouch(event) !== null) gesture = null;
  }

  // Touch is followed through touch events: the root lets the browser pan, and once a pan starts it
  // cancels the pointer and stops sending pointermove, while touchmove keeps coming.
  const listeners: Array<[string, (event: Event) => void]> = [
    ['pointerdown', onPointerDown],
    ['pointermove', onPointerMove],
    ['pointerup', onPointerEnd],
    ['pointercancel', onPointerEnd],
    ['touchstart', onTouchStart],
    ['touchmove', onTouchMove],
    ['touchend', onTouchEnd],
    ['touchcancel', onTouchEnd],
  ];
  const listenerOptions = { capture: true, passive: true } as AddEventListenerOptions;
  for (let index = 0; index < listeners.length; index++) {
    document.addEventListener(listeners[index][0], listeners[index][1], listenerOptions);
  }

  return {
    destroy: () => {
      for (let index = 0; index < listeners.length; index++) {
        document.removeEventListener(listeners[index][0], listeners[index][1], listenerOptions);
      }
      gesture = null;
      lastDown = null;
    },
  };
}
