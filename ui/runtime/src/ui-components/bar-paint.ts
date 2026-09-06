import { UiNode } from '../ui-framework/ui-node.interface';
import {
  barEnd,
  barFillStyle,
  barHasMarker,
  barMarkerDiameterPx,
  barMarkerLeftPx,
  barMarkerStrokePx,
  barStart,
  progressFillStyle,
} from './bar';
import { nodeThicknessPx } from './style';
import { UiComponentProperties } from './component-properties';
import { nodeProgressRef, resolveProgressFraction } from '../macrodeck-components/progress';
import type { UiComponentContext } from '../ui-framework/component-registry';
import { px } from './px.util';

export function paintBarCommon<TState>(node: UiNode, ctx: UiComponentContext<TState>, isProgress: boolean): void {
  const element = ctx.element as HTMLElement;
  ctx.setClassName(element, 'widget-range-bar');
  ctx.sizeTo(element, ctx.box);

  if (!isProgress && ctx.host.simpleRendering()) {
    ctx.dropPart('barTrack');
    ctx.dropPart('barMarker');
    return;
  }

  const thickness = nodeThicknessPx(node, ctx.basis, ctx.crossExtent);
  const track = ctx.part('barTrack', 'div');
  ctx.setClassName(track, 'widget-range-bar-track');
  ctx.setStyle(track, 'height', px(thickness));
  ctx.setStyle(track, 'border-radius', px(thickness / 2));

  const fillStyle = isProgress ? progressFillStyle(node) : barFillStyle(node);
  if (fillStyle !== null) {
    const fill = ctx.part('barFill', 'div', undefined, track);
    ctx.setClassName(fill, isProgress ? 'widget-range-bar-fill widget-progress-fill' : 'widget-range-bar-fill');

    const reference = isProgress ? nodeProgressRef(node, UiComponentProperties.Value) : undefined;
    const left = isProgress ? 0 : barStart(node);
    const width = isProgress
      ? (reference ? resolveProgressFraction(reference, ctx.host.now()) : 0)
      : Math.max(0, barEnd(node) - barStart(node));
    ctx.setStyle(fill, 'left', `${left * 100}%`);
    ctx.setStyle(fill, 'width', `${width * 100}%`);
    ctx.setStyle(fill, 'border-radius', px(thickness / 2));
    ctx.setStyle(fill, 'background', fillStyle);
  } else {
    ctx.dropPart('barFill');
  }

  if (!isProgress && barHasMarker(node)) {
    const marker = ctx.part('barMarker', 'div');
    ctx.setClassName(marker, 'widget-range-bar-marker');
    const diameter = barMarkerDiameterPx(thickness);
    ctx.setStyle(marker, 'left', px(barMarkerLeftPx(node, ctx.box.width ?? 0, thickness)));
    ctx.setStyle(marker, 'width', px(diameter));
    ctx.setStyle(marker, 'height', px(diameter));
    ctx.setStyle(marker, 'border', `${barMarkerStrokePx(thickness)}px solid var(--color-bg-secondary)`);
    ctx.setStyle(marker, 'background', 'var(--color-text-primary)');
  } else {
    ctx.dropPart('barMarker');
  }
}
