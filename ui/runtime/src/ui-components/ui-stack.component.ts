import { UiComponents, UiComponentDirections } from './ui-component-types';
import { nodeString } from '../ui-framework/node-properties.util';
import { nodeLength, resolveLength } from '../ui-framework/length';
import { UiComponentProperties } from './component-properties';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { bindPressGesture, createPressGestureState, PressGestureState, releasePressGesture } from './press-gesture';
import { paintStackLayout } from './stack-paint';

export interface UiStackState {
  press: PressGestureState | null;
}

export const uiStackComponent: UiComponentDefinition<UiStackState> = {
  type: UiComponents.Stack,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiStackState {
    return { press: null };
  },

  bind(ctx) {
    const press = createPressGestureState(ctx);
    ctx.state.press = press;
    bindPressGesture(ctx.element, ctx, press);
  },

  paint(node, ctx) {
    paintStackLayout(node, ctx, false);
  },

  release(ctx) {
    if (ctx.state.press) releasePressGesture(ctx.state.press);
  },

  intrinsicMainPx(node, m) {
    const children = node.children ?? [];
    if (children.length === 0) return 0;

    const ownHorizontal =
      nodeString(node, UiComponentProperties.Direction) === UiComponentDirections.Horizontal;
    const padding = resolveLength(nodeLength(node, UiComponentProperties.Padding), m.basis, m.crossExtent) ?? 0;
    const gap = resolveLength(nodeLength(node, UiComponentProperties.Gap), m.basis, m.crossExtent) ?? 0;
    const extents = children.map(child => m.ofChild(child));

    const inner = ownHorizontal === m.horizontal
      ? extents.reduce((a, b) => a + b, 0) + Math.max(0, children.length - 1) * gap
      : extents.reduce((a, b) => Math.max(a, b), 0);

    return inner + 2 * padding;
  },
};
