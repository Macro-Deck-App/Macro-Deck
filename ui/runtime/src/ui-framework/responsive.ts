import { UiNode } from './ui-node.interface';
import { UiComponentBox } from './layout';
import { UI_COMPONENT_CELL } from './length';
import { nodeRaw } from './node-properties.util';
import { UiComponents } from '../ui-components/ui-component-types';
import { UiComponentProperties } from '../ui-components/component-properties';

export const UI_RESPONSIVE_TOLERANCE = 0.0001;

function known(extent: number | null | undefined): number | null {
  return typeof extent === 'number' && Number.isFinite(extent) && extent > 0 ? extent : null;
}

function bound(condition: Record<string, unknown>, name: string): number | null {
  const value = condition[name];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function min(condition: Record<string, unknown>, name: string, value: number | null): boolean {
  const limit = bound(condition, name);
  return limit === null || (value !== null && value >= limit - UI_RESPONSIVE_TOLERANCE);
}

function max(condition: Record<string, unknown>, name: string, value: number | null): boolean {
  const limit = bound(condition, name);
  return limit === null || (value !== null && value < limit - UI_RESPONSIVE_TOLERANCE);
}

function holds(condition: unknown, width: number | null, height: number | null): boolean {
  if (typeof condition !== 'object' || condition === null || Array.isArray(condition)) return false;
  const members = condition as Record<string, unknown>;
  const aspect = width !== null && height !== null ? width / height : null;
  return min(members, 'minWidth', width)
    && max(members, 'maxWidth', width)
    && min(members, 'minHeight', height)
    && max(members, 'maxHeight', height)
    && min(members, 'minAspect', aspect)
    && max(members, 'maxAspect', aspect);
}

export function selectResponsiveChild(
  variants: unknown,
  childCount: number,
  widthCells: number | null,
  heightCells: number | null,
): number {
  if (childCount <= 0) return -1;
  if (!Array.isArray(variants)) return 0;

  const width = known(widthCells);
  const height = known(heightCells);
  for (let index = 0; index < variants.length && index + 1 < childCount; index++) {
    if (holds(variants[index], width, height)) return index + 1;
  }
  return 0;
}

export function responsiveChildIndex(node: UiNode, box: UiComponentBox | null): number {
  const width = box?.width ?? null;
  const height = box?.height ?? null;
  return selectResponsiveChild(
    nodeRaw(node, UiComponentProperties.Variants),
    (node.children ?? []).length,
    width === null ? null : width / UI_COMPONENT_CELL,
    height === null ? null : height / UI_COMPONENT_CELL);
}

export function responsiveChild(node: UiNode, box: UiComponentBox | null): UiNode | null {
  const index = responsiveChildIndex(node, box);
  return index < 0 ? null : (node.children ?? [])[index] ?? null;
}

function passesWholeBox(node: UiNode): boolean {
  if (node.type === UiComponents.Layer || node.type === UiComponents.Transform) return true;
  if (node.type !== UiComponents.Modifier) return false;
  return nodeRaw(node, UiComponentProperties.Padding) === undefined
    && nodeRaw(node, UiComponentProperties.Frame) === undefined;
}

// The children a tile-level walk descends into. A ui.responsive reached from the tile only through
// nodes that hand on the whole box is resolved with the tile's box; any other one counts its default.
export function tileWalkChildren(
  node: UiNode,
  box: UiComponentBox | null,
): Array<{ child: UiNode; box: UiComponentBox | null }> {
  const children = node.children ?? [];
  if (node.type === UiComponents.Responsive) {
    const chosen = responsiveChild(node, box);
    return chosen === null ? [] : [{ child: chosen, box }];
  }
  const passed = passesWholeBox(node) ? box : null;
  return children.map(child => ({ child, box: passed }));
}

export function effectiveTreeRoot(root: UiNode | null | undefined, box: UiComponentBox | null): UiNode | null {
  let node = root ?? null;
  while (node !== null && node.type === UiComponents.Responsive) node = responsiveChild(node, box);
  return node;
}
