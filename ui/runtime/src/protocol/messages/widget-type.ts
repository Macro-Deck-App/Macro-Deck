import { LocalizedText } from '../../localization/localized-text';

export interface WidgetTypeDto {
  id: string;
  providerId: string;
  isBuiltIn: boolean;
  name?: LocalizedText;
  description?: LocalizedText;
  defaultData: Record<string, unknown>;
  supportsConfigUi: boolean;
  configUiModelVersion: number;
}

export interface GetWidgetTypesRequest {}

export interface GetWidgetTypesResponse {
  types: WidgetTypeDto[];
}

export interface WidgetTypeCatalogChangedEvent {
  types: WidgetTypeDto[];
}
