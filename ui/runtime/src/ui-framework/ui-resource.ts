import { UiNode } from './ui-node.interface';
import { nodeRaw } from './node-properties.util';

export interface UiResource {
  resourceId: string;
  contentHash?: string;
  mediaType?: string;
  byteLength?: number;
}

export interface UiResourceHint {
  displayPx?: number;
  widgetId?: string;
}

const ICON_RESOURCE_PREFIXES = ['app.macro-deck.widget-icon.', 'app.macro-deck.plugin-icon.'];

export function isIconUiResource(resource: UiResource | undefined): boolean {
  return !!resource && ICON_RESOURCE_PREFIXES.some(prefix => resource.resourceId.startsWith(prefix));
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

export function uiResourceUrl(baseUrl: string, resource: UiResource | undefined, size?: number): string | null {
  if (!resource || !baseUrl) return null;

  const path = `${baseUrl}/api/ui/resources/${encodeURIComponent(resource.resourceId)}`;
  const query: string[] = [];
  if (resource.contentHash) query.push(`v=${encodeURIComponent(resource.contentHash)}`);
  if (size !== undefined) query.push(`size=${size}`);
  return query.length > 0 ? `${path}?${query.join('&')}` : path;
}
