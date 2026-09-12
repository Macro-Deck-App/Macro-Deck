import { emitsEvent, nodeRecord } from './node-properties.util';
import { UiNode } from './ui-node.interface';
import { UiComponentEvents } from '../ui-components/component-events';
import { UiComponentModifiers } from '../ui-components/component-modifiers';
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

export function nodeDeclaresGesture(node: UiNode): boolean {
  return emitsEvent(node, UiComponentEvents.Drag)
    || emitsEvent(node, UiComponentEvents.DragEnd)
    || emitsEvent(node, UiComponentEvents.Swipe)
    || emitsEvent(node, UiComponentEvents.Pinch)
    || emitsEvent(node, UiComponentEvents.PinchEnd);
}

export function nodeIsDisabledRegion(node: UiNode | null | undefined): boolean {
  return nodeRecord(node, UiComponentProperties.Modifiers)?.[UiComponentModifiers.Disabled] === true;
}

export function findInteractiveNode(node: UiNode | null | undefined): UiNode | null {
  if (!node) return null;
  if (nodeIsDisabledRegion(node) || nodeClaimsGesture(node) || nodeClaimsValue(node) || nodeDeclaresGesture(node)) {
    return node;
  }

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

export type UiActivationClaim = { node: UiNode } | 'absorbed' | 'none';

export function activationClaim(tree: UiNode | null | undefined): UiActivationClaim {
  let absorbed = false;

  function visit(node: UiNode): UiNode | null {
    if (nodeIsDisabledRegion(node)) {
      absorbed = true;
      return null;
    }
    if (nodeClaimsGesture(node) || nodeClaimsValue(node)) return node;

    const children = node.children ?? [];
    for (let index = 0; index < children.length; index++) {
      const found = visit(children[index]);
      if (found !== null) return found;
    }
    return null;
  }

  const found = tree ? visit(tree) : null;
  if (found !== null) return { node: found };
  return absorbed ? 'absorbed' : 'none';
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
