import { WidgetBorder, WidgetData, WidgetType } from '../domain/widget.interface';
import { nodeBoolean, nodeNumber, nodeRaw, nodeString } from '../ui-framework/node-properties.util';
import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeHexColor, nodeLength, resolveLength } from '../ui-framework/length';
import {
  UI_COMPONENT_BORDER_STYLES_WELL_KNOWN,
  UiComponentAlignments,
  UiComponentButtonCorners,
  UiComponentDirections,
  UiComponentImageFits,
  UiComponentImageTransitions,
  UiComponentJustify,
  UiComponentTextRoles,
  UiComponentTextWeights,
} from './ui-component-types';
import { UiComponentProperties } from './component-properties';

export function clampUnit(value: number): number {
  return Math.min(1, Math.max(0, value));
}

export function nodeDirection(node: UiNode): string {
  return nodeString(node, UiComponentProperties.Direction) === UiComponentDirections.Horizontal
    ? UiComponentDirections.Horizontal
    : UiComponentDirections.Vertical;
}

export function nodeIsHorizontal(node: UiNode): boolean {
  return nodeDirection(node) === UiComponentDirections.Horizontal;
}

export function nodeJustify(node: UiNode): string {
  switch (nodeString(node, UiComponentProperties.Justify)) {
    case UiComponentJustify.Center: return 'center';
    case UiComponentJustify.End: return 'flex-end';
    case UiComponentJustify.SpaceBetween: return 'space-between';
    default: return 'flex-start';
  }
}

export function nodeAlign(node: UiNode): string {
  switch (nodeString(node, UiComponentProperties.Align)) {
    case UiComponentAlignments.Start: return 'flex-start';
    case UiComponentAlignments.Center: return 'center';
    case UiComponentAlignments.End: return 'flex-end';
    case UiComponentAlignments.Baseline: return 'baseline';
    default: return 'stretch';
  }
}

function lengthPx(node: UiNode, key: string, basis: number, crossExtent: number | null): number {
  return resolveLength(nodeLength(node, key), basis, crossExtent) ?? 0;
}

export function nodePaddingPx(node: UiNode, basis: number, crossExtent: number | null): number {
  return lengthPx(node, UiComponentProperties.Padding, basis, crossExtent);
}

export function nodeGapPx(node: UiNode, basis: number, crossExtent: number | null): number {
  return lengthPx(node, UiComponentProperties.Gap, basis, crossExtent);
}

export function nodeThicknessPx(node: UiNode, basis: number, crossExtent: number | null): number {
  return lengthPx(node, UiComponentProperties.Thickness, basis, crossExtent);
}

export function stackBackground(node: UiNode): string | undefined {
  return nodeHexColor(node, UiComponentProperties.Background);
}

export function buttonBackground(node: UiNode): string {
  return nodeHexColor(node, UiComponentProperties.Background) ?? 'var(--color-accent)';
}

export function buttonTakesTileCorner(node: UiNode): boolean {
  return nodeString(node, UiComponentProperties.Corner) === UiComponentButtonCorners.Tile;
}

export function buttonFit(node: UiNode): string {
  return nodeString(node, UiComponentProperties.Fit) === UiComponentImageFits.Cover ? 'cover' : 'contain';
}

export function buttonZoom(node: UiNode): number {
  const zoom = nodeNumber(node, UiComponentProperties.Zoom);
  return zoom !== undefined && zoom > 0 ? zoom : 1;
}

export function buttonOpacity(node: UiNode): number {
  const opacity = nodeNumber(node, UiComponentProperties.Opacity);
  return opacity !== undefined ? clampUnit(opacity) : 1;
}

export function buttonArtworkTransform(node: UiNode): string {
  const offsetX = (nodeNumber(node, UiComponentProperties.OffsetX) ?? 0) * 100;
  const offsetY = (nodeNumber(node, UiComponentProperties.OffsetY) ?? 0) * 100;
  return `translate(${offsetX}%, ${offsetY}%) scale(${buttonZoom(node)})`;
}

export function buttonBorder(node: UiNode): WidgetBorder | undefined {
  const style = nodeString(node, UiComponentProperties.BorderStyle);
  if (!style || !UI_COMPONENT_BORDER_STYLES_WELL_KNOWN.includes(style)) return undefined;
  return { style: style as WidgetBorder['style'], color: nodeHexColor(node, UiComponentProperties.BorderColor) };
}

/**
 * The border a deck tile draws around a widget, in screen space.
 *
 * An Action Button's is resolved host-side per active state and travels on its `ui.button` root node,
 * so the tree owns the value entirely: no tree yet means no ring, and a state whose border is `off`
 * suppresses one the stored data still carries. Every other widget type keeps its border in its
 * stored data, which is what the tile reads. Pairs with {@link UiRenderHost.ownsRootWidgetBorder} -
 * whoever answers `true` there is the one that must call this.
 */
export function widgetTileBorder(
  widgetType: string,
  data: WidgetData,
  root: UiNode | null,
): WidgetBorder | undefined {
  if (widgetType === WidgetType.ActionButton) {
    return root === null ? undefined : buttonBorder(root);
  }

  return (data as { border?: WidgetBorder }).border;
}

export function textWraps(node: UiNode): boolean {
  return nodeBoolean(node, UiComponentProperties.Wrap) === true;
}

export function textFontWeight(node: UiNode): number {
  switch (nodeString(node, UiComponentProperties.Weight)) {
    case UiComponentTextWeights.Medium: return 500;
    case UiComponentTextWeights.SemiBold: return 600;
    case UiComponentTextWeights.Bold: return 700;
    default: return 400;
  }
}

export function textRoleColor(node: UiNode): string {
  switch (nodeString(node, UiComponentProperties.Role)) {
    case UiComponentTextRoles.Secondary: return 'var(--color-text-secondary)';
    case UiComponentTextRoles.Muted: return 'var(--color-text-muted)';
    default: return 'var(--color-text-primary)';
  }
}

export function textFillColor(node: UiNode): string {
  return nodeHexColor(node, UiComponentProperties.Color) ?? textRoleColor(node);
}

export function textAlign(node: UiNode): string {
  switch (nodeString(node, UiComponentProperties.Align)) {
    case UiComponentAlignments.Center: return 'center';
    case UiComponentAlignments.End: return 'end';
    default: return 'start';
  }
}

export function textMaxLines(node: UiNode): number {
  const lines = nodeNumber(node, UiComponentProperties.MaxLines);
  if (lines !== undefined && lines >= 1) return Math.floor(lines);
  return textWraps(node) ? 0 : 1;
}

export function showsSeconds(node: UiNode): boolean {
  return nodeBoolean(node, UiComponentProperties.Seconds) === true;
}

export function textDigits(node: UiNode): number | null {
  const digits = nodeNumber(node, UiComponentProperties.Digits);
  return digits !== undefined && digits > 0 ? digits : null;
}

export function chartColor(node: UiNode): string {
  return nodeHexColor(node, UiComponentProperties.Color) ?? 'var(--color-accent)';
}

export function chartPoints(node: UiNode): number[] {
  const raw = nodeRaw(node, UiComponentProperties.Points);
  if (!Array.isArray(raw)) return [];

  return raw
    .filter((value): value is number => typeof value === 'number' && Number.isFinite(value))
    .map(clampUnit);
}

export function chartPlotTop(node: UiNode): number {
  return clampUnit(nodeNumber(node, UiComponentProperties.PlotTop) ?? 0);
}

export function artworkFilter(node: UiNode): string | null {
  const brightness = nodeNumber(node, UiComponentProperties.Brightness);
  const saturation = nodeNumber(node, UiComponentProperties.Saturation);
  if (brightness === undefined && saturation === undefined) return null;

  const parts: string[] = [];
  if (brightness !== undefined) parts.push(`brightness(${Math.min(2, Math.max(0, brightness))})`);
  if (saturation !== undefined) parts.push(`saturate(${Math.min(2, Math.max(0, saturation))})`);
  return parts.join(' ');
}

export function artworkCrossfades(node: UiNode): boolean {
  return nodeString(node, UiComponentProperties.Transition) === UiComponentImageTransitions.Crossfade;
}
