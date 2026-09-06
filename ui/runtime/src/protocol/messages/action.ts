import { LocalizedText } from '../../localization/localized-text';
import type { VariableScope } from '../../domain/variable.interface';

export enum ActionParameterType {
  String = 'String',
  Number = 'Number',
  Boolean = 'Boolean',
  Choice = 'Choice',
  Password = 'Password',
  Secret = 'Secret',
  DynamicChoice = 'DynamicChoice',
  Autocomplete = 'Autocomplete',
  MultiSelect = 'MultiSelect',
  Color = 'Color',
  File = 'File',
  Folder = 'Folder',
  Hotkey = 'Hotkey',
  Duration = 'Duration',
  DateTime = 'DateTime',
  Json = 'Json',
  Code = 'Code',
  KeyValue = 'KeyValue',
  Object = 'Object',
  Array = 'Array',
  IpAddress = 'IpAddress',
  Url = 'Url',
  Icon = 'Icon',
  Image = 'Image',
  KeyboardSequence = 'KeyboardSequence',
  KeyboardCombo = 'KeyboardCombo',
  WidgetTarget = 'WidgetTarget',
}

export interface ActionParameterOption {
  value: string;
  label?: LocalizedText;
  metadata?: Record<string, string>;
}

export interface ActionParameterDef {
  name: string;
  type: ActionParameterType;
  description: LocalizedText;
  label?: LocalizedText;
  placeholder?: LocalizedText;
  autoPrefixHttps?: boolean;
  defaultValue?: unknown;
  required?: boolean;
  multiline?: boolean;
  supportsReset?: boolean;
  literalOnly?: boolean;
  validationRegex?: string;
  maxLength?: number;
  min?: number;
  max?: number;
  step?: number;
  showSlider?: boolean;
  options?: ActionParameterOption[];
  dynamicOptions?: boolean;
  optionsSourceId?: string;
  allowSelf?: boolean;
  widgetTypes?: string[];
  fileExtensions?: string[];
  language?: string;
  children?: ActionParameterDef[];
  itemTemplate?: ActionParameterDef;
  visibleWhen?: ParameterVisibility;
}

export interface ParameterVisibility {
  parameterName: string;
  values: string[];
}

export interface ActionDefinition {
  id: string;
  integrationId: string;
  integrationName: LocalizedText;
  name: LocalizedText;
  description: LocalizedText;
  parameters: ActionParameterDef[];
  descriptiveUiSchema?: string;
  isStateProviderAction?: boolean;
  isIconProviderAction?: boolean;
  supportsConfigUi?: boolean;
  configUiModelVersion?: number;
}

export interface GetActionsRequest {}

export interface ExecuteActionButtonTriggerRequest {
  widgetId: string;
  folderId: string;
  triggerType: string;
  clientId?: string;
}

export interface GetActionsResponse {
  actions: ActionDefinition[];
}

export type ActionExecutionStatus = 'Accepted' | 'Succeeded' | 'PartiallyFailed' | 'Failed' | 'Cancelled';

export function isFailureExecutionStatus(status: ActionExecutionStatus | undefined): boolean {
  return status === 'Failed' || status === 'PartiallyFailed' || status === 'Cancelled';
}

export type ActionOutcomeStatus = 'Succeeded' | 'Accepted' | 'Skipped' | 'Failed';

export interface ActionOutcome {
  blockId: string;
  label?: string;
  integrationId?: string;
  actionId?: string;
  status: ActionOutcomeStatus;
  errorCode?: string;
  errorMessage?: LocalizedText;
  durationMs: number;
}

export interface ActionExecutionResult {
  success: boolean;
  error?: {
    code: string;
    message: LocalizedText;
  };
  executionId?: string;
  status?: ActionExecutionStatus;
  durationMs?: number;
  actions?: ActionOutcome[];
}

export interface ExecuteActionButtonTriggerResponse extends ActionExecutionResult {}

export interface ExecuteActionRequest {
  integrationId: string;
  actionId: string;
  parameters?: Record<string, unknown>;
  clientId?: string;
}

export interface ExecuteActionResponse extends ActionExecutionResult {}

export interface RunActionFlowRequest {
  flows: string;
  triggerId: string;
  scope?: VariableScope;
  scopeRefId?: string;
  clientId?: string;
}

export interface RunActionFlowResponse extends ActionExecutionResult {}

export interface ActionExecutionStatusEvent {
  executionId: string;
  status: ActionExecutionStatus;
  durationMs: number;
  widgetId?: string;
  triggerType?: string;
  error?: {
    code: string;
    message: LocalizedText;
  };
  actions: ActionOutcome[];
}

export interface GetActionParameterOptionsRequest {
  integrationId: string;
  actionId: string;
  eventId?: string;
  eventParameterKind?: 'configuration' | 'payload';
  optionsSourceId?: string;
  widgetTypes?: string[];
  parameterName: string;
  filter?: string;
  currentParameters?: Record<string, unknown>;
}

export interface GetActionParameterOptionsResponse {
  options: ActionParameterOption[];
  allowsCustomValue: boolean;
  cacheSeconds?: number;
  error?: {
    code: string;
    message: LocalizedText;
  };
}

export interface ActionButtonStateAppearance {
  label?: string;
  backgroundColor?: string;
  labelColor?: string;
  iconId?: string;
}

export interface ActionButtonStateOption {
  id: string;
  label: LocalizedText;
  defaultAppearance?: ActionButtonStateAppearance;
}

export interface GetActionButtonStateOptionsRequest {
  integrationId: string;
  actionId: string;
  parameters?: Record<string, unknown>;
}

export interface GetActionButtonStateOptionsResponse {
  states: ActionButtonStateOption[];
  error?: {
    code: string;
    message: LocalizedText;
  };
}

export interface ActionProviderIconReference {
  type: string;
  reference: string;
}

export interface GetActionProviderIconRequest {
  integrationId: string;
  actionId: string;
  parameters?: Record<string, unknown>;
}

export interface GetActionProviderIconResponse {
  hasSnapshot: boolean;
  version: string;
  reference?: ActionProviderIconReference;
  mediaType?: string;
  noIcon: boolean;
  error?: {
    code: string;
    message: LocalizedText;
  };
}
