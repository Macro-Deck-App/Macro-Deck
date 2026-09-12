import { emitsEvent, nodeBoolean, nodeNumber } from './node-properties.util';
import { UiNode } from './ui-node.interface';
import { UiComponentEvents } from '../ui-components/component-events';
import { UiComponents } from '../ui-components/ui-component-types';
import { UiComponentProperties } from '../ui-components/component-properties';
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

export interface UiNodeActivation {
  name: string;
  payload: unknown;
}

export function activationFor(node: UiNode): UiNodeActivation | null {
  if (!emitsEvent(node, UiComponentEvents.Change)) return null;
  if (node.type === UiComponents.Toggle) {
    return { name: UiComponentEvents.Change, payload: nodeBoolean(node, UiComponentProperties.On) !== true };
  }
  if (node.type === UiComponents.Segmented) {
    const count = (node.children ?? []).length;
    if (count === 0) return null;
    const selected = nodeNumber(node, UiComponentProperties.Selected);
    const current = selected !== undefined && selected >= 0 && selected < count ? Math.floor(selected) : -1;
    return { name: UiComponentEvents.Change, payload: (current + 1) % count };
  }
  return null;
}

export function findInteractiveNode(node: UiNode | null | undefined): UiNode | null {
  if (!node) return null;
  if (nodeClaimsGesture(node) || nodeClaimsValue(node)) return node;
  if (node.type === UiComponents.Segmented) return null;

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
