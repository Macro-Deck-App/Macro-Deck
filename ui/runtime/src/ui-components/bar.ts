import { nodeNumber } from '../ui-framework/node-properties.util';
import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeHexColor } from '../ui-framework/length';
import { UiComponentDirections } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { clampUnit } from './style';
import { nodeString } from '../ui-framework/node-properties.util';

export function barStart(node: UiNode): number {
  return clampUnit(nodeNumber(node, UiComponentProperties.Start) ?? 0);
}

export function barEnd(node: UiNode): number {
  return clampUnit(nodeNumber(node, UiComponentProperties.End) ?? 1);
}

export function barFillStyle(node: UiNode): string | null {
  const start = nodeHexColor(node, UiComponentProperties.StartColor);
  const end = nodeHexColor(node, UiComponentProperties.EndColor);
  return start && end ? `linear-gradient(to right, ${start}, ${end})` : null;
}

export function progressFillStyle(node: UiNode): string {
  const start = nodeHexColor(node, UiComponentProperties.StartColor) ?? 'var(--color-accent)';
  const end = nodeHexColor(node, UiComponentProperties.EndColor) ?? 'var(--color-accent)';
  return `linear-gradient(to right, ${start}, ${end})`;
}

export function barHasMarker(node: UiNode): boolean {
  return nodeNumber(node, UiComponentProperties.Marker) !== undefined
    && nodeHexColor(node, UiComponentProperties.EndColor) !== undefined;
}

export function barMarkerStrokePx(thicknessPx: number): number {
  return 0.28 * thicknessPx;
}

export function barMarkerDiameterPx(thicknessPx: number): number {
  return Math.max(0, 1.5 * thicknessPx - barMarkerStrokePx(thicknessPx));
}

export function barMarkerLeftPx(node: UiNode, widthPx: number, thicknessPx: number): number {
  const radius = 0.75 * thicknessPx;
  const inset = radius + barMarkerStrokePx(thicknessPx) / 2;
  if (widthPx < 2 * inset) return widthPx / 2;
  const marker = clampUnit(nodeNumber(node, UiComponentProperties.Marker) ?? 0);
  return Math.min(Math.max(marker * widthPx, inset), widthPx - inset);
}

export function sliderDirection(node: UiNode): string {
  return nodeString(node, UiComponentProperties.Direction) === UiComponentDirections.Vertical
    ? UiComponentDirections.Vertical
    : UiComponentDirections.Horizontal;
}

export function sliderIsVertical(node: UiNode): boolean {
  return sliderDirection(node) === UiComponentDirections.Vertical;
}

export function sliderLevel(node: UiNode): number {
  return clampUnit(nodeNumber(node, UiComponentProperties.Level) ?? 0);
}

export function sliderLevelColor(node: UiNode): string {
  return nodeHexColor(node, UiComponentProperties.LevelColor) ?? 'var(--color-accent)';
}

export function sliderThumbStrokePx(thicknessPx: number): number {
  return 0.28 * thicknessPx;
}

export function sliderThumbDiameterPx(thicknessPx: number): number {
  return Math.max(0, 2.5 * thicknessPx - sliderThumbStrokePx(thicknessPx));
}

export function sliderThumbOffsetPx(level: number, extentPx: number, thicknessPx: number): number {
  const stroke = sliderThumbStrokePx(thicknessPx);
  const inset = 1.25 * thicknessPx + stroke / 2;
  const centre = extentPx < 2 * inset
    ? extentPx / 2
    : Math.min(Math.max(level * extentPx, inset), extentPx - inset);

  return centre - sliderThumbDiameterPx(thicknessPx) / 2 - stroke;
}

export function sliderLevelFromPointer(
  positionPx: number,
  extentPx: number,
  vertical: boolean,
  step?: number,
): number {
  if (extentPx <= 0) return 0;
  const raw = clampUnit(vertical ? 1 - positionPx / extentPx : positionPx / extentPx);
  if (step === undefined || !(step > 0)) return raw;
  return clampUnit(Math.round(raw / step) * step);
}
