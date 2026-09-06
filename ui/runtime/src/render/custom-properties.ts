// The styling fallback for engines with no CSS custom properties (issue #829). Sheets are authored
// with var(); the web client ships a copy with every token resolved for the compatibility floor
// - Safari 9, iOS 9, Chrome 30-48, Android 4 - which drop a declaration containing var() rather
// than ignoring the function. The handful of tokens the renderer writes per element and lets the
// cascade carry down cannot be resolved at build time, so they are supplied here instead.
export function supportsCustomProperties(): boolean {
  if (cached === null) cached = detectCustomProperties();
  return cached;
}

let cached: boolean | null = null;

export function setCustomPropertySupportForTesting(value: boolean | null): void {
  cached = value;
}

function detectCustomProperties(): boolean {
  const css = typeof CSS === 'undefined' ? undefined : CSS;
  return css !== undefined && typeof css.supports === 'function' && css.supports('--md-probe', '0');
}

export const SCALED_RADIUS_SELECTORS = ['.deck-grid-cell', '.deck-grid-tile'];
export const REFERENCE_RADIUS_SELECTORS = [
  '.deck-grid-tile-surface',
  '.widget-button.widget-node-root',
  '.widget-button.widget-tile-corner',
];

export function widgetRadiusFallbackCss(scope: string, radiusPx: number, scale: number): string {
  const rule = (selectors: readonly string[], radius: number) =>
    `${selectors.map((selector) => `${scope} ${selector}`).join(',')}{border-radius:${radius}px}`;
  return rule(SCALED_RADIUS_SELECTORS, radiusPx * scale) + rule(REFERENCE_RADIUS_SELECTORS, radiusPx);
}

export const TINTED_RING_STYLES = ['static', 'heartbeat', 'breathing', 'blink'];
