export type LocalizedText = string | LocalizedTextRef | null;

export interface LocalizedTextRef {
  $localized: {
    scope: string;
    key: string;
    arguments?: Record<string, unknown>;
  };
}

export interface LocalizedRef {
  scope: string;
  key: string;
  arguments?: Record<string, unknown>;
}

export interface LocalizationTranslator {
  translate(scope: string, key: string, args?: Record<string, unknown>): string;
}

export function asLocalizedRef(value: unknown): LocalizedRef | undefined {
  if (!isPlainObject(value)) return undefined;
  const ref = value['$localized'];
  if (!isPlainObject(ref) || typeof ref['scope'] !== 'string' || typeof ref['key'] !== 'string') return undefined;
  return {
    scope: ref['scope'],
    key: ref['key'],
    arguments: isPlainObject(ref['arguments']) ? ref['arguments'] : undefined,
  };
}

export function resolveLocalizedText(
  value: LocalizedText | undefined,
  localization: LocalizationTranslator,
): string {
  if (typeof value === 'string') return value;

  const ref = asLocalizedRef(value);
  return ref ? localization.translate(ref.scope, ref.key, ref.arguments) : '';
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
