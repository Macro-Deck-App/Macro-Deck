export type ConnectSessionStatus =
  | 'signedOut'
  | 'signingIn'
  | 'signedIn'
  | 'reauthenticationRequired'
  | 'suspended';

export type ConnectSignInFailure = 'denied' | 'expired' | 'unreachable' | 'failed';

export type ConnectConnectivity = 'ok' | 'offline';

export interface ConnectAccount {
  subject: string;
  displayName: string;
  avatarAvailable: boolean;
  avatarVersion: string | null;
  creatorUsername: string | null;
  roles: string[];
}

export interface GetConnectSessionResponse {
  status: ConnectSessionStatus;
  connectivity: ConnectConnectivity;
  offlineSince: string | null;
  account: ConnectAccount | null;
  lastSuccessfulRefreshUtc: string | null;
  message: string | null;
  signInFailure: ConnectSignInFailure | null;
  accountManagementUrl: string;
}

export interface StartConnectSignInResponse {
  verificationUriComplete: string;
  verificationUri: string;
  userCode: string;
  expiresAtUtc: string;
}

export interface ConnectSessionChangedNotification {}
