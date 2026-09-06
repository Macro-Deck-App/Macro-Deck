import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { AppStrings, GetConnectSessionResponse, StartConnectSignInResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { ExternalLinkService } from './external-link.service';

const STATUS_LABEL_KEYS: Record<GetConnectSessionResponse['status'], string> = {
  signedOut: AppStrings.Account.StatusSignedOut,
  signingIn: AppStrings.Account.StatusSigningIn,
  signedIn: AppStrings.Account.StatusSignedIn,
  reauthenticationRequired: AppStrings.Account.StatusSignInRequired,
  suspended: AppStrings.Account.StatusSuspended,
};

@Injectable({ providedIn: 'root' })
export class ConnectAccountService {
  private readonly api = inject(ApiService);
  private readonly externalLink = inject(ExternalLinkService);
  private readonly localization = inject(LocalizationService);

  readonly session = signal<GetConnectSessionResponse | null>(null);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly signInPrompt = signal<StartConnectSignInResponse | null>(null);

  private inFlightLoad: Promise<void> | null = null;

  readonly isSignedIn = computed(() => this.session()?.status === 'signedIn');

  readonly needsAttention = computed(() => {
    const status = this.session()?.status;
    return status === 'suspended' || status === 'reauthenticationRequired';
  });

  readonly statusLabel = computed(() => {
    const session = this.session();
    const key = session ? STATUS_LABEL_KEYS[session.status] : STATUS_LABEL_KEYS.signedOut;
    return this.localization.translateKey(key);
  });

  readonly avatarUrl = computed(() => {
    const account = this.session()?.account;
    if (!account?.avatarAvailable) {
      return null;
    }

    // The host serves every avatar from one fixed URL, so the version has to distinguish a replaced
    // picture; without it the browser keeps showing the cached one.
    const base = this.api.getConnectAvatarUrl();
    return account.avatarVersion ? `${base}?v=${encodeURIComponent(account.avatarVersion)}` : base;
  });

  constructor() {
    this.api.onConnectSessionChanged().subscribe(() => {
      void this.load();
    });

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        void this.load();
      }
    });
  }

  load(): Promise<void> {
    this.inFlightLoad ??= this.runLoad().finally(() => {
      this.inFlightLoad = null;
    });
    return this.inFlightLoad;
  }

  private async runLoad(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const next = await this.api.getConnectSession();
      this.session.set(next);

      // The prompt is only meaningful while the host says an attempt is running: an expired, denied or
      // completed attempt must not leave a stale code on screen.
      if (next.status !== 'signingIn') {
        this.signInPrompt.set(null);
      }
    } catch (error) {
      console.error('Failed to load the Macro Deck Connect session:', error);
      this.loadError.set(
        error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Account.LoadFailed),
      );
    } finally {
      this.isLoading.set(false);
    }
  }

  // Opens the confirmation page in the system browser via ExternalLinkService - never window.open,
  // which the Tauri WebView silently drops. The outcome arrives later as a
  // ConnectSessionChangedNotification, not as this call's return value.
  async signIn(): Promise<void> {
    const response = await this.api.startConnectSignIn();
    this.signInPrompt.set(response);
    this.externalLink.open(response.verificationUriComplete);
  }

  async cancelSignIn(): Promise<void> {
    await this.api.cancelConnectSignIn();
    this.signInPrompt.set(null);
  }

  signOut(): Promise<void> {
    return this.api.signOutConnect();
  }
}
