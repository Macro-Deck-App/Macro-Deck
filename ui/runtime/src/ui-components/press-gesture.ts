import { UiComponentEvents } from './component-events';
import { nodeClaimsGesture } from '../ui-framework/node-gestures';
import type { UiComponentContext } from '../ui-framework/component-registry';
import { PressFeedback } from '../render/press-feedback';

const LONG_PRESS_MS = 600;

export interface PressGestureState {
  readonly feedback: PressFeedback;
  pointerId: number | null;
  longPressTimer: ReturnType<typeof setTimeout> | null;
  longPressTriggered: boolean;
}

export function createPressGestureState<TState>(ctx: UiComponentContext<TState>): PressGestureState {
  return {
    feedback: new PressFeedback(pressed => {
      const node = ctx.current();
      // Painted through the engine's own scaffolding, then the active class is toggled directly here -
      // a press/release happens on a pointer event, not a paint.
      ctx.pressTint(node);
      if (nodeClaimsGesture(node)) {
        ctx.setClass(ctx.part('tint', 'div'), 'widget-press-tint-active', pressed);
      }
      if (ctx.host.setPressed) ctx.host.setPressed(node, pressed);
    }),
    pointerId: null,
    longPressTimer: null,
    longPressTriggered: false,
  };
}

function clearTimer(timer: ReturnType<typeof setTimeout> | null): null {
  if (timer !== null) clearTimeout(timer);
  return null;
}

export function bindPressGesture<TState>(
  element: HTMLElement | SVGElement,
  ctx: UiComponentContext<TState>,
  state: PressGestureState,
): void {
  function endPress(committed: boolean): void {
    const current = ctx.current();
    state.longPressTimer = clearTimer(state.longPressTimer);
    state.pointerId = null;

    state.feedback.release();
    ctx.emit(current, UiComponentEvents.PressEnd);
    if (committed && !state.longPressTriggered) ctx.emit(current, UiComponentEvents.Press);
  }

  element.addEventListener('pointerdown', (event: Event) => {
    const current = ctx.current();
    // A node declaring no press event offers no interaction at all: it must not claim the pointer, must
    // not stop the event and must paint no tint, so whatever wraps this tree keeps working exactly as
    // if this node were not here.
    if (!nodeClaimsGesture(current) || ctx.isDisabled()) return;

    const pointer = event as PointerEvent;
    // A second finger already mid-press, or a right-click, must never start one.
    if (state.pointerId !== null || pointer.button > 0) return;

    event.preventDefault();
    event.stopPropagation();

    state.pointerId = pointer.pointerId;
    state.longPressTriggered = false;

    // Painted before anything is emitted: feedback must not wait on a round trip.
    state.feedback.press();

    try {
      // Attempted, never required: a synthetic pointer event has no OS pointer session behind it, so
      // capture throws, and the legacy build shims per-touch implicit capture instead. Where it is
      // unavailable, the pointerleave below is what ends the press.
      (element as HTMLElement).setPointerCapture(pointer.pointerId);
    } catch {
      // See above - the press still ends, through pointerleave rather than through capture.
    }

    ctx.emit(current, UiComponentEvents.PressStart);

    state.longPressTimer = setTimeout(() => {
      state.longPressTimer = null;
      state.longPressTriggered = true;
      ctx.emit(ctx.current(), UiComponentEvents.LongPress);
    }, LONG_PRESS_MS);
  });

  const finish = (committed: boolean) => (event: Event) => {
    const pointer = event as PointerEvent;
    if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
    if (committed) {
      event.preventDefault();
      event.stopPropagation();
    }
    endPress(committed);
  };

  element.addEventListener('widget-gesture-cancel', () => {
    const pointerId = state.pointerId;
    if (pointerId === null) return;
    endPress(false);
    try {
      (element as HTMLElement).releasePointerCapture(pointerId);
    } catch {
      // A synthetic pointer holds no capture to release.
    }
  });

  element.addEventListener('pointerup', finish(true));
  element.addEventListener('pointercancel', finish(false));
  element.addEventListener('pointerleave', finish(false));
}

export function releasePressGesture(state: PressGestureState): void {
  state.longPressTimer = clearTimer(state.longPressTimer);
  state.pointerId = null;
  state.feedback.dispose();
}
