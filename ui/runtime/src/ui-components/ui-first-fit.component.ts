import { UiComponents } from './ui-component-types';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { contentFits } from '../render/content-fit';

const HIDDEN_ATTRIBUTE = 'data-first-fit-hidden';
const LAST_ATTRIBUTE = 'data-first-fit-last';

export interface UiFirstFitState {
  chosen: number;
  stop: (() => void) | null;
  observer: ResizeObserver | null;
}

function candidates(element: Element): HTMLElement[] {
  return (Array.from(element.children) as HTMLElement[]).filter(child => child.hasAttribute('data-node-id'));
}

// Written straight to the elements, not through ctx, so showing another candidate never marks the
// text-fit scope dirty and cannot start another settle.
function show(element: Element, chosen: number): void {
  const all = candidates(element);
  const index = chosen < 0 || chosen >= all.length ? all.length - 1 : chosen;
  for (let at = 0; at < all.length; at++) {
    if (at === all.length - 1) all[at].setAttribute(LAST_ATTRIBUTE, '');
    else all[at].removeAttribute(LAST_ATTRIBUTE);
    if (at === index) {
      all[at].removeAttribute(HIDDEN_ATTRIBUTE);
      all[at].removeAttribute('aria-hidden');
    } else {
      all[at].setAttribute(HIDDEN_ATTRIBUTE, '');
      all[at].setAttribute('aria-hidden', 'true');
    }
  }
}

function choose(ctx: UiComponentContext<UiFirstFitState>): void {
  const element = ctx.element;
  const all = candidates(element);
  let chosen = all.length - 1;
  for (let index = 0; index < all.length - 1; index++) {
    if (contentFits(all[index])) {
      chosen = index;
      break;
    }
  }
  ctx.state.chosen = chosen;
  show(element, chosen);
}

export const uiFirstFitComponent: UiComponentDefinition<UiFirstFitState> = {
  type: UiComponents.FirstFit,

  transparentRoot: true,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiFirstFitState {
    return { chosen: -1, stop: null, observer: null };
  },

  bind(ctx) {
    ctx.state.stop = ctx.onSettled(() => choose(ctx));
    if (typeof ResizeObserver !== 'undefined') {
      ctx.state.observer = new ResizeObserver(() => choose(ctx));
      ctx.state.observer.observe(ctx.element);
    }
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-first-fit');
    ctx.sizeTo(element, ctx.box);

    const entries = (node.children ?? []).map(child => ({ child, box: ctx.box, crossExtent: null }));
    ctx.syncChildren(element, entries);
    show(element, ctx.state.chosen);
  },

  release(ctx) {
    if (ctx.state.stop !== null) ctx.state.stop();
    if (ctx.state.observer !== null) ctx.state.observer.disconnect();
    ctx.state.stop = null;
    ctx.state.observer = null;
  },

  intrinsicMainPx(node, m) {
    const children = node.children ?? [];
    return children.length === 0 ? 0 : m.nested().ofChild(children[children.length - 1]);
  },
};
