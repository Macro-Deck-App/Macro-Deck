import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { UiComponentEvents } from './component-events';
import { nodeNumber } from '../ui-framework/node-properties.util';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { sliderLevelColor } from './bar';
import { px } from './px.util';
import {
  bindValuePress,
  claimsChange,
  createValuePressState,
  holdUntilSettled,
  paintValueTint,
  releaseValuePress,
  ValuePressState,
} from './value-press';

export const SEGMENT_FACE_INSET = 0.08;

export interface UiSegmentedState extends ValuePressState {
  held: number | null;
  producerAtRelease: number | undefined;
}

function producerSelected(node: UiNode): number | undefined {
  return nodeNumber(node, UiComponentProperties.Selected);
}

export function segmentedDisplayedIndex(node: UiNode, state: UiSegmentedState): number | undefined {
  const producer = producerSelected(node);
  return state.held !== null && producer === state.producerAtRelease ? state.held : producer;
}

export const uiSegmentedComponent: UiComponentDefinition<UiSegmentedState> = {
  type: UiComponents.Segmented,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiSegmentedState {
    return { ...createValuePressState(), held: null, producerAtRelease: undefined };
  },

  bind(ctx) {
    bindValuePress(ctx, ctx.state, pointer => {
      const node = ctx.current();
      const count = (node.children ?? []).length;
      const rect = (ctx.element as HTMLElement).getBoundingClientRect();
      if (count === 0 || rect.width <= 0) return;

      const index = Math.max(0, Math.min(count - 1, Math.floor(((pointer.clientX - rect.left) / rect.width) * count)));
      if (index === segmentedDisplayedIndex(node, ctx.state)) return;

      ctx.state.producerAtRelease = producerSelected(node);
      ctx.state.held = index;
      holdUntilSettled(ctx, ctx.state, () => { ctx.state.held = null; });
      ctx.emit(node, UiComponentEvents.Change, index);
      ctx.repaint();
    }, true);

    // Capture phase, after the segmented's own handling: a pointer on segment content never reaches it,
    // even where pointer-events is not honoured, so a child's declared events are never offered.
    for (const type of ['pointerdown', 'pointerup', 'pointercancel', 'pointerleave', 'click']) {
      ctx.element.addEventListener(type, (event: Event) => {
        if (event.target !== ctx.element && claimsChange(ctx.current())) event.stopPropagation();
      }, true);
    }
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-segmented');
    ctx.sizeTo(element, ctx.box);

    const width = ctx.box.width ?? ctx.basis;
    const height = ctx.box.height ?? ctx.basis;
    const children = node.children ?? [];
    const segment = children.length > 0 ? width / children.length : width;

    const track = ctx.part('track', 'div');
    ctx.setClassName(track, 'widget-segmented-track');
    ctx.setStyle(track, 'border-radius', px(height / 2));

    const selected = segmentedDisplayedIndex(node, ctx.state);
    const face = ctx.part('face', 'div');
    const inset = SEGMENT_FACE_INSET * height;
    const shown = selected !== undefined && selected >= 0 && selected < children.length;
    ctx.setClassName(face, 'widget-segmented-face');
    ctx.setStyle(face, 'display', shown ? null : 'none');
    ctx.setStyle(face, 'left', px((shown ? selected! : 0) * segment + inset));
    ctx.setStyle(face, 'top', px(inset));
    ctx.setStyle(face, 'width', px(Math.max(0, segment - 2 * inset)));
    ctx.setStyle(face, 'height', px(Math.max(0, height - 2 * inset)));
    ctx.setStyle(face, 'border-radius', px(Math.max(0, height / 2 - inset)));
    ctx.setStyle(face, 'background', sliderLevelColor(node));

    const content = ctx.part('content', 'div');
    ctx.setClassName(content, 'widget-segmented-content');
    ctx.syncChildren(content, children.map(child => ({ child, box: { width: segment, height }, crossExtent: null })));
    const placed = Array.from(content.children).filter(child => child.hasAttribute('data-node-id'));
    for (let index = 0; index < placed.length; index++) {
      ctx.setStyle(placed[index] as HTMLElement, 'left', px(index * segment));
    }

    paintValueTint(node, ctx);
  },

  release(ctx) {
    releaseValuePress(ctx.state);
  },
};
