import { TOOLTIP_GAP_PX, TOOLTIP_VIEWPORT_PADDING_PX, TooltipAnchorRect, TooltipSize, TooltipViewport, computeTooltipPosition } from './tooltip-position';

describe('computeTooltipPosition', () => {
  const viewport: TooltipViewport = { width: 1000, height: 800 };

  it('places the tooltip to the right of the anchor with the standard gap', () => {
    const anchor: TooltipAnchorRect = { top: 100, left: 200, right: 250, height: 20 };
    const size: TooltipSize = { width: 80, height: 24 };

    const result = computeTooltipPosition(anchor, size, viewport);

    expect(result.placement).toBe('right');
    expect(result.left).toBe(anchor.right + TOOLTIP_GAP_PX);
  });

  it('vertically centres the tooltip on the anchor', () => {
    const anchor: TooltipAnchorRect = { top: 100, left: 200, right: 250, height: 20 };
    const size: TooltipSize = { width: 80, height: 24 };

    const result = computeTooltipPosition(anchor, size, viewport);

    expect(result.top).toBe(anchor.top + anchor.height / 2 - size.height / 2);
  });

  it('flips to the left when the right side would overflow the viewport', () => {
    const anchor: TooltipAnchorRect = { top: 100, left: 900, right: 950, height: 20 };
    const size: TooltipSize = { width: 100, height: 24 };

    const result = computeTooltipPosition(anchor, size, viewport);

    expect(result.placement).toBe('left');
    expect(result.left).toBe(anchor.left - TOOLTIP_GAP_PX - size.width);
  });

  it('stays on the preferred side, clamped inside the padding, when neither side fits', () => {
    const narrowViewport: TooltipViewport = { width: 300, height: 800 };
    const anchor: TooltipAnchorRect = { top: 50, left: 100, right: 200, height: 20 };
    const size: TooltipSize = { width: 250, height: 24 };

    const result = computeTooltipPosition(anchor, size, narrowViewport);

    expect(result.placement).toBe('right');
    expect(result.left).toBe(narrowViewport.width - TOOLTIP_VIEWPORT_PADDING_PX - size.width);
  });

  it('flips back to the right for a left-preferred tooltip with no room on the left', () => {
    const anchor: TooltipAnchorRect = { top: 50, left: 20, right: 60, height: 20 };
    const size: TooltipSize = { width: 100, height: 24 };

    const result = computeTooltipPosition(anchor, size, viewport, 'left');

    expect(result.placement).toBe('right');
    expect(result.left).toBe(anchor.right + TOOLTIP_GAP_PX);
  });

  it('clamps the top edge to the viewport padding for an anchor at the very top', () => {
    const anchor: TooltipAnchorRect = { top: 0, left: 100, right: 150, height: 20 };
    const size: TooltipSize = { width: 80, height: 100 };

    const result = computeTooltipPosition(anchor, size, viewport);

    expect(result.top).toBe(TOOLTIP_VIEWPORT_PADDING_PX);
  });

  it('clamps the bottom edge for an anchor at the very bottom', () => {
    const anchor: TooltipAnchorRect = { top: 780, left: 100, right: 150, height: 20 };
    const size: TooltipSize = { width: 80, height: 100 };

    const result = computeTooltipPosition(anchor, size, viewport);

    expect(result.top).toBe(viewport.height - TOOLTIP_VIEWPORT_PADDING_PX - size.height);
  });

  it('pins a tooltip taller than the viewport at the padding instead of inverting the clamp', () => {
    const anchor: TooltipAnchorRect = { top: 300, left: 100, right: 150, height: 20 };
    const size: TooltipSize = { width: 80, height: 1000 };

    const result = computeTooltipPosition(anchor, size, viewport);

    expect(result.top).toBe(TOOLTIP_VIEWPORT_PADDING_PX);
  });

  it('never lets a wide tooltip run past the right viewport padding', () => {
    const narrowViewport: TooltipViewport = { width: 600, height: 800 };
    const anchor: TooltipAnchorRect = { top: 50, left: 550, right: 590, height: 20 };
    const size: TooltipSize = { width: 550, height: 24 };

    const result = computeTooltipPosition(anchor, size, narrowViewport);

    expect(result.left + size.width).toBeLessThanOrEqual(narrowViewport.width - TOOLTIP_VIEWPORT_PADDING_PX);
  });
});
