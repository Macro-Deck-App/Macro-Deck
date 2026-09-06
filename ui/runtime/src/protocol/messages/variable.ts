import type { LocalizedText } from '../../localization/localized-text';
import type { Variable, VariableScope, VariableType } from '../../domain/variable.interface';
import { ApiError, ResultResponse } from './common';

export interface GetVariablesRequest {
  scope?: VariableScope;
  scopeRefId?: string;
}

export interface GetVariablesResponse {
  variables: Variable[];
}

export interface CreateVariableRequest {
  name: string;
  scope: VariableScope;
  scopeRefId?: string;
  type: VariableType;
  initialValue?: string;
  decimalPlaces?: number;
  resourceIntegrationId?: string;
  resourceKey?: string;
}

export interface CreateVariableResponse extends ResultResponse {
  variable?: Variable;
}

export interface UpdateVariableRequest {
  id: string;
  name?: string;
  value?: string;
  decimalPlaces?: number;
  resourceIntegrationId?: string;
  resourceKey?: string;
}

export interface UpdateVariableResponse extends ResultResponse {
  variable?: Variable;
}

export interface DeleteVariableRequest {
  id: string;
}

export interface DeleteVariableResponse extends ResultResponse {}

export interface SetVariableValueRequest {
  id: string;
  value?: string;
  resourceIntegrationId?: string;
  resourceKey?: string;
}

export interface SetVariableValueResponse extends ResultResponse {
  variable?: Variable;
  pending?: boolean;
}

export interface SanitizeVariableNameRequest {
  input: string;
}

export interface SanitizeVariableNameResponse {
  sanitized: string;
  isValid: boolean;
}

export interface VariablesChangedEvent {
  upserted: Variable[];
  deletedIds: string[];
}

export interface VariableCatalogNode {
  id: string;
  name: string;
  displayName: LocalizedText;
  description?: LocalizedText;
  hasChildren: boolean;
  type?: VariableType;
  icon?: string;
  suggestedName?: string;
  boundVariableId?: string;
  decimalPlaces?: number;
  canWrite?: boolean;
}

export interface DiscoverCatalogVariablesRequest {
  integrationId: string;
  parentId?: string;
  search?: string;
  cursor?: string;
  limit?: number;
}

export interface DiscoverCatalogVariablesResponse {
  nodes: VariableCatalogNode[];
  nextCursor?: string;
  hasMore: boolean;
  available: boolean;
}

export interface ResolveCatalogVariableRequest {
  integrationId: string;
  resourceId: string;
}

export interface ResolveCatalogVariableResponse {
  node?: VariableCatalogNode;
  error?: ApiError;
}

export interface BindCatalogVariableRequest {
  integrationId: string;
  resourceId: string;
  name?: string;
  type?: VariableType;
}

export interface BindCatalogVariableResponse {
  variable?: Variable;
  error?: ApiError;
}

export interface UnbindCatalogVariableRequest {
  variableId: string;
}

export interface UnbindCatalogVariableResponse extends ResultResponse {}

export interface RenameCatalogVariableRequest {
  variableId: string;
  name: string;
}

export interface RenameCatalogVariableResponse {
  variable?: Variable;
  error?: ApiError;
}

export interface VariableCatalogProvider {
  integrationId: string;
  name: LocalizedText;
  supportsManualIds: boolean;
  supportsSearch: boolean;
  unboundCount?: number;
}

export interface GetVariableCatalogProvidersRequest {}

export interface GetVariableCatalogProvidersResponse {
  providers: VariableCatalogProvider[];
}
