import {
  ActionButtonData,
  DEFAULT_ICON_DISPLAY,
  ICON_DISPLAY_LIMITS,
  WidgetIconDisplay,
  WidgetIconFit,
} from './widget.interface';
import { WidgetIconRef, readWidgetIconRef } from './widget-icon-ref.util';

export function momentaryIcon(data: ActionButtonData): {
  icon: WidgetIconRef | undefined;
  iconDisplay: WidgetIconDisplay | undefined;
} {
  if (data.icon !== undefined || data.iconId !== undefined || data.iconDisplay !== undefined) {
    return { icon: readWidgetIconRef(data.icon, data.iconId), iconDisplay: data.iconDisplay };
  }
  const first = data.states?.[0]?.appearance;
  return { icon: readWidgetIconRef(first?.icon, first?.iconId), iconDisplay: first?.iconDisplay };
}

function clamp(value: number | undefined, fallback: number, min: number, max: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback;
}

export function resolveIconDisplay(display: WidgetIconDisplay | undefined): Required<WidgetIconDisplay> {
  const fit: WidgetIconFit = display?.fit === 'cover' ? 'cover' : DEFAULT_ICON_DISPLAY.fit;
  return {
    fit,
    zoom: clamp(display?.zoom, DEFAULT_ICON_DISPLAY.zoom, ICON_DISPLAY_LIMITS.zoom.min, ICON_DISPLAY_LIMITS.zoom.max),
    offsetX: clamp(display?.offsetX, DEFAULT_ICON_DISPLAY.offsetX,
      ICON_DISPLAY_LIMITS.offset.min, ICON_DISPLAY_LIMITS.offset.max),
    offsetY: clamp(display?.offsetY, DEFAULT_ICON_DISPLAY.offsetY,
      ICON_DISPLAY_LIMITS.offset.min, ICON_DISPLAY_LIMITS.offset.max),
    opacity: clamp(display?.opacity, DEFAULT_ICON_DISPLAY.opacity,
      ICON_DISPLAY_LIMITS.opacity.min, ICON_DISPLAY_LIMITS.opacity.max),
  };
}

export function iconDisplayStyle(display: WidgetIconDisplay | undefined): Record<string, string> {
  const { fit, zoom, offsetX, offsetY, opacity } = resolveIconDisplay(display);
  return {
    'object-fit': fit,
    'transform': `translate(${offsetX}%, ${offsetY}%) scale(${zoom / 100})`,
    'opacity': String(opacity / 100),
  };
}
