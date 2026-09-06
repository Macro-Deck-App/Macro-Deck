import { UiNode } from './ui-node.interface';

export function rootNodeOf(payload: unknown): UiNode | null {
  if (typeof payload !== 'object' || payload === null) return null;

  const candidate = payload as { root?: unknown };
  const node = (typeof candidate.root === 'object' && candidate.root !== null
    ? candidate.root
    : candidate) as { type?: unknown; id?: unknown };

  return typeof node.type === 'string' && typeof node.id === 'string' ? (node as UiNode) : null;
}
