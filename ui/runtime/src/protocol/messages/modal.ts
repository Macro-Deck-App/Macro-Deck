import { LocalizedText } from '../../localization/localized-text';

export interface UiModalOpenedEvent {
  modalId: string;
  title?: LocalizedText;
}

export interface OpenModalUiSessionRequest {
  modalId: string;
}

export interface OpenModalUiSessionResponse {
  accepted: boolean;
  sessionId: string;
  code?: string;
  message?: string;
}

export interface CompleteUiModalRequest {
  modalId: string;
  cancelled: boolean;
  value?: unknown;
}

export interface CompleteUiModalResponse {
  accepted: boolean;
}
