import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents } from './ui-component-types';
import { emitsEvent, nodeNumber } from '../ui-framework/node-properties.util';
import { nodeClaimsValue } from '../ui-framework/node-gestures';
import { UiComponentProperties } from './component-properties';
import { nodeThicknessPx } from './style';
import {
  sliderIsVertical,
  sliderLevel,
  sliderLevelColor,
  sliderLevelFromPointer,
  sliderThumbDiameterPx,
  sliderThumbOffsetPx,
  sliderThumbStrokePx,
} from './bar';
import { UiComponentEvents } from './component-events';
import { nodeLength, resolveLength } from '../ui-framework/length';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { px } from './px.util';

const SLIDER_ADJUST_THROTTLE_MS = 100;

const SLIDER_SETTLE_TIMEOUT_MS = 1000;

export interface UiSliderState {
  pointerId: number | null;
  interactionLevel: number | null;
  settledLevel: number | null;
  producerLevelAtRelease: number;
  settleTimer: ReturnType<typeof setTimeout> | null;
  adjustTimer: ReturnType<typeof setTimeout> | null;
  pendingAdjustLevel: number | null;
  lastAdjustSentAt: number | null;
  lastSentLevel: number | null;
}

function clearTimer(timer: ReturnType<typeof setTimeout> | null): null {
  if (timer !== null) clearTimeout(timer);
  return null;
}

function sliderDisplayLevel(node: UiNode, state: UiSliderState): number {
  if (state.interactionLevel !== null) return state.interactionLevel;

  const producer = sliderLevel(node);
  // The first producer level that differs from the one in place at release is its answer, whatever it
  // says - the producer is authoritative and may well have clamped or refused the value.
  return state.settledLevel !== null && producer === state.producerLevelAtRelease ? state.settledLevel : producer;
}

function paintSliderLevel(node: UiNode, ctx: UiComponentContext<UiSliderState>): void {
  const track = ctx.part('sliderTrack', 'div');
  const fill = ctx.part('sliderFill', 'div', undefined, track);
  const thumb = ctx.part('sliderThumb', 'div');
  const state = ctx.state;

  const vertical = sliderIsVertical(node);
  const thickness = nodeThicknessPx(node, ctx.basis, ctx.crossExtent);
  const extent = (vertical ? ctx.box.height : ctx.box.width) ?? 0;
  const level = sliderDisplayLevel(node, state);
  const dragging = state.interactionLevel !== null;

  // The parts outlive an orientation change - the renderer rebuilds a node's DOM only when its type
  // changes - so the axis just left behind is cleared, or the stale inline value keeps beating the
  // stylesheet that would otherwise span the fill across the track and centre the thumb on it.
  ctx.setStyle(fill, vertical ? 'height' : 'width', `${level * 100}%`);
  ctx.setStyle(fill, vertical ? 'width' : 'height', null);
  ctx.setStyle(thumb, vertical ? 'bottom' : 'left', px(sliderThumbOffsetPx(level, extent, thickness)));
  ctx.setStyle(thumb, vertical ? 'left' : 'bottom', null);
  ctx.setClass(fill, 'widget-slider-fill-dragging', dragging);
  ctx.setClass(thumb, 'widget-slider-thumb-dragging', dragging);
}

function sendSliderAdjust(ctx: UiComponentContext<UiSliderState>, node: UiNode, level: number, now: number): void {
  ctx.state.lastAdjustSentAt = now;
  ctx.state.lastSentLevel = level;
  ctx.emit(node, UiComponentEvents.Adjust, level);
}

function queueSliderAdjust(ctx: UiComponentContext<UiSliderState>, node: UiNode, level: number): void {
  const state = ctx.state;
  if (level === state.lastSentLevel && state.pendingAdjustLevel === null) return;
  if (!emitsEvent(node, UiComponentEvents.Adjust)) return;

  const now = Date.now();
  if (state.lastAdjustSentAt === null || now - state.lastAdjustSentAt >= SLIDER_ADJUST_THROTTLE_MS) {
    sendSliderAdjust(ctx, node, level, now);
    return;
  }

  state.pendingAdjustLevel = level;
  if (state.adjustTimer !== null) return;

  state.adjustTimer = setTimeout(() => {
    state.adjustTimer = null;
    const pending = state.pendingAdjustLevel;
    state.pendingAdjustLevel = null;
    if (pending === null || pending === state.lastSentLevel) return;
    sendSliderAdjust(ctx, ctx.current(), pending, Date.now());
  }, SLIDER_ADJUST_THROTTLE_MS - (now - state.lastAdjustSentAt));
}

function applySliderLevel(element: HTMLElement, event: PointerEvent, ctx: UiComponentContext<UiSliderState>): void {
  const node = ctx.current();
  const vertical = sliderIsVertical(node);
  // Measured, never taken from the resolved box: the tile scales its content, so the box is in
  // reference pixels while the pointer is in real ones. Dividing one by the other reads every position
  // short by exactly the deck scale.
  const rect = element.getBoundingClientRect();
  const extent = vertical ? rect.height : rect.width;
  if (extent <= 0) return;

  const step = nodeNumber(node, UiComponentProperties.Step);
  const position = vertical ? event.clientY - rect.top : event.clientX - rect.left;
  const level = sliderLevelFromPointer(position, extent, vertical, step);

  ctx.state.interactionLevel = level;
  paintSliderLevel(node, ctx);
  queueSliderAdjust(ctx, node, level);
}

function endSliderInteraction(commit: boolean, ctx: UiComponentContext<UiSliderState>): void {
  const node = ctx.current();
  const state = ctx.state;
  state.adjustTimer = clearTimer(state.adjustTimer);
  state.pendingAdjustLevel = null;

  const level = state.interactionLevel;
  state.pointerId = null;
  state.lastAdjustSentAt = null;
  state.lastSentLevel = null;

  // Read before the interaction level is released, or the display falls through to the producer's
  // pre-drag level for one frame.
  state.producerLevelAtRelease = sliderLevel(node);
  state.settledLevel = commit ? level : null;
  state.interactionLevel = null;
  state.settleTimer = clearTimer(state.settleTimer);

  if (commit && level !== null) {
    // The level the interaction landed on is held until the producer answers for it, because that
    // answer may be silence: it refuses the value while the deck is locked or while the integration
    // behind it is unreachable, and an event is fire-and-forget. Without the timeout the pill would sit
    // at a level nothing ever accepted until the next drag.
    state.settleTimer = setTimeout(() => {
      state.settleTimer = null;
      state.settledLevel = null;
      paintSliderLevel(ctx.current(), ctx);
    }, SLIDER_SETTLE_TIMEOUT_MS);
    ctx.emit(node, UiComponentEvents.Change, level);
  }

  paintSliderLevel(node, ctx);
}

export const uiSliderComponent: UiComponentDefinition<UiSliderState> = {
  type: UiComponents.Slider,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiSliderState {
    return {
      pointerId: null,
      interactionLevel: null,
      settledLevel: null,
      producerLevelAtRelease: 0,
      settleTimer: null,
      adjustTimer: null,
      pendingAdjustLevel: null,
      lastAdjustSentAt: null,
      lastSentLevel: null,
    };
  },

  bind(ctx) {
    const element = ctx.element as HTMLElement;
    const state = ctx.state;

    element.addEventListener('pointerdown', (event: Event) => {
      const node = ctx.current();
      // A node with no declared events offers no interaction, so it leaves the pointer to the press
      // handling on whatever encloses this tree.
      if (!nodeClaimsValue(node)) return;

      const pointer = event as PointerEvent;
      if (state.pointerId !== null || pointer.button > 0) return;

      event.preventDefault();
      event.stopPropagation();

      state.pointerId = pointer.pointerId;
      state.settleTimer = clearTimer(state.settleTimer);
      state.settledLevel = null;

      try {
        // Capture is what keeps the moves arriving once the pointer leaves the box mid-drag. Where it
        // is unavailable the pointerId filter carries the drag and the settle timeout ends it.
        element.setPointerCapture(pointer.pointerId);
      } catch {
        // See above.
      }

      applySliderLevel(element, pointer, ctx);
    });

    element.addEventListener('pointermove', (event: Event) => {
      const pointer = event as PointerEvent;
      if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
      event.preventDefault();
      applySliderLevel(element, pointer, ctx);
    });

    element.addEventListener('pointerup', (event: Event) => {
      const pointer = event as PointerEvent;
      if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
      event.preventDefault();
      event.stopPropagation();
      endSliderInteraction(true, ctx);
    });

    element.addEventListener('pointercancel', (event: Event) => {
      const pointer = event as PointerEvent;
      if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
      // A gesture the OS took over is not a value the user chose to land on, so it is dropped rather
      // than committed - the pill reverts to the producer's level.
      endSliderInteraction(false, ctx);
    });
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    const vertical = sliderIsVertical(node);
    ctx.setClassName(element, 'widget-slider');
    ctx.setClass(element, 'widget-slider-vertical', vertical);
    ctx.setClass(element, 'widget-pressable', nodeClaimsValue(node));
    ctx.sizeTo(element, ctx.box);

    const thickness = nodeThicknessPx(node, ctx.basis, ctx.crossExtent);

    const track = ctx.part('sliderTrack', 'div');
    ctx.setClassName(track, 'widget-slider-track');
    ctx.setStyle(track, vertical ? 'width' : 'height', px(thickness));
    ctx.setStyle(track, vertical ? 'height' : 'width', null);
    ctx.setStyle(track, 'border-radius', px(thickness / 2));

    const fill = ctx.part('sliderFill', 'div', undefined, track);
    ctx.setClassName(fill, 'widget-slider-fill');
    ctx.setStyle(fill, 'border-radius', px(thickness / 2));
    ctx.setStyle(fill, 'background', sliderLevelColor(node));

    const thumb = ctx.part('sliderThumb', 'div');
    ctx.setClassName(thumb, 'widget-slider-thumb');
    const diameter = sliderThumbDiameterPx(thickness);
    ctx.setStyle(thumb, 'width', px(diameter));
    ctx.setStyle(thumb, 'height', px(diameter));
    ctx.setStyle(thumb, 'border', `${sliderThumbStrokePx(thickness)}px solid var(--color-bg-secondary)`);

    paintSliderLevel(node, ctx);
  },

  release(ctx) {
    ctx.state.settleTimer = clearTimer(ctx.state.settleTimer);
    ctx.state.adjustTimer = clearTimer(ctx.state.adjustTimer);
  },

  intrinsicMainPx(node, m) {
    return m.horizontal
      ? 0
      : resolveLength(nodeLength(node, UiComponentProperties.Thickness), m.basis, m.crossExtent) ?? 0;
  },
};
