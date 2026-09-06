import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../transport';
import {
  AppStringsDefaults,
  asLocalizedRef,
  formatTemplate,
  GetLocalizationResponse,
  LocalizationCatalogChangedEvent,
  LocalizationCultureChangedEvent,
  pluralForm,
  StringsDefaults,
  UpdateLocalizationSettingsRequest,
} from '@macro-deck/runtime';

const STORAGE_KEY_CULTURE = 'md.localization.culture';
const STORAGE_KEY_FALLBACK_CULTURE = 'md.localization.fallbackCulture';
const STORAGE_KEY_TRANSLATIONS = 'md.localization.translations';
const STORAGE_KEY_AVAILABLE_CULTURES = 'md.localization.availableCultures';
const STORAGE_KEY_FOLLOW_SYSTEM = 'md.localization.followSystem';

export const DEFAULT_CULTURE = 'en';

const MAX_ARGUMENT_DEPTH = 4;

const BUNDLED_DEFAULTS: Readonly<Record<string, string>> = { ...StringsDefaults, ...AppStringsDefaults };

@Injectable({ providedIn: 'root' })
export class LocalizationService {
  private readonly api = inject(ApiService);

  readonly culture = signal<string>(DEFAULT_CULTURE);
  readonly fallbackCulture = signal<string>(DEFAULT_CULTURE);
  readonly availableCultures = signal<string[]>([]);
  readonly followsSystem = signal(false);

  private readonly translations = signal<Record<string, string>>({ ...BUNDLED_DEFAULTS });

  readonly catalogVersion = signal(0);

  constructor() {
    this.restoreFromCache();

    this.api
      .onNotification<LocalizationCultureChangedEvent>('LocalizationCultureChangedEvent')
      .subscribe(() => void this.loadFromHost());

    this.api
      .onNotification<LocalizationCatalogChangedEvent>('LocalizationCatalogChangedEvent')
      .subscribe(() => void this.loadFromHost());
  }

  async loadFromHost(): Promise<void> {
    try {
      this.applyCatalog(await this.api.getLocalization());
    } catch {
    }
  }

  async setCulture(culture: string): Promise<void> {
    await this.applyLocalizationSettings({ culture });
  }

  async followSystemCulture(): Promise<void> {
    await this.applyLocalizationSettings({ followSystem: true });
  }

  private async applyLocalizationSettings(request: UpdateLocalizationSettingsRequest): Promise<void> {
    try {
      const response = await this.api.updateLocalizationSettings(request);
      if (!response.success) return;
    } catch {
      return;
    }
    await this.loadFromHost();
  }

  translate(scope: string, key: string, args?: Record<string, unknown>): string {
    const qualified = `${scope}:${key}`;
    const template = this.templateFor(qualified, args);
    return template === undefined
      ? `[[${qualified}]]`
      : formatTemplate(template, this.resolveArguments(args));
  }

  private resolveArguments(
    args: Record<string, unknown> | undefined,
    depth = 0,
  ): Record<string, unknown> | undefined {
    if (!args || depth >= MAX_ARGUMENT_DEPTH) return args;

    let resolved: Record<string, unknown> | undefined;

    for (const [name, value] of Object.entries(args)) {
      const ref = asLocalizedRef(value);
      if (!ref) continue;

      resolved ??= { ...args };
      resolved[name] = this.translate(ref.scope, ref.key, this.resolveArguments(ref.arguments, depth + 1));
    }

    return resolved ?? args;
  }

  private templateFor(qualified: string, args?: Record<string, unknown>): string | undefined {
    const translations = this.translations();
    const exact = translations[qualified];
    if (exact !== undefined) return exact;

    const count = args?.['count'];
    if (count === undefined) return undefined;

    return translations[`${qualified}.${pluralForm(count)}`] ?? translations[`${qualified}.Other`];
  }

  translateKey(qualifiedKey: string, args?: Record<string, unknown>): string {
    const separator = qualifiedKey.indexOf(':');
    if (separator < 0) return `[[${qualifiedKey}]]`;
    return this.translate(qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1), args);
  }

  private applyCatalog(response: GetLocalizationResponse): void {
    this.culture.set(response.culture || DEFAULT_CULTURE);
    this.fallbackCulture.set(response.fallbackCulture || DEFAULT_CULTURE);
    this.merge(response.translations ?? {});
    this.availableCultures.set(response.availableCultures ?? []);
    this.followsSystem.set(response.followSystem ?? false);
    this.catalogVersion.update(v => v + 1);
    this.persist();
  }

  private merge(translations: Record<string, string>): void {
    this.translations.set({ ...BUNDLED_DEFAULTS, ...translations });
  }

  private persist(): void {
    try {
      localStorage.setItem(STORAGE_KEY_CULTURE, this.culture());
      localStorage.setItem(STORAGE_KEY_FALLBACK_CULTURE, this.fallbackCulture());
      localStorage.setItem(STORAGE_KEY_TRANSLATIONS, JSON.stringify(this.translations()));
      localStorage.setItem(STORAGE_KEY_AVAILABLE_CULTURES, JSON.stringify(this.availableCultures()));
      localStorage.setItem(STORAGE_KEY_FOLLOW_SYSTEM, String(this.followsSystem()));
    } catch {
    }
  }

  private restoreFromCache(): void {
    try {
      const culture = localStorage.getItem(STORAGE_KEY_CULTURE);
      const fallbackCulture = localStorage.getItem(STORAGE_KEY_FALLBACK_CULTURE);
      const rawTranslations = localStorage.getItem(STORAGE_KEY_TRANSLATIONS);
      const rawAvailableCultures = localStorage.getItem(STORAGE_KEY_AVAILABLE_CULTURES);
      const followSystem = localStorage.getItem(STORAGE_KEY_FOLLOW_SYSTEM);
      if (culture) this.culture.set(culture);
      if (fallbackCulture) this.fallbackCulture.set(fallbackCulture);
      if (followSystem) this.followsSystem.set(followSystem === 'true');
      if (rawTranslations) {
        const parsed: unknown = JSON.parse(rawTranslations);
        if (isTranslationMap(parsed)) this.merge(parsed);
      }
      if (rawAvailableCultures) {
        const parsed: unknown = JSON.parse(rawAvailableCultures);
        if (isStringArray(parsed)) this.availableCultures.set(parsed);
      }
    } catch {
    }
  }
}

function isTranslationMap(value: unknown): value is Record<string, string> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
    && Object.values(value).every(entry => typeof entry === 'string');
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(entry => typeof entry === 'string');
}
