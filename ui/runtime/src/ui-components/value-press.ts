import { UiNode } from '../ui-framework/ui-node.interface';
import { emitsEvent } from '../ui-framework/node-properties.util';
import type { UiComponentContext } from '../ui-framework/component-registry';
import { PressFeedback } from '../render/press-feedback';
import { UiComponentEvents } from './component-events';

export const VALUE_SETTLE_TIMEOUT_MS = 1000;

export interface ValuePressState {
  feedback: PressFeedback | null;
  pointerId: number | null;
  settleTimer: ReturnType<typeof setTimeout> | null;
}

export function createValuePressState(): ValuePressState {
  return { feedback: null, pointerId: null, settleTimer: null };
}

export function claimsChange(node: UiNode): boolean {
  return emitsEvent(node, UiComponentEvents.Change);
}

export function paintValueTint<TState>(node: UiNode, ctx: UiComponentContext<TState>): void {
  const claims = claimsChange(node) && !ctx.isDisabled();
  ctx.setClass(ctx.element, 'widget-pressable', claims);
  if (!claims) {
    ctx.dropPart('tint');
    return;
  }
  const tint = ctx.part('tint', 'div');
  const held = tint.classList.contains('widget-press-tint-active');
  ctx.setClassName(tint, 'widget-press-tint');
  ctx.setClass(tint, 'widget-press-tint-active', held);
}

export function bindValuePress<TState>(
  ctx: UiComponentContext<TState>,
  state: ValuePressState,
  commit: (event: PointerEvent) => void,
  capture = false,
): void {
  const element = ctx.element as HTMLElement;
  state.feedback = new PressFeedback(pressed => {
    const node = ctx.current();
    if (claimsChange(node)) ctx.setClass(ctx.part('tint', 'div'), 'widget-press-tint-active', pressed);
    if (ctx.host.setPressed) ctx.host.setPressed(node, pressed);
  });

  element.addEventListener('pointerdown', (event: Event) => {
    const pointer = event as PointerEvent;
    if (!claimsChange(ctx.current()) || ctx.isDisabled() || state.pointerId !== null || pointer.button > 0) return;
    event.preventDefault();
    event.stopPropagation();
    state.pointerId = pointer.pointerId;
    state.feedback!.press();
    try {
      element.setPointerCapture(pointer.pointerId);
    } catch {
      // Synthetic and shimmed pointers have no capture; pointerleave then ends the press.
    }
  }, capture);

  const finish = (committed: boolean) => (event: Event) => {
    const pointer = event as PointerEvent;
    if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
    // Leave does not bubble but is still captured; only leaving the control itself ends the press.
    if (event.type === 'pointerleave' && event.target !== element) return;
    state.pointerId = null;
    state.feedback!.release();
    if (!committed) return;
    event.preventDefault();
    event.stopPropagation();
    commit(pointer);
  };

  element.addEventListener('pointerup', finish(true), capture);
  element.addEventListener('pointercancel', finish(false), capture);
  element.addEventListener('pointerleave', finish(false), capture);
}

export function holdUntilSettled<TState>(
  ctx: UiComponentContext<TState>,
  state: ValuePressState,
  onSettle: () => void,
): void {
  if (state.settleTimer !== null) clearTimeout(state.settleTimer);
  state.settleTimer = setTimeout(() => {
    state.settleTimer = null;
    onSettle();
    ctx.repaint();
  }, VALUE_SETTLE_TIMEOUT_MS);
}

export function releaseValuePress(state: ValuePressState): void {
  if (state.settleTimer !== null) clearTimeout(state.settleTimer);
  state.settleTimer = null;
  state.pointerId = null;
  state.feedback?.dispose();
}
