import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeNumber, nodeString } from '../ui-framework/node-properties.util';
import { nodeLength, resolveLength } from '../ui-framework/length';
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

export function paintTextCommon<TState>(
  node: UiNode,
  ctx: UiComponentContext<TState>,
  extraClass: string | null,
  text: string,
): void {
  const element = ctx.element as HTMLElement;
  const maxLines = textMaxLines(node);
  const wraps = textWraps(node);
  const faceId = nodeString(node, UiComponentProperties.FontFace);
  const digits = textDigits(node);

  ctx.setClassName(element, extraClass === null ? 'widget-text' : `widget-text ${extraClass}`);
  ctx.setClass(element, 'widget-text-clamp', maxLines > 1);
  ctx.setClass(element, 'widget-text-tabular', digits !== null);

  ctx.setStyle(element, 'width', px(ctx.box.width));
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

  // A final line break in pre-wrap text opens no line of its own, so a label's trailing blank line
  // would vanish. One more break keeps it, the way a native text view lays it out.
  const painted = wraps && text.endsWith('\n') ? `${text}\n` : text;
  if (element.textContent !== painted) element.textContent = painted;

  const size = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx.basis, ctx.crossExtent);
  const minSize = resolveLength(nodeLength(node, UiComponentProperties.MinSize), ctx.basis, ctx.crossExtent);
  const fontReady = faceId ? ctx.host.fontReady(faceId) : true;
  ctx.keepFit(
    element, textFit(element, size, minSize), `${size}|${minSize}|${text}|${faceId ?? ''}|${fontReady}`);
}

export function textIntrinsicMainPx(
  node: UiNode,
  m: { basis: number; crossExtent: number | null; horizontal: boolean },
): number {
  const size = resolveLength(nodeLength(node, UiComponentProperties.Size), m.basis, m.crossExtent);
  return m.horizontal ? 0 : (size ?? 0) * Math.max(1, nodeNumber(node, UiComponentProperties.MaxLines) ?? 1);
}
