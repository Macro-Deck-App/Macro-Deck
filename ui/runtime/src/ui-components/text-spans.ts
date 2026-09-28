import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeRaw } from '../ui-framework/node-properties.util';
import { asHexColor } from '../ui-framework/length';
import { UiResource } from '../ui-framework/ui-resource';
import { UiComponentProperties } from './component-properties';
import { textWeightValue } from './style';
import { setImageSource } from './image-recovery';

export type UiTextSpanRun =
  | { kind: 'text'; text: string; color: string | undefined; weight: string | undefined }
  | { kind: 'image'; image: UiResource; alt: string | undefined };

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
    const span = entry as { text?: unknown; image?: unknown; alt?: unknown; color?: unknown; weight?: unknown };
    if (typeof span.text === 'string') {
      runs.push({ kind: 'text', text: span.text, color: asHexColor(span.color), weight: optionalString(span.weight) });
      continue;
    }
    const image = asResource(span.image);
    if (image !== undefined) runs.push({ kind: 'image', image, alt: optionalString(span.alt) });
  }
  return runs;
}

export function textSpansSignature(runs: UiTextSpanRun[]): string {
  return JSON.stringify(runs);
}

function textSpan(doc: Document, text: string, color?: string, weight?: string): HTMLElement {
  const span = doc.createElement('span');
  span.textContent = text;
  if (color !== undefined) span.style.setProperty('color', color);
  if (weight !== undefined) span.style.setProperty('font-weight', String(textWeightValue(weight)));
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
      element.appendChild(textSpan(doc, run.text, run.color, run.weight));
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
