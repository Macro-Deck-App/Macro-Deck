export interface GetLocalizationResponse {
  culture: string;
  fallbackCulture: string;
  translations: Record<string, string>;
  availableCultures: string[];
  followSystem: boolean;
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
}

export interface UpdateLocalizationSettingsRequest {
  culture?: string;
  followSystem?: boolean;
}

export interface UpdateLocalizationSettingsResponse {
  success: boolean;
  error: string | null;
  culture: string;
  fallbackCulture: string;
  followSystem: boolean;
}
