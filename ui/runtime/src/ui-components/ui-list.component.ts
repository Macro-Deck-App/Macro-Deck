import { emitsEvent } from '../ui-framework/node-properties.util';
import { UiComponents } from './ui-component-types';
import { UiComponentEvents } from './component-events';
import { nodeGapPx, nodeIsHorizontal, nodePaddingPx, stackBackground } from './style';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { px } from './px.util';
import { nodeModifierOwns } from '../render/node-modifiers';

const LIST_REVEAL_THROTTLE_MS = 500;

export interface UiListState {
  revealed: number;
  revealTimer: ReturnType<typeof setTimeout> | null;
}

function lastRevealedIndex(element: HTMLElement, horizontal: boolean): number {
  const end = horizontal ? element.scrollLeft + element.clientWidth : element.scrollTop + element.clientHeight;
  let last = -1;
  for (let index = 0; index < element.children.length; index++) {
    const child = element.children[index] as HTMLElement;
    if ((horizontal ? child.offsetLeft : child.offsetTop) <= end) last = index;
  }
  return last;
}

function reportReveal(element: HTMLElement, ctx: UiComponentContext<UiListState>): void {
  const state = ctx.state;
  if (state.revealTimer !== null) return;

  const node = ctx.current();
  if (!emitsEvent(node, UiComponentEvents.Reveal)) return;

  // A list with no box has revealed nothing. Without this, a list painted before it has been laid out
  // reads every child as visible at offset zero, claims the last index and never asks again.
  const horizontal = nodeIsHorizontal(node);
  if ((horizontal ? element.clientWidth : element.clientHeight) <= 0) return;

  const index = lastRevealedIndex(element, horizontal);
  if (index <= state.revealed) return;

  state.revealed = index;
  ctx.emit(node, UiComponentEvents.Reveal, index);
  state.revealTimer = setTimeout(() => { state.revealTimer = null; }, LIST_REVEAL_THROTTLE_MS);
}

export const uiListComponent: UiComponentDefinition<UiListState> = {
  type: UiComponents.List,
  // Version 2 adds the horizontal direction, which a version 1 reader would silently scroll vertically.
  version: { minimum: 1, maximum: 2 },

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiListState {
    return { revealed: -1, revealTimer: null };
  },

  bind(ctx) {
    const element = ctx.element as HTMLElement;
    element.addEventListener('scroll', () => reportReveal(element, ctx));
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    const padding = nodePaddingPx(node, ctx.basis, ctx.crossExtent);
    const gap = nodeGapPx(node, ctx.basis, ctx.crossExtent);

    const horizontal = nodeIsHorizontal(node);

    ctx.setClassName(element, 'widget-list');
    ctx.setClass(element, 'widget-list-horizontal', horizontal);
    ctx.setStyle(element, 'gap', px(gap));
    ctx.setStyle(element, 'padding', px(padding));
    if (!nodeModifierOwns(node, ctx, 'background')) ctx.setStyle(element, 'background', stackBackground(node) ?? null);
    // The main axis is unbounded, so only the cross axis takes an extent from the box.
    ctx.setStyle(element, 'width', px(ctx.box.width));
    ctx.setStyle(element, 'height', px(ctx.box.height));

    // Children take their natural extent along the scroll axis: a fraction of an unbounded axis means
    // nothing, which is the whole reason this is not a stack.
    const children = node.children ?? [];
    const layOut = (inner: number | null) => {
      const entries: Array<{ child: typeof children[number]; box: { width: number | null; height: number | null }; crossExtent: number | null }> = [];
      for (let index = 0; index < children.length; index++) {
        const box = horizontal ? { width: null, height: inner } : { width: inner, height: null };
        entries.push({ child: children[index], box, crossExtent: inner });
      }
      ctx.syncChildren(element, entries);
    };

    const boxCross = horizontal ? ctx.box.height : ctx.box.width;
    const given = boxCross === null ? null : Math.max(0, boxCross - 2 * padding);
    layOut(given);

    const client = horizontal ? element.clientHeight : element.clientWidth;
    const available = client === 0 ? null : Math.max(0, client - 2 * padding);
    if (available !== null && (given === null || Math.abs(available - given) >= 0.5)) layOut(available);

    // Asked once after every paint as well as on scroll: a list whose content does not fill its box has
    // been read to the end the moment it is drawn, and nothing would ever scroll to say so.
    reportReveal(element, ctx);
  },

  release(ctx) {
    if (ctx.state.revealTimer !== null) {
      clearTimeout(ctx.state.revealTimer);
      ctx.state.revealTimer = null;
    }
  },

  intrinsicMainPx() {
    // None: a list's main axis is unbounded by definition, so it is only ever sized from outside - a
    // stack holding one gives it a `mainSize` or lets it fill.
    return 0;
  },
};
