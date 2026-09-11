import { UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { nodeNumber } from '../ui-framework/node-properties.util';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import type { UiNode } from '../ui-framework/ui-node.interface';
import { bindPressGesture, createPressGestureState, PressGestureState, releasePressGesture } from './press-gesture';

export interface UiTransformState {
  press: PressGestureState | null;
}

function finiteNumber(node: UiNode, key: string): number | undefined {
  const value = nodeNumber(node, key);
  return value !== undefined && Number.isFinite(value) ? value : undefined;
}

function transformOf(node: UiNode): { transform: string; origin: string } | null {
  const rotation = finiteNumber(node, UiComponentProperties.Rotation) ?? 0;
  const requestedZoom = finiteNumber(node, UiComponentProperties.Zoom);
  const zoom = requestedZoom !== undefined && requestedZoom > 0 ? requestedZoom : 1;
  const offsetX = finiteNumber(node, UiComponentProperties.OffsetX) ?? 0;
  const offsetY = finiteNumber(node, UiComponentProperties.OffsetY) ?? 0;
  if (rotation === 0 && zoom === 1 && offsetX === 0 && offsetY === 0) return null;

  const originX = finiteNumber(node, UiComponentProperties.OriginX) ?? 0.5;
  const originY = finiteNumber(node, UiComponentProperties.OriginY) ?? 0.5;
  return {
    transform: `translate(${offsetX * 100}%, ${offsetY * 100}%) rotate(${rotation}deg) scale(${zoom})`,
    origin: `${originX * 100}% ${originY * 100}%`,
  };
}

export const uiTransformComponent: UiComponentDefinition<UiTransformState> = {
  type: UiComponents.Transform,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiTransformState {
    return { press: null };
  },

  bind(ctx) {
    const press = createPressGestureState(ctx);
    ctx.state.press = press;
    bindPressGesture(ctx.element, ctx, press);
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-transform');
    ctx.sizeTo(element, ctx.box);

    const transform = transformOf(node);
    // The web client's browser floor (Chrome 30, Android 4.4) only knows the prefixed names.
    for (const prefix of ['', '-webkit-']) {
      ctx.setStyle(element, `${prefix}transform`, transform?.transform ?? null);
      ctx.setStyle(element, `${prefix}transform-origin`, transform?.origin ?? null);
    }

    const children = node.children ?? [];
    ctx.syncChildren(element, children.map(child => ({ child, box: ctx.box, crossExtent: null })));
    ctx.pressTint(node);
  },

  release(ctx) {
    if (ctx.state.press) releasePressGesture(ctx.state.press);
  },

  intrinsicMainPx(node, m) {
    const children = node.children ?? [];
    return children.reduce((widest, child) => Math.max(widest, m.ofChild(child)), 0);
  },
};
