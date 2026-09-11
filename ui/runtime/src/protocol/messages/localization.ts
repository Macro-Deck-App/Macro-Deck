export type LocalizationTimeFormat = 'system' | '12h' | '24h';

export type LocalizationHourCycle = 'h12' | 'h23';

export interface GetLocalizationResponse {
  culture: string;
  fallbackCulture: string;
  translations: Record<string, string>;
  availableCultures: string[];
  followSystem: boolean;
  timeFormat?: LocalizationTimeFormat;
  hourCycle?: LocalizationHourCycle;
}

export interface LocalizationCultureChangedEvent {
  culture: string;
  fallbackCulture: string;
}

export interface LocalizationCatalogChangedEvent {
  scope: string;
}

export interface GetLocalizationSettingsResponse {
  culture: string;
  fallbackCulture: string;
  followSystem: boolean;
  timeFormat?: LocalizationTimeFormat;
}

export interface UpdateLocalizationSettingsRequest {
  culture?: string;
  followSystem?: boolean;
  timeFormat?: LocalizationTimeFormat;
}

export interface UpdateLocalizationSettingsResponse {
  success: boolean;
  error: string | null;
  culture: string;
  fallbackCulture: string;
  followSystem: boolean;
  timeFormat?: LocalizationTimeFormat;
}
