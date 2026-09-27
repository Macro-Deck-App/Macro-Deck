import type { UiComponentContext } from '../ui-framework/component-registry';
import { emitsEvent } from '../ui-framework/node-properties.util';
import { nodeClaimsValue, nodeDeclaresGesture, nodeDeclaresPointerFamily } from '../ui-framework/node-gestures';
import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponentEvents } from '../ui-components/component-events';
import {
  UI_GESTURE_SLOP,
  UI_GESTURE_THROTTLE_MS,
  UI_SWIPE_MAX_DURATION_MS,
  UI_SWIPE_MIN_DISTANCE,
} from '../ui-components/component-modifiers';
import { UiComponents } from '../ui-components/ui-component-types';
import { capturePointer, descendantsOnPath, measureBasisUnit } from './node-pointer-path';

export const WIDGET_GESTURE_CANCEL_EVENT = 'widget-gesture-cancel';

interface TrackedPointer {
  id: number;
  startX: number;
  startY: number;
  x: number;
  y: number;
}

type Phase = 'idle' | 'pending' | 'drag' | 'pinch';

export function ownsItsPointer(node: UiNode): boolean {
  return nodeClaimsValue(node) || nodeDeclaresGesture(node) || node.type === UiComponents.List;
}

export function bindNodeGestures(element: HTMLElement | SVGElement, ctx: UiComponentContext<unknown>): () => void {
  let pointers: TrackedPointer[] = [];
  let phase: Phase = 'idle';
  let origin: EventTarget | null = null;
  let startedAt = 0;
  let unitPx = 1;
  let pinchStart = 0;
  let scale = 1;
  let lastEmitAt = -Infinity;
  let pending: { name: string; payload: unknown } | null = null;
  let timer: ReturnType<typeof setTimeout> | null = null;

  function reset(): void {
    if (timer !== null) clearTimeout(timer);
    timer = null;
    pending = null;
    pointers = [];
    phase = 'idle';
    origin = null;
  }

  function endActive(): void {
    const ended = phase;
    const moved = pointers.length > 0 ? translation(pointers[0]) : { x: 0, y: 0 };
    reset();
    if (ended === 'drag') ctx.emit(ctx.current(), UiComponentEvents.DragEnd, moved);
    else if (ended === 'pinch') ctx.emit(ctx.current(), UiComponentEvents.PinchEnd, scale);
  }

  function belongsToDescendant(target: EventTarget | null, owns: (node: UiNode) => boolean): boolean {
    const path = descendantsOnPath(element, ctx.current(), target);
    for (let index = 0; index < path.length; index++) if (owns(path[index])) return true;
    return false;
  }

  function capture(pointerId: number): void {
    capturePointer(element, pointerId);
  }

  function flush(): void {
    if (pending === null) return;
    const emitted = pending;
    pending = null;
    lastEmitAt = Date.now();
    ctx.emit(ctx.current(), emitted.name, emitted.payload);
  }

  function throttled(name: string, payload: unknown): void {
    pending = { name, payload };
    if (timer !== null) return;
    const wait = UI_GESTURE_THROTTLE_MS - (Date.now() - lastEmitAt);
    if (wait <= 0) {
      flush();
      return;
    }
    timer = setTimeout(() => {
      timer = null;
      flush();
    }, wait);
  }

  function activate(next: Phase): void {
    phase = next;
    lastEmitAt = -Infinity;
    if (origin !== null) {
      const cancel = document.createEvent('Event');
      cancel.initEvent(WIDGET_GESTURE_CANCEL_EVENT, true, false);
      origin.dispatchEvent(cancel);
    }
    for (let index = 0; index < pointers.length; index++) capture(pointers[index].id);
  }

  function spread(): number {
    return Math.sqrt(Math.pow(pointers[0].x - pointers[1].x, 2) + Math.pow(pointers[0].y - pointers[1].y, 2));
  }

  function translation(pointer: TrackedPointer): { x: number; y: number } {
    return { x: (pointer.x - pointer.startX) / unitPx, y: (pointer.y - pointer.startY) / unitPx };
  }

  function declaresSinglePointer(node: UiNode): boolean {
    return emitsEvent(node, UiComponentEvents.Drag)
      || emitsEvent(node, UiComponentEvents.DragEnd)
      || emitsEvent(node, UiComponentEvents.Swipe);
  }

  function declaresPinch(node: UiNode): boolean {
    return emitsEvent(node, UiComponentEvents.Pinch) || emitsEvent(node, UiComponentEvents.PinchEnd);
  }

  element.addEventListener('pointerdown', (event: Event) => {
    const node = ctx.current();
    const pointer = event as PointerEvent;
    if (!nodeDeclaresGesture(node) || ctx.isDisabled() || pointer.button > 0) return;

    const tracked = {
      id: pointer.pointerId, startX: pointer.clientX, startY: pointer.clientY, x: pointer.clientX, y: pointer.clientY,
    };

    if (pointers.length === 1 && pointers[0].id !== pointer.pointerId && phase === 'pending' && declaresPinch(node)) {
      if (belongsToDescendant(event.target, nodeDeclaresPointerFamily)) return;
      pointers.push(tracked);
      pinchStart = spread();
      scale = 1;
      return;
    }

    if (phase !== 'idle') endActive();
    if (belongsToDescendant(event.target, ownsItsPointer)) return;

    pointers = [tracked];
    phase = 'pending';
    origin = event.target;
    startedAt = Date.now();
    unitPx = measureBasisUnit(element, ctx);
    capture(pointer.pointerId);
  }, true);

  element.addEventListener('pointermove', (event: Event) => {
    const pointer = event as PointerEvent;
    let tracked: TrackedPointer | undefined;
    for (let index = 0; index < pointers.length; index++) {
      if (pointers[index].id === pointer.pointerId) tracked = pointers[index];
    }
    if (tracked === undefined) return;
    tracked.x = pointer.clientX;
    tracked.y = pointer.clientY;

    if (pointers.length === 2) {
      const distance = spread();
      if (phase !== 'pinch' && Math.abs(distance - pinchStart) / unitPx > UI_GESTURE_SLOP) activate('pinch');
      if (phase === 'pinch' && pinchStart > 0) {
        scale = distance / pinchStart;
        throttled(UiComponentEvents.Pinch, scale);
      }
      return;
    }

    const moved = translation(tracked);
    if (phase === 'pending' && declaresSinglePointer(ctx.current())
      && Math.sqrt(moved.x * moved.x + moved.y * moved.y) > UI_GESTURE_SLOP) {
      activate('drag');
    }
    if (phase === 'drag') throttled(UiComponentEvents.Drag, moved);
  }, true);

  const finish = (committed: boolean) => (event: Event) => {
    const pointer = event as PointerEvent;
    let tracked: TrackedPointer | undefined;
    for (let index = 0; index < pointers.length; index++) {
      if (pointers[index].id === pointer.pointerId) tracked = pointers[index];
    }
    if (tracked === undefined) return;
    if (committed) {
      tracked.x = pointer.clientX;
      tracked.y = pointer.clientY;
    }

    const ended = phase;
    const moved = translation(tracked);
    const elapsed = Date.now() - startedAt;
    reset();

    if (ended === 'pinch') {
      ctx.emit(ctx.current(), UiComponentEvents.PinchEnd, scale);
      return;
    }
    if (ended !== 'drag') return;

    ctx.emit(ctx.current(), UiComponentEvents.DragEnd, moved);
    const horizontal = Math.abs(moved.x) >= Math.abs(moved.y);
    const distance = horizontal ? moved.x : moved.y;
    if (committed && elapsed <= UI_SWIPE_MAX_DURATION_MS && Math.abs(distance) >= UI_SWIPE_MIN_DISTANCE) {
      const direction = horizontal ? (distance > 0 ? 'right' : 'left') : (distance > 0 ? 'down' : 'up');
      ctx.emit(ctx.current(), UiComponentEvents.Swipe, direction);
    }
  };

  element.addEventListener('pointerup', finish(true), true);
  element.addEventListener('pointercancel', finish(false), true);

  return reset;
}
