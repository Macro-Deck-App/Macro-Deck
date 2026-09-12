import { UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { UiComponentClips } from './component-modifiers';
import { nodeNumber, nodeRaw, nodeString } from '../ui-framework/node-properties.util';
import { nodeLength, resolveLength } from '../ui-framework/length';
import { resolveFrame } from '../ui-framework/frame';
import { UiNode } from '../ui-framework/ui-node.interface';
import { gradientCss, UiModifierInputs } from '../render/node-modifiers';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { bindPressGesture, createPressGestureState, PressGestureState, releasePressGesture } from './press-gesture';
import { px } from './px.util';

export interface UiModifierState {
  press: PressGestureState | null;
}

const CAPSULE_RADIUS = '9999px';

function paddingPx(node: UiNode, basis: number, crossExtent: number | null): number {
  return resolveLength(nodeLength(node, UiComponentProperties.Padding), basis, crossExtent) ?? 0;
}

function maskCss(node: UiNode): string | undefined {
  return gradientCss(nodeRaw(node, UiComponentProperties.Mask), stop => {
    const opacity = stop['opacity'];
    return typeof opacity === 'number' && opacity >= 0 && opacity <= 1 ? `rgba(0, 0, 0, ${opacity})` : undefined;
  });
}

export const uiModifierComponent: UiComponentDefinition<UiModifierState> = {
  type: UiComponents.Modifier,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiModifierState {
    return { press: null };
  },

  bind(ctx) {
    const press = createPressGestureState(ctx);
    ctx.state.press = press;
    bindPressGesture(ctx.element, ctx, press);
  },

  modifierInputs(node) {
    const inputs: UiModifierInputs = {};
    const opacity = nodeNumber(node, UiComponentProperties.Opacity);
    if (opacity !== undefined) inputs.opacity = Math.min(1, Math.max(0, opacity));
    if (nodeString(node, UiComponentProperties.Clip) === UiComponentClips.Capsule) inputs.radius = CAPSULE_RADIUS;
    return inputs;
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    const frame = resolveFrame(node, ctx.box, ctx.basis, ctx.crossExtent);
    const face = frame.box;
    ctx.setClassName(element, 'widget-modifier'
      + (face.width === null ? ' widget-modifier-fill-width' : '')
      + (face.height === null ? ' widget-modifier-fill-height' : ''));
    ctx.sizeTo(element, face);
    ctx.setStyle(element, 'min-width', face.width === null ? px(frame.minWidth) : null);
    ctx.setStyle(element, 'max-width', face.width === null ? px(frame.maxWidth) : null);
    ctx.setStyle(element, 'min-height', face.height === null ? px(frame.minHeight) : null);
    ctx.setStyle(element, 'max-height', face.height === null ? px(frame.maxHeight) : null);

    const padding = paddingPx(node, ctx.basis, ctx.crossExtent);
    ctx.setStyle(element, 'padding', padding > 0 ? px(padding) : null);

    const clip = nodeString(node, UiComponentProperties.Clip);
    ctx.setStyle(element, 'overflow',
      clip === UiComponentClips.Bounds || clip === UiComponentClips.Capsule ? 'hidden' : null);
    const circle = clip === UiComponentClips.Circle ? 'circle(50% at 50% 50%)' : null;
    ctx.setStyle(element, '-webkit-clip-path', circle);
    ctx.setStyle(element, 'clip-path', circle);

    const mask = maskCss(node) ?? null;
    ctx.setStyle(element, '-webkit-mask-image', mask);
    ctx.setStyle(element, 'mask-image', mask);

    const inner = (outer: number | null) => (outer === null ? null : Math.max(0, outer - 2 * padding));
    const child = node.children?.[0];
    ctx.syncChildren(element, child === undefined
      ? []
      : [{ child, box: { width: inner(face.width), height: inner(face.height) }, crossExtent: inner(ctx.crossExtent) }]);
    ctx.pressTint(node);
  },

  release(ctx) {
    if (ctx.state.press) releasePressGesture(ctx.state.press);
  },

  intrinsicMainPx(node, m) {
    const box = m.horizontal ? { width: null, height: m.crossExtent } : { width: m.crossExtent, height: null };
    const frame = resolveFrame(node, box, m.basis, m.crossExtent);
    const fixed = m.horizontal ? frame.box.width : frame.box.height;
    if (fixed !== null) return fixed;

    const child = node.children?.[0];
    let main = (child === undefined ? 0 : m.ofChild(child)) + 2 * paddingPx(node, m.basis, m.crossExtent);
    const max = m.horizontal ? frame.maxWidth : frame.maxHeight;
    const min = m.horizontal ? frame.minWidth : frame.minHeight;
    if (max !== undefined) main = Math.min(main, max);
    if (min !== undefined) main = Math.max(main, min);
    return main;
  },
};
