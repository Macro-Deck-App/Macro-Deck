import type { UiComponentContext } from '../ui-framework/component-registry';

const SVG_NAMESPACE = 'http://www.w3.org/2000/svg';
const FILTERS_ID = 'macro-deck-tint-filters';
const FILTER_ID_PREFIX = 'macro-deck-tint-';

export function supportsMasks(): boolean {
  return typeof CSS !== 'undefined' && typeof CSS.supports === 'function' &&
    (CSS.supports('mask-image', 'url("x")') || CSS.supports('-webkit-mask-image', 'url("x")'));
}

// Before filters applied to HTML, engines parsed filter for SVG content only, where a filter function is invalid.
export function supportsFilterTint(): boolean {
  return typeof CSS !== 'undefined' && typeof CSS.supports === 'function'
    && CSS.supports('filter', 'url(#a) brightness(1)');
}

export function repaintOnLoad<TState>(image: HTMLImageElement, ctx: UiComponentContext<TState>): void {
  const flagged = image as HTMLImageElement & { __mdTintRepaint?: boolean };
  if (flagged.__mdTintRepaint === true) return;
  flagged.__mdTintRepaint = true;
  image.addEventListener('load', () => ctx.repaint());
}

export function maskImageUrl(src: string): string {
  return `url("${src.replace(/["\\]/g, '\\$&')}")`;
}

export interface TintFilterHold {
  doc: Document | null;
  color: string | null;
}

export function createTintFilterHold(): TintFilterHold {
  return { doc: null, color: null };
}

const holders = new WeakMap<Document, Map<string, number>>();

function filterId(color: string): string {
  return FILTER_ID_PREFIX + color.replace(/^#/, '').toLowerCase();
}

function floodOf(color: string): { rgb: string; opacity: string } {
  const hex = color.replace(/^#/, '');
  const channel = (at: number) => parseInt(hex.slice(at, at + 2), 16);
  const alpha = hex.length === 8 ? channel(6) / 255 : 1;
  return { rgb: `rgb(${channel(0)}, ${channel(2)}, ${channel(4)})`, opacity: String(alpha) };
}

function filtersOf(doc: Document): Element {
  const existing = doc.getElementById(FILTERS_ID);
  if (existing !== null) return existing;
  const svg = doc.createElementNS(SVG_NAMESPACE, 'svg');
  svg.setAttribute('id', FILTERS_ID);
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');
  svg.setAttribute('width', '0');
  svg.setAttribute('height', '0');
  // Hidden by size rather than display:none, which stops several engines from resolving the filters.
  svg.setAttribute('style', 'position:absolute;width:0;height:0;overflow:hidden;pointer-events:none');
  (doc.body ?? doc.documentElement).appendChild(svg);
  return svg;
}

function acquire(doc: Document, color: string): void {
  let counts = holders.get(doc);
  if (counts === undefined) {
    counts = new Map();
    holders.set(doc, counts);
  }
  const count = counts.get(color) ?? 0;
  counts.set(color, count + 1);
  if (count > 0 && doc.getElementById(filterId(color)) !== null) return;

  const flood = floodOf(color);
  const filter = doc.createElementNS(SVG_NAMESPACE, 'filter');
  filter.setAttribute('id', filterId(color));
  filter.setAttribute('color-interpolation-filters', 'sRGB');
  const fill = doc.createElementNS(SVG_NAMESPACE, 'feFlood');
  fill.setAttribute('flood-color', flood.rgb);
  fill.setAttribute('flood-opacity', flood.opacity);
  const shape = doc.createElementNS(SVG_NAMESPACE, 'feComposite');
  shape.setAttribute('in2', 'SourceGraphic');
  shape.setAttribute('operator', 'in');
  filter.appendChild(fill);
  filter.appendChild(shape);
  filtersOf(doc).appendChild(filter);
}

function release(doc: Document, color: string): void {
  const counts = holders.get(doc);
  const count = counts?.get(color) ?? 0;
  if (counts === undefined || count === 0) return;
  if (count > 1) {
    counts.set(color, count - 1);
    return;
  }
  counts.delete(color);
  doc.getElementById(filterId(color))?.remove();
  const filters = doc.getElementById(FILTERS_ID);
  if (filters !== null && filters.childNodes.length === 0) filters.remove();
}

// On the image itself: a mask is a second image, which an engine may animate on a clock of its own.
export function holdTintFilter(hold: TintFilterHold, doc: Document, tint: string | null): string | null {
  const color = tint === null ? null : tint.toLowerCase();
  if (hold.color !== color || hold.doc !== doc) {
    releaseTintFilter(hold);
    if (color !== null) {
      acquire(doc, color);
      hold.doc = doc;
      hold.color = color;
    }
  }
  return color === null ? null : `url(#${filterId(color)})`;
}

export function releaseTintFilter(hold: TintFilterHold): void {
  if (hold.doc !== null && hold.color !== null) release(hold.doc, hold.color);
  hold.doc = null;
  hold.color = null;
}

export function withTintFilter(tintFilter: string | null, filter: string | null): string | null {
  return [tintFilter, filter].filter(part => part !== null && part !== '').join(' ') || null;
}
