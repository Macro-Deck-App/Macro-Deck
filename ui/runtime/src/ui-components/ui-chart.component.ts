import { UiComponents } from './ui-component-types';
import { chartPaths } from './chart';
import { chartColor, chartPlotTop, chartPoints, nodeThicknessPx } from './style';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { SVG_NS } from './render-constants';

export const uiChartComponent: UiComponentDefinition = {
  type: UiComponents.Chart,

  create(doc: Document) {
    return doc.createElementNS(SVG_NS, 'svg') as unknown as SVGElement;
  },

  paint(node, ctx) {
    const element = ctx.element as SVGElement;
    const width = ctx.box.width ?? ctx.basis;
    const height = ctx.box.height ?? ctx.basis;

    ctx.setClassName(element, 'widget-chart');
    ctx.setAttribute(element, 'width', String(width));
    ctx.setAttribute(element, 'height', String(height));
    ctx.setAttribute(element, 'aria-hidden', 'true');

    const paths = ctx.host.simpleRendering()
      ? null
      : chartPaths(chartPoints(node), width, height, chartPlotTop(node));
    if (paths === null) {
      ctx.dropPart('chartArea');
      ctx.dropPart('chartLine');
      return;
    }

    const colour = chartColor(node);
    const area = ctx.part('chartArea', 'path', SVG_NS);
    ctx.setClassName(area, 'widget-chart-area');
    ctx.setAttribute(area, 'd', paths.area);
    ctx.setStyle(area, 'fill', colour);

    const line = ctx.part('chartLine', 'path', SVG_NS);
    ctx.setClassName(line, 'widget-chart-line');
    ctx.setAttribute(line, 'd', paths.line);
    ctx.setAttribute(line, 'stroke-width', String(nodeThicknessPx(node, ctx.basis, ctx.crossExtent)));
    ctx.setStyle(line, 'stroke', colour);
  },
};
