import {
  BORDER_ANIMATION_PERIOD_MS,
  resolveWidgetBorder,
  WIDGET_BORDER_WIDTH,
  WidgetBorder,
} from '../domain/widget.interface';
import { supportsCustomProperties, TINTED_RING_STYLES } from './custom-properties';

export interface WidgetBorderOptions {
  insideScaledContent?: boolean;
}

export interface WidgetBorderHandle {
  update(border: WidgetBorder | undefined): void;
  destroy(): void;
}

export function renderWidgetBorder(
  overlay: HTMLElement,
  now: () => number = () => Date.now(),
  options: WidgetBorderOptions = {},
): WidgetBorderHandle {
  // A fixed count of *screen* pixels, whatever the deck scale. A ring mounted inside the tile's
  // scaled content is authored in reference pixels, so it has to divide the scale back out - two
  // rings of the same configured width would otherwise be drawn at different thicknesses depending
  // only on where in the tile they happen to be mounted.
  overlay.style.setProperty('--wb-width', options.insideScaledContent === true
    ? `calc(${WIDGET_BORDER_WIDTH}px / var(--deck-scale, 1))`
    : `${WIDGET_BORDER_WIDTH}px`);

  let ring: HTMLElement | null = null;
  let painted: string | null = null;
  let anchoredOffset = 0;

  const OFFSET_EPSILON_MS = 50;

  return {
    update(border: WidgetBorder | undefined): void {
      const resolved = resolveWidgetBorder(border);
      const key = resolved === null ? null : `${resolved.style} ${resolved.color}`;
      const offset = now() - Date.now();
      if (key === painted && Math.abs(offset - anchoredOffset) < OFFSET_EPSILON_MS) return;

      painted = key;
      anchoredOffset = offset;
      if (ring !== null && ring.parentNode) ring.parentNode.removeChild(ring);
      ring = null;
      if (resolved === null) return;

      const element = document.createElement('div');
      element.className = `ring wb-${resolved.style}`;
      const phase = `${-((now() % BORDER_ANIMATION_PERIOD_MS) / 1000)}s`;
      element.style.setProperty('--wb-color', resolved.color);
      element.style.setProperty('--wb-phase', phase);
      if (!supportsCustomProperties()) {
        // The ring reads all three tokens off itself or its overlay, so unlike the widget radius
        // these need no rule of their own - the same values as inline styles do it. The width is
        // the uncounter-scaled one: the deck scale it would divide out is a token too, and a ring
        // one scale factor thick on an engine from 2015 beats no ring at all.
        element.style.padding = `${WIDGET_BORDER_WIDTH}px`;
        element.style.animationDelay = phase;
        element.style.setProperty('-webkit-animation-delay', phase);
        if (TINTED_RING_STYLES.indexOf(resolved.style) !== -1) element.style.background = resolved.color;
      }
      overlay.appendChild(element);
      ring = element;
    },

    destroy(): void {
      if (ring !== null && ring.parentNode) ring.parentNode.removeChild(ring);
      ring = null;
      painted = null;
      anchoredOffset = 0;
    },
  };
}
