import { LocalizationTranslator, LocalizedRef, asLocalizedRef } from '../localization/localized-text';
import { UiConfigProperties, UiNodeOption } from '../ui-config/config-properties';
import { UiNode } from './ui-node.interface';

export type { LocalizationTranslator, LocalizedRef };

export function nodeString(node: UiNode | null | undefined, key: string): string | undefined {
  const value = node?.properties?.[key];
  return typeof value === 'string' ? value : undefined;
}

export function nodeText(
  node: UiNode | null | undefined,
  key: string,
  localization: LocalizationTranslator,
): string | undefined {
  const value = node?.properties?.[key];
  if (typeof value === 'string') return value;

  const ref = asLocalizedRef(value);
  return ref ? localization.translate(ref.scope, ref.key, ref.arguments) : undefined;
}

export function nodeNumber(node: UiNode | null | undefined, key: string): number | undefined {
  const value = node?.properties?.[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

export function nodeBoolean(node: UiNode | null | undefined, key: string): boolean | undefined {
  const value = node?.properties?.[key];
  return typeof value === 'boolean' ? value : undefined;
}

export function nodeStringArray(node: UiNode | null | undefined, key: string): string[] | undefined {
  const value = node?.properties?.[key];
  if (!Array.isArray(value)) return undefined;
  return value.every(item => typeof item === 'string') ? (value as string[]) : undefined;
}

export function nodeRecord(
  node: UiNode | null | undefined,
  key: string,
): Record<string, unknown> | undefined {
  const value = node?.properties?.[key];
  return isPlainObject(value) ? value : undefined;
}

export function nodeRaw(node: UiNode | null | undefined, key: string): unknown {
  return node?.properties?.[key];
}

export function nodeOptions(
  node: UiNode | null | undefined,
  localization: LocalizationTranslator,
): UiNodeOption[] | undefined {
  const value = node?.properties?.[UiConfigProperties.Options];
  if (!Array.isArray(value)) return undefined;

  const options: UiNodeOption[] = [];
  for (const item of value) {
    if (!isPlainObject(item) || typeof item['value'] !== 'string') return undefined;
    const option: UiNodeOption = { value: item['value'] };
    const label = resolveOptionLabel(item['label'], localization);
    if (label !== undefined) option.label = label;
    const optionDescription = resolveOptionLabel(item['description'], localization);
    if (optionDescription !== undefined) option.description = optionDescription;
    const optionBadge = resolveOptionLabel(item['badge'], localization);
    if (optionBadge !== undefined) option.badge = optionBadge;
    if (typeof item['icon'] === 'string') option.icon = item['icon'];
    if (isPlainObject(item['metadata'])) option.metadata = item['metadata'];
    options.push(option);
  }
  return options;
}

function resolveOptionLabel(value: unknown, localization: LocalizationTranslator): string | undefined {
  if (typeof value === 'string') return value;
  const ref = asLocalizedRef(value);
  return ref ? localization.translate(ref.scope, ref.key, ref.arguments) : undefined;
}

export interface NodeVisibleWhen {
  parameterName: string;
  values: string[];
}

export function nodeVisibleWhen(node: UiNode | null | undefined): NodeVisibleWhen | undefined {
  const value = node?.properties?.[UiConfigProperties.VisibleWhen];
  if (!isPlainObject(value)) return undefined;

  const parameterName = value['parameterName'];
  const values = value['values'];
  if (typeof parameterName !== 'string' || !Array.isArray(values)) return undefined;
  if (!values.every(item => typeof item === 'string')) return undefined;

  return { parameterName, values: values as string[] };
}

export function nodeEvents(node: UiNode | null | undefined): string[] {
  return nodeStringArray(node, UiConfigProperties.Events) ?? [];
}

export function emitsEvent(node: UiNode | null | undefined, name: string): boolean {
  return nodeEvents(node).includes(name);
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
