import { LocalizedText } from '../../localization/localized-text';
import { ActionParameterDef } from './action';

export interface ConfigFlowLinkDto {
  label: LocalizedText;
  url: string;
}

export interface ConfigFlowCopyValueDto {
  label: LocalizedText;
  value: string;
}

export interface ConfigFlowInstructionDto {
  text: LocalizedText;
  values?: ConfigFlowCopyValueDto[];
}

export interface ConfigFlowStepDto {
  stepId: string;
  title?: LocalizedText;
  description?: LocalizedText;
  values?: ConfigFlowCopyValueDto[];
  instructions?: ConfigFlowInstructionDto[];
  links?: ConfigFlowLinkDto[];
  fields: ActionParameterDef[];
  advancedFields?: ActionParameterDef[];
}

export interface StartConfigFlowResponse {
  supported: boolean;
  flowId?: string;
  step?: ConfigFlowStepDto;
  supportsConfigUi?: boolean;
  configUiModelVersion?: number;
  initialValues?: Record<string, unknown>;
  storedSecretFields?: string[];
  error?: { code: string; message: string };
}

export type ConfigFlowOutcomeKind = 'Step' | 'Error' | 'Complete' | 'External';

export interface SubmitConfigFlowStepRequest {
  flowId: string;
  stepId: string;
  values: Record<string, unknown>;
  clearedSecretFields?: string[];
}

export interface SubmitConfigFlowStepResponse {
  kind: ConfigFlowOutcomeKind;
  step?: ConfigFlowStepDto;
  message?: LocalizedText;
  fieldErrors?: Record<string, LocalizedText>;
  entryId?: string;
  externalUrl?: string;
  resumeStepId?: string;
  error?: { code: string; message: LocalizedText };
}

export interface ConfigFlowAuthorizedNotification {
  flowId: string;
  success: boolean;
  error?: string;
}

export interface ConfigEntryDto {
  id: string;
  title: string;
  createdAt: string;
  status?: 'Ready' | 'Connecting' | 'Connected' | 'Reconnecting' | 'Disconnected' | 'NeedsReconfiguration';
  usable?: boolean;
}

export interface GetConfigEntriesResponse {
  entries: ConfigEntryDto[];
}

export interface DeleteConfigEntryResponse {
  success: boolean;
  error?: { code: string; message: string };
}

export interface RenameConfigEntryResponse {
  success: boolean;
  error?: { code: string; message: string };
}
