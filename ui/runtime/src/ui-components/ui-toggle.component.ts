import { UiNode } from '../ui-framework/ui-node.interface';
import { UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { UiComponentEvents } from './component-events';
import { nodeBoolean } from '../ui-framework/node-properties.util';
import { nodeLength, resolveLength } from '../ui-framework/length';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { sliderLevelColor } from './bar';
import { px } from './px.util';
import {
  bindValuePress,
  createValuePressState,
  holdUntilSettled,
  paintValueTint,
  releaseValuePress,
  ValuePressState,
} from './value-press';

export const TOGGLE_ASPECT = 1.75;

export interface UiToggleState extends ValuePressState {
  held: boolean | null;
  producerAtRelease: boolean;
}

function producerOn(node: UiNode): boolean {
  return nodeBoolean(node, UiComponentProperties.On) === true;
}

export function toggleDisplayedOn(node: UiNode, state: UiToggleState): boolean {
  const producer = producerOn(node);
  return state.held !== null && producer === state.producerAtRelease ? state.held : producer;
}

export function toggleTrackHeight(node: UiNode, width: number, height: number, basis: number, cross: number | null): number {
  return resolveLength(nodeLength(node, UiComponentProperties.Size), basis, cross)
    ?? Math.min(height, width / TOGGLE_ASPECT);
}

export const uiToggleComponent: UiComponentDefinition<UiToggleState> = {
  type: UiComponents.Toggle,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiToggleState {
    return { ...createValuePressState(), held: null, producerAtRelease: false };
  },

  bind(ctx) {
    bindValuePress(ctx, ctx.state, () => {
      const node = ctx.current();
      const next = !toggleDisplayedOn(node, ctx.state);
      ctx.state.producerAtRelease = producerOn(node);
      ctx.state.held = next;
      holdUntilSettled(ctx, ctx.state, () => { ctx.state.held = null; });
      ctx.emit(node, UiComponentEvents.Change, next);
      ctx.repaint();
    });
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-toggle');
    ctx.sizeTo(element, ctx.box);

    const width = ctx.box.width ?? ctx.basis;
    const height = ctx.box.height ?? ctx.basis;
    const trackHeight = toggleTrackHeight(node, width, height, ctx.basis, ctx.crossExtent);
    const trackWidth = TOGGLE_ASPECT * trackHeight;
    const on = toggleDisplayedOn(node, ctx.state);

    const track = ctx.part('track', 'div');
    ctx.setClassName(track, 'widget-toggle-track');
    ctx.setStyle(track, 'width', px(trackWidth));
    ctx.setStyle(track, 'height', px(trackHeight));
    ctx.setStyle(track, 'left', px((width - trackWidth) / 2));
    ctx.setStyle(track, 'top', px((height - trackHeight) / 2));
    ctx.setStyle(track, 'border-radius', px(trackHeight / 2));
    ctx.setStyle(track, 'background', on ? sliderLevelColor(node) : 'var(--color-bg-tertiary)');

    const knob = ctx.part('knob', 'div', undefined, track);
    const diameter = 0.8 * trackHeight;
    const inset = 0.1 * trackHeight;
    ctx.setClassName(knob, 'widget-toggle-knob');
    ctx.setStyle(knob, 'width', px(diameter));
    ctx.setStyle(knob, 'height', px(diameter));
    ctx.setStyle(knob, 'top', px(inset));
    ctx.setStyle(knob, 'left', px(on ? trackWidth - inset - diameter : inset));

    paintValueTint(node, ctx);
  },

  release(ctx) {
    releaseValuePress(ctx.state);
  },

  intrinsicMainPx(node, m) {
    const size = resolveLength(nodeLength(node, UiComponentProperties.Size), m.basis, m.crossExtent) ?? 0;
    return m.horizontal ? TOGGLE_ASPECT * size : size;
  },
};
