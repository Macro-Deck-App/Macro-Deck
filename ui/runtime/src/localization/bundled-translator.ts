import { AppStringsDefaults } from './generated/app-strings';
import { StringsDefaults } from './generated/strings';

export function bundledTranslator(key: string, args?: Record<string, unknown>): string {
  const template = { ...StringsDefaults, ...AppStringsDefaults }[key];
  if (template === undefined) return `[[${key}]]`;
  return Object.entries(args ?? {}).reduce(
    (text, [name, value]) => text.split(`{${name}}`).join(String(value)),
    template,
  );
}
