import { UiComponents } from './ui-component-types';
import { emitsEvent, nodeString, nodeText } from '../ui-framework/node-properties.util';
import { nodeClaimsValue } from '../ui-framework/node-gestures';
import { nodeLength, resolveLength } from '../ui-framework/length';
import {
  WIDGET_FIELD_BORDER_PX,
  WIDGET_FIELD_LINE_HEIGHT,
  WIDGET_FIELD_PADDING_EM,
} from '../ui-framework/layout';
import { UiComponentEvents } from './component-events';
import { UiComponentProperties } from './component-properties';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { px } from './px.util';

const FIELD_ADJUST_THROTTLE_MS = 100;

export interface UiTextFieldState {
  focused: boolean;
  adjustTimer: ReturnType<typeof setTimeout> | null;
  pendingValue: string | null;
  lastSentAt: number | null;
  lastSent: string | null;
}

function clearTimer(timer: ReturnType<typeof setTimeout> | null): null {
  if (timer !== null) clearTimeout(timer);
  return null;
}

function sendFieldAdjust(ctx: UiComponentContext<UiTextFieldState>, value: string, now: number): void {
  ctx.state.lastSentAt = now;
  ctx.state.lastSent = value;
  ctx.emit(ctx.current(), UiComponentEvents.Adjust, value);
}

function queueFieldAdjust(ctx: UiComponentContext<UiTextFieldState>, value: string): void {
  const node = ctx.current();
  const state = ctx.state;
  if (value === state.lastSent && state.pendingValue === null) return;
  if (!emitsEvent(node, UiComponentEvents.Adjust)) return;

  const now = Date.now();
  if (state.lastSentAt === null || now - state.lastSentAt >= FIELD_ADJUST_THROTTLE_MS) {
    sendFieldAdjust(ctx, value, now);
    return;
  }

  state.pendingValue = value;
  if (state.adjustTimer !== null) return;

  state.adjustTimer = setTimeout(() => {
    state.adjustTimer = null;
    const pending = state.pendingValue;
    state.pendingValue = null;
    if (pending === null || pending === state.lastSent) return;
    sendFieldAdjust(ctx, pending, Date.now());
  }, FIELD_ADJUST_THROTTLE_MS - (now - state.lastSentAt));
}

function settleField(element: HTMLInputElement, ctx: UiComponentContext<UiTextFieldState>): void {
  const state = ctx.state;
  state.adjustTimer = clearTimer(state.adjustTimer);
  state.pendingValue = null;
  state.lastSentAt = null;
  state.lastSent = null;
  ctx.emit(ctx.current(), UiComponentEvents.Change, element.value);
}

export const uiTextFieldComponent: UiComponentDefinition<UiTextFieldState> = {
  type: UiComponents.TextField,

  create(doc: Document) {
    const field = doc.createElement('input');
    field.setAttribute('type', 'text');
    // A deck is not a form, and the browser's own suggestions would cover the tree below it.
    field.setAttribute('autocomplete', 'off');
    field.setAttribute('autocapitalize', 'off');
    field.setAttribute('spellcheck', 'false');
    return field;
  },

  createState(): UiTextFieldState {
    return { focused: false, adjustTimer: null, pendingValue: null, lastSentAt: null, lastSent: null };
  },

  bind(ctx) {
    const element = ctx.element as HTMLInputElement;
    const state = ctx.state;
    element.addEventListener('focus', () => { state.focused = true; });
    element.addEventListener('blur', () => {
      state.focused = false;
      settleField(element, ctx);
    });
    element.addEventListener('input', () => queueFieldAdjust(ctx, element.value));
    element.addEventListener('keydown', (event: Event) => {
      if ((event as KeyboardEvent).key !== 'Enter') return;
      event.preventDefault();
      settleField(element, ctx);
    });
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLInputElement;
    ctx.setClassName(element, 'widget-text-field');

    const editable = nodeClaimsValue(node) && !ctx.isDisabled();
    element.readOnly = !editable;
    ctx.setAttribute(element, 'placeholder',
      nodeText(node, UiComponentProperties.Placeholder, ctx.host.localization) ?? '');

    ctx.setStyle(element, 'width', px(ctx.box.width));
    ctx.setStyle(element, 'font-size', px(
      resolveLength(nodeLength(node, UiComponentProperties.Size), ctx.basis, ctx.crossExtent)));

    // Never while the field has focus - the producer is authoritative for the value, but a filtering
    // producer echoes the query back on every keystroke, and applying that as a patch would move the
    // caret to somewhere the user did not put it.
    const value = nodeString(node, UiComponentProperties.Text) ?? '';
    if (!ctx.state.focused && element.value !== value) element.value = value;
  },

  release(ctx) {
    ctx.state.adjustTimer = clearTimer(ctx.state.adjustTimer);
  },

  intrinsicMainPx(node, m) {
    if (m.horizontal) return 0;
    const size = resolveLength(nodeLength(node, UiComponentProperties.Size), m.basis, m.crossExtent);
    return (size ?? 0) * (WIDGET_FIELD_LINE_HEIGHT + 2 * WIDGET_FIELD_PADDING_EM) + 2 * WIDGET_FIELD_BORDER_PX;
  },
};
