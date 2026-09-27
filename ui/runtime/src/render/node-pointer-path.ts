import type { UiComponentContext } from '../ui-framework/component-registry';
import { UiNode } from '../ui-framework/ui-node.interface';

function collect(node: UiNode, into: { [id: string]: UiNode }): void {
  into[node.id] = node;
  const children = node.children ?? [];
  for (let index = 0; index < children.length; index++) collect(children[index], into);
  if (node.fallback) collect(node.fallback, into);
}

export function descendantsOnPath(element: Element, root: UiNode, target: EventTarget | null): UiNode[] {
  const byId: { [id: string]: UiNode } = {};
  collect(root, byId);
  const found: UiNode[] = [];
  let current = target as Element | null;
  while (current !== null && current !== element) {
    const id = current.getAttribute ? current.getAttribute('data-node-id') : null;
    const node = id === null ? undefined : byId[id];
    if (node !== undefined) found.push(node);
    current = current.parentNode as Element | null;
  }
  return found;
}

export function measureBasisUnit(element: Element, ctx: UiComponentContext<unknown>): number {
  const rect = element.getBoundingClientRect();
  const boxWidth = ctx.box.width;
  const ratio = rect.width > 0 && boxWidth ? rect.width / boxWidth : 1;
  return ctx.basis * ratio > 0 ? ctx.basis * ratio : 1;
}

export function capturePointer(element: Element, pointerId: number): void {
  try {
    (element as HTMLElement).setPointerCapture(pointerId);
  } catch {
    // A synthetic pointer has no OS session to capture; the capture-phase listeners still see it.
  }
}
