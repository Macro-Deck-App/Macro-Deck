import { UiMacroDeckComponents } from './macrodeck-component-types';
import { nodeHexColor, nodeLength, resolveLength } from '../ui-framework/length';
import { UiComponentProperties } from '../ui-components/component-properties';
import { showsSeconds } from '../ui-components/style';
import { dialHands, dialMetrics, dialTicks } from './dial';
import { nodeTimeRef, zonedClockParts } from './time';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { MINUTE_MS, SECOND_MS, SVG_NS } from '../ui-components/render-constants';

export const macrodeckClockDialComponent: UiComponentDefinition = {
  type: UiMacroDeckComponents.ClockDial,

  create(doc: Document) {
    return doc.createElementNS(SVG_NS, 'svg') as unknown as SVGElement;
  },

  paint(node, ctx) {
    const element = ctx.element as SVGElement;
    const width = ctx.box.width ?? ctx.basis;
    const height = ctx.box.height ?? ctx.basis;

    ctx.setClassName(element, 'widget-clock-dial');
    ctx.setAttribute(element, 'width', String(width));
    ctx.setAttribute(element, 'height', String(height));

    const metrics = dialMetrics(width, height);
    const face = ctx.part('dialFace', 'circle', SVG_NS);
    ctx.setClassName(face, 'widget-clock-dial-face');
    ctx.setAttribute(face, 'cx', String(metrics.cx));
    ctx.setAttribute(face, 'cy', String(metrics.cy));
    ctx.setAttribute(face, 'r', String(metrics.faceRadius));
    ctx.setStyle(face, 'fill', 'var(--color-bg-tertiary)');

    // Every mark the dial would draw in one of the reader's text colours follows `color` when the
    // producer set one; the face, the second hand and the hub keep the theme, so the moving hand still
    // reads against a tinted face.
    const tint = nodeHexColor(node, UiComponentProperties.Color);

    const ticks = dialTicks(width, height);
    for (let index = 0; index < ticks.length; index++) {
      const tick = ticks[index];
      const mark = ctx.part(`dialTick${index}`, 'line', SVG_NS);
      ctx.setClassName(mark, 'widget-clock-dial-tick');
      ctx.setAttribute(mark, 'x1', String(tick.x1));
      ctx.setAttribute(mark, 'y1', String(tick.y1));
      ctx.setAttribute(mark, 'x2', String(tick.x2));
      ctx.setAttribute(mark, 'y2', String(tick.y2));
      ctx.setAttribute(mark, 'stroke-width', String(tick.strokeWidth));
      ctx.setStyle(mark, 'stroke', tint ?? tick.color);
    }

    const at = zonedClockParts(new Date(ctx.host.now()), nodeTimeRef(node, UiComponentProperties.Value)?.zone);
    const hands = dialHands(width, height, at.h, at.m, at.s);
    const drawn: Array<{ key: string; className: string; line: typeof hands.hour; stroke: number; color: string }> = [
      {
        key: 'dialHour', className: 'widget-clock-dial-hand-hour', line: hands.hour,
        stroke: metrics.hourStroke, color: tint ?? 'var(--color-text-primary)',
      },
      {
        key: 'dialMinute', className: 'widget-clock-dial-hand-minute', line: hands.minute,
        stroke: metrics.minuteStroke, color: tint ?? 'var(--color-text-primary)',
      },
    ];
    if (showsSeconds(node)) {
      drawn.push({
        key: 'dialSecond', className: 'widget-clock-dial-hand-second', line: hands.second,
        stroke: metrics.secondStroke, color: 'var(--color-accent)',
      });
    } else {
      ctx.dropPart('dialSecond');
    }

    for (let index = 0; index < drawn.length; index++) {
      const entry = drawn[index];
      const hand = ctx.part(entry.key, 'line', SVG_NS);
      ctx.setClassName(hand, entry.className);
      ctx.setAttribute(hand, 'x1', String(entry.line.x1));
      ctx.setAttribute(hand, 'y1', String(entry.line.y1));
      ctx.setAttribute(hand, 'x2', String(entry.line.x2));
      ctx.setAttribute(hand, 'y2', String(entry.line.y2));
      ctx.setAttribute(hand, 'stroke-width', String(entry.stroke));
      ctx.setStyle(hand, 'stroke', entry.color);
    }

    const hub = ctx.part('dialHub', 'circle', SVG_NS);
    // Re-appended so it stays over the hands, which are added after it on the first paint.
    if (hub.nextSibling !== null) element.appendChild(hub);
    ctx.setClassName(hub, 'widget-clock-dial-hub');
    ctx.setAttribute(hub, 'cx', String(metrics.cx));
    ctx.setAttribute(hub, 'cy', String(metrics.cy));
    ctx.setAttribute(hub, 'r', String(metrics.hubRadius));
    ctx.setStyle(hub, 'fill', 'var(--color-accent)');
  },

  intrinsicMainPx(node, m) {
    return resolveLength(nodeLength(node, UiComponentProperties.Size), m.basis, m.crossExtent) ?? 0;
  },

  tickPeriodMs(node) {
    if (!nodeTimeRef(node, UiComponentProperties.Value)) return null;
    return showsSeconds(node) ? SECOND_MS : MINUTE_MS;
  },
};
