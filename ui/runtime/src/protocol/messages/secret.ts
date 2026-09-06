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
