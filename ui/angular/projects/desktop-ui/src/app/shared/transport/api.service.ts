import { Injectable, Signal, inject, signal } from '@angular/core';
import { BehaviorSubject, Subject, Observable, filter, map } from 'rxjs';
import { HOST_URL_RESOLVER } from './host-url';
import {
  AdbStateChangedEvent,
  AdvanceWebClientTargetProvisioningRequest,
  ApiError,
  ApprovePluginPairingRequestRequest,
  AuthStatusResponse,
  BackupRecoveryKeyResponse,
  BindCatalogVariableRequest,
  BindCatalogVariableResponse,
  CancelIconImportBatchResponse,
  CancelRestoreRequest,
  CancelRestoreResponse,
  ChangePasswordRequest,
  ChangeUsernameRequest,
  CloneSecretResponse,
  CommitRestoreRequest,
  CommitRestoreResponse,
  CompleteUiModalRequest,
  CompleteUiModalResponse,
  ConnectSessionChangedNotification,
  CreateAutomationRequest,
  CreateAutomationResponse,
  CreateBackupRequest,
  CreateBackupResponse,
  CreateFolderRequest,
  CreateFolderResponse,
  CreateIconPackRequest,
  CreateIconPackResponse,
  CreatePluginTokenRequest,
  CreatePluginTokenResponse,
  CreateProfileRequest,
  CreateProfileResponse,
  CreateScriptRequest,
  CreateScriptResponse,
  CreateSecretRequest,
  CreateSecretResponse,
  CreateVariableRequest,
  CreateVariableResponse,
  CreateWidgetFromApplicationRequest,
  CreateWidgetRequest,
  CreateWidgetResponse,
  CreateWidgetsRequest,
  CreateWidgetsResponse,
  DeleteAutomationResponse,
  DeleteBackupResponse,
  DeleteConfigEntryResponse,
  DeleteFolderFocusRuleResponse,
  DeleteFolderRequest,
  DeleteFolderResponse,
  DeleteIconPackResponse,
  DeleteIconResponse,
  DeleteIconsRequest,
  DeleteIconsResponse,
  DeletePluginTokenResponse,
  DeleteProfileRequest,
  DeleteProfileResponse,
  DeleteScriptResponse,
  DeleteSecretResponse,
  DeleteVariableRequest,
  DeleteVariableResponse,
  DeleteWidgetRequest,
  DeleteWidgetResponse,
  DeleteWidgetsRequest,
  DeleteWidgetsResponse,
  DiscoverCatalogVariablesRequest,
  DiscoverCatalogVariablesResponse,
  DownloadAdbPlatformToolsResponse,
  DuplicateAutomationResponse,
  DuplicateFolderRequest,
  DuplicateFolderResponse,
  DuplicateScriptResponse,
  EvaluateConditionRequest,
  EvaluateConditionResponse,
  EvaluateExpressionRequest,
  EvaluateExpressionResponse,
  ExecuteActionButtonTriggerRequest,
  ExecuteActionButtonTriggerResponse,
  ExecuteActionRequest,
  ExecuteActionResponse,
  ExportFolderRequest,
  ExportProfileRequest,
  ExportWidgetsRequest,
  FolderViewCatalogChangedEvent,
  GetAboutInfoResponse,
  GetActionButtonStateOptionsRequest,
  GetActionButtonStateOptionsResponse,
  GetActionParameterOptionsRequest,
  GetActionParameterOptionsResponse,
  GetActionProviderIconRequest,
  GetActionProviderIconResponse,
  GetActionsResponse,
  GetAdbSettingsResponse,
  GetAppearanceSettingsResponse,
  GetApplicationFocusCapabilityResponse,
  GetAutomationsResponse,
  GetAutostartSettingsResponse,
  GetBackupRecoveryKeyStateResponse,
  GetBackupSettingsResponse,
  GetBackupsResponse,
  GetBackupStatusResponse,
  GetConfigEntriesResponse,
  GetConnectionInfoResponse,
  GetConnectSessionResponse,
  GetDataDirectoryResponse,
  CompleteOnboardingResponse,
  GetDeveloperSettingsResponse,
  GetOnboardingStateResponse,
  GetDeviceSetupResponse,
  GetDevicesResponse,
  GetVariableCatalogProvidersResponse,
  GetEventDefinitionsResponse,
  GetFilesystemEntriesRequest,
  GetFilesystemEntriesResponse,
  GetFolderFocusRulesResponse,
  GetFoldersResponse,
  GetFolderViewsResponse,
  GetHostLockStateResponse,
  GetHostSessionResponse,
  GetIconImportBatchResponse,
  GetIconPacksResponse,
  GetIconsRequest,
  GetIconsResponse,
  GetInstalledPluginsResponse,
  GetIntegrationCapabilitiesResponse,
  GetIntegrationIssuesResponse,
  GetIntegrationsResponse,
  GetKeyRingProtectionResponse,
  GetKeyRingStatusResponse,
  GetLocalizationResponse,
  GetLocalizationSettingsResponse,
  GetLockScreenSettingsResponse,
  GetLoggingSettingsResponse,
  GetLogSourcesResponse,
  GetLogsRequest,
  GetLogsResponse,
  GetMusicPlayerInstancesResponse,
  GetMusicPlayerStateResponse,
  GetNetworkSettingsResponse,
  GetPairedPluginsResponse,
  GetPluginCompatibilityResponse,
  GetPluginPairingRequestsResponse,
  GetPluginRuntimeResponse,
  GetPluginSessionsResponse,
  GetPluginTokensResponse,
  GetProfilesResponse,
  GetRunningApplicationsResponse,
  GetScriptsResponse,
  GetScriptUsagesResponse,
  GetServerTimeResponse,
  GetStoreCatalogResponse,
  GetStoreExtensionResponse,
  GetStoreOperationsResponse,
  GetStoreStatusResponse,
  GetStoreUpdatesResponse,
  GetSystemFontsResponse,
  GetUserNotificationsResponse,
  GetVariablesRequest,
  GetVariablesResponse,
  GetVersionResponse,
  GetWeatherInstancesResponse,
  GetWeatherStateResponse,
  GetWidgetDataSchemasResponse,
  HostLockStateChangedEvent,
  ImportFolderResponse,
  ImportIconPacksResponse,
  ImportIconsFromPathRequest,
  ImportIconsResponse,
  ImportProfileResponse,
  ImportSingleIconFromPathRequest,
  ImportSingleIconResponse,
  ImportWidgetsResponse,
  InspectArchiveResponse,
  InspectBackupResponse,
  InstallStoreExtensionRequest,
  LabelImagePreviewRequest,
  ListUiPreviewsResponse,
  LocalizedText,
  LoginRequest,
  LogoutDeviceResponse,
  MigrationImportResponse,
  MigrationPreviewResponse,
  MigrationRequestBody,
  MigrationSourcesResponse,
  MoveFolderRequest,
  MoveFolderResponse,
  OpenConfigUiSessionRequest,
  OpenConfigUiSessionResponse,
  OpenDataDirectoryResponse,
  OpenFolderUiSessionRequest,
  OpenFolderUiSessionResponse,
  OpenModalUiSessionRequest,
  OpenModalUiSessionResponse,
  OpenProfileOnDeviceRequest,
  OpenProfileOnDeviceResponse,
  OpenUiPreviewSessionRequest,
  OpenUiPreviewSessionResponse,
  OpenWidgetUiSessionRequest,
  OpenWidgetUiSessionResponse,
  PairingCodeResponse,
  PluginInstallActionResponse,
  PluginPairingActionResponse,
  PluginRuntimeOperationResponse,
  PrepareRestoreRequest,
  PrepareRestoreResponse,
  RedeemDeviceEnrollmentRequest,
  RefreshStoreRegistryResponse,
  RemoveDeviceResponse,
  RenameConfigEntryResponse,
  RenameDeviceRequest,
  RenameDeviceResponse,
  RenameCatalogVariableRequest,
  RenameCatalogVariableResponse,
  RenderTemplateRequest,
  RenderTemplateResponse,
  ResolveCatalogVariableRequest,
  ResolveCatalogVariableResponse,
  ResolveIntegrationIssueResponse,
  RestartAdbServerResponse,
  RestartApplicationRequest,
  RestartApplicationResponse,
  RevealBackupRecoveryKeyRequest,
  RevealSecretResponse,
  RevokePluginTokenResponse,
  RunActionFlowRequest,
  RunActionFlowResponse,
  RunScriptRequest,
  RunScriptResponse,
  SanitizeVariableNameRequest,
  SanitizeVariableNameResponse,
  SetDeviceStartupProfileRequest,
  SetDeviceStartupProfileResponse,
  SetFolderFocusRuleRequest,
  SetFolderFocusRuleResponse,
  SetIntegrationEnabledRequest,
  SetIntegrationEnabledResponse,
  SetupRequest,
  SetVariableValueRequest,
  SetVariableValueResponse,
  SetWidgetPinnedRequest,
  SetWidgetPinnedResponse,
  SetWidgetsPinnedRequest,
  SetWidgetsPinnedResponse,
  StartConfigFlowResponse,
  StartConnectSignInResponse,
  StoreCatalogSection,
  StoreExtensionKind,
  StoreOperationActionResponse,
  SubmitConfigFlowStepRequest,
  SubmitConfigFlowStepResponse,
  TerminatePluginSessionResponse,
  TokenResponse,
  TransportError,
  TriggerEventRequest,
  TriggerEventResponse,
  UiAttachSessionResponse,
  UiModalOpenedEvent,
  UiSendEventRequest,
  UiSendEventResponse,
  UiSessionClosedEvent,
  UiSessionInvalidatedEvent,
  UiSessionPatchedEvent,
  UiSessionTreeUpdatedEvent,
  UnbindCatalogVariableRequest,
  UnbindCatalogVariableResponse,
  UninstallStoreExtensionResponse,
  UnlockKeyRingRequest,
  UnlockKeyRingResponse,
  UpdateAdbSettingsRequest,
  UpdateAdbSettingsResponse,
  UpdateAppearanceSettingsRequest,
  UpdateAppearanceSettingsResponse,
  UpdateAutomationRequest,
  UpdateAutomationResponse,
  UpdateAutostartSettingsRequest,
  UpdateAutostartSettingsResponse,
  UpdateBackupSettingsRequest,
  UpdateBackupSettingsResponse,
  UpdateDeveloperSettingsRequest,
  UpdateDeveloperSettingsResponse,
  UpdateFolderRequest,
  UpdateFolderResponse,
  UpdateIconPackRequest,
  UpdateIconPackResponse,
  UpdateIconRequest,
  UpdateIconResponse,
  UpdateLocalizationSettingsRequest,
  UpdateLocalizationSettingsResponse,
  UpdateLockScreenSettingsRequest,
  UpdateLockScreenSettingsResponse,
  UpdateLoggingSettingsRequest,
  UpdateLoggingSettingsResponse,
  UpdateNetworkSettingsRequest,
  UpdateNetworkSettingsResponse,
  UpdateNetworkTlsCertificateRequest,
  UpdateProfileRequest,
  UpdateProfileResponse,
  UpdateScriptRequest,
  UpdateScriptResponse,
  UpdateSecretRequest,
  UpdateSecretResponse,
  UpdateVariableRequest,
  UpdateVariableResponse,
  UpdateWidgetDataRequest,
  UpdateWidgetDataResponse,
  UpdateWidgetPositionsRequest,
  UpdateWidgetPositionsResponse,
  UpdateWidgetRequest,
  UpdateWidgetResponse,
  UpdateWidgetStateRequest,
  UpdateWidgetStateResponse,
  WebClientTargetDto,
  WebClientTargetProvisioningResultDto,
  WebSocketTransport,
  WidgetTypeCatalogChangedEvent,
} from '@macro-deck/runtime';
import { v4 as uuidv4 } from 'uuid';

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface WidgetTypeInfo {
  id: string;
  providerId: string;
  isBuiltIn: boolean;
  name?: LocalizedText;
  description?: LocalizedText;
  defaultData: Record<string, unknown>;
  supportsConfigUi: boolean;
  configUiModelVersion: number;
}

export interface GetWidgetTypesResponse {
  success: boolean;
  error?: ApiError;
  types: WidgetTypeInfo[];
}

const UI_SOCKET_PATH = '/ws/ui';

interface IncomingNotification<T = unknown> {
  method: string;
  params: T;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly resolveHostUrl = inject(HOST_URL_RESOLVER);

  private readonly _connectionState = new BehaviorSubject<ConnectionState>('disconnected');
  private readonly _connectionStateSignal = signal<ConnectionState>('disconnected');
  private readonly _notifications = new Subject<IncomingNotification>();

  private connection: WebSocketTransport | null = null;
  private connecting = false;
  private connectionAttempt = 0;
  private baseUrl = '';

  private static readonly RECONNECT_DELAY_MS = 2000;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  // Whether the app wants a connection at all: connect() sets it, disconnect() clears it. Gates the
  // retry loop so an intentional teardown (logout) is never resurrected by a late failure.
  private wantsConnection = false;

  private readonly _clientId = uuidv4();

  // Auth hooks, registered by the AuthService (avoids a DI cycle: AuthService uses ApiService for
  // the auth endpoints). Without hooks (e.g. in tests) requests go out unauthenticated, which the
  // loopback-trusted desktop transport accepts.
  private accessTokenProvider: (() => string | null) | null = null;
  private unauthorizedHandler: (() => Promise<boolean>) | null = null;
  private connectionUnauthorizedHandler: (() => void) | null = null;
  private forbiddenHandler: (() => void) | null = null;

  readonly connectionState$ = this._connectionState.asObservable();

  readonly connectionStateSignal: Signal<ConnectionState> = this._connectionStateSignal.asReadonly();

  get clientId(): string {
    return this._clientId;
  }

  setAuthHooks(
    accessTokenProvider: () => string | null,
    onUnauthorized: () => Promise<boolean>,
    onConnectionUnauthorized: () => void,
    onForbidden?: () => void,
  ): void {
    this.accessTokenProvider = accessTokenProvider;
    this.unauthorizedHandler = onUnauthorized;
    this.connectionUnauthorizedHandler = onConnectionUnauthorized;
    this.forbiddenHandler = onForbidden ?? null;
  }

  async resolveBaseUrl(): Promise<string | null> {
    if (this.baseUrl) {
      return this.baseUrl;
    }
    const baseUrl = await this.resolveHostUrl();
    if (baseUrl) {
      this.baseUrl = baseUrl;
    }
    return baseUrl;
  }

  private setConnectionState(state: ConnectionState): void {
    this._connectionStateSignal.set(state);
    this._connectionState.next(state);
  }

  connect(): void {
    this.wantsConnection = true;
    if (this.connecting || this.connection) {
      return;
    }

    this.connecting = true;
    this.setConnectionState(this.connectionState === 'disconnected' ? 'connecting' : 'reconnecting');
    void this.establishConnection(++this.connectionAttempt);
  }

  disconnect(): void {
    this.wantsConnection = false;
    this.connectionAttempt++;
    this.connecting = false;
    this.clearRetryTimer();
    const connection = this.connection;
    this.connection = null;
    this.baseUrl = '';
    connection?.disconnect();
    this.setConnectionState('disconnected');
  }

  reconnectNow(): void {
    if (!this.wantsConnection || this.connecting) {
      return;
    }

    const connection = this.connection;
    if (connection && this.connectionState === 'connected') {
      return;
    }

    this.clearRetryTimer();
    if (connection) {
      this.connection = null;
      connection.disconnect();
    }

    this.connecting = true;
    this.setConnectionState(this.connectionState === 'disconnected' ? 'connecting' : 'reconnecting');
    void this.establishConnection(++this.connectionAttempt);
  }

  private async establishConnection(attempt: number): Promise<void> {
    let attemptedConnection: WebSocketTransport | null = null;
    try {
      const baseUrl = await this.resolveHostUrl();
      if (!this.isCurrentAttempt(attempt)) {
        return;
      }
      if (!baseUrl) {
        this.setConnectionState(this.wantsConnection ? 'reconnecting' : 'disconnected');
        this.scheduleReconnect();
        return;
      }

      this.baseUrl = baseUrl;

      const ticket = await this.mintUiSocketTicket();
      if (!this.isCurrentAttempt(attempt)) {
        return;
      }
      const connection = new WebSocketTransport(() => {
        if (this.connection !== connection) {
          return;
        }
        this.connection = null;
        this.setConnectionState(this.wantsConnection ? 'reconnecting' : 'disconnected');
        this.scheduleReconnect();
      });
      attemptedConnection = connection;
      connection.onAny((method, params) => this._notifications.next({ method, params }));
      this.connection = connection;
      await connection.connect(`${ApiService.websocketUrl(baseUrl)}${UI_SOCKET_PATH}?ticket=${encodeURIComponent(ticket)}`);
      if (!this.isCurrentAttempt(attempt)) {
        if (this.connection === connection) this.connection = null;
        connection.disconnect();
        return;
      }
      await connection.request<void>('RegisterClient', [this._clientId]);
      if (!this.isCurrentAttempt(attempt)) {
        if (this.connection === connection) this.connection = null;
        connection.disconnect();
        return;
      }
      this.clearRetryTimer();
      this.setConnectionState('connected');
    } catch (err) {
      if (attemptedConnection && this.connection === attemptedConnection) {
        this.connection = null;
        attemptedConnection.disconnect();
      }
      if (!this.isCurrentAttempt(attempt)) {
        return;
      }
      this.setConnectionState(this.wantsConnection ? 'reconnecting' : 'disconnected');
      if (ApiService.isUnauthorizedError(err) && this.connectionUnauthorizedHandler) {
        this.connectionUnauthorizedHandler();
      } else {
        this.scheduleReconnect();
      }
    } finally {
      if (attempt === this.connectionAttempt) {
        this.connecting = false;
      }
    }
  }

  private isCurrentAttempt(attempt: number): boolean {
    return this.wantsConnection && attempt === this.connectionAttempt;
  }

  private static isUnauthorizedError(err: unknown): boolean {
    return /\b401\b/.test(String(err));
  }

  private async mintUiSocketTicket(): Promise<string> {
    const response = await this.fetchWithAuth('/api/ui-websocket/tickets', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-MacroDeck-Ui-Protocol': '1',
      },
      body: '{}',
    });
    if (!response.ok) {
      throw new Error(`UI WebSocket ticket request failed: ${response.status}`);
    }
    const body = await response.json() as { value?: unknown };
    if (typeof body.value !== 'string' || !body.value) {
      throw new Error('UI WebSocket ticket response was invalid.');
    }
    return body.value;
  }

  private static websocketUrl(baseUrl: string): string {
    const url = new URL(baseUrl, window.location.href);
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    return url.toString().replace(/\/$/, '');
  }

  private scheduleReconnect(): void {
    if (!this.wantsConnection || this.retryTimer !== null || this.connection) {
      return;
    }
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.connect();
    }, ApiService.RECONNECT_DELAY_MS);
  }

  private clearRetryTimer(): void {
    if (this.retryTimer !== null) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
  }

  async invoke(method: string, ...args: unknown[]): Promise<void> {
    const connection = this.connection;
    if (!connection) {
      return;
    }
    try {
      await connection.request<void>(method, args);
    } catch (err) {
      console.warn(`UI WebSocket request '${method}' failed:`, err);
    }
  }

  async invokeResult<T>(method: string, ...args: unknown[]): Promise<T | null> {
    const connection = this.connection;
    if (!connection) {
      return null;
    }
    try {
      return await connection.request<T>(method, args);
    } catch (err) {
      console.warn(`UI WebSocket request '${method}' failed:`, err);
      return null;
    }
  }

  onNotification<T>(method: string): Observable<T> {
    return this._notifications.pipe(
      filter(n => n.method === method),
      map(n => n.params as T),
    );
  }

  get isConnected(): boolean {
    return this._connectionState.value === 'connected';
  }

  get connectionState(): ConnectionState {
    return this._connectionState.value;
  }

  private async http<T>(method: string, path: string, body?: unknown, skipAuthRetry = false): Promise<T> {
    const response = await this.fetchWithAuth(path, {
      method,
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }, skipAuthRetry);
    return ApiService.parseResponse<T>(response);
  }

  private async fetchWithAuth(path: string, init: RequestInit, skipAuthRetry = false): Promise<Response> {
    const response = await fetch(`${this.baseUrl}${path}`, this.buildRequestInit(init));
    if (skipAuthRetry) {
      return response;
    }

    if (response.status === 403) {
      this.forbiddenHandler?.();
      return response;
    }

    if (response.status !== 401 || !this.unauthorizedHandler) {
      return response;
    }

    const retry = await this.unauthorizedHandler();
    if (!retry) {
      return response;
    }

    return fetch(`${this.baseUrl}${path}`, this.buildRequestInit(init));
  }

  private buildRequestInit(init: RequestInit): RequestInit {
    const token = this.accessTokenProvider?.();
    if (!token) {
      return { ...init, cache: 'no-store' };
    }

    return {
      ...init,
      cache: 'no-store',
      headers: {
        ...(init.headers as Record<string, string> | undefined),
        Authorization: `Bearer ${token}`,
      },
    };
  }

  private static async parseResponse<T>(response: Response): Promise<T> {
    const text = await response.text();

    if (!response.ok) {
      const problem = ApiService.tryParseJson(text);
      const message = problem?.title ?? problem?.detail ?? problem?.message
        ?? response.statusText ?? `HTTP ${response.status}`;
      throw new TransportError(response.status, message, problem?.code);
    }

    return (text ? JSON.parse(text) : undefined) as T;
  }

  private static tryParseJson(
    text: string,
  ): { title?: string; detail?: string; message?: string; code?: string } | null {
    if (!text) {
      return null;
    }
    try {
      const parsed: unknown = JSON.parse(text);
      return typeof parsed === 'object' && parsed !== null ? parsed : null;
    } catch {
      return null;
    }
  }

  getAuthStatus(): Promise<AuthStatusResponse> {
    return this.http('GET', '/api/auth/status', undefined, true);
  }

  setupUser(request: SetupRequest): Promise<void> {
    return this.http('POST', '/api/auth/setup', request, true);
  }

  login(request: LoginRequest): Promise<TokenResponse> {
    return this.http('POST', '/api/auth/login', request, true);
  }

  refreshSession(): Promise<TokenResponse> {
    return this.http('POST', '/api/auth/refresh', undefined, true);
  }

  logoutSession(): Promise<void> {
    return this.http('POST', '/api/auth/logout', undefined, true);
  }

  changePassword(request: ChangePasswordRequest): Promise<void> {
    return this.http('POST', '/api/auth/change-password', request);
  }

  changeUsername(request: ChangeUsernameRequest): Promise<void> {
    return this.http('POST', '/api/auth/change-username', request);
  }

  // Devices (issue #250, admin-only)
  getDevices(): Promise<GetDevicesResponse> {
    return this.http('GET', '/api/devices');
  }

  renameDevice(id: string, request: RenameDeviceRequest): Promise<RenameDeviceResponse> {
    return this.http('PATCH', `/api/devices/${encodeURIComponent(id)}/name`, request);
  }

  setDeviceStartupProfile(id: string, request: SetDeviceStartupProfileRequest): Promise<SetDeviceStartupProfileResponse> {
    return this.http('PATCH', `/api/devices/${encodeURIComponent(id)}/startup-profile`, request);
  }

  openProfileOnDevice(id: string, request: OpenProfileOnDeviceRequest): Promise<OpenProfileOnDeviceResponse> {
    return this.http('POST', `/api/devices/${encodeURIComponent(id)}/open-profile`, request);
  }

  logoutDevice(id: string): Promise<LogoutDeviceResponse> {
    return this.http('POST', `/api/devices/${encodeURIComponent(id)}/logout`);
  }

  removeDevice(id: string): Promise<RemoveDeviceResponse> {
    return this.http('DELETE', `/api/devices/${encodeURIComponent(id)}`);
  }

  // Plugin tokens & sessions (issue #411, admin-only)
  getPluginTokens(): Promise<GetPluginTokensResponse> {
    return this.http('GET', '/api/plugin-tokens');
  }

  createPluginToken(request: CreatePluginTokenRequest): Promise<CreatePluginTokenResponse> {
    return this.http('POST', '/api/plugin-tokens', request);
  }

  revokePluginToken(id: string): Promise<RevokePluginTokenResponse> {
    return this.http('POST', `/api/plugin-tokens/${encodeURIComponent(id)}/revoke`);
  }

  deletePluginToken(id: string): Promise<DeletePluginTokenResponse> {
    return this.http('DELETE', `/api/plugin-tokens/${encodeURIComponent(id)}`);
  }

  getPluginSessions(): Promise<GetPluginSessionsResponse> {
    return this.http('GET', '/api/plugin-sessions');
  }

  terminatePluginSession(sessionId: string): Promise<TerminatePluginSessionResponse> {
    return this.http('POST', `/api/plugin-sessions/${encodeURIComponent(sessionId)}/terminate`);
  }

  // Interactive plugin pairing (issue #588, desktop-only - rejected for a LAN caller even with a
  // valid admin token, so only the loopback-trusted desktop UI can call any of these).
  getPluginPairingRequests(): Promise<GetPluginPairingRequestsResponse> {
    return this.http('GET', '/api/plugin-pairing/requests');
  }

  approvePluginPairingRequest(
    requestId: string,
    request: ApprovePluginPairingRequestRequest
  ): Promise<PluginPairingActionResponse> {
    return this.http('POST', `/api/plugin-pairing/requests/${encodeURIComponent(requestId)}/approve`, request);
  }

  rejectPluginPairingRequest(requestId: string): Promise<PluginPairingActionResponse> {
    return this.http('POST', `/api/plugin-pairing/requests/${encodeURIComponent(requestId)}/reject`);
  }

  getPairedPlugins(): Promise<GetPairedPluginsResponse> {
    return this.http('GET', '/api/plugin-pairing/registrations');
  }

  revokePairedPlugin(pluginId: string): Promise<void> {
    return this.http('POST', `/api/plugin-pairing/registrations/${encodeURIComponent(pluginId)}/revoke`);
  }

  // Managed plugin runtime (issue #412, admin-only)
  getPluginRuntime(): Promise<GetPluginRuntimeResponse> {
    return this.http('GET', '/api/plugin-runtime');
  }

  startPlugin(pluginId: string): Promise<PluginRuntimeOperationResponse> {
    return this.http('POST', `/api/plugin-runtime/${encodeURIComponent(pluginId)}/start`);
  }

  stopPlugin(pluginId: string): Promise<PluginRuntimeOperationResponse> {
    return this.http('POST', `/api/plugin-runtime/${encodeURIComponent(pluginId)}/stop`);
  }

  restartPlugin(pluginId: string): Promise<PluginRuntimeOperationResponse> {
    return this.http('POST', `/api/plugin-runtime/${encodeURIComponent(pluginId)}/restart`);
  }

  // Plugin compatibility diagnostics (issue #418, admin-only)
  getPluginCompatibility(): Promise<GetPluginCompatibilityResponse> {
    return this.http('GET', '/api/plugin-compatibility');
  }

  // Plugin installation (issue #528, admin-only)
  getInstalledPlugins(): Promise<GetInstalledPluginsResponse> {
    return this.http('GET', '/api/plugin-installation');
  }

  inspectPluginArtifact(file: File): Promise<PluginInstallActionResponse> {
    return this.uploadArchive('/api/plugin-installation/inspect', file, {});
  }

  installPluginArtifact(file: File, force?: boolean, allowUnsigned?: boolean): Promise<PluginInstallActionResponse> {
    const query = new URLSearchParams();
    if (force) {
      query.set('force', 'true');
    }
    if (allowUnsigned) {
      query.set('allowUnsigned', 'true');
    }
    const suffix = query.size > 0 ? `?${query}` : '';
    return this.uploadArchive(`/api/plugin-installation/install${suffix}`, file, {});
  }

  inspectPluginArtifactPath(path: string): Promise<PluginInstallActionResponse> {
    return this.http('POST', '/api/plugin-installation/inspect-path', { path });
  }

  installPluginArtifactPath(path: string,
    force?: boolean,
    allowUnsigned?: boolean): Promise<PluginInstallActionResponse> {
    return this.http('POST', '/api/plugin-installation/install-path',
      { path, force: force ?? false, allowUnsigned: allowUnsigned ?? false });
  }

  uninstallPlugin(pluginId: string, options?: { keepData?: boolean; force?: boolean }): Promise<PluginInstallActionResponse> {
    const params = new URLSearchParams();
    if (options?.keepData === false) {
      params.set('keepData', 'false');
    }
    if (options?.force) {
      params.set('force', 'true');
    }
    const query = params.toString() ? `?${params.toString()}` : '';
    return this.http('DELETE', `/api/plugin-installation/${encodeURIComponent(pluginId)}${query}`);
  }

  // Extension store (issue #517, admin-only). The browser never installs anything or fetches an
  // artifact directly - every action below is a host call, and no artifact URL is ever exposed here.
  getStoreStatus(): Promise<GetStoreStatusResponse> {
    return this.http('GET', '/api/store/status');
  }

  refreshStoreRegistry(): Promise<RefreshStoreRegistryResponse> {
    return this.http('POST', '/api/store/registry/refresh');
  }

  getStoreCatalog(options?: {
    kind?: StoreExtensionKind;
    kinds?: StoreExtensionKind[];
    search?: string;
    section?: StoreCatalogSection;
    skip?: number;
    take?: number;
  }): Promise<GetStoreCatalogResponse> {
    const query = new URLSearchParams();
    if (options?.kind) {
      query.set('kind', options.kind);
    }
    // Repeated rather than comma-joined: the host binds `kinds` as a collection, and a single joined
    // value would reach it as one unparseable enum.
    for (const kind of options?.kinds ?? []) {
      query.append('kinds', kind);
    }
    if (options?.search) {
      query.set('search', options.search);
    }
    if (options?.section) {
      query.set('section', options.section);
    }
    if (options?.skip !== undefined) {
      query.set('skip', String(options.skip));
    }
    if (options?.take !== undefined) {
      query.set('take', String(options.take));
    }
    const suffix = query.size > 0 ? `?${query}` : '';
    return this.http('GET', `/api/store/catalog${suffix}`);
  }

  getStoreExtension(kind: StoreExtensionKind, packageId: string): Promise<GetStoreExtensionResponse> {
    return this.http('GET', `/api/store/catalog/${encodeURIComponent(kind)}/${encodeURIComponent(packageId)}`);
  }

  getStoreUpdates(): Promise<GetStoreUpdatesResponse> {
    return this.http('GET', '/api/store/updates');
  }

  checkStoreUpdates(): Promise<GetStoreUpdatesResponse> {
    return this.http('POST', '/api/store/updates/check');
  }

  getStoreOperations(): Promise<GetStoreOperationsResponse> {
    return this.http('GET', '/api/store/operations');
  }

  installStoreExtension(request: InstallStoreExtensionRequest): Promise<StoreOperationActionResponse> {
    return this.http('POST', '/api/store/install', request);
  }

  retryStoreOperation(operationId: string): Promise<StoreOperationActionResponse> {
    return this.http('POST', `/api/store/operations/${encodeURIComponent(operationId)}/retry`);
  }

  cancelStoreOperation(operationId: string): Promise<StoreOperationActionResponse> {
    return this.http('POST', `/api/store/operations/${encodeURIComponent(operationId)}/cancel`);
  }

  dismissStoreOperation(operationId: string): Promise<StoreOperationActionResponse> {
    return this.http('DELETE', `/api/store/operations/${encodeURIComponent(operationId)}`);
  }

  uninstallStoreExtension(kind: StoreExtensionKind, id: string): Promise<UninstallStoreExtensionResponse> {
    return this.http('POST', '/api/store/uninstall', { kind, id });
  }

  getStoreExtensionIconUrl(kind: StoreExtensionKind, packageId: string): string {
    return `${this.baseUrl}/api/store/media/${encodeURIComponent(kind)}/${encodeURIComponent(packageId)}/icon`;
  }

  getStoreScreenshotUrl(kind: StoreExtensionKind, packageId: string, index: number): string {
    return `${this.baseUrl}/api/store/media/${encodeURIComponent(kind)}/${encodeURIComponent(packageId)}`
      + `/screenshots/${encodeURIComponent(String(index))}`;
  }

  getVersion(): Promise<GetVersionResponse> {
    return this.http('GET', '/api/system/version');
  }

  getServerTime(): Promise<GetServerTimeResponse> {
    return this.http('GET', '/api/system/time');
  }

  getAboutInfo(): Promise<GetAboutInfoResponse> {
    return this.http('GET', '/api/system/about');
  }

  getAppearanceSettings(): Promise<GetAppearanceSettingsResponse> {
    return this.http('GET', '/api/settings/appearance');
  }

  updateAppearanceSettings(
    request: UpdateAppearanceSettingsRequest
  ): Promise<UpdateAppearanceSettingsResponse> {
    return this.http('PUT', '/api/settings/appearance', request);
  }

  getLoggingSettings(): Promise<GetLoggingSettingsResponse> {
    return this.http('GET', '/api/settings/logging');
  }

  updateLoggingSettings(
    request: UpdateLoggingSettingsRequest
  ): Promise<UpdateLoggingSettingsResponse> {
    return this.http('PUT', '/api/settings/logging', request);
  }

  getAutostartSettings(): Promise<GetAutostartSettingsResponse> {
    return this.http('GET', '/api/settings/autostart');
  }

  updateAutostartSettings(
    request: UpdateAutostartSettingsRequest
  ): Promise<UpdateAutostartSettingsResponse> {
    return this.http('PUT', '/api/settings/autostart', request);
  }

  getNetworkSettings(): Promise<GetNetworkSettingsResponse> {
    return this.http('GET', '/api/settings/network');
  }

  updateNetworkSettings(
    request: UpdateNetworkSettingsRequest
  ): Promise<UpdateNetworkSettingsResponse> {
    return this.http('PUT', '/api/settings/network', request);
  }

  updateNetworkTlsCertificate(
    request: UpdateNetworkTlsCertificateRequest
  ): Promise<UpdateNetworkSettingsResponse> {
    return this.http('PUT', '/api/settings/network/tls/certificate', request);
  }

  reissueNetworkTlsCertificate(): Promise<UpdateNetworkSettingsResponse> {
    return this.http('POST', '/api/settings/network/tls/certificate/reissue');
  }

  regenerateNetworkTlsCertificateAuthority(): Promise<UpdateNetworkSettingsResponse> {
    return this.http('POST', '/api/settings/network/tls/certificate-authority/regenerate');
  }

  restartApplication(reason?: string): Promise<RestartApplicationResponse> {
    const request: RestartApplicationRequest = { reason };
    return this.http('POST', '/api/host/restart', request);
  }

  getDataDirectory(): Promise<GetDataDirectoryResponse> {
    return this.http('GET', '/api/host/data-directory');
  }

  openDataDirectory(): Promise<OpenDataDirectoryResponse> {
    return this.http('POST', '/api/host/data-directory/open');
  }

  // ADB (issue #112, admin-only)
  getAdbSettings(): Promise<GetAdbSettingsResponse> {
    return this.http('GET', '/api/settings/adb');
  }

  updateAdbSettings(request: UpdateAdbSettingsRequest): Promise<UpdateAdbSettingsResponse> {
    return this.http('PUT', '/api/settings/adb', request);
  }

  restartAdbServer(): Promise<RestartAdbServerResponse> {
    return this.http('POST', '/api/settings/adb/restart-server');
  }

  downloadAdbPlatformTools(): Promise<DownloadAdbPlatformToolsResponse> {
    return this.http('POST', '/api/settings/adb/download-platform-tools');
  }

  // Every device-target endpoint below is loopback-only on the host, so these are reachable from the
  // desktop UI and nowhere else (issue #727).
  getWebClientTargets(): Promise<WebClientTargetDto[]> {
    return this.http('GET', '/api/client-targets');
  }

  startWebClientTargetProvisioning(targetId: string): Promise<WebClientTargetProvisioningResultDto> {
    return this.http('POST', `/api/client-targets/${encodeURIComponent(targetId)}/provisioning/start`);
  }

  advanceWebClientTargetProvisioning(
    targetId: string,
    request: AdvanceWebClientTargetProvisioningRequest
  ): Promise<WebClientTargetProvisioningResultDto> {
    return this.http('POST', `/api/client-targets/${encodeURIComponent(targetId)}/provisioning/advance`, request);
  }

  redeemDeviceEnrollment(request: RedeemDeviceEnrollmentRequest): Promise<TokenResponse> {
    return this.http('POST', '/api/auth/device-enrollment/redeem', request);
  }

  getPairingCode(): Promise<PairingCodeResponse> {
    return this.http('GET', '/api/auth/pairing-code');
  }

  rotatePairingCode(): Promise<PairingCodeResponse> {
    return this.http('POST', '/api/auth/pairing-code');
  }

  getOnboardingState(): Promise<GetOnboardingStateResponse> {
    return this.http('GET', '/api/settings/onboarding');
  }

  completeOnboarding(): Promise<CompleteOnboardingResponse> {
    return this.http('POST', '/api/settings/onboarding/complete');
  }

  getDeveloperSettings(): Promise<GetDeveloperSettingsResponse> {
    return this.http('GET', '/api/settings/developer');
  }

  updateDeveloperSettings(request: UpdateDeveloperSettingsRequest): Promise<UpdateDeveloperSettingsResponse> {
    return this.http('PUT', '/api/settings/developer', request);
  }

  getLockScreenSettings(): Promise<GetLockScreenSettingsResponse> {
    return this.http('GET', '/api/settings/lock-screen');
  }

  updateLockScreenSettings(request: UpdateLockScreenSettingsRequest): Promise<UpdateLockScreenSettingsResponse> {
    return this.http('PUT', '/api/settings/lock-screen', request);
  }

  onAdbStateChanged(): Observable<AdbStateChangedEvent> {
    return this.onNotification<AdbStateChangedEvent>('AdbStateChangedEvent');
  }

  getProfiles(): Promise<GetProfilesResponse> {
    return this.http('GET', '/api/profiles');
  }

  createProfile(request: CreateProfileRequest): Promise<CreateProfileResponse> {
    return this.http('POST', '/api/profiles', request);
  }

  updateProfile(request: UpdateProfileRequest): Promise<UpdateProfileResponse> {
    return this.http('PUT', '/api/profiles', request);
  }

  deleteProfile(request: DeleteProfileRequest): Promise<DeleteProfileResponse> {
    return this.http('DELETE', `/api/profiles/${encodeURIComponent(request.id)}`);
  }

  getScripts(): Promise<GetScriptsResponse> {
    return this.http('GET', '/api/scripts');
  }

  getScriptUsages(id: string): Promise<GetScriptUsagesResponse> {
    return this.http('GET', `/api/scripts/${encodeURIComponent(id)}/usages`);
  }

  createScript(request: CreateScriptRequest): Promise<CreateScriptResponse> {
    return this.http('POST', '/api/scripts', request);
  }

  updateScript(request: UpdateScriptRequest): Promise<UpdateScriptResponse> {
    return this.http('PUT', `/api/scripts/${encodeURIComponent(request.id)}`, request);
  }

  duplicateScript(id: string): Promise<DuplicateScriptResponse> {
    return this.http('POST', `/api/scripts/${encodeURIComponent(id)}/duplicate`, {});
  }

  deleteScript(id: string): Promise<DeleteScriptResponse> {
    return this.http('DELETE', `/api/scripts/${encodeURIComponent(id)}`);
  }

  runScript(request: RunScriptRequest): Promise<RunScriptResponse> {
    return this.http('POST', `/api/scripts/${encodeURIComponent(request.id)}/run`, request);
  }

  getAutomations(): Promise<GetAutomationsResponse> {
    return this.http('GET', '/api/automations');
  }

  createAutomation(request: CreateAutomationRequest): Promise<CreateAutomationResponse> {
    return this.http('POST', '/api/automations', request);
  }

  updateAutomation(request: UpdateAutomationRequest): Promise<UpdateAutomationResponse> {
    return this.http('PUT', `/api/automations/${encodeURIComponent(request.id)}`, request);
  }

  duplicateAutomation(id: string): Promise<DuplicateAutomationResponse> {
    return this.http('POST', `/api/automations/${encodeURIComponent(id)}/duplicate`, {});
  }

  deleteAutomation(id: string): Promise<DeleteAutomationResponse> {
    return this.http('DELETE', `/api/automations/${encodeURIComponent(id)}`);
  }

  exportProfile(profileId: string, request: ExportProfileRequest): Promise<{ blob: Blob; fileName: string }> {
    return this.exportArchive(`/api/profiles/${encodeURIComponent(profileId)}/export`, request, 'profile.macroDeckProfile');
  }

  importProfile(file: File, password?: string): Promise<ImportProfileResponse> {
    return this.uploadArchive<ImportProfileResponse>('/api/profiles/import', file, { password });
  }

  exportFolder(folderId: string, request: ExportFolderRequest): Promise<{ blob: Blob; fileName: string }> {
    return this.exportArchive(`/api/folders/${encodeURIComponent(folderId)}/export`, request, 'folder.macroDeckFolder');
  }

  importFolder(profileId: string, parentFolderId: string | null, file: File, password?: string):
    Promise<ImportFolderResponse> {
    return this.uploadArchive<ImportFolderResponse>('/api/folders/import', file, {
      profileId,
      parentFolderId: parentFolderId ?? undefined,
      password,
    });
  }

  exportWidgets(request: ExportWidgetsRequest): Promise<{ blob: Blob; fileName: string }> {
    return this.exportArchive('/api/widgets/export', request, 'widgets.macroDeckWidget');
  }

  importWidgets(folderId: string, anchorX: number, anchorY: number, file: File, password?: string):
    Promise<ImportWidgetsResponse> {
    return this.uploadArchive<ImportWidgetsResponse>('/api/widgets/import', file, {
      folderId,
      anchorX: String(anchorX),
      anchorY: String(anchorY),
      password,
    });
  }

  inspectArchive(file: File): Promise<InspectArchiveResponse> {
    return this.uploadArchive<InspectArchiveResponse>('/api/portable/inspect', file, {});
  }

  importProfileFromPath(path: string, password?: string): Promise<ImportProfileResponse> {
    return this.http('POST', '/api/profiles/import-path', { path, password });
  }

  importFolderFromPath(
    profileId: string,
    parentFolderId: string | null,
    path: string,
    password?: string
  ): Promise<ImportFolderResponse> {
    return this.http('POST', '/api/folders/import-path', {
      profileId,
      parentFolderId: parentFolderId ?? undefined,
      path,
      password,
    });
  }

  importWidgetsFromPath(
    folderId: string,
    anchorX: number,
    anchorY: number,
    path: string,
    password?: string
  ): Promise<ImportWidgetsResponse> {
    return this.http('POST', '/api/widgets/import-path', { folderId, anchorX, anchorY, path, password });
  }

  inspectArchivePath(path: string): Promise<InspectArchiveResponse> {
    return this.http('POST', '/api/portable/inspect-path', { path });
  }

  getMigrationSources(): Promise<MigrationSourcesResponse> {
    return this.http('GET', '/api/migration/sources');
  }

  previewMigration(request: MigrationRequestBody): Promise<MigrationPreviewResponse> {
    return this.http('POST', '/api/migration/preview', request);
  }

  importMigration(request: MigrationRequestBody): Promise<MigrationImportResponse> {
    return this.http('POST', '/api/migration/import', request);
  }

  previewMigrationUpload(file: File, sourceId: string, decryptionKey?: string, skipDecryption?: boolean):
    Promise<MigrationPreviewResponse> {
    return this.uploadArchive<MigrationPreviewResponse>('/api/migration/preview-upload', file, {
      sourceId,
      decryptionKey,
      skipDecryption: skipDecryption ? 'true' : undefined,
    });
  }

  importMigrationUpload(file: File, sourceId: string, decryptionKey?: string, skipDecryption?: boolean):
    Promise<MigrationImportResponse> {
    return this.uploadArchive<MigrationImportResponse>('/api/migration/import-upload', file, {
      sourceId,
      decryptionKey,
      skipDecryption: skipDecryption ? 'true' : undefined,
    });
  }

  private async exportArchive(path: string, body: unknown, fallbackName: string):
    Promise<{ blob: Blob; fileName: string }> {
    const response = await this.fetchWithAuth(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      const error = await response.json().catch(() => null);
      throw new TransportError(response.status, error?.message ?? `Export failed (${response.status})`, error?.code);
    }

    const fileName = parseContentDispositionFileName(response.headers.get('Content-Disposition')) ?? fallbackName;
    return { blob: await response.blob(), fileName };
  }

  private async uploadArchive<T>(path: string, file: File, fields: Record<string, string | undefined>): Promise<T> {
    const form = new FormData();
    form.append('file', file, file.name);
    for (const [key, value] of Object.entries(fields)) {
      if (value !== undefined) {
        form.append(key, value);
      }
    }

    const response = await this.fetchWithAuth(path, { method: 'POST', body: form });
    return ApiService.parseResponse<T>(response);
  }

  getFolders(profileId?: string): Promise<GetFoldersResponse> {
    const query = profileId ? `?profileId=${encodeURIComponent(profileId)}` : '';
    return this.http('GET', `/api/folders${query}`);
  }

  createFolder(request: CreateFolderRequest): Promise<CreateFolderResponse> {
    return this.http('POST', '/api/folders', request);
  }

  updateFolder(request: UpdateFolderRequest): Promise<UpdateFolderResponse> {
    return this.http('PUT', '/api/folders', request);
  }

  deleteFolder(request: DeleteFolderRequest): Promise<DeleteFolderResponse> {
    return this.http('DELETE', `/api/folders/${request.id}`);
  }

  duplicateFolder(request: DuplicateFolderRequest): Promise<DuplicateFolderResponse> {
    return this.http('POST', `/api/folders/${request.id}/duplicate`);
  }

  moveFolder(request: MoveFolderRequest): Promise<MoveFolderResponse> {
    return this.http('POST', `/api/folders/${request.id}/move`, {
      targetId: request.targetId,
      position: request.position
    });
  }

  // Folder focus rules (issue #274, admin-only)
  getFolderFocusRules(): Promise<GetFolderFocusRulesResponse> {
    return this.http('GET', '/api/folders/focus-rules');
  }

  setFolderFocusRule(request: SetFolderFocusRuleRequest): Promise<SetFolderFocusRuleResponse> {
    const { folderId, ...body } = request;
    return this.http('PUT', `/api/folders/${encodeURIComponent(folderId)}/focus-rules`, body);
  }

  deleteFolderFocusRule(folderId: string, ruleId: string): Promise<DeleteFolderFocusRuleResponse> {
    return this.http(
      'DELETE',
      `/api/folders/${encodeURIComponent(folderId)}/focus-rules/${encodeURIComponent(ruleId)}`,
    );
  }

  getIntegrations(): Promise<GetIntegrationsResponse> {
    return this.http('GET', '/api/integrations');
  }

  setIntegrationEnabled(request: SetIntegrationEnabledRequest): Promise<SetIntegrationEnabledResponse> {
    return this.http('PATCH', `/api/integrations/${request.id}/enabled`, { enabled: request.enabled });
  }

  startConfigFlow(integrationId: string): Promise<StartConfigFlowResponse> {
    return this.http('POST', `/api/integrations/${encodeURIComponent(integrationId)}/config-flow/start`);
  }

  startNewConfigFlow(integrationId: string, title: string): Promise<StartConfigFlowResponse> {
    return this.http(
      'POST',
      `/api/integrations/${encodeURIComponent(integrationId)}/config-flow/start-new`,
      { title },
    );
  }

  startEditConfigFlow(integrationId: string, entryId: string): Promise<StartConfigFlowResponse> {
    return this.http(
      'POST',
      `/api/integrations/${encodeURIComponent(integrationId)}/config-entries/${encodeURIComponent(entryId)}/config-flow/start`,
    );
  }

  submitConfigFlowStep(integrationId: string, request: SubmitConfigFlowStepRequest): Promise<SubmitConfigFlowStepResponse> {
    return this.http('POST', `/api/integrations/${encodeURIComponent(integrationId)}/config-flow/submit`, request);
  }

  getConfigEntries(integrationId: string): Promise<GetConfigEntriesResponse> {
    return this.http('GET', `/api/integrations/${encodeURIComponent(integrationId)}/config-entries`);
  }

  renameConfigEntry(integrationId: string, entryId: string, title: string): Promise<RenameConfigEntryResponse> {
    return this.http(
      'PATCH',
      `/api/integrations/${encodeURIComponent(integrationId)}/config-entries/${encodeURIComponent(entryId)}`,
      { title },
    );
  }

  deleteConfigEntry(integrationId: string, entryId: string, confirmed = false): Promise<DeleteConfigEntryResponse> {
    return this.http(
      'DELETE',
      `/api/integrations/${encodeURIComponent(integrationId)}/config-entries/${encodeURIComponent(entryId)}?confirmed=${confirmed}`,
    );
  }

  getIntegrationIssues(integrationId: string): Promise<GetIntegrationIssuesResponse> {
    return this.http('GET', `/api/integrations/${encodeURIComponent(integrationId)}/issues`);
  }

  getIntegrationCapabilities(integrationId: string): Promise<GetIntegrationCapabilitiesResponse> {
    return this.http('GET', `/api/integrations/${encodeURIComponent(integrationId)}/capabilities`);
  }

  resolveIntegrationIssue(integrationId: string, issueId: string): Promise<ResolveIntegrationIssueResponse> {
    return this.http(
      'POST',
      `/api/integrations/${encodeURIComponent(integrationId)}/issues/${encodeURIComponent(issueId)}/resolve`,
    );
  }

  createWidget(request: CreateWidgetRequest): Promise<CreateWidgetResponse> {
    return this.http('POST', '/api/widgets', request);
  }

  // Atomic batch (issue #213): either every widget is created, or none are - the same holds for the
  // delete and pin batches below, whose pin semantics are a set, never a toggle.
  createWidgets(request: CreateWidgetsRequest): Promise<CreateWidgetsResponse> {
    return this.http('POST', '/api/widgets/batch', request);
  }

  deleteWidgets(request: DeleteWidgetsRequest): Promise<DeleteWidgetsResponse> {
    return this.http('POST', '/api/widgets/batch/delete', request);
  }

  setWidgetsPinned(request: SetWidgetsPinnedRequest): Promise<SetWidgetsPinnedResponse> {
    return this.http('PUT', '/api/widgets/pinned/batch', request);
  }

  createWidgetFromApplication(request: CreateWidgetFromApplicationRequest): Promise<CreateWidgetResponse> {
    return this.http('POST', '/api/widgets/from-application', request);
  }

  updateWidget(request: UpdateWidgetRequest): Promise<UpdateWidgetResponse> {
    return this.http('PUT', '/api/widgets', request);
  }

  updateWidgetPositions(request: UpdateWidgetPositionsRequest): Promise<UpdateWidgetPositionsResponse> {
    return this.http('PUT', '/api/widgets/positions', request);
  }

  deleteWidget(request: DeleteWidgetRequest): Promise<DeleteWidgetResponse> {
    return this.http('DELETE', `/api/widgets/${request.id}?folderId=${encodeURIComponent(request.folderId)}`);
  }

  setWidgetPinned(request: SetWidgetPinnedRequest): Promise<SetWidgetPinnedResponse> {
    return this.http('PUT', `/api/widgets/${request.widgetId}/pinned`, request);
  }

  updateWidgetState(request: UpdateWidgetStateRequest): Promise<UpdateWidgetStateResponse> {
    return this.http('PATCH', `/api/widgets/${request.widgetId}/state`, request);
  }

  updateWidgetData(request: UpdateWidgetDataRequest): Promise<UpdateWidgetDataResponse> {
    return this.http('PATCH', `/api/widgets/${request.widgetId}/data`, request);
  }

  getWidgetDataSchemas(): Promise<GetWidgetDataSchemasResponse> {
    return this.http('GET', '/api/widgets/schemas');
  }

  getWidgetTypes(): Promise<GetWidgetTypesResponse> {
    return this.http('GET', '/api/widgets/types');
  }

  getActions(): Promise<GetActionsResponse> {
    return this.http('GET', '/api/actions');
  }

  executeActionButtonTrigger(request: ExecuteActionButtonTriggerRequest): Promise<ExecuteActionButtonTriggerResponse> {
    return this.http('POST', '/api/actions/execute', request);
  }

  getEventDefinitions(): Promise<GetEventDefinitionsResponse> {
    return this.http('GET', '/api/events');
  }

  triggerEvent(request: TriggerEventRequest): Promise<TriggerEventResponse> {
    return this.http('POST', '/api/events/trigger', request);
  }

  reportFolderChanged(folderId: string, navigationToken?: string, isResync?: boolean): void {
    void this.invoke('ReportFolderChanged', {
      folderId,
      clientId: this.clientId,
      navigationToken,
      isResync,
    }).catch(() => undefined);
  }

  executeAction(request: ExecuteActionRequest): Promise<ExecuteActionResponse> {
    return this.http('POST', '/api/actions/run', request);
  }

  runActionFlow(request: RunActionFlowRequest): Promise<RunActionFlowResponse> {
    return this.http('POST', '/api/actions/run-flow', request);
  }

  getActionParameterOptions(request: GetActionParameterOptionsRequest): Promise<GetActionParameterOptionsResponse> {
    return this.http('POST', '/api/actions/options', request);
  }

  getActionButtonStateOptions(
    request: GetActionButtonStateOptionsRequest,
  ): Promise<GetActionButtonStateOptionsResponse> {
    return this.http('POST', '/api/actions/provider-states', request);
  }

  getActionProviderIcon(request: GetActionProviderIconRequest): Promise<GetActionProviderIconResponse> {
    return this.http('POST', '/api/actions/provider-icon', request);
  }

  getFilesystemEntries(request: GetFilesystemEntriesRequest): Promise<GetFilesystemEntriesResponse> {
    return this.http('POST', '/api/filesystem/list', request);
  }

  createSecret(request: CreateSecretRequest): Promise<CreateSecretResponse> {
    return this.http('POST', '/api/secrets', request);
  }

  updateSecret(request: UpdateSecretRequest): Promise<UpdateSecretResponse> {
    return this.http('PUT', '/api/secrets', request);
  }

  deleteSecret(id: string): Promise<DeleteSecretResponse> {
    return this.http('DELETE', `/api/secrets/${encodeURIComponent(id)}`);
  }

  revealSecret(id: string): Promise<RevealSecretResponse> {
    return this.http('POST', `/api/secrets/${encodeURIComponent(id)}/reveal`);
  }

  cloneSecret(id: string): Promise<CloneSecretResponse> {
    return this.http('POST', `/api/secrets/${encodeURIComponent(id)}/clone`);
  }

  renderLabelPreview(request: LabelImagePreviewRequest): Promise<string | null> {
    return this.invokeResult<string | null>('RenderLabelPreview', request);
  }

  getFontFileUrl(faceId: string): string {
    return `${this.baseUrl}/api/system/fonts/${encodeURIComponent(faceId)}/file`;
  }

  getMusicPlayerInstances(): Promise<GetMusicPlayerInstancesResponse | null> {
    return this.invokeResult<GetMusicPlayerInstancesResponse>('GetMusicPlayerInstances');
  }

  getMusicPlayerState(instanceId?: string): Promise<GetMusicPlayerStateResponse | null> {
    return this.invokeResult<GetMusicPlayerStateResponse>('GetMusicPlayerState', instanceId ?? null);
  }

  getMusicPlayerArtworkUrl(instanceId: string, artworkId: string, size?: number): string {
    return `${this.baseUrl}/api/music-player/artwork/${encodeURIComponent(artworkId)}`
      + `?instanceId=${encodeURIComponent(instanceId)}`
      + (size ? `&size=${size}` : '');
  }

  getIntegrationIconUrl(integrationId: string, version?: string | null): string {
    const url = `${this.baseUrl}/api/integrations/${encodeURIComponent(integrationId)}/icon`;
    return version ? `${url}?v=${encodeURIComponent(version)}` : url;
  }

  getWeatherInstances(): Promise<GetWeatherInstancesResponse | null> {
    return this.invokeResult<GetWeatherInstancesResponse>('GetWeatherInstances');
  }

  getWeatherState(instanceId?: string): Promise<GetWeatherStateResponse | null> {
    return this.invokeResult<GetWeatherStateResponse>('GetWeatherState', instanceId ?? null);
  }

  // Config UI sessions (issue #543): a control-plane-independent Macro Deck UI tree standing in for
  // an integration's config flow or an action's parameter list.
  openConfigUiSession(request: OpenConfigUiSessionRequest): Promise<OpenConfigUiSessionResponse | null> {
    return this.invokeResult<OpenConfigUiSessionResponse>('OpenConfigUiSession', request);
  }

  openWidgetUiSession(request: OpenWidgetUiSessionRequest): Promise<OpenWidgetUiSessionResponse | null> {
    return this.invokeResult<OpenWidgetUiSessionResponse>('OpenWidgetUiSession', request);
  }

  openFolderUiSession(request: OpenFolderUiSessionRequest): Promise<OpenFolderUiSessionResponse | null> {
    return this.invokeResult<OpenFolderUiSessionResponse>('OpenFolderUiSession', request);
  }

  openModalUiSession(request: OpenModalUiSessionRequest): Promise<OpenModalUiSessionResponse | null> {
    return this.invokeResult<OpenModalUiSessionResponse>('OpenModalUiSession', request);
  }

  completeUiModal(request: CompleteUiModalRequest): Promise<CompleteUiModalResponse | null> {
    return this.invokeResult<CompleteUiModalResponse>('CompleteUiModal', request);
  }

  onUiModalOpened(): Observable<UiModalOpenedEvent> {
    return this.onNotification<UiModalOpenedEvent>('UiModalOpenedEvent');
  }

  getFolderViews(): Promise<GetFolderViewsResponse | null> {
    return this.invokeResult<GetFolderViewsResponse>('GetFolderViews', {});
  }

  listUiPreviews(): Promise<ListUiPreviewsResponse | null> {
    return this.invokeResult<ListUiPreviewsResponse>('ListUiPreviews', {});
  }

  openUiPreviewSession(request: OpenUiPreviewSessionRequest): Promise<OpenUiPreviewSessionResponse | null> {
    return this.invokeResult<OpenUiPreviewSessionResponse>('OpenUiPreviewSession', request);
  }

  onFolderViewCatalogChanged(): Observable<FolderViewCatalogChangedEvent> {
    return this.onNotification<FolderViewCatalogChangedEvent>('FolderViewCatalogChangedEvent');
  }

  onWidgetTypeCatalogChanged(): Observable<WidgetTypeCatalogChangedEvent> {
    return this.onNotification<WidgetTypeCatalogChangedEvent>('WidgetTypeCatalogChangedEvent');
  }

  attachUiSession(sessionId: string): Promise<UiAttachSessionResponse | null> {
    return this.invokeResult<UiAttachSessionResponse>('AttachUiSession', { sessionId });
  }

  detachUiSession(sessionId: string): Promise<void> {
    return this.invoke('DetachUiSession', sessionId);
  }

  closeUiSession(sessionId: string): Promise<boolean | null> {
    return this.invokeResult<boolean>('CloseUiSession', sessionId);
  }

  sendUiEvent(request: UiSendEventRequest): Promise<UiSendEventResponse | null> {
    return this.invokeResult<UiSendEventResponse>('SendUiEvent', request);
  }

  onUiSessionTreeUpdated(): Observable<UiSessionTreeUpdatedEvent> {
    return this.onNotification<UiSessionTreeUpdatedEvent>('UiSessionTreeUpdatedEvent');
  }

  onUiSessionPatched(): Observable<UiSessionPatchedEvent> {
    return this.onNotification<UiSessionPatchedEvent>('UiSessionPatchedEvent');
  }

  onUiSessionInvalidated(): Observable<UiSessionInvalidatedEvent> {
    return this.onNotification<UiSessionInvalidatedEvent>('UiSessionInvalidatedEvent');
  }

  onUiSessionClosed(): Observable<UiSessionClosedEvent> {
    return this.onNotification<UiSessionClosedEvent>('UiSessionClosedEvent');
  }

  getIconPacks(): Promise<GetIconPacksResponse> {
    return this.http('GET', '/api/icon-packs');
  }

  getIcons(request: GetIconsRequest): Promise<GetIconsResponse> {
    return this.http('GET', `/api/icons?packId=${encodeURIComponent(request.packId)}`);
  }

  createIconPack(request: CreateIconPackRequest): Promise<CreateIconPackResponse> {
    return this.http('POST', '/api/icon-packs', request);
  }

  updateIconPack(packId: string, request: UpdateIconPackRequest): Promise<UpdateIconPackResponse> {
    return this.http('PUT', `/api/icon-packs/${encodeURIComponent(packId)}`, request);
  }

  deleteIconPack(packId: string): Promise<DeleteIconPackResponse> {
    return this.http('DELETE', `/api/icon-packs/${encodeURIComponent(packId)}`);
  }

  renameIcon(iconId: string, request: UpdateIconRequest): Promise<UpdateIconResponse> {
    return this.http('PATCH', `/api/icons/${encodeURIComponent(iconId)}`, request);
  }

  deleteIcon(iconId: string): Promise<DeleteIconResponse> {
    return this.http('DELETE', `/api/icons/${encodeURIComponent(iconId)}`);
  }

  deleteIcons(request: DeleteIconsRequest): Promise<DeleteIconsResponse> {
    return this.http('POST', '/api/icons/delete', request);
  }

  async uploadIcons(packId: string | null, files: File[]): Promise<ImportIconsResponse> {
    const form = new FormData();
    for (const file of files) {
      const relativePath = (file as File & { webkitRelativePath?: string }).webkitRelativePath;
      form.append('files', file, relativePath || file.name);
    }

    const path = packId
      ? `/api/icon-packs/${encodeURIComponent(packId)}/import`
      : '/api/icons/import';
    const response = await this.fetchWithAuth(path, { method: 'POST', body: form });
    return ApiService.parseResponse<ImportIconsResponse>(response);
  }

  importIconsFromPath(packId: string, request: ImportIconsFromPathRequest): Promise<ImportIconsResponse> {
    return this.http('POST', `/api/icon-packs/${encodeURIComponent(packId)}/import-path`, request);
  }

  importSingleIconFromPath(request: ImportSingleIconFromPathRequest): Promise<ImportSingleIconResponse> {
    return this.http('POST', '/api/icons/single-from-path', request);
  }

  async importIconPacks(files: File[]): Promise<ImportIconPacksResponse> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }

    const response = await this.fetchWithAuth('/api/icon-packs/import', { method: 'POST', body: form });
    return ApiService.parseResponse<ImportIconPacksResponse>(response);
  }

  restoreIconPackFromPath(path: string): Promise<ImportIconPacksResponse> {
    return this.http('POST', '/api/icon-packs/restore-from-path', { path });
  }

  async exportIconPack(packId: string): Promise<{ blob: Blob; fileName: string }> {
    const response = await this.fetchWithAuth(
      `/api/icon-packs/${encodeURIComponent(packId)}/export`,
      { method: 'GET' }
    );
    if (!response.ok) {
      throw new Error(`Icon pack export failed with status ${response.status}`);
    }

    const fileName =
      parseContentDispositionFileName(response.headers.get('Content-Disposition')) ??
      'icon-pack.macroDeckIconPack';
    return { blob: await response.blob(), fileName };
  }

  getIconImportBatch(batchId: string): Promise<GetIconImportBatchResponse> {
    return this.http('GET', `/api/icons/import-batches/${encodeURIComponent(batchId)}`);
  }

  cancelIconImportBatch(batchId: string): Promise<CancelIconImportBatchResponse> {
    return this.http('POST', `/api/icons/import-batches/${encodeURIComponent(batchId)}/cancel`);
  }

  getIconImageUrl(iconId: string, size?: number): string {
    const query = size ? `?size=${size}` : '';
    return `${this.baseUrl}/api/icons/${encodeURIComponent(iconId)}/image${query}`;
  }

  getSystemFonts(): Promise<GetSystemFontsResponse> {
    return this.http('GET', '/api/system/fonts');
  }

  getConnectionInfo(): Promise<GetConnectionInfoResponse> {
    return this.http('GET', '/api/system/connection-info');
  }

  getDeviceSetup(): Promise<GetDeviceSetupResponse> {
    return this.http('GET', '/api/device-setup');
  }

  getApplicationFocusSupport(): Promise<GetApplicationFocusCapabilityResponse> {
    return this.http('GET', '/api/system/application-focus');
  }

  getRunningApplications(filter?: string): Promise<GetRunningApplicationsResponse> {
    const query = filter ? `?filter=${encodeURIComponent(filter)}` : '';
    return this.http('GET', `/api/system/running-applications${query}`);
  }

  getHostLockState(): Promise<GetHostLockStateResponse> {
    return this.http('GET', '/api/system/lock-state');
  }

  onHostLockStateChanged(): Observable<HostLockStateChangedEvent> {
    return this.onNotification<HostLockStateChangedEvent>('HostLockStateChangedEvent');
  }

  // Macro Deck Connect (issue #524)
  getConnectSession(): Promise<GetConnectSessionResponse> {
    return this.http('GET', '/api/connect/session');
  }

  // The returned authorizeUrl must be opened in the system browser, never navigated to in-app.
  startConnectSignIn(): Promise<StartConnectSignInResponse> {
    return this.http('POST', '/api/connect/signin/start');
  }

  cancelConnectSignIn(): Promise<void> {
    return this.http('POST', '/api/connect/signin/cancel');
  }

  signOutConnect(): Promise<void> {
    return this.http('POST', '/api/connect/signout');
  }

  getConnectAvatarUrl(): string {
    return `${this.baseUrl}/api/connect/avatar`;
  }

  onConnectSessionChanged(): Observable<ConnectSessionChangedNotification> {
    return this.onNotification<ConnectSessionChangedNotification>('ConnectSessionChangedNotification');
  }

  getLogs(request: GetLogsRequest = {}): Promise<GetLogsResponse> {
    const query = new URLSearchParams();
    if (request.limit !== undefined) {
      query.set('limit', String(request.limit));
    }
    if (request.before) {
      query.set('before', request.before);
    }
    for (const level of request.levels ?? []) {
      query.append('levels', level);
    }
    if (request.source) {
      query.set('source', request.source);
    }
    if (request.integrationId) {
      query.set('integrationId', request.integrationId);
    }
    if (request.category) {
      query.set('category', request.category);
    }
    if (request.search) {
      query.set('search', request.search);
    }
    if (request.from) {
      query.set('from', request.from);
    }
    if (request.to) {
      query.set('to', request.to);
    }

    const suffix = query.size === 0 ? '' : `?${query.toString()}`;

    return this.http('GET', `/api/logs${suffix}`);
  }

  getLogSources(): Promise<GetLogSourcesResponse> {
    return this.http('GET', '/api/logs/sources');
  }

  getNotifications(): Promise<GetUserNotificationsResponse> {
    return this.http('GET', '/api/notifications');
  }

  dismissNotification(id: string): Promise<void> {
    return this.http('DELETE', `/api/notifications/${encodeURIComponent(id)}`);
  }

  dismissAllNotifications(): Promise<void> {
    return this.http('DELETE', '/api/notifications');
  }

  getVariables(request: GetVariablesRequest = {}): Promise<GetVariablesResponse> {
    return this.http('GET', '/api/variables');
  }

  createVariable(request: CreateVariableRequest): Promise<CreateVariableResponse> {
    return this.http('POST', '/api/variables', request);
  }

  updateVariable(request: UpdateVariableRequest): Promise<UpdateVariableResponse> {
    return this.http('PUT', '/api/variables', request);
  }

  deleteVariable(request: DeleteVariableRequest): Promise<DeleteVariableResponse> {
    return this.http('DELETE', `/api/variables/${request.id}`);
  }

  setVariableValue(request: SetVariableValueRequest): Promise<SetVariableValueResponse> {
    return this.http('PATCH', `/api/variables/${request.id}/value`, request);
  }

  sanitizeVariableName(request: SanitizeVariableNameRequest): Promise<SanitizeVariableNameResponse> {
    return this.http('POST', '/api/variables/sanitize-name', request);
  }

  getVariableCatalogProviders(): Promise<GetVariableCatalogProvidersResponse | null> {
    return this.invokeResult<GetVariableCatalogProvidersResponse>('GetVariableCatalogProviders');
  }

  discoverCatalogVariables(
    request: DiscoverCatalogVariablesRequest,
  ): Promise<DiscoverCatalogVariablesResponse | null> {
    return this.invokeResult<DiscoverCatalogVariablesResponse>('DiscoverCatalogVariables', request);
  }

  resolveCatalogVariable(request: ResolveCatalogVariableRequest): Promise<ResolveCatalogVariableResponse | null> {
    return this.invokeResult<ResolveCatalogVariableResponse>('ResolveCatalogVariable', request);
  }

  bindCatalogVariable(request: BindCatalogVariableRequest): Promise<BindCatalogVariableResponse> {
    return this.http('POST', '/api/variables/catalog/bind', request);
  }

  unbindCatalogVariable(request: UnbindCatalogVariableRequest): Promise<UnbindCatalogVariableResponse> {
    return this.http('DELETE', `/api/variables/catalog/${encodeURIComponent(request.variableId)}`);
  }

  renameCatalogVariable(request: RenameCatalogVariableRequest): Promise<RenameCatalogVariableResponse> {
    return this.http('PATCH', `/api/variables/catalog/${encodeURIComponent(request.variableId)}/name`, request);
  }

  renderTemplate(request: RenderTemplateRequest): Promise<RenderTemplateResponse> {
    return this.http('POST', '/api/templates/render', request);
  }

  evaluateCondition(request: EvaluateConditionRequest): Promise<EvaluateConditionResponse> {
    return this.http('POST', '/api/templates/evaluate-condition', request);
  }

  evaluateExpression(request: EvaluateExpressionRequest): Promise<EvaluateExpressionResponse> {
    return this.http('POST', '/api/templates/evaluate-expression', request);
  }

  // Key ring protection (ADR 0047, issue #672). Status is anonymous and always served, even while
  // the host is locked - it is how a client discovers that it is locked at all. Unlock is anonymous
  // and loopback-only: it 404s for anything but the desktop shell's own connection, the same
  // restriction the backup endpoints below use, because a locked host answers 503 to the endpoints
  // that would otherwise authenticate a remote caller.
  getKeyRingStatus(): Promise<GetKeyRingStatusResponse> {
    return this.http('GET', '/api/key-ring/status');
  }

  unlockKeyRing(request: UnlockKeyRingRequest): Promise<UnlockKeyRingResponse> {
    return this.http('POST', '/api/key-ring/unlock', request);
  }

  getKeyRingProtection(): Promise<GetKeyRingProtectionResponse> {
    return this.http('GET', '/api/key-ring/protection');
  }

  // Backups (issue #36). Download, Inspect, PrepareRestore, CommitRestore and CancelRestore are
  // loopback-only on the host (a backup archive holds the Data Protection key ring and every
  // stored secret it protects) - they 404 for anything but the desktop shell's own connection.
  getBackups(): Promise<GetBackupsResponse> {
    return this.http('GET', '/api/backups');
  }

  createBackup(request: CreateBackupRequest = {}): Promise<CreateBackupResponse> {
    return this.http('POST', '/api/backups', request);
  }

  deleteBackup(backupId: string): Promise<DeleteBackupResponse> {
    return this.http('DELETE', `/api/backups/${encodeURIComponent(backupId)}`);
  }

  getBackupSettings(): Promise<GetBackupSettingsResponse> {
    return this.http('GET', '/api/backups/settings');
  }

  updateBackupSettings(request: UpdateBackupSettingsRequest): Promise<UpdateBackupSettingsResponse> {
    return this.http('PATCH', '/api/backups/settings', request);
  }

  getBackupRecoveryKeyState(): Promise<GetBackupRecoveryKeyStateResponse> {
    return this.http('GET', '/api/backups/recovery-key');
  }

  createBackupRecoveryKey(): Promise<BackupRecoveryKeyResponse> {
    return this.http('POST', '/api/backups/recovery-key');
  }

  acknowledgeBackupRecoveryKey(): Promise<BackupRecoveryKeyResponse> {
    return this.http('POST', '/api/backups/recovery-key/acknowledge');
  }

  revealBackupRecoveryKey(request: RevealBackupRecoveryKeyRequest): Promise<BackupRecoveryKeyResponse> {
    return this.http('POST', '/api/backups/recovery-key/reveal', request);
  }

  getBackupStatus(): Promise<GetBackupStatusResponse> {
    return this.http('GET', '/api/backups/status');
  }

  getHostSession(): Promise<GetHostSessionResponse> {
    return this.http('GET', '/api/host/session');
  }

  async downloadBackup(backupId: string): Promise<{ blob: Blob; fileName: string }> {
    const response = await this.fetchWithAuth(`/api/backups/${encodeURIComponent(backupId)}/download`, {
      method: 'GET',
    });
    if (!response.ok) {
      throw new TransportError(response.status, `Download failed (${response.status})`);
    }

    const fileName = parseContentDispositionFileName(response.headers.get('Content-Disposition')) ??
      'backup.macroDeckBackup';
    return { blob: await response.blob(), fileName };
  }

  inspectBackup(backupId: string, recoveryKey?: string): Promise<InspectBackupResponse> {
    return this.http('POST', `/api/backups/${encodeURIComponent(backupId)}/inspect`, { recoveryKey });
  }

  prepareRestore(request: PrepareRestoreRequest): Promise<PrepareRestoreResponse> {
    return this.http('POST', '/api/backups/restore/prepare', request);
  }

  commitRestore(request: CommitRestoreRequest): Promise<CommitRestoreResponse> {
    return this.http('POST', '/api/backups/restore/commit', request);
  }

  cancelRestore(request: CancelRestoreRequest): Promise<CancelRestoreResponse> {
    return this.http('POST', '/api/backups/restore/cancel', request);
  }

  getLocalization(): Promise<GetLocalizationResponse> {
    return this.http('GET', '/api/localization');
  }

  getLocalizationSettings(): Promise<GetLocalizationSettingsResponse> {
    return this.http('GET', '/api/settings/localization');
  }

  updateLocalizationSettings(
    request: UpdateLocalizationSettingsRequest
  ): Promise<UpdateLocalizationSettingsResponse> {
    return this.http('PUT', '/api/settings/localization', request);
  }
}

export function parseContentDispositionFileName(header: string | null): string | null {
  if (!header) {
    return null;
  }

  const extended = /filename\*=(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (extended) {
    try {
      return decodeURIComponent(extended[1].trim());
    } catch {
    }
  }

  const plain = /filename="?([^";]+)"?/.exec(header);
  return plain ? plain[1].trim() : null;
}
