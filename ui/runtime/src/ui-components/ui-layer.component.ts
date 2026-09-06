import { UiComponents } from './ui-component-types';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { bindPressGesture, createPressGestureState, PressGestureState, releasePressGesture } from './press-gesture';

export interface UiLayerState {
  press: PressGestureState | null;
}

export const uiLayerComponent: UiComponentDefinition<UiLayerState> = {
  type: UiComponents.Layer,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiLayerState {
    return { press: null };
  },

  bind(ctx) {
    const press = createPressGestureState(ctx);
    ctx.state.press = press;
    bindPressGesture(ctx.element, ctx, press);
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-layer');
    ctx.sizeTo(element, ctx.box);

    const layered = node.children ?? [];
    const entries = layered.map(child => ({ child, box: ctx.box, crossExtent: null }));
    ctx.syncChildren(element, entries);
    ctx.pressTint(node);
  },

  release(ctx) {
    if (ctx.state.press) releasePressGesture(ctx.state.press);
  },

  intrinsicMainPx(node, m) {
    const layered = node.children ?? [];
    return layered.reduce((widest, child) => Math.max(widest, m.ofChild(child)), 0);
  },
};
