import { LocalizedText } from '../../localization/localized-text';
import { ApiError, ResultResponse } from './common';

export type IntegrationIssueSeverity = 'info' | 'warning' | 'error';

export interface IpcIntegration {
  id: string;
  name: LocalizedText;
  version: string;
  isInternal: boolean;
  enabled: boolean;
  actionCount: number;
  variableCount: number;
  supportsConfigFlow: boolean;
  allowsMultipleConfigurations: boolean;
  configuredEntryCount: number;
  hasIcon: boolean;
  iconVersion?: string | null;
  issueCount: number;
  issueSeverity?: IntegrationIssueSeverity | null;
  isInitialized?: boolean;
  variablesDependOnConfiguration?: boolean;
  providedCapabilities?: IpcProvidedCapability[];
}

export interface IpcProvidedCapability {
  kind: string;
  name: LocalizedText;
}

export type CapabilityAvailability =
  | 'Ready'
  | 'SetupRequired'
  | 'AvailableAfterSetup'
  | 'IntegrationDisabled'
  | 'Unavailable';

export interface IpcIntegrationActionCapability {
  id: string;
  name: LocalizedText;
  description: LocalizedText;
  parameterCount: number;
  parameterSummary: string[];
  availability: CapabilityAvailability;
  availabilityReason: string;
}

export interface IpcIntegrationVariableCapability {
  name: string;
  type: 'text' | 'numeric' | 'boolean';
  decimalPlaces?: number | null;
  refreshIntervalSeconds?: number | null;
  isTemplate: boolean;
  availability: CapabilityAvailability;
  availabilityReason: string;
  value?: string | null;
  valueAvailable: boolean;
}

export interface GetIntegrationCapabilitiesResponse {
  found: boolean;
  enabled: boolean;
  isInitialized: boolean;
  supportsConfigFlow: boolean;
  requiresSetup: boolean;
  variablesDependOnConfiguration: boolean;
  configuredEntryCount: number;
  actions: IpcIntegrationActionCapability[];
  variables: IpcIntegrationVariableCapability[];
}

export interface IpcIntegrationIssue {
  id: string;
  title: LocalizedText;
  description?: LocalizedText;
  severity: IntegrationIssueSeverity;
  actionLabel?: LocalizedText;
}

export type IssueResolutionFollowUp = 'None' | 'StartConfigFlow';

export interface GetIntegrationsRequest {}

export interface SetIntegrationEnabledRequest {
  id: string;
  enabled: boolean;
}

export interface GetIntegrationsResponse {
  integrations: IpcIntegration[];
}

export interface SetIntegrationEnabledResponse extends ResultResponse {}

export interface GetIntegrationIssuesResponse {
  issues: IpcIntegrationIssue[];
}

export interface ResolveIntegrationIssueResponse {
  success: boolean;
  message?: LocalizedText;
  followUp: IssueResolutionFollowUp;
  error?: ApiError;
}

export interface IntegrationIssuesChangedEvent {
  integrationId: string;
  issues: IpcIntegrationIssue[];
  issueCount: number;
  severity: IntegrationIssueSeverity | null;
}
