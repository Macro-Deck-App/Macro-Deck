import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeNumber } from '../ui-framework/node-properties.util';
import { UiComponentProperties } from './component-properties';

export const ARC_DEFAULT_START = -135;

export const ARC_DEFAULT_END = 135;

export const DIAL_DEAD_ZONE = 0.2;

export interface ArcSweep {
  start: number;
  sweep: number;
}

export interface ArcMetrics {
  cx: number;
  cy: number;
  radius: number;
}

export function arcSweep(node: UiNode): ArcSweep {
  const start = nodeNumber(node, UiComponentProperties.StartAngle) ?? ARC_DEFAULT_START;
  const end = nodeNumber(node, UiComponentProperties.EndAngle) ?? ARC_DEFAULT_END;
  return { start, sweep: Math.max(-360, Math.min(360, end - start)) };
}

export function arcMetrics(width: number, height: number, thickness: number): ArcMetrics {
  return { cx: width / 2, cy: height / 2, radius: Math.max(0, (Math.min(width, height) - thickness) / 2) };
}

export function arcPoint(m: ArcMetrics, angle: number): { x: number; y: number } {
  const radians = (angle * Math.PI) / 180;
  return { x: m.cx + m.radius * Math.sin(radians), y: m.cy - m.radius * Math.cos(radians) };
}

function fixed(value: number): string {
  return String(Math.round(value * 1000) / 1000);
}

export function arcPath(m: ArcMetrics, start: number, sweep: number): string {
  const from = arcPoint(m, start);
  const head = `M ${fixed(from.x)} ${fixed(from.y)}`;
  if (sweep === 0) return `${head} L ${fixed(from.x)} ${fixed(from.y)}`;

  // An SVG arc whose end equals its start draws nothing, so a full turn is two half turns.
  const halves = Math.abs(sweep) >= 360 ? [sweep / 2, sweep / 2] : [sweep];
  const flag = sweep > 0 ? 1 : 0;
  let at = start;
  let d = head;
  for (const part of halves) {
    at += part;
    const to = arcPoint(m, at);
    const large = Math.abs(part) > 180 ? 1 : 0;
    d += ` A ${fixed(m.radius)} ${fixed(m.radius)} 0 ${large} ${flag} ${fixed(to.x)} ${fixed(to.y)}`;
  }
  return d;
}

export function pointerAngle(m: ArcMetrics, x: number, y: number): number | null {
  const dx = x - m.cx;
  const dy = y - m.cy;
  if (Math.sqrt(dx * dx + dy * dy) < DIAL_DEAD_ZONE * m.radius) return null;
  const degrees = (Math.atan2(dx, -dy) * 180) / Math.PI;
  return degrees < 0 ? degrees + 360 : degrees;
}

function along(sweep: ArcSweep, angle: number): number {
  const turned = (angle - sweep.start) * (sweep.sweep < 0 ? -1 : 1);
  return ((turned % 360) + 360) % 360;
}

export function dialTravelOnPress(sweep: ArcSweep, angle: number): number {
  const span = Math.abs(sweep.sweep);
  const travel = along(sweep, angle);
  if (travel <= span) return travel;
  return travel - span <= 360 - travel ? span : 0;
}

export function dialTravelOnMove(sweep: ArcSweep, previousAngle: number, angle: number, travel: number): number {
  const span = Math.abs(sweep.sweep);
  let delta = (angle - previousAngle) * (sweep.sweep < 0 ? -1 : 1);
  delta = ((delta % 360) + 540) % 360 - 180;
  return Math.max(0, Math.min(span, travel + delta));
}
