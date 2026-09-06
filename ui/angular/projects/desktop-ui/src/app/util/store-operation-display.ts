import { AppStrings, StoreExtensionKind, StoreExtensionTrust, StoreOperationBody, StoreOperationState } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { formatBytes } from './format-bytes';

const TERMINAL_STATES: ReadonlySet<StoreOperationState> = new Set(['Completed', 'Failed', 'Cancelled']);

export function isTerminalStoreOperationState(state: StoreOperationState): boolean {
  return TERMINAL_STATES.has(state);
}

const STATE_LABEL_KEYS: Record<StoreOperationState, string> = {
  Queued: AppStrings.Store.Queued,
  Downloading: AppStrings.Store.Downloading,
  Validating: AppStrings.Store.Validating,
  Installing: AppStrings.Store.Installing,
  Completed: AppStrings.Store.Installed,
  Failed: AppStrings.Store.Failed,
  Cancelled: AppStrings.Store.Cancelled,
};

export function storeOperationStateLabelKey(state: StoreOperationState): string | null {
  return STATE_LABEL_KEYS[state] ?? null;
}

export function storeOperationPercent(operation: StoreOperationBody): number | null {
  if (operation.state === 'Validating' || operation.state === 'Installing') {
    return null;
  }
  if (!operation.totalBytes || operation.totalBytes <= 0) {
    return null;
  }
  const percent = (operation.bytesDownloaded / operation.totalBytes) * 100;
  return Math.max(0, Math.min(100, Math.round(percent)));
}

export function formatEta(etaSeconds: number | null | undefined,
  localization: LocalizationService): string | null {
  if (etaSeconds === null || etaSeconds === undefined || etaSeconds < 0 || !Number.isFinite(etaSeconds)) {
    return null;
  }

  if (etaSeconds < 60) {
    return localization.translateKey(AppStrings.Store.EtaSecondsLeft,
      { count: Math.max(1, Math.round(etaSeconds)) });
  }

  return localization.translateKey(AppStrings.Store.EtaMinutesLeft, { count: Math.round(etaSeconds / 60) });
}

export function storeOperationByteReadout(operation: StoreOperationBody,
  localization: LocalizationService): string {
  const downloaded = formatBytes(operation.bytesDownloaded);
  if (!operation.totalBytes || operation.totalBytes <= 0) {
    return downloaded;
  }

  return localization.translateKey(AppStrings.Store.DownloadReadout, {
    downloaded,
    total: formatBytes(operation.totalBytes),
  });
}

export function storeTrustLabelKey(kind: StoreExtensionKind, trust: StoreExtensionTrust | string): string | null {
  if (kind !== 'Plugin') {
    return null;
  }

  if (trust === 'PublisherVerified') {
    return AppStrings.Store.PublisherVerified;
  }

  return null;
}

const KIND_LABEL_KEYS: Record<StoreExtensionKind, string> = {
  Plugin: AppStrings.Store.KindLabel.Plugin,
  IconPack: AppStrings.Store.KindLabel.IconPack,
  ProfileTemplate: AppStrings.Store.KindLabel.ProfileTemplate,
};

export function storeKindLabelKey(kind: StoreExtensionKind): string | null {
  return KIND_LABEL_KEYS[kind] ?? null;
}

const KIND_ICONS: Record<StoreExtensionKind, string> = {
  Plugin: 'puzzle',
  IconPack: 'image',
  ProfileTemplate: 'layers',
};

export function storeKindIcon(kind: StoreExtensionKind): string {
  return KIND_ICONS[kind] ?? 'puzzle';
}

export function storeOperationErrorKey(code: string | null | undefined): string {
  switch (code) {
    case 'SignatureInvalid':
    case 'SignatureUntrusted':
      return AppStrings.Store.Error.SignatureUnverifiable;
    case 'UnsignedNotPermitted':
      return AppStrings.Store.Error.UnsignedNotPermitted;
    case 'TrustDowngrade':
      return AppStrings.Store.Error.TrustDowngrade;
    case 'ChecksumMismatch':
    case 'SizeMismatch':
      return AppStrings.Store.Error.DownloadMismatch;
    case 'DownloadFailed':
      return AppStrings.Store.Error.DownloadFailed;
    case 'ArtifactTooLarge':
      return AppStrings.Store.Error.ArtifactTooLarge;
    case 'MalformedPackage':
      return AppStrings.Store.Error.MalformedPackage;
    case 'Incompatible':
      return AppStrings.Store.Error.Incompatible;
    case 'Unsupported':
      return AppStrings.Store.Error.Unsupported;
    case 'PackageRemoved':
      return AppStrings.Store.Error.PackageRemoved;
    case 'PackageNotFound':
      return AppStrings.Store.Error.PackageNotFound;
    case 'RegistryUnavailable':
      return AppStrings.Store.Error.RegistryUnavailable;
    case 'Interrupted':
      return AppStrings.Store.Error.Interrupted;
    case 'Cancelled':
      return AppStrings.Store.Error.Cancelled;
    default:
      return AppStrings.Store.Error.Failed;
  }
}

const UNINSTALL_MESSAGE_KEYS: Record<StoreExtensionKind, string> = {
  Plugin: AppStrings.Store.UninstallMessagePlugin,
  IconPack: AppStrings.Store.UninstallMessageIconPack,
  ProfileTemplate: AppStrings.Store.UninstallMessageProfileTemplate,
};

export function storeUninstallMessageKey(kind: StoreExtensionKind): string {
  return UNINSTALL_MESSAGE_KEYS[kind];
}

export function storeUninstallErrorKey(code: string | null | undefined): string | null {
  switch (code) {
    case 'DependencyInUse':
      return AppStrings.Store.UninstallDependencyInUse;
    case 'LastProfileProtected':
      return AppStrings.Store.UninstallLastProfile;
    case 'NotInstalled':
      return AppStrings.Store.UninstallNotInstalled;
    case 'OperationInProgress':
      return AppStrings.Store.UninstallOperationInProgress;
    default:
      return null;
  }
}
