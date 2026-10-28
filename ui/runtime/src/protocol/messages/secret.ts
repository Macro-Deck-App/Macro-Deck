import { LocalizedText } from '../../localization/localized-text';

export type SecretKind = 'Password' | 'Secret';

export interface CreateSecretRequest {
  value: string;
  kind: SecretKind;
}

export interface CreateSecretResponse {
  id: string;
}

export interface UpdateSecretRequest {
  id: string;
  value: string;
}

export interface UpdateSecretResponse {
  success: boolean;
}

export interface DeleteSecretResponse {
  success: boolean;
}

export interface CloneSecretResponse {
  id?: string | null;
  error?: {
    code: string;
    message: string;
  };
}

export interface RevealSecretResponse {
  value?: string;
  error?: {
    code: string;
    message: string;
  };
}

export type SecretApprovalOwnerKind = 'widget' | 'automation' | 'script';

export interface SecretApprovalParameter {
  name: string;
  label: LocalizedText | null;
  secret: boolean;
  approved: string | null;
  current: string | null;
  changed: boolean;
  alsoUsedBy: string[];
}

export interface SecretApprovalItem {
  ownerKind: SecretApprovalOwnerKind;
  ownerId: string;
  ownerName: string;
  ownerContext: string | null;
  blockId: string;
  integrationId: string;
  actionId: string;
  actionName: LocalizedText;
  fingerprint: string;
  approvedBefore: boolean;
  unknown?: boolean;
  parameters: SecretApprovalParameter[];
}

export interface GetSecretApprovalsResponse {
  count: number;
  items: SecretApprovalItem[];
}

export interface ApproveSecretBlockRequest {
  ownerKind: SecretApprovalOwnerKind;
  ownerId: string;
  blockId: string;
  fingerprint: string;
}

export interface ApproveSecretBlockResponse {
  success: boolean;
  error: {
    code: string;
    message: LocalizedText;
  } | null;
}
