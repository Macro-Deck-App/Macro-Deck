import { UiComponents } from './ui-component-types';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { responsiveChild } from '../ui-framework/responsive';

export const uiResponsiveComponent: UiComponentDefinition = {
  type: UiComponents.Responsive,

  transparentRoot: true,

  create(doc: Document) {
    return doc.createElement('div');
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-responsive');
    ctx.sizeTo(element, ctx.box);

    const chosen = responsiveChild(node, ctx.givenBox ?? null);
    ctx.syncChildren(element, chosen === null ? [] : [{ child: chosen, box: ctx.box, crossExtent: null }]);
  },

  intrinsicMainPx(node, m) {
    const cross = m.crossExtent;
    const chosen = responsiveChild(node, m.horizontal
      ? { width: null, height: cross }
      : { width: cross, height: null });
    return chosen === null ? 0 : m.ofChild(chosen);
  },
};
