import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { UiComponentEvents } from './component-events';
import { emitsEvent, nodeNumber } from '../ui-framework/node-properties.util';
import { nodeClaimsValue } from '../ui-framework/node-gestures';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { clampUnit, nodeThicknessPx } from './style';
import { sliderLevel, sliderThumbStrokePx } from './bar';
import { arcMetrics, arcPoint, arcSweep, dialTravelOnMove, dialTravelOnPress, pointerAngle } from './arc';
import { paintGaugeArc } from './gauge-paint';
import { SVG_NS } from './render-constants';

const DIAL_ADJUST_THROTTLE_MS = 100;

const DIAL_SETTLE_TIMEOUT_MS = 1000;

export interface UiDialState {
  pointerId: number | null;
  interactionLevel: number | null;
  settledLevel: number | null;
  producerLevelAtRelease: number;
  settleTimer: ReturnType<typeof setTimeout> | null;
  adjustTimer: ReturnType<typeof setTimeout> | null;
  pendingAdjustLevel: number | null;
  lastAdjustSentAt: number | null;
  lastSentLevel: number | null;
  travel: number;
  lastAngle: number | null;
  captured: boolean;
}

function clearTimer(timer: ReturnType<typeof setTimeout> | null): null {
  if (timer !== null) clearTimeout(timer);
  return null;
}

export function dialDisplayLevel(node: UiNode, state: UiDialState): number {
  if (state.interactionLevel !== null) return state.interactionLevel;
  const producer = sliderLevel(node);
  return state.settledLevel !== null && producer === state.producerLevelAtRelease ? state.settledLevel : producer;
}

export function snapDialLevel(travel: number, span: number, step: number | undefined): number {
  const raw = span > 0 ? clampUnit(travel / span) : 0;
  if (step === undefined || !(step > 0)) return raw;
  return clampUnit(Math.round(raw / step) * step);
}

function paintDial(node: UiNode, ctx: UiComponentContext<UiDialState>): void {
  const level = dialDisplayLevel(node, ctx.state);
  const metrics = paintGaugeArc(node, ctx, level);
  const thickness = nodeThicknessPx(node, ctx.basis, ctx.crossExtent);
  const sweep = arcSweep(node);
  const at = arcPoint(metrics, sweep.start + sweep.sweep * level);

  const thumb = ctx.part('dialThumb', 'circle', SVG_NS);
  // Re-appended so it stays over the fill, which is dropped and re-added whenever the level passes 0.
  if (thumb.nextSibling !== null) ctx.element.appendChild(thumb);
  ctx.setClassName(thumb, 'widget-dial-thumb');
  ctx.setAttribute(thumb, 'cx', String(at.x));
  ctx.setAttribute(thumb, 'cy', String(at.y));
  ctx.setAttribute(thumb, 'r', String(1.25 * thickness));
  ctx.setAttribute(thumb, 'stroke-width', String(sliderThumbStrokePx(thickness)));
}

function sendAdjust(ctx: UiComponentContext<UiDialState>, node: UiNode, level: number, now: number): void {
  ctx.state.lastAdjustSentAt = now;
  ctx.state.lastSentLevel = level;
  ctx.emit(node, UiComponentEvents.Adjust, level);
}

function queueAdjust(ctx: UiComponentContext<UiDialState>, node: UiNode, level: number): void {
  const state = ctx.state;
  if (level === state.lastSentLevel && state.pendingAdjustLevel === null) return;
  if (!emitsEvent(node, UiComponentEvents.Adjust)) return;

  const now = Date.now();
  if (state.lastAdjustSentAt === null || now - state.lastAdjustSentAt >= DIAL_ADJUST_THROTTLE_MS) {
    sendAdjust(ctx, node, level, now);
    return;
  }

  state.pendingAdjustLevel = level;
  if (state.adjustTimer !== null) return;
  state.adjustTimer = setTimeout(() => {
    state.adjustTimer = null;
    const pending = state.pendingAdjustLevel;
    state.pendingAdjustLevel = null;
    if (pending === null || pending === state.lastSentLevel) return;
    sendAdjust(ctx, ctx.current(), pending, Date.now());
  }, DIAL_ADJUST_THROTTLE_MS - (now - state.lastAdjustSentAt));
}

function trackPointer(element: Element, event: PointerEvent, ctx: UiComponentContext<UiDialState>, pressing: boolean): void {
  const node = ctx.current();
  const sweep = arcSweep(node);
  const span = Math.abs(sweep.sweep);
  // Measured rather than taken from the box: the tile scales its content, so the box is in reference pixels.
  const rect = element.getBoundingClientRect();
  const scale = ctx.box.width ? rect.width / ctx.box.width : 1;
  const metrics = arcMetrics(rect.width, rect.height, nodeThicknessPx(node, ctx.basis, ctx.crossExtent) * scale);
  const angle = pointerAngle(metrics, event.clientX - rect.left, event.clientY - rect.top);
  const state = ctx.state;

  if (pressing) {
    state.travel = angle === null ? sliderLevel(node) * span : dialTravelOnPress(sweep, angle);
  } else if (angle !== null) {
    state.travel = state.lastAngle === null
      ? dialTravelOnPress(sweep, angle)
      : dialTravelOnMove(sweep, state.lastAngle, angle, state.travel);
  }
  if (angle !== null) state.lastAngle = angle;

  const level = snapDialLevel(state.travel, span, nodeNumber(node, UiComponentProperties.Step));
  state.interactionLevel = level;
  paintDial(node, ctx);
  queueAdjust(ctx, node, level);
}

function endInteraction(commit: boolean, ctx: UiComponentContext<UiDialState>): void {
  const node = ctx.current();
  const state = ctx.state;
  state.adjustTimer = clearTimer(state.adjustTimer);
  state.pendingAdjustLevel = null;

  const level = state.interactionLevel;
  state.pointerId = null;
  state.lastAdjustSentAt = null;
  state.lastSentLevel = null;
  state.lastAngle = null;

  state.producerLevelAtRelease = sliderLevel(node);
  state.settledLevel = commit ? level : null;
  state.interactionLevel = null;
  state.settleTimer = clearTimer(state.settleTimer);

  if (commit && level !== null) {
    state.settleTimer = setTimeout(() => {
      state.settleTimer = null;
      state.settledLevel = null;
      paintDial(ctx.current(), ctx);
    }, DIAL_SETTLE_TIMEOUT_MS);
    ctx.emit(node, UiComponentEvents.Change, level);
  }

  paintDial(node, ctx);
}

export const uiDialComponent: UiComponentDefinition<UiDialState> = {
  type: UiComponents.Dial,

  create(doc: Document) {
    return doc.createElementNS(SVG_NS, 'svg') as unknown as SVGElement;
  },

  createState(): UiDialState {
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
      travel: 0,
      lastAngle: null,
      captured: false,
    };
  },

  bind(ctx) {
    const element = ctx.element;
    const state = ctx.state;

    element.addEventListener('pointerdown', (event: Event) => {
      const node = ctx.current();
      if (!nodeClaimsValue(node) || ctx.isDisabled() || arcSweep(node).sweep === 0) return;
      const pointer = event as PointerEvent;
      if (state.pointerId !== null || pointer.button > 0) return;

      event.preventDefault();
      event.stopPropagation();
      state.pointerId = pointer.pointerId;
      state.settleTimer = clearTimer(state.settleTimer);
      state.settledLevel = null;
      try {
        (element as Element).setPointerCapture(pointer.pointerId);
        state.captured = true;
      } catch {
        // Without capture a release outside the element never arrives here, so leaving it ends the drag.
        state.captured = false;
      }
      trackPointer(element, pointer, ctx, true);
    });

    element.addEventListener('pointermove', (event: Event) => {
      const pointer = event as PointerEvent;
      if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
      event.preventDefault();
      trackPointer(element, pointer, ctx, false);
    });

    element.addEventListener('pointerup', (event: Event) => {
      const pointer = event as PointerEvent;
      if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
      event.preventDefault();
      event.stopPropagation();
      endInteraction(true, ctx);
    });

    element.addEventListener('pointerleave', (event: Event) => {
      const pointer = event as PointerEvent;
      if (state.pointerId === null || pointer.pointerId !== state.pointerId || state.captured) return;
      endInteraction(true, ctx);
    });

    element.addEventListener('pointercancel', (event: Event) => {
      const pointer = event as PointerEvent;
      if (state.pointerId === null || pointer.pointerId !== state.pointerId) return;
      endInteraction(false, ctx);
    });
  },

  paint(node, ctx) {
    ctx.setClassName(ctx.element, 'widget-dial');
    ctx.setClass(ctx.element, 'widget-pressable', nodeClaimsValue(node) && !ctx.isDisabled() && arcSweep(node).sweep !== 0);
    paintDial(node, ctx);
  },

  release(ctx) {
    ctx.state.settleTimer = clearTimer(ctx.state.settleTimer);
    ctx.state.adjustTimer = clearTimer(ctx.state.adjustTimer);
  },
};
