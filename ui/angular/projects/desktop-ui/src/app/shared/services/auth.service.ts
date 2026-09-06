import { Injectable, InjectionToken, OnDestroy, inject, signal } from '@angular/core';
import { ApiService } from '../transport/api.service';
import {
  AppStrings,
  AuthScope,
  DeviceSessionRevokedEvent,
  isUnreachable,
  RefreshOutcome,
  TokenResponse,
  TransportError,
} from '@macro-deck/runtime';
import { LocalizationService } from '../localization';
import { CROSS_TAB_LOCK_ENVIRONMENT, runExclusively } from './cross-tab-lock';
import { DeviceIdentityService } from './device-identity.service';

export type AuthState = 'unknown' | 'setupRequired' | 'loggedOut' | 'authenticated';

export interface AuthResult {
  ok: boolean;
  message?: string;
}

export const AUTH_REQUIRED_SCOPE = new InjectionToken<AuthScope | null>('AUTH_REQUIRED_SCOPE', {
  providedIn: 'root',
  factory: () => null,
});

const REFRESH_LEEWAY_SECONDS = 60;
const MIN_REFRESH_DELAY_MS = 5000;

const INITIAL_SESSION_RETRY_DELAY_MS = 2000;
const MAX_SESSION_RETRY_DELAY_MS = 60000;

export interface EnrollmentUrlEnvironment {
  readHash(): string;
  clearHash(): void;
}

export const ENROLLMENT_URL_ENVIRONMENT = new InjectionToken<EnrollmentUrlEnvironment>(
  'ENROLLMENT_URL_ENVIRONMENT',
  {
    providedIn: 'root',
    factory: () => ({
      readHash: () => window.location.hash,
      // replaceState, not a navigation: the credential has to leave the address bar without adding a
      // history entry a Back gesture could return to.
      clearHash: () => window.history.replaceState(null, '',
        window.location.pathname + window.location.search),
    }),
  });

@Injectable({ providedIn: 'root' })
export class AuthService implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly requiredScope = inject(AUTH_REQUIRED_SCOPE);
  private readonly deviceIdentity = inject(DeviceIdentityService);
  private readonly lockEnvironment = inject(CROSS_TAB_LOCK_ENVIRONMENT);
  private readonly enrollmentEnvironment = inject(ENROLLMENT_URL_ENVIRONMENT);

  private readonly _state = signal<AuthState>('unknown');
  private readonly _scope = signal<AuthScope | null>(null);
  private readonly _username = signal<string | null>(null);
  private readonly _currentDeviceId = signal<string | null>(null);
  // This session's assigned startup profile id (issue #251), from the same login/refresh `device`
  // block - see startupProfileId doc for why applySession sets this one unconditionally.
  private readonly _startupProfileId = signal<string | null>(null);
  private readonly _trusted = signal(false);

  readonly state = this._state.asReadonly();
  readonly scope = this._scope.asReadonly();
  readonly username = this._username.asReadonly();

  readonly currentDeviceId = this._currentDeviceId.asReadonly();

  readonly startupProfileId = this._startupProfileId.asReadonly();

  readonly trusted = this._trusted.asReadonly();

  private accessToken: string | null = null;
  // Wall-clock instant (Date.now() domain) the current access token expires at; null while there
  // is none. Kept alongside the proactive refresh timer so resume() (issue #257) can answer "is a
  // refresh needed right now" without waiting for that timer to fire.
  private accessTokenExpiresAt: number | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshPromise: Promise<RefreshOutcome> | null = null;
  private deviceRevokedSubscribed = false;
  private sessionRetryTimer: ReturnType<typeof setTimeout> | null = null;
  private sessionRetryDelayMs = INITIAL_SESSION_RETRY_DELAY_MS;

  // Cross-tab lock (issue #257) guarding every refresh: the `md_refresh` cookie is shared by `/`
  // and `/admin`, and `visibilitychange` fires in every tab at the same instant on unlock. Two tabs
  // rotating the shared refresh token concurrently trips the host's reuse detection, which revokes
  // EVERY session for the user (host/src/MacroDeckHost.Application/Auth/AuthService.cs) - including
  // the desktop admin's.
  private static readonly REFRESH_LOCK_NAME = 'md.auth.refreshLock';

  constructor() {
    this.api.setAuthHooks(
      () => this.accessToken,
      () => this.handleUnauthorized(),
      () => { void this.handleConnectionUnauthorized(); },
      () => this.handleForbidden(),
    );
  }

  async bootstrap(): Promise<void> {
    this.subscribeToDeviceSessionRevoked();
    try {
      const baseUrl = await this.api.resolveBaseUrl();
      if (!baseUrl) {
        return;
      }

      // Before anything else: a device that cannot be signed in by hand may have been handed a
      // one-time credential by its setup (issue #727). Spending it here means the ordinary path
      // below finds an established session instead of a login form nobody can fill in.
      if (await this.tryRedeemEnrollment()) {
        return;
      }

      const status = await this.api.getAuthStatus();
      if (!status.setupComplete) {
        this._state.set('setupRequired');
        return;
      }

      // Only transport-level trust (loopback/desktop) skips tokens. A status that is merely
      // cookie-authenticated (media cookie surviving a reload) still needs a real refresh,
      // otherwise the socket-ticket request would go out token-less and fail.
      if (status.trusted) {
        this._trusted.set(true);
        this._scope.set(status.scope ?? 'admin');
        this._username.set(status.username ?? null);
        this._state.set('authenticated');
        return;
      }

      const outcome = await this.tryRefresh();
      if (outcome === 'unreachable') {
        // A host that answers `/api/auth/status` but not `/api/auth/refresh` - a captive portal, a
        // host mid-restart - is not a host that has refused the session. Leaving the state at
        // `'unknown'` keeps `AppComponent.bootstrapWithRetry()` retrying behind the splash instead of
        // falling through to a login form the still-good refresh cookie should never have to see.
        return;
      }
      this._state.set(outcome === 'ok' ? 'authenticated' : 'loggedOut');
    } catch {
    }
  }

  private async tryRedeemEnrollment(): Promise<boolean> {
    const hash = this.enrollmentEnvironment.readHash();
    const token = /(?:^|[#&])enroll=([^&]+)/.exec(hash)?.[1];
    if (!token) {
      return false;
    }

    this.enrollmentEnvironment.clearHash();

    try {
      const response = await this.api.redeemDeviceEnrollment({
        token: decodeURIComponent(token),
        device: this.deviceIdentity.buildLoginInfo(),
      });
      this.applySession(response);
      if (!this.hasSufficientScope()) {
        this.clearSession();
        return false;
      }

      this.deviceIdentity.adopt(response.device);
      this._state.set('authenticated');
      return true;
    } catch {
      // A spent or expired credential is not an error worth surfacing: the ordinary path takes over
      // and shows whatever the device is actually entitled to.
      return false;
    }
  }

  async login(username: string, password: string, scope: AuthScope): Promise<AuthResult> {
    try {
      const response = await this.api.login({
        username, password, scope,
        device: this.deviceIdentity.buildLoginInfo(),
      });
      this.applySession(response);
      if (!this.hasSufficientScope()) {
        this.clearSession();
        return { ok: false, message: this.localization.translateKey(AppStrings.Errors.Auth.NotAuthorized) };
      }
      this.deviceIdentity.adopt(response.device);
      this._state.set('authenticated');
      return { ok: true };
    } catch (err) {
      return this.toAuthResult(err, this.localization.translateKey(AppStrings.Errors.Auth.LoginFailed));
    }
  }

  async logout(): Promise<void> {
    try {
      await this.api.logoutSession();
    } catch {
    }
    this.clearSession();
    this._state.set('loggedOut');
    this.api.disconnect();
  }

  async setup(username: string, password: string): Promise<AuthResult> {
    try {
      await this.api.setupUser({ username, password });
      this._state.set('unknown');
      await this.bootstrap();
      return { ok: true };
    } catch (err) {
      return this.toAuthResult(err, this.localization.translateKey(AppStrings.Errors.Auth.SetupFailed));
    }
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<AuthResult> {
    try {
      await this.api.changePassword({ currentPassword, newPassword });
      return { ok: true };
    } catch (err) {
      return this.toAuthResult(err, this.localization.translateKey(AppStrings.Errors.Auth.ChangePasswordFailed));
    }
  }

  async changeUsername(currentPassword: string, newUsername: string): Promise<AuthResult> {
    try {
      await this.api.changeUsername({ currentPassword, newUsername });
      this._username.set(newUsername.trim());
      return { ok: true };
    } catch (err) {
      return this.toAuthResult(err, this.localization.translateKey(AppStrings.Errors.Auth.ChangeUsernameFailed));
    }
  }

  async resume(): Promise<void> {
    if (this._state() !== 'authenticated' || this._trusted()) {
      return;
    }
    if (!this.isAccessTokenExpiringSoon()) {
      return;
    }

    const outcome = await this.tryRefresh();
    if (outcome === 'invalid' && this._state() === 'authenticated') {
      this.sessionLost();
    } else if (outcome === 'unreachable') {
      this.scheduleSessionRetry();
    }
  }

  private isAccessTokenExpiringSoon(): boolean {
    if (this.accessTokenExpiresAt === null) {
      return true;
    }
    return Date.now() >= this.accessTokenExpiresAt - REFRESH_LEEWAY_SECONDS * 1000;
  }

  private refreshExclusively(): Promise<RefreshOutcome> {
    // A lock that failed means the refresh was never attempted, which is what 'unreachable' says.
    // Rejecting instead would leave every caller's retry unarmed, and those timers are the only
    // thing keeping a still-good session alive while the host cannot be reached.
    return runExclusively(AuthService.REFRESH_LOCK_NAME, () => this.refreshOnce(), this.lockEnvironment)
      .catch<RefreshOutcome>(() => 'unreachable');
  }

  private async handleUnauthorized(): Promise<boolean> {
    if (this._trusted()) {
      return false;
    }

    const outcome = await this.tryRefresh();
    if (outcome === 'invalid') {
      if (this._state() === 'authenticated') this.sessionLost();
      return false;
    }
    if (outcome === 'unreachable') {
      this.scheduleSessionRetry();
      return false;
    }
    return true;
  }

  private handleForbidden(): void {
    if (this._trusted() || this._state() !== 'authenticated') {
      return;
    }
    if (!this.hasSufficientScope()) {
      this.sessionLost();
    }
  }

  private subscribeToDeviceSessionRevoked(): void {
    if (this.deviceRevokedSubscribed) {
      return;
    }
    this.deviceRevokedSubscribed = true;
    this.api.onNotification<DeviceSessionRevokedEvent>('DeviceSessionRevokedEvent').subscribe(() => {
      if (this._state() === 'authenticated') {
        this.sessionLost();
      }
    });
  }

  private async handleConnectionUnauthorized(): Promise<void> {
    if (this._trusted()) {
      return;
    }

    const outcome = await this.tryRefresh();
    if (outcome === 'ok') {
      this.api.connect();
    } else if (outcome === 'invalid') {
      this.sessionLost();
    } else {
      // Not a refusal - the session stands, and the socket is left exactly where ApiService put it.
      // `retrySession()`'s eventual `reconnectNow()` is what nudges it again, once the host answers.
      this.scheduleSessionRetry();
    }
  }

  private tryRefresh(): Promise<RefreshOutcome> {
    this.refreshPromise ??= this.refreshExclusively().finally(() => {
      this.refreshPromise = null;
    });
    return this.refreshPromise;
  }

  private async refreshOnce(): Promise<RefreshOutcome> {
    let response: TokenResponse;
    try {
      response = await this.api.refreshSession();
    } catch (err) {
      return isUnreachable(err) ? 'unreachable' : 'invalid';
    }
    // A 2xx carrying no token is a captive portal answering in the host's place, not the host
    // refusing the session - the cookie was never actually asked.
    if (typeof response?.accessToken !== 'string') {
      return 'unreachable';
    }
    this.applySession(response);
    if (!this.hasSufficientScope()) {
      this.clearSession();
      return 'invalid';
    }
    return 'ok';
  }

  private scheduleSessionRetry(): void {
    if (this.sessionRetryTimer !== null) {
      return;
    }
    if (this._trusted() || this._state() !== 'authenticated') {
      return;
    }

    const delayMs = this.sessionRetryDelayMs;
    this.sessionRetryTimer = setTimeout(() => {
      this.sessionRetryTimer = null;
      void this.retrySession();
    }, delayMs);
    this.sessionRetryDelayMs = Math.min(delayMs * 2, MAX_SESSION_RETRY_DELAY_MS);
  }

  private async retrySession(): Promise<void> {
    if (this._trusted() || this._state() !== 'authenticated') {
      return;
    }

    const outcome = await this.tryRefresh();
    if (outcome === 'ok') {
      this.sessionRetryDelayMs = INITIAL_SESSION_RETRY_DELAY_MS;
      this.api.reconnectNow();
    } else if (outcome === 'invalid') {
      this.sessionLost();
    } else {
      this.scheduleSessionRetry();
    }
  }

  private clearSessionRetryTimer(): void {
    this.sessionRetryDelayMs = INITIAL_SESSION_RETRY_DELAY_MS;
    if (this.sessionRetryTimer === null) {
      return;
    }
    clearTimeout(this.sessionRetryTimer);
    this.sessionRetryTimer = null;
  }

  private hasSufficientScope(): boolean {
    return AuthService.scopeSatisfies(this._scope(), this.requiredScope);
  }

  private static scopeSatisfies(scope: AuthScope | null, required: AuthScope | null): boolean {
    if (required === null) {
      return true;
    }
    return scope === 'admin' || scope === required;
  }

  private applySession(response: TokenResponse): void {
    this.accessToken = response.accessToken;
    this._scope.set(response.scope);
    this._username.set(response.username);
    if (response.device?.deviceId) {
      this._currentDeviceId.set(response.device.deviceId);
    }
    this._startupProfileId.set(response.device?.startupProfileId ?? null);
    this.accessTokenExpiresAt = Date.now() + response.expiresInSeconds * 1000;
    this.scheduleRefresh(response.expiresInSeconds);
  }

  private scheduleRefresh(expiresInSeconds: number): void {
    this.clearRefreshTimer();
    const delayMs = Math.max((expiresInSeconds - REFRESH_LEEWAY_SECONDS) * 1000, MIN_REFRESH_DELAY_MS);
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = null;
      void this.tryRefresh().then(outcome => {
        if (this._state() !== 'authenticated') return;
        if (outcome === 'invalid') this.sessionLost();
        else if (outcome === 'unreachable') this.scheduleSessionRetry();
      });
    }, delayMs);
  }

  private sessionLost(): void {
    this.clearSession();
    this._state.set('loggedOut');
    this.api.disconnect();
  }

  ngOnDestroy(): void {
    this.clearRefreshTimer();
    this.clearSessionRetryTimer();
  }

  private clearSession(): void {
    this.accessToken = null;
    this.accessTokenExpiresAt = null;
    this._scope.set(null);
    this._username.set(null);
    this._currentDeviceId.set(null);
    this._startupProfileId.set(null);
    this.clearRefreshTimer();
    this.clearSessionRetryTimer();
  }

  private clearRefreshTimer(): void {
    if (this.refreshTimer !== null) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  private toAuthResult(err: unknown, fallback: string): AuthResult {
    return {
      ok: false,
      message: err instanceof TransportError ? err.message : fallback,
    };
  }
}
