import { ApiError } from './common';

export type StoreExtensionKind = 'Plugin' | 'IconPack' | 'ProfileTemplate';

export type StoreInstallState = 'NotInstalled' | 'Installed' | 'UpdateAvailable' | 'Unsupported';

export type StoreExtensionTrust = 'RegistryAuthenticated' | 'PublisherVerified';

export type StoreOperationState = 'Queued' | 'Downloading' | 'Validating' | 'BackingUp' | 'Installing' | 'Completed' | 'Failed' | 'Cancelled';

export type StoreOperationKind = 'Install' | 'Update' | 'TestInstall';

export type StoreCatalogSection = 'all' | 'newest' | 'recentlyUpdated' | 'name' | 'featured' | 'popular';

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
  installedTestBuild?: string | null;
  unsupportedReason?: string | null;
  trust: StoreExtensionTrust;
  hasIcon: boolean;
  iconSha256?: string | null;
  activeOperationId?: string | null;
  // Absent from an older host.
  previewScreenshotSha256?: string | null;
}

export interface StoreScreenshotBody {
  index: number;
  caption?: string | null;
  sha256?: string | null;
}

export type StoreVersionUnavailableReason = 'UnsupportedPlatform' | 'Unavailable';

// size, installable and unavailableReason are absent from an older host, which cannot install an older version.
export interface StoreVersionHistoryBody {
  version: string;
  releasedAt?: string | null;
  changelog?: string | null;
  size?: number | null;
  installable?: boolean;
  unavailableReason?: StoreVersionUnavailableReason | null;
}

// A standard type is labelled by the client in the viewer's language; only custom links carry a label.
export interface StoreExtensionLinkBody {
  type: string;
  url: string;
  label?: string | null;
}

// The publisher's own declaration, shown as plain text. Null or absent means undeclared, never "no AI".
export interface StoreAiDeclarationBody {
  interaction: boolean;
  generatedContent: boolean;
  generatedAssets: boolean;
  services: string[];
}

// longDescription and changelog are third-party registry markdown: render through
// StoreMarkdownComponent's safe-subset parser, never innerHTML or a DomSanitizer escape hatch.
// history is newest first; an older host sends an empty array, so callers fall back to changelog.
export interface StoreExtensionDetailBody extends StoreCatalogItemBody {
  longDescription?: string | null;
  changelog?: string | null;
  repository?: string | null;
  license?: string | null;
  // Absent from an older host.
  additionalLinks?: StoreExtensionLinkBody[];
  screenshots: StoreScreenshotBody[];
  downloadSize: number;
  supportedOperatingSystems: string[];
  languages: string[];
  // Absent from an older host.
  ai?: StoreAiDeclarationBody | null;
  // Absent from an older host; empty when the registry declares none.
  tags?: string[];
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
  testBuildId?: string | null;
  testBuild?: string | null;
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
  versionPinned?: boolean;
}

export interface StoreAvailableUpdateBody {
  kind: StoreExtensionKind;
  packageId: string;
  name: string;
  installedVersion: string;
  latestVersion: string;
}

export type StoreRegistryRefreshTrigger = 'Manual' | 'Scheduled';

export type StoreRegistryRefreshRunState = 'Running' | 'Succeeded' | 'Failed' | 'Cancelled';

export type StoreRegistryRefreshStep =
  | 'Started'
  | 'FetchingManifest'
  | 'FetchingSignature'
  | 'UpToDate'
  | 'DownloadingFiles'
  | 'Verifying'
  | 'ReadingCatalog'
  | 'Applied'
  | 'Failed'
  | 'Cancelled'
  | 'WaitingForRegistryUpdate';

export interface StoreRegistryRefreshLogEntryBody {
  at: string;
  step: StoreRegistryRefreshStep;
  count?: number | null;
  sequence?: number | null;
  error?: string | null;
  detail?: string | null;
}

// hostInstanceId changes with every host process and revision grows with every published change,
// so a client keeps a snapshot only when it is from another host process or not older than its own.
export interface StoreRegistryRefreshRunBody {
  hostInstanceId: string;
  id: string;
  revision: number;
  trigger: StoreRegistryRefreshTrigger;
  state: StoreRegistryRefreshRunState;
  startedAt: string;
  finishedAt?: string | null;
  filesCompleted: number;
  filesTotal: number;
  entries: StoreRegistryRefreshLogEntryBody[];
}

export interface GetStoreStatusResponse {
  registry: StoreRegistryStatusBody;
  developerMode: boolean;
  refreshRun?: StoreRegistryRefreshRunBody | null;
}

export interface RefreshStoreRegistryResponse {
  success: boolean;
  registry: StoreRegistryStatusBody;
  error?: ApiError | null;
  refreshRun?: StoreRegistryRefreshRunBody | null;
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

export interface GetStoreSimilarResponse {
  items: StoreCatalogItemBody[];
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

export type StoreTestsErrorCode = 'sign_in_required' | 'account_suspended' | 'rate_limited' | 'unavailable';

export type InstallStoreTestBuildErrorCode = StoreTestsErrorCode | 'invalid_request' | 'consent_required' | 'not_found';

export interface StoreTestBuildBody {
  id: string;
  version: string;
  build: string;
  changelog?: string | null;
  sizeInBytes: number;
  uploadedAt: string;
  availableAt: string;
}

// builds are newest first. installedTestBuildId is set only while exactly that test build is the active
// install; installedVersion is whatever version of the plugin is installed, from the Store or a test.
export interface StoreTestBody {
  packageId: string;
  displayName: string;
  joinedAt: string;
  hasIcon: boolean;
  iconSha256?: string | null;
  installedVersion?: string | null;
  installedTestBuildId?: string | null;
  storeVersion?: string | null;
  activeOperationId?: string | null;
  builds: StoreTestBuildBody[];
}

export interface GetStoreTestsResponse {
  success: boolean;
  error?: ApiError | null;
  tests: StoreTestBody[];
}

// The host refuses the install unless consent is true, which a client sends only after the user confirmed.
export interface InstallStoreTestBuildRequest {
  packageId: string;
  buildId: string;
  consent: boolean;
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

export interface StoreRegistryRefreshChangedEvent {
  run: StoreRegistryRefreshRunBody;
}

export interface StoreOperationChangedEvent {
  operation: StoreOperationBody;
}

export interface StoreUpdatesChangedEvent {
  updates: StoreAvailableUpdateBody[];
}

export interface StoreRatingSummaryBody {
  rating?: number | null;
  ratingCount: number;
}

export interface GetStoreRatingsResponse {
  available: boolean;
  ratings: Record<string, StoreRatingSummaryBody>;
}

export interface GetStoreInstallsResponse {
  available: boolean;
  installs: Record<string, number>;
}

export interface StoreRatingBucketBody {
  stars: number;
  count: number;
}

export interface GetStoreRatingResponse {
  available: boolean;
  rating?: number | null;
  ratingCount: number;
  distribution: StoreRatingBucketBody[];
}

// title, body and the reply body are third-party text: render with interpolation only, never markdown or innerHTML.
export interface StoreReviewReplyBody {
  body: string;
  createdAt: string;
  updatedAt: string;
  isEdited: boolean;
}

export interface StoreReviewBody {
  id: string;
  rating: number;
  title?: string | null;
  body?: string | null;
  authorDisplayName: string;
  authorAvatarUrl?: string | null;
  createdAt: string;
  isEdited: boolean;
  downloadedBeforeReview: boolean;
  reply?: StoreReviewReplyBody | null;
}

export type StoreReviewSortOrder = 'Newest' | 'Oldest';

export interface GetStoreReviewsResponse {
  available: boolean;
  items: StoreReviewBody[];
  page: number;
  pageSize: number;
  totalCount: number;
  reviewCount: number;
}

export type StoreReviewComposeState = 'SignedOut' | 'NotEntitled' | 'Entitled' | 'Unavailable';

export interface StoreOwnReviewBody {
  id: string;
  rating: number;
  title?: string | null;
  body?: string | null;
  visibility: string;
  moderationReason?: string | null;
  createdAt: string;
  updatedAt: string;
  isEdited: boolean;
}

export interface GetStoreOwnReviewResponse {
  state: StoreReviewComposeState;
  review?: StoreOwnReviewBody | null;
}

export interface PutStoreOwnReviewRequest {
  rating: number;
  title?: string | null;
  body?: string | null;
}

export type StoreReviewWriteErrorCode =
  | 'sign_in_required'
  | 'download_required'
  | 'account_suspended'
  | 'forbidden'
  | 'moderated'
  | 'gone'
  | 'cooldown'
  | 'retry_later'
  | 'validation'
  | 'not_found'
  | 'already_reported'
  | 'report_unavailable'
  | 'platform_unavailable';

export interface StoreReviewWriteError {
  code: StoreReviewWriteErrorCode;
  field?: 'Rating' | 'Title' | 'Body' | 'Category' | 'Detail' | null;
  retryAfterSeconds?: number | null;
}

export interface StoreOwnReviewWriteResponse {
  success: boolean;
  review?: StoreOwnReviewBody | null;
  error?: StoreReviewWriteError | null;
}

export type StoreEntryReportCategory =
  | 'InappropriateContent'
  | 'Misleading'
  | 'Impersonation'
  | 'Malicious'
  | 'Spam'
  | 'Other';

export type StoreReviewReportCategory = 'Spam' | 'Abuse' | 'OffTopic' | 'Other';

export interface ReportStoreContentRequest {
  category: StoreEntryReportCategory | StoreReviewReportCategory;
  detail?: string | null;
}

export interface StoreReportResponse {
  success: boolean;
  error?: StoreReviewWriteError | null;
}
