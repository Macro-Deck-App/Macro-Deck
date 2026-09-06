import { UiComponents } from './ui-component-types';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { bindPressGesture, createPressGestureState, PressGestureState, releasePressGesture } from './press-gesture';
import {
  createStackButtonState,
  paintButtonArtwork,
  paintStackLayout,
  releaseStackButtonState,
  StackButtonState,
} from './stack-paint';

export interface UiButtonState {
  press: PressGestureState | null;
  artwork: StackButtonState;
}

export const uiButtonComponent: UiComponentDefinition<UiButtonState> = {
  type: UiComponents.Button,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiButtonState {
    return { press: null, artwork: createStackButtonState() };
  },

  bind(ctx) {
    const press = createPressGestureState(ctx);
    ctx.state.press = press;
    bindPressGesture(ctx.element, ctx, press);
  },

  paint(node, ctx) {
    paintStackLayout(node, ctx, true);
    paintButtonArtwork(node, ctx, ctx.state.artwork);
  },

  release(ctx) {
    if (ctx.state.press) releasePressGesture(ctx.state.press);
    releaseStackButtonState(ctx.state.artwork);
  },

  // A button declares no `intrinsicMainPx` of its own, so it defaults to 0 like the rest of the
  // unlisted types - the same as before this refactor, where it was not one of the switch's own cases.
};
