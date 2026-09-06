export interface GetKeyRingStatusResponse {
  locked: boolean;
  lockReason: string;
  restartSupported: boolean;
  restartUnsupportedReason?: string | null;
}

export interface UnlockKeyRingRequest {
  recoveryKey: string;
}

export interface UnlockKeyRingError {
  code: string;
  message: string;
}

export interface UnlockKeyRingResponse {
  success: boolean;
  error?: UnlockKeyRingError | null;
  restartRequested: boolean;
  restartSupported: boolean;
  restartUnsupportedReason?: string | null;
}

export type KeyRingProtectionState = 'Unprotected' | 'Protected' | 'Locked';

export type KeyRingBackend = 'None' | 'WindowsCredentialManager' | 'MacOsKeychain' | 'LinuxSecretService';

export interface GetKeyRingProtectionResponse {
  state: KeyRingProtectionState;
  lockReason: string;
  unprotectedReason: string;
  backend: KeyRingBackend;
  backendAvailable: boolean;
  backendUnavailableReason?: string | null;
  kekId?: string | null;
  escrowWrapCount: number;
  recoveryKeyExported: boolean;
  migrationPending: boolean;
}
