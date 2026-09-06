import { negotiateComponent, UiComponentRange } from './ui-capabilities';
import { UiNode } from './ui-node.interface';

export interface UnsupportedResolution {
  id: string;
  unsupportedType: string;
}

export type NodeResolution = UiNode | UnsupportedResolution;

export function isUnsupportedResolution(resolution: NodeResolution): resolution is UnsupportedResolution {
  return 'unsupportedType' in resolution;
}

export function resolveRenderableNode(
  node: UiNode,
  capabilities: Readonly<Record<string, UiComponentRange>>,
): NodeResolution {
  let current = node;
  for (;;) {
    if (negotiateComponent(current, capabilities)) return current;
    if (!current.fallback) return { id: current.id, unsupportedType: current.type };
    current = current.fallback;
  }
}
