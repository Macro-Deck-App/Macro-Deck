import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponentDirections, UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { UiComponentEvents } from './component-events';
import { nodeNumber, nodeString } from '../ui-framework/node-properties.util';
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

export const VERTICAL_SEGMENT_EXTENT = 0.14;

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

export function segmentedIsVertical(node: UiNode): boolean {
  return nodeString(node, UiComponentProperties.Direction) === UiComponentDirections.Vertical;
}

function verticalNaturalHeight(node: UiNode, basis: number): number {
  return (node.children ?? []).length * VERTICAL_SEGMENT_EXTENT * basis;
}

export const uiSegmentedComponent: UiComponentDefinition<UiSegmentedState> = {
  type: UiComponents.Segmented,
  // Version 2 adds the vertical direction, which a version 1 reader would squeeze into one row.
  version: { minimum: 1, maximum: 2 },

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
      const vertical = segmentedIsVertical(node);
      const extent = vertical ? rect.height : rect.width;
      if (count === 0 || extent <= 0) return;

      const offset = vertical ? pointer.clientY - rect.top : pointer.clientX - rect.left;
      const index = Math.max(0, Math.min(count - 1, Math.floor((offset / extent) * count)));
      if (index === segmentedDisplayedIndex(node, ctx.state)) return;

      ctx.state.producerAtRelease = producerSelected(node);
      ctx.state.held = index;
      holdUntilSettled(ctx, ctx.state, () => { ctx.state.held = null; });
      ctx.emit(node, UiComponentEvents.Change, index);
      ctx.repaint();
    }, true, () => segmentedIsVertical(ctx.current()));

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
    const vertical = segmentedIsVertical(node);
    ctx.setClass(element, 'widget-segmented-vertical', vertical);
    const naturalHeight = vertical && ctx.box.height === null ? verticalNaturalHeight(node, ctx.basis) : null;
    ctx.sizeTo(element, naturalHeight === null ? ctx.box : { width: ctx.box.width, height: naturalHeight });

    const width = ctx.box.width ?? ctx.basis;
    const height = naturalHeight ?? ctx.box.height ?? ctx.basis;
    const children = node.children ?? [];
    const count = children.length;
    const segment = count > 0 ? (vertical ? height : width) / count : (vertical ? height : width);
    const edge = vertical ? Math.min(segment, width) : height;
    const inset = SEGMENT_FACE_INSET * edge;

    const track = ctx.part('track', 'div');
    ctx.setClassName(track, 'widget-segmented-track');
    ctx.setStyle(track, 'border-radius', px(edge / 2));

    const selected = segmentedDisplayedIndex(node, ctx.state);
    const face = ctx.part('face', 'div');
    const shown = selected !== undefined && selected >= 0 && selected < count;
    const offset = (shown ? selected! : 0) * segment + inset;
    ctx.setClassName(face, 'widget-segmented-face');
    ctx.setStyle(face, 'display', shown ? null : 'none');
    ctx.setStyle(face, 'left', px(vertical ? inset : offset));
    ctx.setStyle(face, 'top', px(vertical ? offset : inset));
    ctx.setStyle(face, 'width', px(Math.max(0, (vertical ? width : segment) - 2 * inset)));
    ctx.setStyle(face, 'height', px(Math.max(0, (vertical ? segment : height) - 2 * inset)));
    ctx.setStyle(face, 'border-radius', px(Math.max(0, edge / 2 - inset)));
    ctx.setStyle(face, 'background', sliderLevelColor(node));

    const content = ctx.part('content', 'div');
    ctx.setClassName(content, 'widget-segmented-content');
    const box = vertical ? { width, height: segment } : { width: segment, height };
    ctx.syncChildren(content, children.map(child => ({ child, box, crossExtent: null })));
    const placed = Array.from(content.children).filter(child => child.hasAttribute('data-node-id'));
    for (let index = 0; index < placed.length; index++) {
      ctx.setStyle(placed[index] as HTMLElement, 'left', px(vertical ? 0 : index * segment));
      ctx.setStyle(placed[index] as HTMLElement, 'top', vertical ? px(index * segment) : null);
    }

    paintValueTint(node, ctx);
  },

  release(ctx) {
    releaseValuePress(ctx.state);
  },

  intrinsicMainPx(node, m) {
    return !m.horizontal && segmentedIsVertical(node) ? verticalNaturalHeight(node, m.basis) : 0;
  },
};
