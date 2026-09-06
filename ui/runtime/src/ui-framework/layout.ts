import { nodeBoolean } from './node-properties.util';
import { UiNode } from './ui-node.interface';
import { nodeLength, resolveLength } from './length';
import { UiComponentProperties } from '../ui-components/component-properties';
import type { UiComponentRegistry } from './component-registry';

export interface UiComponentBox {
  width: number | null;
  height: number | null;
}

export interface UiComponentStackChild {
  child: UiNode;
  box: UiComponentBox;
  crossExtent: number | null;
}

// renderer.css paints these numbers and the intrinsic height below adds them up - a field whose
// height the layout does not know contributes nothing to its stack, and the sibling filling the
// remaining space then overflows it by exactly the field's height.
export const WIDGET_FIELD_LINE_HEIGHT = 1.2;

export const WIDGET_FIELD_PADDING_EM = 0.4;

export const WIDGET_FIELD_BORDER_PX = 1;

export interface UiIntrinsicMetrics {
  basis: number;
  crossExtent: number | null;
  horizontal: boolean;
  ofChild(child: UiNode): number;
}

function metricsFor(basis: number, crossExtent: number | null, horizontal: boolean, registry: UiComponentRegistry): UiIntrinsicMetrics {
  const m: UiIntrinsicMetrics = {
    basis,
    crossExtent,
    horizontal,
    ofChild: child => intrinsicMainPx(child, m, registry),
  };
  return m;
}

export function intrinsicMainPx(
  node: UiNode,
  m: UiIntrinsicMetrics,
  registry: UiComponentRegistry,
): number {
  const declared = resolveLength(nodeLength(node, UiComponentProperties.MainSize), m.basis, m.crossExtent);
  if (declared !== undefined) return declared;

  const definition = registry.get(node.type);
  return definition?.intrinsicMainPx ? definition.intrinsicMainPx(node, m) : 0;
}

export function layoutStackChildren(
  node: UiNode,
  box: UiComponentBox,
  basis: number,
  padding: number,
  gap: number,
  horizontal: boolean,
  registry: UiComponentRegistry,
): UiComponentStackChild[] {
  const inner = (outer: number | null) => (outer === null ? null : Math.max(0, outer - 2 * padding));
  const contentWidth = inner(box.width);
  const contentHeight = inner(box.height);
  const mainTotal = horizontal ? contentWidth : contentHeight;
  const crossTotal = horizontal ? contentHeight : contentWidth;
  const m = metricsFor(basis, crossTotal, horizontal, registry);

  const children = node.children ?? [];
  const specs = children.map(child => {
    const mainSize = resolveLength(nodeLength(child, UiComponentProperties.MainSize), basis, crossTotal);
    const fill = mainSize === undefined && nodeBoolean(child, UiComponentProperties.Fill) === true;
    const intrinsic = mainSize === undefined && !fill
      ? intrinsicMainPx(child, m, registry)
      : 0;
    return { child, mainSize, fill, intrinsic };
  });

  const gapsTotal = Math.max(0, children.length - 1) * gap;
  const fixedTotal = specs.reduce((sum, spec) => sum + (spec.mainSize ?? spec.intrinsic), 0);
  const fillCount = specs.filter(spec => spec.fill).length;
  const fillShare = mainTotal === null || fillCount === 0
    ? null
    : Math.max(0, mainTotal - gapsTotal - fixedTotal) / fillCount;

  return specs.map(spec => {
    const mainDim = spec.mainSize !== undefined ? spec.mainSize : (spec.fill ? fillShare : null);
    const childBox: UiComponentBox = horizontal
      ? { width: mainDim, height: crossTotal }
      : { width: crossTotal, height: mainDim };
    return { child: spec.child, box: childBox, crossExtent: crossTotal };
  });
}
