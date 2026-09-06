import { ApiError } from './common';

export type StoreExtensionKind = 'Plugin' | 'IconPack' | 'ProfileTemplate';

export type StoreInstallState = 'NotInstalled' | 'Installed' | 'UpdateAvailable' | 'Unsupported';

export type StoreExtensionTrust = 'RegistryAuthenticated' | 'PublisherVerified';

export type StoreOperationState = 'Queued' | 'Downloading' | 'Validating' | 'Installing' | 'Completed' | 'Failed' | 'Cancelled';

export type StoreOperationKind = 'Install' | 'Update';

export type StoreCatalogSection = 'all' | 'newest' | 'recentlyUpdated' | 'name' | 'featured';

export interface StoreRegistryStatusBody {
  hasCatalog: boolean;
  sequence: number;
  signedAt?: string | null;
  fetchedAt?: string | null;
  lastSuccessAt?: string | null;
  lastAttemptAt?: string | null;
  refreshing: boolean;
  stale: boolean;
  lastError?: string | null;
  lastErrorMessage?: string | null;
}

export interface StoreCatalogItemBody {
  kind: StoreExtensionKind;
  id: string;
  name: string;
  description?: string | null;
  publisher?: string | null;
  latestVersion: string;
  createdAt?: string | null;
  updatedAt?: string | null;
  installState: StoreInstallState;
  installedVersion?: string | null;
  unsupportedReason?: string | null;
  trust: StoreExtensionTrust;
  hasIcon: boolean;
  activeOperationId?: string | null;
}

export interface StoreScreenshotBody {
  index: number;
  caption?: string | null;
}

export interface StoreVersionHistoryBody {
  version: string;
  releasedAt?: string | null;
  changelog?: string | null;
}

// longDescription and changelog are third-party registry markdown: render through
// StoreMarkdownComponent's safe-subset parser, never innerHTML or a DomSanitizer escape hatch.
// history is newest first; an older host sends an empty array, so callers fall back to changelog.
export interface StoreExtensionDetailBody extends StoreCatalogItemBody {
  longDescription?: string | null;
  changelog?: string | null;
  repository?: string | null;
  license?: string | null;
  screenshots: StoreScreenshotBody[];
  downloadSize: number;
  supportedOperatingSystems: string[];
  languages: string[];
  history: StoreVersionHistoryBody[];
}

export interface StoreOperationBody {
  id: string;
  kind: StoreOperationKind;
  extensionKind: StoreExtensionKind;
  packageId: string;
  version: string;
  displayName: string;
  previousVersion?: string | null;
  state: StoreOperationState;
  bytesDownloaded: number;
  totalBytes?: number | null;
  etaSeconds?: number | null;
  startedAt: string;
  updatedAt: string;
  completedAt?: string | null;
  error?: string | null;
  errorMessage?: string | null;
  canRetry: boolean;
}

export interface StoreAvailableUpdateBody {
  kind: StoreExtensionKind;
  packageId: string;
  name: string;
  installedVersion: string;
  latestVersion: string;
}

export interface GetStoreStatusResponse {
  registry: StoreRegistryStatusBody;
  developerMode: boolean;
}

export interface RefreshStoreRegistryResponse {
  success: boolean;
  registry: StoreRegistryStatusBody;
  error?: ApiError | null;
}

export interface GetStoreCatalogResponse {
  items: StoreCatalogItemBody[];
  total: number;
  registry: StoreRegistryStatusBody;
}

export interface GetStoreExtensionResponse {
  extension?: StoreExtensionDetailBody | null;
  error?: ApiError | null;
}

export interface GetStoreUpdatesResponse {
  updates: StoreAvailableUpdateBody[];
}

export interface GetStoreOperationsResponse {
  operations: StoreOperationBody[];
}

export interface StoreOperationActionResponse {
  success: boolean;
  operation?: StoreOperationBody | null;
  error?: ApiError | null;
}

export interface InstallStoreExtensionRequest {
  kind: StoreExtensionKind;
  packageId: string;
  version?: string | null;
  allowUnsigned?: boolean;
}

export interface UninstallStoreExtensionRequest {
  kind: StoreExtensionKind;
  id: string;
}

export interface UninstallStoreExtensionResponse {
  success: boolean;
  error?: ApiError | null;
}

export type StoreCatalogChangedEvent = Record<string, never>;

export interface StoreRegistryStatusChangedEvent {
  registry: StoreRegistryStatusBody;
}

export interface StoreOperationChangedEvent {
  operation: StoreOperationBody;
}

export interface StoreUpdatesChangedEvent {
  updates: StoreAvailableUpdateBody[];
}
