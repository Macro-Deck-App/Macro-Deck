import { emitsEvent } from './node-properties.util';
import { UiNode } from './ui-node.interface';
import { UiComponentEvents } from '../ui-components/component-events';
import type { UiComponentRegistry } from './component-registry';

export function nodeClaimsGesture(node: UiNode): boolean {
  return emitsEvent(node, UiComponentEvents.Press)
    || emitsEvent(node, UiComponentEvents.LongPress)
    || emitsEvent(node, UiComponentEvents.PressStart)
    || emitsEvent(node, UiComponentEvents.PressEnd);
}

export function nodeClaimsValue(node: UiNode): boolean {
  return emitsEvent(node, UiComponentEvents.Adjust) || emitsEvent(node, UiComponentEvents.Change);
}

export function findInteractiveNode(node: UiNode | null | undefined): UiNode | null {
  if (!node) return null;
  if (nodeClaimsGesture(node) || nodeClaimsValue(node)) return node;

  const children = node.children ?? [];
  for (let index = 0; index < children.length; index++) {
    const found = findInteractiveNode(children[index]);
    if (found !== null) return found;
  }
  return null;
}

export function treeClaimsGesture(node: UiNode | null | undefined): boolean {
  return findInteractiveNode(node) !== null;
}

export function containsTickingNode(node: UiNode | null | undefined, registry: UiComponentRegistry): boolean {
  if (!node) return false;

  const definition = registry.get(node.type);
  if (definition?.tickPeriodMs && definition.tickPeriodMs(node) !== null) return true;

  const children = node.children ?? [];
  for (let index = 0; index < children.length; index++) {
    if (containsTickingNode(children[index], registry)) return true;
  }
  return false;
}
