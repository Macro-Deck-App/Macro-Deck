import { naturalTextWidth } from './text-fit';

// A box overflow this small is sub-pixel rounding of integer DOM metrics, not a cut-off letter.
const CONTENT_FIT_TOLERANCE_PX = 0.5;

// A text is ellipsized on any overflow, so its width gets only float noise (a layout unit is 1/64 px).
const TEXT_WIDTH_TOLERANCE_PX = 0.01;

function computedWidth(element: HTMLElement): number {
  const view = element.ownerDocument.defaultView;
  const width = view === null ? Number.NaN : parseFloat(view.getComputedStyle(element).width);
  return Number.isFinite(width) ? width : element.clientWidth;
}

function textFits(text: HTMLElement): boolean {
  if (text.scrollHeight > text.clientHeight + CONTENT_FIT_TOLERANCE_PX) return false;

  const view = text.ownerDocument.defaultView;
  const singleLine = view !== null && view.getComputedStyle(text).whiteSpace === 'nowrap';
  if (!singleLine) return true;

  const natural = naturalTextWidth(text);
  return natural === null || natural <= computedWidth(text) + TEXT_WIDTH_TOLERANCE_PX;
}

// A text box may reach 0.4em past its parent so descenders are not clipped (renderer.css), so a layout
// is only too tall when it overflows by more than that.
const TEXT_DESCENDER_EM = 0.4;

function descenderSlackPx(texts: NodeListOf<HTMLElement>): number {
  let slack = 0;
  for (let index = 0; index < texts.length; index++) {
    const view = texts[index].ownerDocument.defaultView;
    const size = view === null ? Number.NaN : parseFloat(view.getComputedStyle(texts[index]).fontSize);
    if (Number.isFinite(size)) slack = Math.max(slack, size * TEXT_DESCENDER_EM);
  }
  return slack;
}

export function contentFits(root: HTMLElement): boolean {
  const texts = root.querySelectorAll<HTMLElement>('.widget-text');
  if (root.scrollWidth > root.clientWidth + CONTENT_FIT_TOLERANCE_PX
    || root.scrollHeight > root.clientHeight + CONTENT_FIT_TOLERANCE_PX + descenderSlackPx(texts)) {
    return false;
  }

  for (let index = 0; index < texts.length; index++) {
    if (!textFits(texts[index])) return false;
  }
  return root.classList.contains('widget-text') ? textFits(root) : true;
}
