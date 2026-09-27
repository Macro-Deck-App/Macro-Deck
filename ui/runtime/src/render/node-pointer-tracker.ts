import type { UiComponentContext } from '../ui-framework/component-registry';
import { nodeClaimsGesture, nodeDeclaresPointerFamily } from '../ui-framework/node-gestures';
import { UiComponentEvents } from '../ui-components/component-events';
import {
  UI_GESTURE_SLOP,
  UI_POINTER_MOVE_INTERVAL_MS,
  UI_POINTER_MOVE_MAX_SAMPLES,
  UI_TAP_MAX_DURATION_MS,
} from '../ui-components/component-modifiers';
import { capturePointer, descendantsOnPath, measureBasisUnit } from './node-pointer-path';
import { WIDGET_GESTURE_CANCEL_EVENT, ownsItsPointer } from './node-gesture-recognizer';

const POINTER_ID_SPACE = 0x80000000;

// Ids are drawn from a random start so two decks on one shared session do not report the same finger id.
let nextPointerId = Math.floor(Math.random() * POINTER_ID_SPACE);

function allocatePointerId(): number {
  const id = nextPointerId;
  nextPointerId = (nextPointerId + 1) % POINTER_ID_SPACE;
  return id;
}

function now(): number {
  return typeof performance !== 'undefined' && typeof performance.now === 'function' ? performance.now() : Date.now();
}

function rounded(value: number): number {
  return Math.round(value * 10000) / 10000;
}

interface StreamedPointer {
  domId: number;
  id: number;
  startX: number;
  startY: number;
  x: number;
  y: number;
  origin: EventTarget | null;
  moved: boolean;
}

interface PointerSample {
  id: number;
  x: number;
  y: number;
  t: number;
}

export function bindNodePointers(element: HTMLElement | SVGElement, ctx: UiComponentContext<unknown>): () => void {
  let pointers: StreamedPointer[] = [];
  let samples: PointerSample[] = [];
  let contactStart = 0;
  let maxPointers = 0;
  let tapEligible = false;
  let left = 0;
  let top = 0;
  let unitPx = 1;
  let lastMoveAt = -Infinity;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let taken: Event | null = null;

  function find(domId: number): StreamedPointer | undefined {
    for (let index = 0; index < pointers.length; index++) {
      if (pointers[index].domId === domId) return pointers[index];
    }
    return undefined;
  }

  function sample(pointer: StreamedPointer, clientX: number, clientY: number): PointerSample {
    return {
      id: pointer.id,
      x: rounded((clientX - left) / unitPx),
      y: rounded((clientY - top) / unitPx),
      t: Math.round(now() - contactStart),
    };
  }

  function flushMoves(): void {
    if (timer !== null) clearTimeout(timer);
    timer = null;
    if (samples.length === 0) return;
    const batch = samples;
    samples = [];
    lastMoveAt = now();
    ctx.emit(ctx.current(), UiComponentEvents.PointerMove, { samples: batch });
  }

  function scheduleMoves(): void {
    if (timer !== null) return;
    const wait = UI_POINTER_MOVE_INTERVAL_MS - (now() - lastMoveAt);
    if (wait <= 0) {
      flushMoves();
      return;
    }
    timer = setTimeout(flushMoves, wait);
  }

  function record(pointer: StreamedPointer, clientX: number, clientY: number): void {
    pointer.x = clientX;
    pointer.y = clientY;
    if (!pointer.moved) {
      const distance = Math.sqrt(Math.pow(clientX - pointer.startX, 2) + Math.pow(clientY - pointer.startY, 2));
      if (distance / unitPx > UI_GESTURE_SLOP) {
        pointer.moved = true;
        tapEligible = false;
        if (pointer.origin !== null) {
          const cancel = document.createEvent('Event');
          cancel.initEvent(WIDGET_GESTURE_CANCEL_EVENT, true, false);
          pointer.origin.dispatchEvent(cancel);
        }
      }
    }
    samples.push(sample(pointer, clientX, clientY));
    if (samples.length > UI_POINTER_MOVE_MAX_SAMPLES) samples.splice(0, samples.length - UI_POINTER_MOVE_MAX_SAMPLES);
  }

  function lift(pointer: StreamedPointer, clientX: number, clientY: number, cancelled: boolean): void {
    flushMoves();
    const ended = sample(pointer, clientX, clientY);
    const payload: { [key: string]: unknown } = { id: ended.id, x: ended.x, y: ended.y, t: ended.t };
    if (cancelled) payload['cancelled'] = true;
    pointers.splice(pointers.indexOf(pointer), 1);
    ctx.emit(ctx.current(), UiComponentEvents.PointerUp, payload);
  }

  element.addEventListener('pointerdown', (event: Event) => {
    const pointer = event as PointerEvent;
    const node = ctx.current();
    if (!nodeDeclaresPointerFamily(node) || ctx.isDisabled() || pointer.button > 0) return;
    const stale = find(pointer.pointerId);
    if (stale !== undefined) {
      tapEligible = false;
      lift(stale, stale.x, stale.y, true);
    }

    const path = descendantsOnPath(element, node, event.target);
    for (let index = 0; index < path.length; index++) if (ownsItsPointer(path[index])) return;

    if (pointers.length === 0) {
      const rect = element.getBoundingClientRect();
      left = rect.left;
      top = rect.top;
      unitPx = measureBasisUnit(element, ctx);
      contactStart = now();
      maxPointers = 0;
      tapEligible = true;
    }
    if (nodeClaimsGesture(node)) tapEligible = false;
    for (let index = 0; index < path.length; index++) if (nodeClaimsGesture(path[index])) tapEligible = false;

    flushMoves();
    const streamed: StreamedPointer = {
      domId: pointer.pointerId,
      id: allocatePointerId(),
      startX: pointer.clientX,
      startY: pointer.clientY,
      x: pointer.clientX,
      y: pointer.clientY,
      origin: event.target,
      moved: false,
    };
    pointers.push(streamed);
    if (pointers.length > maxPointers) maxPointers = pointers.length;
    taken = event;
    event.preventDefault();
    capturePointer(element, pointer.pointerId);

    const down = sample(streamed, pointer.clientX, pointer.clientY);
    const rect = element.getBoundingClientRect();
    ctx.emit(node, UiComponentEvents.PointerDown, {
      id: down.id,
      x: down.x,
      y: down.y,
      t: down.t,
      width: rounded(rect.width / unitPx),
      height: rounded(rect.height / unitPx),
    });
  }, true);

  // Registered after the capture-phase handler: older WebKit runs listeners on the target in registration order.
  element.addEventListener('pointerdown', (event: Event) => {
    if (event === taken) event.stopPropagation();
  });

  element.addEventListener('pointermove', (event: Event) => {
    const pointer = event as PointerEvent;
    const streamed = find(pointer.pointerId);
    if (streamed === undefined) return;
    const coalesced = typeof pointer.getCoalescedEvents === 'function' ? pointer.getCoalescedEvents() : [];
    if (coalesced.length > 0) {
      for (let index = 0; index < coalesced.length; index++) {
        record(streamed, coalesced[index].clientX, coalesced[index].clientY);
      }
    } else {
      record(streamed, pointer.clientX, pointer.clientY);
    }
    scheduleMoves();
  }, true);

  const finish = (committed: boolean) => (event: Event) => {
    const pointer = event as PointerEvent;
    const streamed = find(pointer.pointerId);
    if (streamed === undefined) return;
    const travelled = Math.sqrt(Math.pow(pointer.clientX - streamed.startX, 2) + Math.pow(pointer.clientY - streamed.startY, 2));
    if (!committed || streamed.moved || travelled / unitPx > UI_GESTURE_SLOP) tapEligible = false;
    lift(streamed, committed ? pointer.clientX : streamed.x, committed ? pointer.clientY : streamed.y, !committed);
    if (pointers.length > 0) return;

    const tap = tapEligible && now() - contactStart <= UI_TAP_MAX_DURATION_MS;
    tapEligible = false;
    if (tap) ctx.emit(ctx.current(), UiComponentEvents.Tap, { pointers: maxPointers });
  };

  element.addEventListener('pointerup', finish(true), true);
  element.addEventListener('pointercancel', finish(false), true);

  return () => {
    tapEligible = false;
    while (pointers.length > 0) {
      const streamed = pointers[0];
      lift(streamed, streamed.x, streamed.y, true);
    }
    if (timer !== null) clearTimeout(timer);
    timer = null;
    samples = [];
    taken = null;
  };
}
