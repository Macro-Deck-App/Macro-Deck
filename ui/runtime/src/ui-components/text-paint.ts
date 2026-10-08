import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeBoolean, nodeNumber, nodeString } from '../ui-framework/node-properties.util';
import { LengthScope, lengthReference, nodeHexColor, nodeLength, resolveLength, withoutCross } from '../ui-framework/length';
import { UiComponentProperties } from './component-properties';
import {
  textAlign,
  textDigits,
  textFillColor,
  textFontWeight,
  textMaxLines,
  textWraps,
} from './style';
import type { UiComponentContext } from '../ui-framework/component-registry';
import { textFit } from '../render/text-fit';
import { px } from './px.util';
import { forgetTextSpans, paintTextSpans, textSpansSignature, UiTextSpanRun } from './text-spans';
import { textOutlineFilterId } from './text-outline';

const MAX_OUTLINE_OF_BASIS = 0.1;

export function paintTextCommon<TState>(
  node: UiNode,
  ctx: UiComponentContext<TState>,
  extraClass: string | null,
  text: string,
  spans: UiTextSpanRun[] | null = null,
): void {
  const element = ctx.element as HTMLElement;
  const maxLines = textMaxLines(node);
  const wraps = textWraps(node);
  const faceId = nodeString(node, UiComponentProperties.FontFace);
  const digits = textDigits(node);

  const outlineColor = nodeHexColor(node, UiComponentProperties.StrokeColor);
  const outlineLength = nodeLength(node, UiComponentProperties.StrokeWidth);
  const outlineWidth = resolveLength(outlineLength, withoutCross(ctx));
  const outline = outlineColor !== undefined && outlineWidth !== undefined && outlineWidth > 0 && isFinite(outlineWidth)
    ? Math.min(outlineWidth, lengthReference(outlineLength, ctx) * MAX_OUTLINE_OF_BASIS)
    : null;
  const shadowOff = nodeBoolean(node, UiComponentProperties.Shadow) === false;

  ctx.setClassName(element, extraClass === null ? 'widget-text' : `widget-text ${extraClass}`);
  ctx.setClass(element, 'widget-text-clamp', maxLines > 1);
  ctx.setClass(element, 'widget-text-tabular', digits !== null);
  ctx.setClass(element, 'widget-text-no-shadow', shadowOff);
  ctx.setClass(element, 'widget-text-outlined', outline !== null);

  ctx.setStyle(element, 'width', px(ctx.box.width === null ? null : ctx.box.width + 2 * (outline ?? 0)));
  ctx.setStyle(element, 'text-shadow', outline === null ? null : 'none');
  ctx.setStyle(element, 'filter', outline === null
    ? null
    : `url(#${textOutlineFilterId(element.ownerDocument, outlineColor!, outline, !shadowOff && ctx.insideButton())})`);
  ctx.setStyle(element, 'padding-left', outline === null ? null : px(outline));
  ctx.setStyle(element, 'padding-right', outline === null ? null : px(outline));
  ctx.setStyle(element, 'margin-left', outline === null ? null : px(-outline));
  ctx.setStyle(element, 'margin-right', outline === null ? null : px(-outline));
  ctx.setStyle(element, 'padding-top', outline === null ? null : `calc(0.2em + ${px(outline)})`);
  ctx.setStyle(element, 'padding-bottom', outline === null ? null : `calc(0.2em + ${px(outline)})`);
  ctx.setStyle(element, 'margin-top', outline === null ? null : `calc(-0.2em - ${px(outline)})`);
  ctx.setStyle(element, 'margin-bottom', outline === null ? null : `calc(-0.2em - ${px(outline)})`);
  ctx.setStyle(element, 'max-height', outline === null ? null : `calc(100% + 0.4em + ${px(2 * outline)})`);
  // A hugging parent is sized to the text alone, since the outline padding is cancelled by the margins.
  // Capping the padded box at 100% of that took the outline out of the text's own line and wrapped it.
  ctx.setStyle(element, 'max-width', outline === null ? null : `calc(100% + ${px(2 * outline)})`);
  ctx.setStyle(element, 'font-weight', String(textFontWeight(node)));
  ctx.setStyle(element, 'color', textFillColor(node));
  ctx.setStyle(element, 'text-align', textAlign(node));
  ctx.setStyle(element, 'font-family', faceId ? ctx.host.fontFamily(faceId) : null);
  ctx.setStyle(element, 'visibility', !faceId || ctx.host.fontReady(faceId) ? null : 'hidden');
  ctx.setStyle(element, 'white-space', wraps ? 'pre-wrap' : (maxLines <= 1 ? 'nowrap' : null));
  ctx.setStyle(element, 'overflow-wrap', wraps ? 'anywhere' : null);
  ctx.setStyle(element, 'text-overflow', !wraps && maxLines <= 1 ? 'ellipsis' : null);
  ctx.setStyle(element, '-webkit-line-clamp', maxLines > 1 ? String(maxLines) : null);
  ctx.setStyle(element, 'min-width', digits === null ? null : `${digits}ch`);

  let content = text;
  if (spans !== null) {
    paintTextSpans(element, spans, resource => ctx.host.resourceUrl(resource));
    content = textSpansSignature(spans);
  } else {
    // A final line break in pre-wrap text opens no line of its own, so a label's trailing blank line
    // would vanish. One more break keeps it, the way a native text view lays it out.
    const painted = wraps && text.endsWith('\n') ? `${text}\n` : text;
    if (forgetTextSpans(element) || element.textContent !== painted) element.textContent = painted;
  }

  const size = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx);
  const minSize = resolveLength(nodeLength(node, UiComponentProperties.MinSize), ctx);
  const fontReady = faceId ? ctx.host.fontReady(faceId) : true;
  const font = faceId ? `${faceId}|${fontReady}` : `|${ctx.host.uiFontKey?.() ?? ''}`;
  ctx.keepFit(element, textFit(element, size, minSize), `${size}|${minSize}|${content}|${font}`);
}

export function textIntrinsicMainPx(
  node: UiNode,
  m: LengthScope & { horizontal: boolean },
): number {
  const size = resolveLength(nodeLength(node, UiComponentProperties.Size), m);
  return m.horizontal ? 0 : (size ?? 0) * Math.max(1, nodeNumber(node, UiComponentProperties.MaxLines) ?? 1);
}
