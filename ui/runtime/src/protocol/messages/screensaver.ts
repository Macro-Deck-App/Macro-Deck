import { LocalizedText } from '../../localization/localized-text';

// The screensaver every device shows until it selects another, and the one a withdrawn selection
// falls back to. Spelled here so a settings page can name it without a catalog lookup.
export const BUILT_IN_CLOCK_SCREENSAVER_ID = 'app.macro-deck.screensavers::clock';

export interface IpcScreenSaver {
  id: string;
  providerId: string;
  name?: LocalizedText;
  description?: LocalizedText;
  hasConfiguration: boolean;
  interactive: boolean;
  isBuiltIn: boolean;
}

export interface GetScreenSaversRequest {}

export interface GetScreenSaversResponse {
  screenSavers: IpcScreenSaver[];
}

export interface ScreenSaverCatalogChangedEvent {
  screenSavers: IpcScreenSaver[];
}

export interface GetDeviceScreenSaverResponse {
  enabled: boolean;
  idleSeconds: number;
}

export interface OpenScreenSaverUiSessionResponse {
  accepted: boolean;
  sessionId: string;
  code?: string;
  message?: string;
  screenSaverId: string;
  interactive: boolean;
}
