import type { UiComponentContext } from '../ui-framework/component-registry';

export function supportsMasks(): boolean {
  return typeof CSS !== 'undefined' && typeof CSS.supports === 'function' &&
    (CSS.supports('mask-image', 'url("x")') || CSS.supports('-webkit-mask-image', 'url("x")'));
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
