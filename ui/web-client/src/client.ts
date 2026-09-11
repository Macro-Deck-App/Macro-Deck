import {
  type ActionExecutionStatus,
  ClientAppStrings,
  DeckState,
  type ExecuteActionButtonTriggerResponse,
  isFailureExecutionStatus,
  HttpClient,
  UiConnection,
  UiSessionStore,
  type ActionButtonTriggerType,
  type ActionFlow,
  type AuthScope,
  type AuthStatusResponse,
  collectUserAgentInfo,
  type DeviceCredential,
  type DeviceLoginInfo,
  findFlowForTrigger,
  folderFromWire,
  type GetAppearanceSettingsResponse,
  type GetLocalizationResponse,
  type GetProfilesResponse,
  type IpcProfile,
  LocalizationCatalog,
  type LocalizedText,
  type LoginRequest,
  foldersFromWire,
  type GetFoldersResponse,
  type GetKeyRingStatusResponse,
  type ProfileGridDefaults,
  type ReadableStore,
  type ResolvedFolderGrid,
  resolveFolderGrid,
  type Folder,
  type FolderGridSettings,
  type RedeemDeviceEnrollmentRequest,
  type RefreshOutcome,
  isUnreachable,
  resolveLocalDeckNavigation,
  rootNodeOf,
  store,
  type ThemeMode,
  type TokenResponse,
  type UiModalOpenedEvent,
  widgetFromWire,
  WidgetType,
  type WritableStore,
  runExclusively,
  type CrossTabLockEnvironment,
  browserLockEnvironment,
} from '@macro-deck/runtime';
import { AppState } from './app-state';
import { ExecutionFeedback } from './execution-feedback';
import { HostLock } from './host-lock';
import { WidgetSessions } from './widget-sessions';

const REQUESTED_SCOPE: AuthScope = 'client';

const REFRESH_LOCK_NAME = 'macro-deck.auth.refresh';

const REFRESH_LEEWAY_SECONDS = 60;
const MIN_REFRESH_DELAY_MS = 5000;

const INITIAL_LOCALIZATION_RETRY_DELAY_MS = 5000;
const MAX_LOCALIZATION_RETRY_DELAY_MS = 60000;

const INITIAL_SESSION_RETRY_DELAY_MS = 2000;
const MAX_SESSION_RETRY_DELAY_MS = 60000;

const DEVICE_ID_KEY = 'md.device.web-client.id';
const DEVICE_SECRET_KEY = 'md.device.web-client.secret';
const DEVICE_CLIENT_TYPE = 'web-client';

export interface SignInResult {
  ok: boolean;
  message?: string;
}

export interface ClientOptions {
  requiredScope?: AuthScope | null;

  readHash?(): string;
  clearHash?(): void;

  lockEnvironment?: CrossTabLockEnvironment;

  appearance?: AppearanceSink;
}

export interface AppearanceSink {
  applyFromHost(mode: ThemeMode | undefined, accent: string | undefined, fontFamily?: string): void;
}

function defaultReadHash(): string {
  return window.location.hash;
}

function defaultClearHash(): void {
  // replaceState, not a navigation: the credential has to leave the address bar without adding a
  // history entry a Back gesture could return to.
  window.history.replaceState(null, '', window.location.pathname + window.location.search);
}

function readStored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // A client with no storage signs in as a new device every time, which still works.
  }
}

export class Client {
  readonly app = new AppState();
  readonly deck = new DeckState();
  readonly localization = new LocalizationCatalog();
  readonly sessions = new UiSessionStore();
  readonly widgetSessions: WidgetSessions;
  readonly hostLock: HostLock;
  readonly executionFeedback = new ExecutionFeedback(
    this.localization,
    () => this.translate(ClientAppStrings.Errors.Folder.ActionRunFailed));
  readonly http: HttpClient;
  readonly connection: UiConnection;
  readonly modal: WritableStore<UiModalOpenedEvent | null> = store<UiModalOpenedEvent | null>(null);

  private readonly usernameStore: WritableStore<string | null> = store<string | null>(null);
  readonly signedInAs: ReadableStore<string | null> = this.usernameStore;

  private accessToken: string | undefined;
  private accessTokenExpiresAt: number | null = null;
  private scope: AuthScope | null = null;
  private readonly requiredScope: AuthScope | null;
  private readonly readHash: () => string;
  private readonly clearHash: () => void;
  private implicitlyTrusted = false;
  private refreshPromise: Promise<RefreshOutcome> | null = null;
  private readonly lockEnvironment: CrossTabLockEnvironment;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private inAuthRequest = 0;
  private startupProfileId: string | null = null;
  private selectedProfileId: string | null = null;
  private hasConnected = false;
  private deckLoaded = false;
  private sessionRetryTimer: ReturnType<typeof setTimeout> | null = null;
  private sessionRetryDelayMs = INITIAL_SESSION_RETRY_DELAY_MS;
  private hostUnreachable = false;
  private reconnectedAfterRefresh = false;

  private deckMove = 0;

  private pendingNavigation: { folderId: string; token: string } | null = null;
  private lastReportedFolderId: string | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private probing = false;
  private localizationRetryTimer: ReturnType<typeof setTimeout> | null = null;
  private localizationRetryDelayMs = INITIAL_LOCALIZATION_RETRY_DELAY_MS;
  private recoveryPollIntervalMs = 2000;
  private recoveryWatchActive = false;

  private readonly appearance: AppearanceSink | null;

  private gridDefaults: ProfileGridDefaults = {};

  gridFor(folder: FolderGridSettings | null | undefined): ResolvedFolderGrid {
    return resolveFolderGrid(folder, this.gridDefaults, this.ancestorsOf(folder));
  }

  private ancestorsOf(folder: FolderGridSettings | null | undefined): FolderGridSettings[] {
    const start = folder as { id?: unknown; parentId?: unknown } | null | undefined;
    if (!start || typeof start.id !== 'string') return [];

    const folders = this.deck.folders.get();
    const chain: FolderGridSettings[] = [];
    const seen: { [id: string]: true } = {};
    seen[start.id] = true;

    let parentId = typeof start.parentId === 'string' ? start.parentId : null;
    while (parentId !== null && seen[parentId] !== true) {
      seen[parentId] = true;
      let parent: Folder | undefined;
      for (let index = 0; index < folders.length; index++) {
        if (folders[index].id === parentId) { parent = folders[index]; break; }
      }
      if (!parent) break;
      chain.push(parent);
      parentId = parent.parentId;
    }
    return chain;
  }

  constructor(private readonly baseUrl: () => string, clientId: string, options: ClientOptions = {}) {
    this.requiredScope = options.requiredScope === undefined ? null : options.requiredScope;
    this.readHash = options.readHash || defaultReadHash;
    this.clearHash = options.clearHash || defaultClearHash;
    this.lockEnvironment = options.lockEnvironment || browserLockEnvironment();
    this.appearance = options.appearance ?? null;

    this.http = new HttpClient({
      baseUrl,
      accessToken: () => this.accessToken,
      onUnauthorized: () => this.handleUnauthorized(),
      onForbidden: () => this.handleForbidden(),
    });

    this.connection = new UiConnection({
      http: this.http,
      baseUrl,
      clientId,
      onUnauthorized: () => this.handleConnectionUnauthorized(),
    });

    this.widgetSessions = new WidgetSessions(this.connection, this.sessions);
    this.hostLock = new HostLock(this.http);

    this.connection.state.subscribe(state => {
      if (state === 'connected') {
        // A deck that has painted outlives a later drop - see `screenFor`.
        this.app.set({ connected: true, deckRendered: true });
        // The host keeps no sessions across a socket, so they are asked for again rather than reused.
        this.widgetSessions.sync(this.deck.displayedWidgets);
        // A lock or unlock that happened while the socket was down arrived as an event nobody
        // received, so the state is re-read rather than assumed.
        void this.hostLock.load();
        // Everything that changed while this client was away arrived as events nobody received, so a
        // reconnect re-reads the deck rather than trusting what is on screen. The first connect is not
        // one of those, unless `start()`'s own read never landed - a folder read that failed leaves
        // the socket's own connect handler as the only thing left to re-read it.
        if (this.hasConnected || !this.deckLoaded) {
          void this.loadDeck();
          // For the same reason as the lock state: an appearance change pushed while the socket was
          // down was pushed to nobody.
          void this.loadAppearance();
        }
        this.hasConnected = true;
        // A connect that followed a refresh-and-reconnect completed cleanly, so the next 401 on this
        // socket is a fresh occurrence rather than the same tight loop continuing.
        this.reconnectedAfterRefresh = false;
        // Tell the host what this deck shows right now: focus rules take their return-to baseline
        // from these reports, and a socket that was down swallowed any report sent while offline.
        this.reportFolder(true);
        return;
      }
      this.app.set({ connected: false });
      this.sessions.clear();
      this.widgetSessions.reset();
    });

    // A widget that appears needs a session; one that leaves should not keep the host pushing into it.
    const resync = () => {
      if (this.connection.state.get() === 'connected') {
        this.widgetSessions.sync(this.deck.displayedWidgets);
      }
    };
    this.deck.folders.subscribe(resync);
    this.deck.location.subscribe(resync);
    this.deck.location.subscribe(() => this.reportFolder(false));

    this.connection.onNotification((type, payload) => this.onNotification(type, payload));
  }

  watchForRecovery(intervalMs = 2000): () => void {
    this.recoveryPollIntervalMs = intervalMs;
    this.recoveryWatchActive = true;
    const stop = this.app.screen.subscribe(() => {
      if (this.shouldWatchForRecovery()) this.schedulePoll(intervalMs);
      else this.clearPoll();
    });

    if (this.shouldWatchForRecovery()) this.schedulePoll(intervalMs);

    return () => { this.recoveryWatchActive = false; stop(); this.clearPoll(); };
  }

  private shouldWatchForRecovery(): boolean {
    const screen = this.app.screen.get();
    return screen === 'setupRequired' || screen === 'keyRingLocked' || this.hostUnreachable;
  }

  private pollForRecoveryIfNeeded(): void {
    if (!this.recoveryWatchActive) return;
    if (this.shouldWatchForRecovery()) this.schedulePoll(this.recoveryPollIntervalMs);
  }

  private schedulePoll(intervalMs: number): void {
    if (this.pollTimer !== null || this.probing) return;
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      void this.probe().then(() => {
        if (this.shouldWatchForRecovery()) this.schedulePoll(intervalMs);
      });
    }, intervalMs);
  }

  private clearPoll(): void {
    if (this.pollTimer === null) return;
    clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }

  async probe(): Promise<void> {
    if (this.probing) return;
    this.probing = true;
    this.hostUnreachable = false;
    try {
      const keyRing = await this.http.get<GetKeyRingStatusResponse>('/api/key-ring/status');
      if (keyRing.locked) {
        this.app.set({ keyRingLocked: true, probed: true });
        return;
      }
      this.app.set({ keyRingLocked: false });

      // Before anything else: a device nobody can type a password into may have been handed a
      // one-time credential by its setup. Spending it here means the ordinary path below finds an
      // established session instead of a sign-in form that will never be filled in.
      if (await this.tryRedeemEnrollment()) {
        this.app.set({ setupRequired: false, authenticated: true, probed: true });
        await this.start();
        return;
      }

      const auth = await this.http.get<AuthStatusResponse>('/api/auth/status');
      // `setupComplete`, not `setupRequired`: the host reports what is done, not what is missing.
      if (!auth.setupComplete) {
        this.app.set({ setupRequired: true, authenticated: false, probed: true });
        return;
      }
      this.app.set({ setupRequired: false });

      // `authenticated` alone is not enough to work with. A status that is merely cookie-authenticated
      // still holds no access token, and the socket ticket would go out without one; only the trusted
      // loopback transport is genuinely signed in without tokens. Everyone else proves it by spending
      // the refresh cookie, which is also what restores a session across a reload.
      let authenticated: boolean;
      if (auth.trusted) {
        this.implicitlyTrusted = true;
        this.scope = auth.scope || 'admin';
        this.usernameStore.set(auth.username || null);
        authenticated = true;
      } else {
        const outcome = await this.tryRefresh();
        if (outcome === 'unreachable') {
          // Not `probed: true` - a captive portal or a host that answers `/api/auth/status` but not
          // `/api/auth/refresh` must not present a login form nobody can get past with a still-good
          // cookie. `screenFor` stays on `'starting'`, which paints the same "still trying" panel the
          // catch below paints for an outright unreachable host.
          this.hostUnreachable = true;
          return;
        }
        authenticated = outcome === 'ok';
      }

      this.app.set({ authenticated, probed: true });
      if (authenticated) await this.start();
    } catch (error) {
      // An unreachable host is not a locked one and not a signed-out one: settle on the screen that
      // says "still trying" rather than inventing a reason. Not `probed: true` either - that would
      // paint the sign-in form, which a still-good session has no business being sent back to just
      // because the very first request of this probe never reached the host at all.
      if (isUnreachable(error)) {
        this.hostUnreachable = true;
        return;
      }
      this.app.set({ probed: true, authenticated: false });
    } finally {
      this.probing = false;
      this.pollForRecoveryIfNeeded();
    }
  }

  async signIn(username: string, password: string): Promise<SignInResult> {
    const request: LoginRequest = {
      username,
      password,
      scope: REQUESTED_SCOPE,
      device: this.deviceLoginInfo(),
    };

    let response: TokenResponse;
    try {
      response = await this.authRequest<TokenResponse>('/api/auth/login', request);
    } catch {
      // The host's own problem text is not localized, so the catalogue answers instead.
      return { ok: false, message: this.translate(ClientAppStrings.Auth.SignInFailed) };
    }

    if (typeof response.accessToken !== 'string') {
      return { ok: false, message: this.translate(ClientAppStrings.Auth.SignInFailed) };
    }

    this.applySession(response);
    if (!this.hasSufficientScope()) {
      this.clearSession();
      return { ok: false, message: this.translate(ClientAppStrings.Errors.Auth.NotAuthorized) };
    }

    this.adoptDevice(response.device);
    this.app.set({ authenticated: true });
    await this.start();
    return { ok: true };
  }

  async signOut(): Promise<void> {
    try {
      await this.authRequest('/api/auth/logout');
    } catch {
      // Nothing to recover: the session ends here regardless of what the host answered.
    }
    this.endSession();
  }

  async start(): Promise<void> {
    // Before the deck, because the deck is what needs it: a widget's text and the language its dates
    // are written in both come from here, and loading it afterwards paints everything twice.
    await this.loadLocalization();
    await this.loadAppearance();
    try {
      await this.loadDeck();
      this.deckLoaded = true;
    } catch {
      // A network hiccup here must not keep the client off the socket: the `connection.state`
      // handler re-reads the deck on the connect that follows, once `deckLoaded` says the first read
      // never landed.
    }
    this.connection.connect();
  }

  private async loadDeck(): Promise<void> {
    const move = ++this.deckMove;
    const profileId = await this.resolveProfile();
    const query = profileId === null ? '' : `?profileId=${encodeURIComponent(profileId)}`;
    const response = await this.http.get<GetFoldersResponse>(`/api/folders${query}`);
    // Superseded while the host was answering - a switch to another profile, or a newer reload.
    // Applying this now would put one profile's folders under another's grid.
    if (move !== this.deckMove) return;

    this.deck.load(foldersFromWire(response.folders));
  }

  private async resolveProfile(): Promise<string | null> {
    let profiles: IpcProfile[];
    try {
      const response = await this.http.get<GetProfilesResponse>('/api/profiles');
      profiles = (response.profiles || []).slice();
    } catch {
      return this.selectedProfileId;
    }

    const current = this.selectedProfileId;
    if (current !== null && Client.hasProfile(profiles, current)) {
      // Re-read rather than kept: a profile whose grid was edited while this client was away pushed
      // the event to nobody, and the deck came back drawing the shape it had before.
      this.rememberGridDefaults(profiles, current);
      return current;
    }
    if (profiles.length === 0) return null;

    profiles.sort((left, right) => left.order - right.order);
    const preferred = this.startupProfileId !== null && Client.hasProfile(profiles, this.startupProfileId)
      ? this.startupProfileId
      : profiles[0].id;
    this.selectedProfileId = preferred;
    this.rememberGridDefaults(profiles, preferred);
    return preferred;
  }

  private rememberGridDefaults(profiles: IpcProfile[], profileId: string): void {
    for (let index = 0; index < profiles.length; index++) {
      if (profiles[index].id !== profileId) continue;

      const profile = profiles[index];
      this.gridDefaults = {
        columns: profile.defaultColumns,
        rows: profile.defaultRows,
        spacing: profile.defaultWidgetSpacing,
        borderRadius: profile.defaultWidgetBorderRadius,
      };
      return;
    }
  }

  private navigateTo(folderId: string, profileId: string | null, navigationToken: string | null): void {
    if (profileId === null || profileId === this.selectedProfileId) {
      this.deckMove++;
      this.pendingNavigation = navigationToken === null ? null : { folderId, token: navigationToken };
      this.deck.openFolder(folderId);
      return;
    }

    void this.switchProfile(profileId, folderId, navigationToken);
  }

  private async switchProfile(
    profileId: string, folderId: string, navigationToken: string | null): Promise<void> {
    const move = ++this.deckMove;

    let profiles: IpcProfile[];
    let folders: Folder[];
    try {
      // Independent reads: a wall-mounted tablet pays the round trips serially otherwise.
      const answers = await Promise.all([
        this.http.get<GetProfilesResponse>('/api/profiles'),
        this.http.get<GetFoldersResponse>(`/api/folders?profileId=${encodeURIComponent(profileId)}`),
      ]);
      profiles = answers[0].profiles || [];
      folders = foldersFromWire(answers[1].folders);
    } catch {
      this.openLoadedFolder(folderId, navigationToken, move);
      return;
    }

    // Superseded while the host was answering: a newer navigation, or a reconnect's reload, which
    // leaves the deck whole on the profile it was already showing. Nothing here is written - half of
    // this switch would be one profile's folders under another's grid, which is the bug itself.
    if (move !== this.deckMove) return;

    if (!Client.hasProfile(profiles, profileId) || folders.length === 0) {
      this.openLoadedFolder(folderId, navigationToken, move);
      return;
    }

    // The grid defaults before the deck: `load` notifies its subscribers inside the call, and the
    // repaint they run resolves every inheriting folder against whatever is set by then.
    this.selectedProfileId = profileId;
    this.rememberGridDefaults(profiles, profileId);
    this.pendingNavigation = navigationToken === null ? null : { folderId, token: navigationToken };
    this.deck.load(folders, folderId);
  }

  private openLoadedFolder(folderId: string, navigationToken: string | null, move: number): void {
    if (move !== this.deckMove || !this.deck.folder(folderId)) return;

    this.pendingNavigation = navigationToken === null ? null : { folderId, token: navigationToken };
    this.deck.openFolder(folderId);
  }

  private static hasProfile(profiles: { id: string }[], id: string): boolean {
    for (let index = 0; index < profiles.length; index++) {
      if (profiles[index].id === id) return true;
    }
    return false;
  }

  private async loadAppearance(): Promise<void> {
    if (this.appearance === null) return;
    try {
      const settings = await this.http.get<GetAppearanceSettingsResponse>('/api/settings/appearance');
      this.appearance.applyFromHost(settings.themeMode, settings.accentColor, settings.fontFamily);
    } catch {
      // The cached appearance stands rather than snapping back to the default.
    }
  }

  async saveAppearance(mode: ThemeMode, accent: string): Promise<void> {
    try {
      await this.http.put('/api/settings/appearance', { themeMode: mode, accentColor: accent });
    } catch {
      // The choice still applies here; the host simply did not take it.
    }
  }

  // A widget's or plugin's text has no bundled fallback (#833's slice), so a failed fetch here leaves
  // it painting `[[scope:key]]` until a retry catches up; the first attempt still returns promptly so
  // start()/signIn() never block on it.
  private async loadLocalization(): Promise<void> {
    try {
      this.localization.apply(await this.http.get<GetLocalizationResponse>('/api/localization'));
      this.clearLocalizationRetryTimer();
      this.localizationRetryDelayMs = INITIAL_LOCALIZATION_RETRY_DELAY_MS;
    } catch {
      this.scheduleLocalizationRetry();
    }
  }

  private scheduleLocalizationRetry(): void {
    if (this.localizationRetryTimer !== null) return;
    const delayMs = this.localizationRetryDelayMs;
    this.localizationRetryTimer = setTimeout(() => {
      this.localizationRetryTimer = null;
      void this.loadLocalization();
    }, delayMs);
    this.localizationRetryDelayMs = Math.min(delayMs * 2, MAX_LOCALIZATION_RETRY_DELAY_MS);
  }

  private clearLocalizationRetryTimer(): void {
    if (this.localizationRetryTimer === null) return;
    clearTimeout(this.localizationRetryTimer);
    this.localizationRetryTimer = null;
  }

  async executeTrigger(widgetId: string, triggerType: ActionButtonTriggerType): Promise<void> {
    const widget = this.deck.widget(widgetId);
    if (!widget) return;

    // The two types whose tree claims the press fire through the widget-tree event pipeline instead,
    // and would run their flows twice if they also came through here.
    if (widget.type === WidgetType.ActionButton || widget.type === WidgetType.Slider) return;

    const flows = (widget.data as { flows?: ActionFlow[] }).flows;
    if (Array.isArray(flows)) {
      const navigation = resolveLocalDeckNavigation(flows, triggerType);
      if (navigation) {
        // A change-folder naming a folder this deck does not hold is a jump into another profile,
        // which is the host's to answer: resolved here it would open a folder that is not loaded.
        if (navigation.command !== 'changeTo' || this.deck.folder(navigation.folderId)) {
          this.deckMove++;
          if (navigation.command === 'changeTo') this.deck.openFolder(navigation.folderId);
          else if (navigation.command === 'parent') this.deck.parent();
          else this.deck.back();
          return;
        }
      } else {
        const flow = findFlowForTrigger(flows, triggerType);
        if (!flow || flow.children.length === 0) return;
      }
    }

    // One tap fires the tile's whole lifecycle - onTouchStart, then onTouchEnd and onShortPress - so
    // only the gesture the person actually performed reports a refusal. Reporting the lifecycle too
    // would say the same thing three times for one press of an unconfigured tile, whose data carries
    // no flows to have returned above.
    const gesture = triggerType === 'onShortPress' || triggerType === 'onLongPress';

    let response: ExecuteActionButtonTriggerResponse;
    try {
      response = await this.http.post<ExecuteActionButtonTriggerResponse>('/api/actions/execute', {
        widgetId,
        folderId: widget.folderId || this.deck.location.get().folderId,
        triggerType,
        clientId: this.connection.clientId,
      });
    } catch {
      // The deck stays exactly as it was, but a press that never reached the host is still a press
      // that did nothing, and saying nothing reads as if it worked.
      if (gesture) this.executionFeedback.report();
      return;
    }

    // `Accepted` is not an outcome - the run outlived the host's response bound and reports itself
    // later on a pushed ActionExecutionStatusEvent - and the host sends it as a success. Every other
    // answer is final here, which is why a bare `success: false` has to count too: an older host
    // reports a failure without naming a status.
    if (gesture && (isFailureExecutionStatus(response?.status) || response?.success === false)) {
      this.executionFeedback.report(response?.error);
    }
  }

  completeModal(modalId: string, cancelled: boolean, value?: unknown): void {
    void this.connection.request('CompleteUiModal', [{ modalId, cancelled, value }])
      .catch(() => undefined);
    this.modal.set(null);
  }

  translate(qualifiedKey: string, args?: Record<string, unknown>): string {
    const separator = qualifiedKey.indexOf(':');
    if (separator < 0) return `[[${qualifiedKey}]]`;
    return this.localization.translate(
      qualifiedKey.slice(0, separator), qualifiedKey.slice(separator + 1), args);
  }

  async resume(): Promise<void> {
    if (this.implicitlyTrusted) return;
    if (!this.app.conditions.get().authenticated) return;
    if (!this.isAccessTokenExpiringSoon()) return;

    const outcome = await this.tryRefresh();
    if (outcome === 'invalid') this.endSession();
    else if (outcome === 'unreachable') this.scheduleSessionRetry();
  }

  private isAccessTokenExpiringSoon(): boolean {
    if (this.accessTokenExpiresAt === null) return true;
    return Date.now() >= this.accessTokenExpiresAt - REFRESH_LEEWAY_SECONDS * 1000;
  }

  private async authRequest<T>(path: string, body?: unknown): Promise<T> {
    this.inAuthRequest++;
    try {
      return await this.http.post<T>(path, body);
    } finally {
      this.inAuthRequest--;
    }
  }

  private async handleUnauthorized(): Promise<boolean> {
    if (this.implicitlyTrusted || this.inAuthRequest > 0) return false;

    const outcome = await this.tryRefresh();
    if (outcome === 'invalid') {
      if (this.app.conditions.get().authenticated) this.endSession();
      return false;
    }
    if (outcome === 'unreachable') { this.scheduleSessionRetry(); return false; }
    return true;
  }

  private handleForbidden(): void {
    if (this.implicitlyTrusted || !this.app.conditions.get().authenticated) return;
    if (!this.hasSufficientScope()) this.endSession();
  }

  private async handleConnectionUnauthorized(): Promise<boolean> {
    if (this.implicitlyTrusted) return false;

    const outcome = await this.tryRefresh();
    if (outcome === 'invalid') { this.endSession(); return true; }
    if (outcome === 'unreachable') { this.scheduleSessionRetry(); return false; }

    // A ticket refused again right after a refresh-and-reconnect is not a stale credential any more
    // - it is the host rejecting a fresh one, which reconnecting immediately would spin against once
    // per socket attempt with no backoff at all. Taking ownership here (returning true) keeps this
    // connection's own fixed-interval retry from also firing; the session retry's backoff is what
    // eventually tries again, via `reconnectNow()`.
    if (this.reconnectedAfterRefresh) { this.scheduleSessionRetry(); return true; }

    this.reconnectedAfterRefresh = true;
    this.connection.connect();
    return true;
  }

  // One refresh at a time, in this page and across every tab of this origin. Two in flight rotate the
  // same cookie twice, and the host reads the second use of a rotated refresh token as theft and
  // revokes every session the account has. In-page single-flight cannot see the other tab, and
  // / and /admin share this origin, so there usually is one.
  private tryRefresh(): Promise<RefreshOutcome> {
    if (this.refreshPromise === null) {
      this.refreshPromise = runExclusively(
        REFRESH_LOCK_NAME,
        () => this.refreshOnce(),
        this.lockEnvironment,
      ).then(outcome => {
        this.refreshPromise = null;
        return outcome;
      }, () => {
        this.refreshPromise = null;
        // The lock itself failed, so the refresh was never attempted - which is the one thing this
        // is certain of, and exactly what `'unreachable'` means. Rethrowing instead would leave
        // every caller's timer unarmed: these timers are now the only thing keeping a still-good
        // session alive, and a session lost to a failed lock is the bug this class exists to avoid.
        return 'unreachable' as RefreshOutcome;
      });
    }
    return this.refreshPromise;
  }

  private async refreshOnce(): Promise<RefreshOutcome> {
    let response: TokenResponse;
    try {
      response = await this.authRequest<TokenResponse>('/api/auth/refresh');
    } catch (error) {
      return isUnreachable(error) ? 'unreachable' : 'invalid';
    }
    // A 2xx carrying no token is a captive portal answering in the host's place, not the host
    // refusing the session - the cookie was never actually asked.
    if (typeof response.accessToken !== 'string') return 'unreachable';

    this.applySession(response);
    if (!this.hasSufficientScope()) {
      this.clearSession();
      return 'invalid';
    }
    return 'ok';
  }

  private hasSufficientScope(): boolean {
    if (this.requiredScope === null) return true;
    return this.scope === 'admin' || this.scope === this.requiredScope;
  }

  private applySession(response: TokenResponse): void {
    this.accessToken = response.accessToken;
    this.scope = response.scope;
    this.usernameStore.set(response.username);
    // Set from every login and refresh, including the ones that carry no device at all: a session
    // that stopped being tied to a device has no startup profile either, and keeping the last one
    // would scope the deck to a profile this session was never assigned.
    this.startupProfileId = response.device && response.device.startupProfileId
      ? response.device.startupProfileId
      : null;
    this.accessTokenExpiresAt = Date.now() + response.expiresInSeconds * 1000;
    this.scheduleRefresh(response.expiresInSeconds);
  }

  private scheduleRefresh(expiresInSeconds: number): void {
    this.clearRefreshTimer();
    const delayMs = Math.max(
      (expiresInSeconds - REFRESH_LEEWAY_SECONDS) * 1000, MIN_REFRESH_DELAY_MS);
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = null;
      void this.tryRefresh().then(outcome => {
        if (!this.app.conditions.get().authenticated) return;
        if (outcome === 'invalid') this.endSession();
        else if (outcome === 'unreachable') this.scheduleSessionRetry();
      });
    }, delayMs);
  }

  private clearRefreshTimer(): void {
    if (this.refreshTimer === null) return;
    clearTimeout(this.refreshTimer);
    this.refreshTimer = null;
  }

  private scheduleSessionRetry(): void {
    if (this.sessionRetryTimer !== null) return;
    if (this.implicitlyTrusted || !this.app.conditions.get().authenticated) return;

    const delayMs = this.sessionRetryDelayMs;
    this.sessionRetryTimer = setTimeout(() => {
      this.sessionRetryTimer = null;
      void this.retrySession();
    }, delayMs);
    this.sessionRetryDelayMs = Math.min(delayMs * 2, MAX_SESSION_RETRY_DELAY_MS);
  }

  private async retrySession(): Promise<void> {
    if (this.implicitlyTrusted || !this.app.conditions.get().authenticated) return;

    const outcome = await this.tryRefresh();
    if (outcome === 'ok') {
      this.sessionRetryDelayMs = INITIAL_SESSION_RETRY_DELAY_MS;
      this.reconnectedAfterRefresh = false;
      this.connection.reconnectNow();
    } else if (outcome === 'invalid') {
      this.endSession();
    } else {
      this.scheduleSessionRetry();
    }
  }

  private clearSessionRetryTimer(): void {
    this.sessionRetryDelayMs = INITIAL_SESSION_RETRY_DELAY_MS;
    if (this.sessionRetryTimer === null) return;
    clearTimeout(this.sessionRetryTimer);
    this.sessionRetryTimer = null;
  }

  retryNow(): void {
    if (this.app.conditions.get().authenticated) {
      this.clearSessionRetryTimer();
      void this.retrySession();
      this.connection.reconnectNow();
    } else if (this.hostUnreachable) {
      void this.probe();
    }
  }

  private reportFolder(isResync: boolean): void {
    if (this.connection.state.get() !== 'connected') return;
    const folderId = this.deck.location.get().folderId;
    if (folderId === null) return;
    if (!isResync && folderId === this.lastReportedFolderId) return;
    this.lastReportedFolderId = folderId;
    const pending = this.pendingNavigation;
    this.pendingNavigation = null;
    this.connection.request('ReportFolderChanged', {
      folderId,
      clientId: this.connection.clientId,
      navigationToken: pending !== null && pending.folderId === folderId ? pending.token : undefined,
      isResync,
    }).catch(() => undefined);
  }

  private endSession(): void {
    this.clearSession();
    this.widgetSessions.forgetAll();
    this.app.set({ authenticated: false, deckRendered: false });
    this.connection.disconnect();
  }

  private clearSession(): void {
    this.accessToken = undefined;
    this.accessTokenExpiresAt = null;
    this.scope = null;
    this.implicitlyTrusted = false;
    this.usernameStore.set(null);
    this.startupProfileId = null;
    this.selectedProfileId = null;
    this.hasConnected = false;
    this.deckLoaded = false;
    this.reconnectedAfterRefresh = false;
    this.clearRefreshTimer();
    this.clearLocalizationRetryTimer();
    this.clearSessionRetryTimer();
  }

  private async tryRedeemEnrollment(): Promise<boolean> {
    const match = /(?:^|[#&])enroll=([^&]+)/.exec(this.readHash());
    if (!match) return false;

    this.clearHash();

    const request: RedeemDeviceEnrollmentRequest = {
      token: decodeURIComponent(match[1]),
      device: this.deviceLoginInfo(),
    };

    let response: TokenResponse;
    try {
      response = await this.authRequest<TokenResponse>('/api/auth/device-enrollment/redeem', request);
    } catch {
      // A spent or expired credential is not worth reporting: the ordinary path takes over and shows
      // whatever this device is actually entitled to.
      return false;
    }
    if (typeof response.accessToken !== 'string') return false;

    this.applySession(response);
    if (!this.hasSufficientScope()) {
      this.clearSession();
      return false;
    }

    this.adoptDevice(response.device);
    return true;
  }

  private deviceLoginInfo(): DeviceLoginInfo {
    const agent = collectUserAgentInfo();
    const deviceId = readStored(DEVICE_ID_KEY);
    const deviceSecret = readStored(DEVICE_SECRET_KEY);
    return {
      deviceId: deviceId === null ? undefined : deviceId,
      deviceSecret: deviceSecret === null ? undefined : deviceSecret,
      clientType: DEVICE_CLIENT_TYPE,
      proposedName: agent.proposedName,
      platform: agent.platform === null ? undefined : agent.platform,
      browser: agent.browser === null ? undefined : agent.browser,
      formFactor: agent.formFactor,
    };
  }

  private adoptDevice(credential: DeviceCredential | undefined): void {
    if (!credential || !credential.deviceId) return;
    writeStored(DEVICE_ID_KEY, credential.deviceId);
    // Only ever sent back when it was just minted; the stored one stands otherwise.
    if (credential.deviceSecret) writeStored(DEVICE_SECRET_KEY, credential.deviceSecret);
  }

  private onNotification(type: string, payload: unknown): void {
    const body = (payload ?? {}) as Record<string, unknown>;

    switch (type) {
      case 'FolderCreatedEvent':
      case 'FolderUpdatedEvent':
        if (body['folder']) this.deck.folderUpserted(folderFromWire(body['folder'] as never));
        break;
      case 'FolderDeletedEvent':
        if (typeof body['folderId'] === 'string') this.deck.folderDeleted(body['folderId']);
        break;
      case 'ProfileUpdatedEvent': {
        // Most folders state no grid of their own and inherit the profile's, which this client
        // reads once while resolving the profile it starts on. Without this the deck went on
        // drawing the shape the profile had at connect time: changing the column count left every
        // inheriting folder - which is most of them - on the old one until a reload.
        const profile = body['profile'] as IpcProfile | undefined;
        if (profile && profile.id === this.selectedProfileId) {
          this.rememberGridDefaults([profile], profile.id);
          // Re-emits the folders so the deck re-resolves and repaints; the location is preserved.
          this.deck.load(this.deck.folders.get());
        }
        break;
      }
      case 'WidgetCreatedEvent':
      case 'WidgetUpdatedEvent':
        if (typeof body['folderId'] === 'string' && body['widget']) {
          this.deck.widgetsUpserted(
            body['folderId'], [widgetFromWire(body['widget'] as never, body['folderId'])]);
        }
        break;
      case 'WidgetsCreatedEvent':
      case 'WidgetsUpdatedEvent':
      case 'WidgetPositionsUpdatedEvent':
        if (typeof body['folderId'] === 'string' && Array.isArray(body['widgets'])) {
          const folderId = body['folderId'];
          this.deck.widgetsUpserted(
            folderId, (body['widgets'] as never[]).map(widget => widgetFromWire(widget, folderId)));
        }
        break;
      case 'WidgetDeletedEvent':
        if (typeof body['folderId'] === 'string' && typeof body['widgetId'] === 'string') {
          this.deck.widgetDeleted(body['folderId'], body['widgetId']);
        }
        break;
      case 'WidgetsDeletedEvent':
        if (typeof body['folderId'] === 'string' && Array.isArray(body['widgetIds'])) {
          const folderId = body['folderId'];
          for (const widgetId of body['widgetIds'] as string[]) {
            this.deck.widgetDeleted(folderId, widgetId);
          }
        }
        break;
      case 'UiSessionTreeUpdatedEvent': {
        // The tree arrives as a document - revision, surface, root - not as a bare node.
        const root = rootNodeOf(body['tree']);
        if (typeof body['sessionId'] === 'string' && typeof body['revision'] === 'number' && root) {
          this.sessions.treeUpdated(body['sessionId'], body['revision'], root);
        }
        break;
      }
      case 'UiSessionPatchedEvent':
        if (typeof body['sessionId'] === 'string') {
          this.sessions.patched(body['sessionId'], body['patch'] as never);
        }
        break;
      case 'UiSessionInvalidatedEvent':
      case 'UiSessionClosedEvent':
        if (typeof body['sessionId'] === 'string') {
          this.sessions.invalidated(body['sessionId']);
          this.widgetSessions.sessionClosed(body['sessionId']);
          // The widget is still on the deck, so it needs a session again.
          this.widgetSessions.sync(this.deck.displayedWidgets);
        }
        break;
      case 'FolderNavigationEvent':
        // A deck action the host ran on this client's behalf; the client performs the move itself.
        switch (body['command']) {
          case 'changeTo':
            if (typeof body['folderId'] === 'string') {
              this.navigateTo(
                body['folderId'],
                typeof body['profileId'] === 'string' ? body['profileId'] : null,
                typeof body['navigationToken'] === 'string' ? body['navigationToken'] : null);
            }
            break;
          case 'parent':
            this.deckMove++;
            this.deck.parent();
            break;
          case 'back':
            this.deckMove++;
            this.deck.back();
            break;
          default:
            break;
        }
        break;
      case 'UiModalOpenedEvent':
        if (typeof body['modalId'] === 'string') {
          this.modal.set({ modalId: body['modalId'], title: body['title'] as never });
        }
        break;
      case 'AppearanceChangedEvent':
        // Pushed to every client the moment someone changes it, so a deck on a wall follows the
        // change without being reloaded.
        this.appearance?.applyFromHost(
          body['themeMode'] as ThemeMode | undefined,
          typeof body['accentColor'] === 'string' ? body['accentColor'] : undefined,
          typeof body['fontFamily'] === 'string' ? body['fontFamily'] : undefined);
        break;
      case 'LocalizationCultureChangedEvent':
      case 'LocalizationCatalogChangedEvent':
        // Both mean the same thing here: what this client holds is stale, so ask for it again.
        void this.loadLocalization();
        break;
      case 'DeviceSessionRevokedEvent':
        // Only ever pushed to the device being signed out. Treated as a lost session rather than a
        // dropped socket: reconnecting would only be refused, and the sign-in screen is the answer.
        if (this.app.conditions.get().authenticated) this.endSession();
        break;
      case 'ActionExecutionStatusEvent':
        // The only failure signal a widget whose tree claims the press ever gets, and the second
        // half of the REST path's own: a run that answered `Accepted` reports its real outcome here.
        if (isFailureExecutionStatus(body['status'] as ActionExecutionStatus | undefined)) {
          this.executionFeedback.report(body['error'] as { message?: LocalizedText } | undefined);
        }
        break;
      case 'HostLockStateChangedEvent':
        // Not the key ring. The host computer locking is a different thing from the key ring being
        // locked, and mapping one onto the other sent every client to the key-ring panel the moment
        // someone locked their desk.
        this.hostLock.apply({
          locked: body['locked'] === true,
          lockScreenEnabled: body['lockScreenEnabled'] === true,
          supported: body['supported'] === true,
        });
        break;
      default:
        break;
    }
  }
}
