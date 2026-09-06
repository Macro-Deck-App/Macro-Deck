import { UI_CONFIG_PRIMITIVES_WELL_KNOWN } from '../ui-config/config-primitives';
import { UiNode } from './ui-node.interface';

export interface UiComponentRange {
  minimum: number;
  maximum: number;
}

export const UI_CONFIG_RENDER_CAPABILITIES: Readonly<Record<string, UiComponentRange>> = Object.freeze(
  Object.fromEntries(
    UI_CONFIG_PRIMITIVES_WELL_KNOWN.map(type => [type, { minimum: 1, maximum: 1 }]),
  ),
);

export function negotiateComponent(
  node: UiNode,
  capabilities: Readonly<Record<string, UiComponentRange>>,
): boolean {
  const required = node.requiredComponentVersion ?? 1;
  const range = capabilities[node.type];
  if (!range) return false;
  return required >= range.minimum && required <= range.maximum;
}

export function isComponentProfileType(type: string): boolean {
  return type.startsWith('ui.') || type.startsWith('macrodeck.');
}
