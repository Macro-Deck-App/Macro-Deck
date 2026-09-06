import { UiNode } from './ui-node.interface';
import { nodeRaw } from './node-properties.util';

export interface UiResource {
  resourceId: string;
  contentHash?: string;
  mediaType?: string;
  byteLength?: number;
}

export function nodeResource(
  node: UiNode | null | undefined,
  key: string,
): UiResource | undefined {
  const raw = nodeRaw(node, key);
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;

  const candidate = raw as { resourceId?: unknown; contentHash?: unknown };
  if (typeof candidate.resourceId !== 'string' || candidate.resourceId.length === 0) {
    return undefined;
  }

  return {
    resourceId: candidate.resourceId,
    contentHash: typeof candidate.contentHash === 'string' ? candidate.contentHash : undefined,
  };
}

export function uiResourceUrl(baseUrl: string, resource: UiResource | undefined): string | null {
  if (!resource || !baseUrl) return null;

  const path = `${baseUrl}/api/ui/resources/${encodeURIComponent(resource.resourceId)}`;
  return resource.contentHash ? `${path}?v=${encodeURIComponent(resource.contentHash)}` : path;
}
