import { LocalizedText } from '../../localization/localized-text';

export interface WebClientTargetDto {
  targetId: string;
  canProvision: boolean;
}

export interface WebClientTargetProvisioningValueDto {
  label: LocalizedText;
  value: string;
}

export interface WebClientTargetProvisioningLinkDto {
  label: LocalizedText;
  url: string;
}

export interface WebClientTargetProvisioningChoiceDto {
  value: string;
  label: LocalizedText;
}

export interface WebClientTargetProvisioningFieldDto {
  fieldId: string;
  label: LocalizedText;
  description: LocalizedText;
  defaultValue: string;
  choices?: WebClientTargetProvisioningChoiceDto[] | null;
}

export interface WebClientTargetProvisioningStepDto {
  stepId: string;
  title: LocalizedText;
  description: LocalizedText;
  instructions: LocalizedText[];
  values: WebClientTargetProvisioningValueDto[];
  links: WebClientTargetProvisioningLinkDto[];
  fields: WebClientTargetProvisioningFieldDto[];
  canContinue: boolean;
}

export type WebClientTargetProvisioningResultKind = 'Step' | 'Error' | 'Complete';

export interface WebClientTargetProvisioningResultDto {
  kind: WebClientTargetProvisioningResultKind;
  step: WebClientTargetProvisioningStepDto | null;
  message: LocalizedText;
}

export interface AdvanceWebClientTargetProvisioningRequest {
  stepId: string;
  input: Record<string, string>;
}
