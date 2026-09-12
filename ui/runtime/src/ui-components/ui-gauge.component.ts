import { UiComponents } from './ui-component-types';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { sliderLevel } from './bar';
import { paintGaugeArc } from './gauge-paint';
import { SVG_NS } from './render-constants';

export const uiGaugeComponent: UiComponentDefinition = {
  type: UiComponents.Gauge,

  create(doc: Document) {
    return doc.createElementNS(SVG_NS, 'svg') as unknown as SVGElement;
  },

  paint(node, ctx) {
    ctx.setClassName(ctx.element, 'widget-gauge');
    paintGaugeArc(node, ctx, sliderLevel(node));
  },
};
