import { UiNode } from '../ui-framework/ui-node.interface';
import type { UiComponentContext } from '../ui-framework/component-registry';
import { arcMetrics, ArcMetrics, arcPath, arcSweep } from './arc';
import { nodeThicknessPx } from './style';
import { sliderLevelColor } from './bar';
import { SVG_NS } from './render-constants';

export function paintGaugeArc<TState>(node: UiNode, ctx: UiComponentContext<TState>, level: number): ArcMetrics {
  const element = ctx.element as SVGElement;
  const width = ctx.box.width ?? ctx.basis;
  const height = ctx.box.height ?? ctx.basis;
  ctx.setAttribute(element, 'width', String(width));
  ctx.setAttribute(element, 'height', String(height));

  const thickness = nodeThicknessPx(node, ctx.basis, ctx.crossExtent);
  const metrics = arcMetrics(width, height, thickness);
  const sweep = arcSweep(node);

  const track = ctx.part('gaugeTrack', 'path', SVG_NS);
  ctx.setClassName(track, 'widget-gauge-track');
  ctx.setAttribute(track, 'd', arcPath(metrics, sweep.start, sweep.sweep));
  ctx.setAttribute(track, 'stroke-width', String(thickness));

  if (level > 0) {
    const fill = ctx.part('gaugeFill', 'path', SVG_NS);
    ctx.setClassName(fill, 'widget-gauge-fill');
    ctx.setAttribute(fill, 'd', arcPath(metrics, sweep.start, sweep.sweep * level));
    ctx.setAttribute(fill, 'stroke-width', String(thickness));
    ctx.setStyle(fill, 'stroke', sliderLevelColor(node));
  } else {
    ctx.dropPart('gaugeFill');
  }

  return metrics;
}
