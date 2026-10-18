import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeRaw } from '../ui-framework/node-properties.util';
import { asHexColor } from '../ui-framework/length';
import { UiResource } from '../ui-framework/ui-resource';
import { UiComponentProperties } from './component-properties';
import { textWeightValue } from './style';
import { setImageSource } from './image-recovery';

export interface UiTextSpanStyling {
  italic?: true;
  underline?: true;
  strikethrough?: true;
  monospace?: true;
  background?: string;
}

export type UiTextSpanRun =
  | ({ kind: 'text'; text: string; color: string | undefined; weight: string | undefined } & UiTextSpanStyling)
  | { kind: 'image'; image: UiResource; alt: string | undefined };

type SpanStyleSource = UiTextSpanStyling & { color?: string; weight?: string };

const SpanFontMono = 'var(--font-mono, ui-monospace, Menlo, Consolas, monospace)';

interface SpanCarrier extends HTMLElement {
  __mdSpansSignature?: string;
}

function asResource(raw: unknown): UiResource | undefined {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;
  const candidate = raw as { resourceId?: unknown; contentHash?: unknown };
  if (typeof candidate.resourceId !== 'string' || candidate.resourceId.length === 0) return undefined;
  return {
    resourceId: candidate.resourceId,
    contentHash: typeof candidate.contentHash === 'string' ? candidate.contentHash : undefined,
  };
}

function optionalString(raw: unknown): string | undefined {
  return typeof raw === 'string' && raw.length > 0 ? raw : undefined;
}

export function nodeTextSpans(node: UiNode): UiTextSpanRun[] | null {
  const raw = nodeRaw(node, UiComponentProperties.Spans);
  if (!Array.isArray(raw)) return null;

  const runs: UiTextSpanRun[] = [];
  for (const entry of raw) {
    if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) continue;
    const span = entry as Record<string, unknown>;
    if (typeof span.text === 'string') {
      runs.push({
        kind: 'text',
        text: span.text,
        color: asHexColor(span.color),
        weight: optionalString(span.weight),
        ...spanStyling(span),
      });
      continue;
    }
    const image = asResource(span.image);
    if (image !== undefined) runs.push({ kind: 'image', image, alt: optionalString(span.alt) });
  }
  return runs;
}

function spanStyling(span: Record<string, unknown>): UiTextSpanStyling {
  const styling: UiTextSpanStyling = {};
  if (span.italic === true) styling.italic = true;
  if (span.underline === true) styling.underline = true;
  if (span.strikethrough === true) styling.strikethrough = true;
  if (span.monospace === true) styling.monospace = true;
  const background = asHexColor(span.background);
  if (background !== undefined) styling.background = background;
  return styling;
}

export function textSpanStyle(run: SpanStyleSource): Record<string, string> {
  const style: Record<string, string> = {};
  if (run.color !== undefined) style['color'] = run.color;
  if (run.weight !== undefined) style['font-weight'] = String(textWeightValue(run.weight));
  if (run.italic) style['font-style'] = 'italic';
  const lines = [run.underline ? 'underline' : '', run.strikethrough ? 'line-through' : ''].filter(Boolean);
  if (lines.length > 0) style['text-decoration'] = lines.join(' ');
  if (run.monospace) style['font-family'] = SpanFontMono;
  if (run.background !== undefined) {
    style['background-color'] = run.background;
    style['padding'] = '0 0.2em';
    style['border-radius'] = '0.25em';
    style['box-decoration-break'] = 'clone';
  }
  return style;
}

export function textSpansSignature(runs: UiTextSpanRun[]): string {
  return JSON.stringify(runs);
}

function textSpan(doc: Document, text: string, styling: SpanStyleSource = {}): HTMLElement {
  const span = doc.createElement('span');
  span.textContent = text;
  const style = textSpanStyle(styling);
  for (const property of Object.keys(style)) span.style.setProperty(property, style[property]);
  return span;
}

// The alt text only exists while the image is unavailable, so a text fit measuring the element's text
// never counts a caption nobody sees.
function imageSpan(doc: Document, src: string, alt: string | undefined): HTMLImageElement {
  const image = doc.createElement('img');
  image.setAttribute('class', 'widget-text-span-image');
  image.setAttribute('alt', alt ?? '');
  image.setAttribute('draggable', 'false');
  let caption: HTMLElement | null = null;
  image.addEventListener('error', () => {
    image.style.setProperty('display', 'none');
    if (alt === undefined || caption !== null || image.parentNode === null) return;
    caption = textSpan(doc, alt);
    caption.setAttribute('class', 'widget-text-span-alt');
    image.parentNode.insertBefore(caption, image.nextSibling);
  });
  image.addEventListener('load', () => {
    image.style.setProperty('display', '');
    if (caption === null) return;
    if (caption.parentNode) caption.parentNode.removeChild(caption);
    caption = null;
  });
  setImageSource(image, src);
  return image;
}

export function paintTextSpans(
  element: HTMLElement,
  runs: UiTextSpanRun[],
  resourceUrl: (resource: UiResource) => string | null,
): void {
  const carrier = element as SpanCarrier;
  const sources = runs.map(run => (run.kind === 'image' ? resourceUrl(run.image) : null));
  const signature = `${textSpansSignature(runs)}|${JSON.stringify(sources)}`;
  if (carrier.__mdSpansSignature === signature) return;
  carrier.__mdSpansSignature = signature;

  while (element.firstChild) element.removeChild(element.firstChild);
  const doc = element.ownerDocument;
  runs.forEach((run, index) => {
    if (run.kind === 'text') {
      element.appendChild(textSpan(doc, run.text, run));
      return;
    }
    const src = sources[index];
    if (src !== null) element.appendChild(imageSpan(doc, src, run.alt));
    else if (run.alt !== undefined) element.appendChild(textSpan(doc, run.alt));
  });
}

export function forgetTextSpans(element: HTMLElement): boolean {
  const carrier = element as SpanCarrier;
  if (carrier.__mdSpansSignature === undefined) return false;
  delete carrier.__mdSpansSignature;
  return true;
}
