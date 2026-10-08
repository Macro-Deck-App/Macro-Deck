import { textDirection } from '@macro-deck/runtime';

export function applyDocumentLanguage(root: HTMLElement, servedCulture: string): void {
  root.lang = servedCulture;
  root.dir = textDirection(servedCulture);
}
