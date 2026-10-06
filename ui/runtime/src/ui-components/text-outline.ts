import { SVG_NS } from './render-constants';

const FILTER_HOST_CLASS = 'widget-text-outline-filters';
const KEPT_FILTERS = 32;

// Mirrors the legibility text-shadow in renderer.css: an outlined run draws it inside its filter instead.
export const LEGIBILITY_SHADOW = { offsetY: 2, blur: 4, opacity: 0.5 } as const;

interface OutlineFilters {
  host: SVGSVGElement;
  filters: Map<string, Element>;
  sweepPending: boolean;
}

export function textOutlineFilterId(doc: Document, color: string, width: number, shadow: boolean): string {
  const rounded = Math.max(0.25, Math.round(width * 4) / 4);
  const id = `widget-text-outline-${color.slice(1).toLowerCase()}-${Math.round(rounded * 100)}-${shadow ? 's' : 'n'}`;
  const outlines = outlineFilters(doc);
  if (outlines.filters.has(id)) return id;

  const filter = outlineFilter(doc, id, color, rounded, shadow);
  outlines.host.appendChild(filter);
  outlines.filters.set(id, filter);
  if (outlines.filters.size > KEPT_FILTERS) scheduleSweep(doc, outlines);
  return id;
}

// Swept a task later so a run painted before its element was attached is already in the document by then;
// a run whose filter was swept regains it on its next paint.
function scheduleSweep(doc: Document, outlines: OutlineFilters): void {
  const view = doc.defaultView;
  if (outlines.sweepPending || view === null) return;
  outlines.sweepPending = true;
  view.setTimeout(() => {
    outlines.sweepPending = false;
    const used: { [id: string]: true } = {};
    const texts = doc.querySelectorAll('.widget-text-outlined');
    for (let index = 0; index < texts.length; index++) {
      const id = /#([\w-]+)/.exec((texts[index] as HTMLElement).style.filter)?.[1];
      if (id !== undefined) used[id] = true;
    }
    outlines.filters.forEach((filter, id) => {
      if (used[id]) return;
      filter.parentNode?.removeChild(filter);
      outlines.filters.delete(id);
    });
  }, 0);
}

function outlineFilters(doc: Document): OutlineFilters {
  const withFilters = doc as unknown as { __mdTextOutlineFilters?: OutlineFilters };
  const known = withFilters.__mdTextOutlineFilters;
  if (known !== undefined && doc.documentElement.contains(known.host)) return known;

  const host = doc.createElementNS(SVG_NS, 'svg') as SVGSVGElement;
  host.setAttribute('class', FILTER_HOST_CLASS);
  host.setAttribute('aria-hidden', 'true');
  host.setAttribute('width', '0');
  host.setAttribute('height', '0');
  host.style.position = 'absolute';
  (doc.body ?? doc.documentElement).appendChild(host);

  const created = { host, filters: new Map<string, Element>(), sweepPending: false };
  withFilters.__mdTextOutlineFilters = created;
  return created;
}

function outlineFilter(doc: Document, id: string, color: string, width: number, shadow: boolean): SVGFilterElement {
  const filter = svg(doc, 'filter', {
    id,
    x: '-25%',
    y: '-25%',
    width: '150%',
    height: '160%',
    'color-interpolation-filters': 'sRGB',
  }) as SVGFilterElement;

  // A dilation's kernel is square and a blur alone thins fine strokes; a short dilation keeps thin strokes,
  // and a blur under a hard alpha threshold carries the outline the rest of the way with round corners.
  filter.appendChild(svg(doc, 'feMorphology', { in: 'SourceAlpha', operator: 'dilate', radius: fixed(width * 0.35) }));
  filter.appendChild(svg(doc, 'feGaussianBlur', { stdDeviation: fixed(width * 0.45) }));
  const threshold = svg(doc, 'feComponentTransfer', { result: 'outlineAlpha' });
  threshold.appendChild(svg(doc, 'feFuncA', { type: 'linear', slope: '8', intercept: '-0.6' }));
  filter.appendChild(threshold);
  filter.appendChild(svg(doc, 'feFlood', { 'flood-color': color }));
  filter.appendChild(svg(doc, 'feComposite', { in2: 'outlineAlpha', operator: 'in', result: 'outline' }));
  filter.appendChild(merge(doc, ['outline', 'SourceGraphic'], shadow ? 'outlined' : null));

  if (shadow) {
    filter.appendChild(svg(doc, 'feGaussianBlur', { in: 'outlined', stdDeviation: fixed(LEGIBILITY_SHADOW.blur / 2) }));
    filter.appendChild(svg(doc, 'feOffset', { dy: fixed(LEGIBILITY_SHADOW.offsetY), result: 'shadowAlpha' }));
    filter.appendChild(svg(doc, 'feFlood', { 'flood-color': '#000000', 'flood-opacity': fixed(LEGIBILITY_SHADOW.opacity) }));
    filter.appendChild(svg(doc, 'feComposite', { in2: 'shadowAlpha', operator: 'in', result: 'shadow' }));
    filter.appendChild(merge(doc, ['shadow', 'outlined'], null));
  }
  return filter;
}

function merge(doc: Document, inputs: string[], result: string | null): Element {
  const element = svg(doc, 'feMerge', result === null ? {} : { result });
  for (const input of inputs) element.appendChild(svg(doc, 'feMergeNode', { in: input }));
  return element;
}

function svg(doc: Document, tag: string, attributes: { [name: string]: string }): Element {
  const element = doc.createElementNS(SVG_NS, tag);
  for (const name in attributes) element.setAttribute(name, attributes[name]);
  return element;
}

function fixed(value: number): string {
  return String(Math.round(value * 100) / 100);
}
