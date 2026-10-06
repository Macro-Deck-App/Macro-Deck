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

export type IconColorScheme = 'light' | 'dark';
export type IconMotion = 'static' | 'animated';

export interface IconAppearanceContext {
  colorScheme: IconColorScheme;
  motion: IconMotion;
}

const ICON_RESOURCE_PREFIXES = ['app.macro-deck.widget-icon.', 'app.macro-deck.plugin-icon.'];
const APPEARANCES_SUFFIX = '.a';

export function isIconUiResource(resource: UiResource | undefined): boolean {
  return !!resource && ICON_RESOURCE_PREFIXES.some(prefix => resource.resourceId.startsWith(prefix));
}

export function hasIconAppearances(resource: UiResource | undefined): boolean {
  if (!resource || !isIconUiResource(resource)) return false;
  const id = resource.resourceId;
  return id.length > APPEARANCES_SUFFIX.length && id.slice(-APPEARANCES_SUFFIX.length) === APPEARANCES_SUFFIX;
}

export function iconAppearanceContext(colorScheme: IconColorScheme, reducedMotion: boolean): IconAppearanceContext {
  return { colorScheme, motion: reducedMotion ? 'static' : 'animated' };
}

export function iconAppearanceQuery(context: IconAppearanceContext): string {
  return `colorScheme=${encodeURIComponent(context.colorScheme)}&motion=${encodeURIComponent(context.motion)}`;
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

export function uiResourceUrl(
  baseUrl: string,
  resource: UiResource | undefined,
  size?: number,
  context?: IconAppearanceContext,
): string | null {
  if (!resource || !baseUrl) return null;

  const path = `${baseUrl}/api/ui/resources/${encodeURIComponent(resource.resourceId)}`;
  const query: string[] = [];
  if (resource.contentHash) query.push(`v=${encodeURIComponent(resource.contentHash)}`);
  if (size !== undefined) query.push(`size=${size}`);
  if (context !== undefined && hasIconAppearances(resource)) query.push(iconAppearanceQuery(context));
  return query.length > 0 ? `${path}?${query.join('&')}` : path;
}
