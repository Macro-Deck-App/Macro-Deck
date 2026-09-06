import { Folder } from './folder.interface';
import { computeCellDimensions } from '../grid/grid-layout.util';
import { momentaryIcon, resolveIconDisplay } from './icon-display.util';
import { ActionButtonData, GridWidget, SliderData, WidgetType } from './widget.interface';
import { iconPackReferenceOf, readWidgetIconRef } from './widget-icon-ref.util';

export interface IconPrefetchTarget {
  iconId: string;
  size: number;
}

export function iconSizeBucket(px: number): number {
  if (px <= 128) {
    return 128;
  }
  return px <= 256 ? 256 : 512;
}

export function widgetIconZoomFactor(widget: GridWidget): number {
  if (widget.type !== WidgetType.ActionButton) {
    return 1;
  }
  const data = widget.data as ActionButtonData;
  // State-mode aware, like the rendered icon itself: a stateful button carrying a stray root
  // `iconDisplay` must not raise the bucket for a zoom the renderer never applies (only each
  // state's own icon is read in state mode).
  if (data.stateMode && data.states?.length) {
    return Math.max(
      ...data.states.map(state => resolveIconDisplay(state.appearance?.iconDisplay).zoom),
    ) / 100;
  }
  return resolveIconDisplay(momentaryIcon(data).iconDisplay).zoom / 100;
}

export interface IconPrefetchMetrics {
  excludeFolderId?: string | null;
  viewportWidth: number;
  viewportHeight: number;
  outerMargin: number;
  devicePixelRatio: number;
  resolveGrid: (folder: Folder) => { cols: number; rows: number; spacing: number };
}

export function collectIconPrefetchTargets(
  folders: readonly Folder[],
  metrics: IconPrefetchMetrics
): IconPrefetchTarget[] {
  const targets = new Map<string, IconPrefetchTarget>();
  const availableWidth = metrics.viewportWidth - 2 * metrics.outerMargin;
  const availableHeight = metrics.viewportHeight - 2 * metrics.outerMargin;

  if (availableWidth <= 0 || availableHeight <= 0) {
    return [];
  }

  for (const folder of folders) {
    if (folder.id === metrics.excludeFolderId) {
      continue;
    }

    const grid = metrics.resolveGrid(folder);
    const cell = computeCellDimensions(
      availableWidth,
      availableHeight,
      grid.cols,
      grid.rows,
      grid.spacing
    );

    for (const widget of folder.widgets) {
      const widthPx = widget.w * cell.cellWidth + (widget.w - 1) * cell.gap;
      const heightPx = widget.h * cell.cellHeight + (widget.h - 1) * cell.gap;
      const rendered = Math.max(widthPx, heightPx, 1) * metrics.devicePixelRatio;
      const size = iconSizeBucket(rendered * widgetIconZoomFactor(widget));

      for (const iconId of widgetIconIds(widget)) {
        targets.set(`${iconId}|${size}`, { iconId, size });
      }
    }
  }

  return [...targets.values()];
}

function widgetIconIds(widget: GridWidget): string[] {
  switch (widget.type) {
    case WidgetType.ActionButton: {
      const data = widget.data as ActionButtonData;
      if (data.stateMode && data.states?.length) {
        return data.states
          .map(state => iconPackReferenceOf(readWidgetIconRef(state.appearance?.icon, state.appearance?.iconId)))
          .filter((id): id is string => !!id);
      }
      const iconId = iconPackReferenceOf(momentaryIcon(data).icon);
      return iconId ? [iconId] : [];
    }
    case WidgetType.Slider: {
      const data = widget.data as SliderData;
      const iconId = iconPackReferenceOf(readWidgetIconRef(data.icon, data.iconId));
      return iconId ? [iconId] : [];
    }
    default:
      return [];
  }
}
