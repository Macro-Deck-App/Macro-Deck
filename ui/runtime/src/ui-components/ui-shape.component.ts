import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents, UiComponentShapes } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { nodeString } from '../ui-framework/node-properties.util';
import { nodeHexColor, nodeLength, resolveLength } from '../ui-framework/length';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { SVG_NS } from './render-constants';

const PATH_ARITY: { [command: string]: number } = { M: 2, L: 2, H: 1, V: 1, C: 6, Q: 4, A: 7, Z: 0 };

const PATH_NUMBER = /^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?/;

export function isShapePathData(value: string): boolean {
  let rest = value.trim();
  if (rest[0] !== 'M') return false;
  while (rest.length > 0) {
    const arity = PATH_ARITY[rest[0]];
    if (arity === undefined) return false;
    rest = rest.slice(1);
    let count = 0;
    for (;;) {
      rest = rest.replace(/^[\s,]+/, '');
      const number = PATH_NUMBER.exec(rest);
      if (number === null) break;
      rest = rest.slice(number[0].length);
      count++;
    }
    if (arity === 0 ? count !== 0 : count === 0 || count % arity !== 0) return false;
  }
  return true;
}

function roundedRect(width: number, height: number, radius: number): string {
  const r = Math.max(0, Math.min(radius, width / 2, height / 2));
  if (r === 0) return `M 0 0 H ${width} V ${height} H 0 Z`;
  return `M ${r} 0 H ${width - r} A ${r} ${r} 0 0 1 ${width} ${r} V ${height - r} `
    + `A ${r} ${r} 0 0 1 ${width - r} ${height} H ${r} A ${r} ${r} 0 0 1 0 ${height - r} V ${r} `
    + `A ${r} ${r} 0 0 1 ${r} 0 Z`;
}

export function shapeOutline(node: UiNode, width: number, height: number, basis: number): string | null {
  const shape = nodeString(node, UiComponentProperties.Shape) ?? UiComponentShapes.Rectangle;
  switch (shape) {
    case UiComponentShapes.Rectangle:
      return roundedRect(width, height, 0);
    case UiComponentShapes.RoundedRectangle:
      return roundedRect(width, height, resolveLength(nodeLength(node, UiComponentProperties.CornerRadius), basis, null) ?? 0);
    case UiComponentShapes.Capsule:
      return roundedRect(width, height, Math.min(width, height) / 2);
    case UiComponentShapes.Circle: {
      const r = Math.min(width, height) / 2;
      const cx = width / 2;
      const cy = height / 2;
      return `M ${cx - r} ${cy} A ${r} ${r} 0 1 1 ${cx + r} ${cy} A ${r} ${r} 0 1 1 ${cx - r} ${cy} Z`;
    }
    default:
      return null;
  }
}

export const uiShapeComponent: UiComponentDefinition = {
  type: UiComponents.Shape,

  create(doc: Document) {
    return doc.createElementNS(SVG_NS, 'svg') as unknown as SVGElement;
  },

  paint(node, ctx) {
    const element = ctx.element as SVGElement;
    const width = ctx.box.width ?? ctx.basis;
    const height = ctx.box.height ?? ctx.basis;
    ctx.setClassName(element, 'widget-shape');
    ctx.setAttribute(element, 'width', String(width));
    ctx.setAttribute(element, 'height', String(height));

    const isPath = nodeString(node, UiComponentProperties.Shape) === UiComponentShapes.Path;
    const data = nodeString(node, UiComponentProperties.Path);
    const d = isPath
      ? (data !== undefined && isShapePathData(data) ? data : null)
      : shapeOutline(node, width, height, ctx.basis);
    if (d === null) {
      ctx.dropPart('shapePath');
      return;
    }

    const path = ctx.part('shapePath', 'path', SVG_NS);
    ctx.setAttribute(path, 'd', d);
    ctx.setAttribute(path, 'transform', isPath ? `scale(${width} ${height})` : null);
    ctx.setAttribute(path, 'vector-effect', isPath ? 'non-scaling-stroke' : null);
    ctx.setAttribute(path, 'fill', nodeHexColor(node, UiComponentProperties.Color) ?? 'none');

    const stroke = nodeHexColor(node, UiComponentProperties.StrokeColor);
    const strokeWidth = resolveLength(nodeLength(node, UiComponentProperties.StrokeWidth), ctx.basis, null);
    const stroked = stroke !== undefined && strokeWidth !== undefined && strokeWidth > 0;
    ctx.setAttribute(path, 'stroke', stroked ? stroke! : null);
    ctx.setAttribute(path, 'stroke-width', stroked ? String(strokeWidth) : null);
  },
};
