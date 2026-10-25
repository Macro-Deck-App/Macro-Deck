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
  requiresConfiguration?: boolean;
  configuredEntryCount: number;
  hasIcon: boolean;
  iconVersion?: string | null;
  issueCount: number;
  issueSeverity?: IntegrationIssueSeverity | null;
  isInitialized?: boolean;
  variablesDependOnConfiguration?: boolean;
  providedCapabilities?: IpcProvidedCapability[];
  pages?: IpcIntegrationPage[];
}

export interface IpcIntegrationPage {
  id: string;
  title: LocalizedText;
  icon?: string | null;
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
  type: 'text' | 'numeric' | 'boolean' | 'color';
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
  counts?: IpcIntegrationCapabilityCount[];
}

export interface IpcIntegrationCapabilityCount {
  kind: string;
  count: number;
}

export interface IntegrationCapabilityDataChangedEvent {
  kind: string;
  integrationIds: string[];
}

export type IntegrationBatteryChargeState = 'Unknown' | 'Discharging' | 'Charging' | 'Full' | 'NotCharging';

export type IntegrationBatteryHealth =
  | 'Unknown'
  | 'Good'
  | 'Overheat'
  | 'Dead'
  | 'OverVoltage'
  | 'Cold'
  | 'Failure';

export interface IpcIntegrationBattery {
  id: string;
  name: string;
  kind: string;
  levelPercent?: number | null;
  chargeState: IntegrationBatteryChargeState;
  isConnected: boolean;
  timeRemainingSeconds?: number | null;
  timeToFullSeconds?: number | null;
  health: IntegrationBatteryHealth;
  updatedAt: string;
}

export interface GetIntegrationBatteriesResponse {
  found: boolean;
  available: boolean;
  batteries: IpcIntegrationBattery[];
}

export interface IpcIntegrationCalendar {
  key: string;
  name: string;
  color?: string | null;
  isPrimary: boolean;
}

export interface IpcIntegrationCalendarAccount {
  accountId: string;
  displayName: string;
  status: 'Ok' | 'Error';
  calendars: IpcIntegrationCalendar[];
}

export interface IpcIntegrationCalendarEvent {
  instanceKey: string;
  accountId: string;
  calendarKey: string;
  title: string;
  start: string;
  end: string;
  isAllDay: boolean;
  location?: string | null;
  hasMeetingUrl: boolean;
}

export interface GetIntegrationCalendarsResponse {
  found: boolean;
  available: boolean;
  windowStart?: string | null;
  windowEnd?: string | null;
  accounts: IpcIntegrationCalendarAccount[];
  events: IpcIntegrationCalendarEvent[];
}

export type IntegrationEmailInboxStatus = 'Connecting' | 'Connected' | 'Disconnected' | 'Error';

export interface IpcIntegrationEmailMessage {
  id: string;
  senderName?: string | null;
  senderAddress?: string | null;
  subject: string;
  preview?: string | null;
  receivedAt: string;
  isRead: boolean;
}

export interface IpcIntegrationEmailInbox {
  ref: string;
  displayName: string;
  address?: string | null;
  status: IntegrationEmailInboxStatus;
  statusMessage?: string | null;
  unreadCount: number;
  totalCount: number;
  syncedAt?: string | null;
  isStale: boolean;
  messages: IpcIntegrationEmailMessage[];
}

export interface GetIntegrationEmailInboxesResponse {
  found: boolean;
  available: boolean;
  inboxes: IpcIntegrationEmailInbox[];
}

export interface IpcIntegrationIssue {
  id: string;
  title: LocalizedText;
  description?: LocalizedText;
  severity: IntegrationIssueSeverity;
  actionLabel?: LocalizedText;
  helpUrl?: string | null;
}

export type IssueResolutionFollowUp = 'None' | 'StartConfigFlow';

export interface GetIntegrationsRequest {}

export interface SetIntegrationEnabledRequest {
  id: string;
  enabled: boolean;
}

export interface IpcSystemIntegrationName {
  id: string;
  name: LocalizedText;
}

export interface GetIntegrationsResponse {
  integrations: IpcIntegration[];
  systemIntegrations?: IpcSystemIntegrationName[];
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

export interface IntegrationsChangedEvent {
  integrationId: string;
}
