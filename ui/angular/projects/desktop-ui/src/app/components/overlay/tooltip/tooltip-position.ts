export type TooltipPlacement = 'right' | 'left';

export const TOOLTIP_GAP_PX = 8;

export const TOOLTIP_VIEWPORT_PADDING_PX = 8;

export interface TooltipAnchorRect {
  top: number;
  left: number;
  right: number;
  height: number;
}

export interface TooltipSize {
  width: number;
  height: number;
}

export interface TooltipViewport {
  width: number;
  height: number;
}

export function computeTooltipPosition(
  anchor: TooltipAnchorRect,
  size: TooltipSize,
  viewport: TooltipViewport,
  preferred: TooltipPlacement = 'right',
): { top: number; left: number; placement: TooltipPlacement } {
  const fitsRight = anchor.right + TOOLTIP_GAP_PX + size.width
    <= viewport.width - TOOLTIP_VIEWPORT_PADDING_PX;
  const fitsLeft = anchor.left - TOOLTIP_GAP_PX - size.width >= TOOLTIP_VIEWPORT_PADDING_PX;

  let placement = preferred;
  if (preferred === 'right' && !fitsRight && fitsLeft) {
    placement = 'left';
  } else if (preferred === 'left' && !fitsLeft && fitsRight) {
    placement = 'right';
  }

  const left = placement === 'right'
    ? anchor.right + TOOLTIP_GAP_PX
    : anchor.left - TOOLTIP_GAP_PX - size.width;
  const top = anchor.top + anchor.height / 2 - size.height / 2;

  return {
    top: clamp(top, TOOLTIP_VIEWPORT_PADDING_PX, viewport.height - TOOLTIP_VIEWPORT_PADDING_PX - size.height),
    left: clamp(left, TOOLTIP_VIEWPORT_PADDING_PX, viewport.width - TOOLTIP_VIEWPORT_PADDING_PX - size.width),
    placement,
  };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), Math.max(min, max));
}
