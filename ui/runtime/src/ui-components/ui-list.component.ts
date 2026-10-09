import { emitsEvent } from '../ui-framework/node-properties.util';
import { UiComponents } from './ui-component-types';
import { UiComponentEvents } from './component-events';
import { listAnchorsEnd, nodeGapPx, nodeIsHorizontal, nodePaddingPx, stackBackground } from './style';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { px } from './px.util';
import { nodeModifierOwns } from '../render/node-modifiers';
import { ClientAppStrings } from '../localization/generated/client-app-strings';

const LIST_REVEAL_THROTTLE_MS = 500;

const LIST_END_TOLERANCE_PX = 8;

const JUMP_PART = 'jump';

export interface UiListState {
  revealed: number;
  revealedId: string | null;
  childCount: number;
  revealTimer: ReturnType<typeof setTimeout> | null;
  pinned: boolean;
  unseen: boolean;
  lastChildId: string | null;
  childIds: { [id: string]: boolean };
  measuredHeight: number;
  scrolledTo: number;
  gapPx: number;
  jump: HTMLElement | null;
  growth: ResizeObserver | null;
}

interface ScrollAnchor {
  scrollTop: number;
  order: string[];
  tops: { [id: string]: number };
  first: number;
}

function lastRevealedIndex(element: HTMLElement, horizontal: boolean, skip: Element | null): number {
  const end = horizontal ? element.scrollLeft + element.clientWidth : element.scrollTop + element.clientHeight;
  let last = -1;
  let index = 0;
  for (let at = 0; at < element.children.length; at++) {
    const child = element.children[at] as HTMLElement;
    if (child === skip) continue;
    if ((horizontal ? child.offsetLeft : child.offsetTop) <= end) last = index;
    index++;
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

  const index = lastRevealedIndex(element, horizontal, state.jump);
  if (index <= state.revealed) return;

  state.revealed = index;
  state.revealedId = node.children?.[index]?.id ?? null;
  ctx.emit(node, UiComponentEvents.Reveal, index);
  // Asked again when the throttle ends: a user already at the end of the list never scrolls again.
  state.revealTimer = setTimeout(() => {
    state.revealTimer = null;
    reportReveal(element, ctx);
  }, LIST_REVEAL_THROTTLE_MS);
}

function jumpExtent(state: UiListState): number {
  const jump = state.jump;
  if (jump === null || jump.style.display === 'none' || jump.offsetHeight <= 0) return 0;
  return jump.offsetHeight + state.gapPx;
}

function distanceToEnd(element: HTMLElement, state: UiListState): number {
  return element.scrollHeight - jumpExtent(state) - element.scrollTop - element.clientHeight;
}

function captureAnchor(element: HTMLElement, skip: Element | null): ScrollAnchor | null {
  const scrollTop = element.scrollTop;
  const order: string[] = [];
  const tops: { [id: string]: number } = {};
  let first = -1;
  for (let index = 0; index < element.children.length; index++) {
    const child = element.children[index] as HTMLElement;
    const id = child === skip ? null : child.getAttribute('data-node-id');
    if (id === null) continue;
    order.push(id);
    tops[id] = child.offsetTop;
    if (first < 0 && child.offsetTop + child.offsetHeight > scrollTop) first = order.length - 1;
  }
  return first < 0 ? null : { scrollTop, order, tops, first };
}

function restoreAnchor(element: HTMLElement, anchor: ScrollAnchor, skip: Element | null): void {
  const now: { [id: string]: HTMLElement } = {};
  for (let index = 0; index < element.children.length; index++) {
    const child = element.children[index] as HTMLElement;
    const id = child === skip ? null : child.getAttribute('data-node-id');
    if (id !== null) now[id] = child;
  }
  for (let index = anchor.first; index < anchor.order.length; index++) {
    const id = anchor.order[index];
    const survivor = now[id];
    if (survivor === undefined) continue;
    const target = survivor.offsetTop - (anchor.tops[id] - anchor.scrollTop);
    if (element.scrollTop !== target) element.scrollTop = target;
    return;
  }
}

function scrollToEnd(element: HTMLElement, state: UiListState): void {
  element.scrollTop = element.scrollHeight;
  state.scrolledTo = element.scrollTop;
}

// Rows grow after a paint while text fits and images decode, and no paint follows to catch up.
// Only a list the user has not moved since it was last put at the end is carried along.
function followGrowth(element: HTMLElement, ctx: UiComponentContext<UiListState>): void {
  const state = ctx.state;
  if (state.jump === null || !state.pinned || element.scrollTop !== state.scrolledTo) return;
  scrollToEnd(element, state);
}

function watchGrowth(element: HTMLElement, ctx: UiComponentContext<UiListState>): void {
  if (typeof ResizeObserver === 'undefined') return;
  const state = ctx.state;
  if (state.growth === null) state.growth = new ResizeObserver(() => followGrowth(element, ctx));
  state.growth.disconnect();
  state.growth.observe(element);
  for (let index = 0; index < element.children.length; index++) {
    const child = element.children[index];
    if (child !== state.jump) state.growth.observe(child);
  }
}

function stopWatchingGrowth(state: UiListState): void {
  if (state.growth === null) return;
  state.growth.disconnect();
  state.growth = null;
}

function jumpToEnd(element: HTMLElement, ctx: UiComponentContext<UiListState>): void {
  scrollToEnd(element, ctx.state);
  ctx.state.pinned = true;
  ctx.state.unseen = false;
  ctx.repaint();
}

function bindJump(jump: HTMLElement, element: HTMLElement, ctx: UiComponentContext<UiListState>): void {
  // The pill belongs to the reader, not the tree: a press on it must never reach a node or a tile
  // that answers presses around the list.
  for (const type of ['pointerdown', 'pointerup', 'pointercancel']) {
    jump.addEventListener(type, (event: Event) => event.stopPropagation());
  }
  jump.addEventListener('click', (event: Event) => {
    event.stopPropagation();
    jumpToEnd(element, ctx);
  });
}

function paintJump(element: HTMLElement, ctx: UiComponentContext<UiListState>): void {
  const state = ctx.state;
  const jump = ctx.part(JUMP_PART, 'button') as HTMLElement;
  if (state.jump !== jump) {
    state.jump = jump;
    bindJump(jump, element, ctx);
  }
  const qualified = ClientAppStrings.Ui.List.JumpToLatest;
  const separator = qualified.indexOf(':');
  const label = ctx.host.localization.translate(qualified.slice(0, separator), qualified.slice(separator + 1));
  ctx.setClassName(jump, 'widget-list-jump');
  ctx.setAttribute(jump, 'type', 'button');
  ctx.setAttribute(jump, 'aria-label', label);
  ctx.setStyle(jump, 'display', state.unseen && canStick() ? null : 'none');

  const icon = ctx.part('jump-icon', 'span', undefined, jump);
  ctx.setClassName(icon, 'widget-list-jump-icon icon icon-arrow-down');
  ctx.setAttribute(icon, 'aria-hidden', 'true');
  const text = ctx.part('jump-label', 'span', undefined, jump);
  ctx.setClassName(text, 'widget-list-jump-label');
  if (text.textContent !== label) text.textContent = label;
}

// Without sticky positioning the pill sits below the last row, out of view exactly while it is needed.
function canStick(): boolean {
  if (typeof CSS === 'undefined' || typeof CSS.supports !== 'function') return true;
  return CSS.supports('position', 'sticky') || CSS.supports('position', '-webkit-sticky');
}

function dropJump(ctx: UiComponentContext<UiListState>): void {
  const state = ctx.state;
  ctx.dropPart('jump-icon');
  ctx.dropPart('jump-label');
  ctx.dropPart(JUMP_PART);
  state.jump = null;
  state.pinned = true;
  state.unseen = false;
  state.lastChildId = null;
  state.childIds = {};
  state.measuredHeight = 0;
  state.scrolledTo = -1;
  stopWatchingGrowth(state);
}

export const uiListComponent: UiComponentDefinition<UiListState> = {
  type: UiComponents.List,
  // Version 2 adds the horizontal direction, which a version 1 reader would silently scroll vertically.
  // Version 3 adds the end anchor, which an older reader would ignore and leave a feed scrolled away.
  version: { minimum: 1, maximum: 3 },

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiListState {
    return {
      revealed: -1,
      revealedId: null,
      childCount: 0,
      revealTimer: null,
      pinned: true,
      unseen: false,
      lastChildId: null,
      childIds: {},
      measuredHeight: 0,
      scrolledTo: -1,
      gapPx: 0,
      jump: null,
      growth: null,
    };
  },

  bind(ctx) {
    const element = ctx.element as HTMLElement;
    element.addEventListener('scroll', () => {
      const state = ctx.state;
      // The echo of the list's own scroll says nothing about the user, and content that grew since
      // would read as the user having left the end.
      if (listAnchorsEnd(ctx.current()) && element.clientHeight > 0 && element.scrollTop !== state.scrolledTo) {
        state.pinned = distanceToEnd(element, state) <= LIST_END_TOLERANCE_PX;
        state.scrolledTo = element.scrollTop;
        if (state.pinned && state.unseen) {
          state.unseen = false;
          ctx.repaint();
        }
      }
      reportReveal(element, ctx);
    });
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    const state = ctx.state;
    const anchored = listAnchorsEnd(node);

    // Measured before anything this paint writes moves the content, and only when the user has moved
    // the view since the list last placed it: a resized box or grown rows are not the user leaving.
    let anchor: ScrollAnchor | null = null;
    if (anchored) {
      if (element.clientHeight > 0 && element.clientHeight === state.measuredHeight
        && element.scrollTop !== state.scrolledTo) {
        state.pinned = distanceToEnd(element, state) <= LIST_END_TOLERANCE_PX;
      }
      if (!state.pinned) anchor = captureAnchor(element, state.jump);
    }

    const padding = nodePaddingPx(node, ctx);
    const gap = nodeGapPx(node, ctx);

    const horizontal = nodeIsHorizontal(node);

    ctx.setClassName(element, 'widget-list');
    ctx.setClass(element, 'widget-list-horizontal', horizontal);
    ctx.setClass(element, 'widget-list-anchor-end', anchored);
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

    if (anchored) {
      const lastChildId = children.length > 0 ? children[children.length - 1].id : null;
      if (!state.pinned && lastChildId !== null && lastChildId !== state.lastChildId
        && !state.childIds[lastChildId]) state.unseen = true;
      state.lastChildId = lastChildId;
      const childIds: { [id: string]: boolean } = {};
      for (let index = 0; index < children.length; index++) childIds[children[index].id] = true;
      state.childIds = childIds;
      state.gapPx = gap;
      paintJump(element, ctx);

      if (state.pinned) scrollToEnd(element, state);
      else if (anchor !== null) restoreAnchor(element, anchor, state.jump);
      state.scrolledTo = element.scrollTop;
      state.measuredHeight = element.clientHeight;
      watchGrowth(element, ctx);
    } else if (state.jump !== null) {
      dropJump(ctx);
    }

    // Fewer children, or another child at the furthest index sent, means the content was replaced.
    if (children.length < state.childCount
      || (state.revealed >= 0 && children[state.revealed]?.id !== state.revealedId)) {
      state.revealed = -1;
      state.revealedId = null;
    }
    state.childCount = children.length;

    // Asked once after every paint as well as on scroll: a list whose content does not fill its box has
    // been read to the end the moment it is drawn, and nothing would ever scroll to say so.
    reportReveal(element, ctx);
  },

  release(ctx) {
    stopWatchingGrowth(ctx.state);
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
