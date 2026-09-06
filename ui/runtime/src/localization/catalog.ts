import { ClientAppStringsDefaults } from './generated/client-app-strings';
import { StringsDefaults } from './generated/strings';
import { formatTemplate } from './format-template.util';
import { asLocalizedRef, LocalizationTranslator } from './localized-text';
import { pluralForm } from './plural-form.util';

export interface LocalizationCatalogSnapshot {
  culture?: string;
  fallbackCulture?: string;
  translations?: { [key: string]: string };
}

export const DEFAULT_CULTURE = 'en';

const MAX_ARGUMENT_DEPTH = 4;

export class LocalizationCatalog implements LocalizationTranslator {
  private translations: { [key: string]: string } = { ...StringsDefaults, ...ClientAppStringsDefaults };
  private active = DEFAULT_CULTURE;
  private readonly listeners: Array<() => void> = [];

  culture(): string {
    return this.active;
  }

  onChange(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }

  apply(snapshot: LocalizationCatalogSnapshot): void {
    this.active = snapshot.culture || DEFAULT_CULTURE;
    this.translations = { ...StringsDefaults, ...ClientAppStringsDefaults, ...(snapshot.translations ?? {}) };
    for (let index = 0; index < this.listeners.length; index++) this.listeners[index]();
  }

  translate(scope: string, key: string, args?: Record<string, unknown>): string {
    const qualified = `${scope}:${key}`;
    const template = this.templateFor(qualified, args);
    return template === undefined
      ? `[[${qualified}]]`
      : formatTemplate(template, this.resolveArguments(args));
  }

  private templateFor(qualified: string, args?: Record<string, unknown>): string | undefined {
    const exact = this.translations[qualified];
    if (exact !== undefined) return exact;

    const count = args === undefined ? undefined : args['count'];
    if (count === undefined) return undefined;

    return this.translations[`${qualified}.${pluralForm(count)}`]
      ?? this.translations[`${qualified}.Other`];
  }

  private resolveArguments(
    args: Record<string, unknown> | undefined,
    depth = 0,
  ): Record<string, unknown> | undefined {
    if (args === undefined || depth >= MAX_ARGUMENT_DEPTH) return args;

    let resolved: Record<string, unknown> | undefined;
    for (const name in args) {
      if (!Object.prototype.hasOwnProperty.call(args, name)) continue;
      const ref = asLocalizedRef(args[name]);
      if (!ref) continue;

      if (resolved === undefined) resolved = { ...args };
      resolved[name] = this.translate(ref.scope, ref.key, this.resolveArguments(ref.arguments, depth + 1));
    }
    return resolved ?? args;
  }
}
