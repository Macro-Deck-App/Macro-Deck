import { AppStrings, StoreOperationBody } from '@macro-deck/runtime';
import { storeOperationErrorKey, storeOperationPercent, storeTrustLabelKey, storeUninstallErrorKey } from './store-operation-display';

function operation(overrides: Partial<StoreOperationBody> = {}): StoreOperationBody {
  return {
    id: 'op-1',
    kind: 'Install',
    extensionKind: 'Plugin',
    packageId: 'app.example.plugin',
    version: '1.0.0',
    displayName: 'Example',
    state: 'Downloading',
    bytesDownloaded: 0,
    totalBytes: null,
    etaSeconds: null,
    startedAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    canRetry: false,
    ...overrides,
  };
}

describe('storeTrustLabelKey', () => {
  it('never yields a verified label for an icon pack, whatever trust value it is handed', () => {
    expect(storeTrustLabelKey('IconPack', 'PublisherVerified')).toBeNull();
    expect(storeTrustLabelKey('IconPack', 'RegistryAuthenticated')).toBeNull();
    expect(storeTrustLabelKey('IconPack', 'some-unrecognised-value')).toBeNull();
  });

  it('never yields a verified label for a profile template, whatever trust value it is handed', () => {
    expect(storeTrustLabelKey('ProfileTemplate', 'PublisherVerified')).toBeNull();
    expect(storeTrustLabelKey('ProfileTemplate', 'RegistryAuthenticated')).toBeNull();
    expect(storeTrustLabelKey('ProfileTemplate', 'unrecognised')).toBeNull();
  });

  it('only yields a verified label for a plugin with PublisherVerified trust', () => {
    expect(storeTrustLabelKey('Plugin', 'PublisherVerified')).toBe(AppStrings.Store.PublisherVerified);
    expect(storeTrustLabelKey('Plugin', 'RegistryAuthenticated')).toBeNull();
    expect(storeTrustLabelKey('Plugin', 'unrecognised')).toBeNull();
  });
});

describe('storeOperationPercent', () => {
  it('is null while validating, even with a full byte count on record', () => {
    expect(storeOperationPercent(operation({ state: 'Validating', bytesDownloaded: 100, totalBytes: 100 })))
      .toBeNull();
  });

  it('is null while installing, even with a full byte count on record', () => {
    expect(storeOperationPercent(operation({ state: 'Installing', bytesDownloaded: 100, totalBytes: 100 })))
      .toBeNull();
  });

  it('is null when downloading but the host has not reported a total yet', () => {
    expect(storeOperationPercent(operation({ state: 'Downloading', bytesDownloaded: 40, totalBytes: null })))
      .toBeNull();
  });

  it('is a rounded percentage when downloading with a known total', () => {
    expect(storeOperationPercent(operation({ state: 'Downloading', bytesDownloaded: 25, totalBytes: 100 })))
      .toBe(25);
  });
});

describe('storeOperationErrorKey', () => {
  it('explains a refused signature as a statement about the package, not about the mechanism', () => {
    expect(storeOperationErrorKey('SignatureUntrusted')).toBe(AppStrings.Store.Error.SignatureUnverifiable);
    expect(storeOperationErrorKey('SignatureInvalid')).toBe(AppStrings.Store.Error.SignatureUnverifiable);
  });

  it('falls back to a plain sentence for a code this build does not know', () => {
    expect(storeOperationErrorKey('SomethingAddedLater')).toBe(AppStrings.Store.Error.Failed);
    expect(storeOperationErrorKey(undefined)).toBe(AppStrings.Store.Error.Failed);
  });
});

describe('storeUninstallErrorKey', () => {
  it('maps each actionable code to its own key', () => {
    expect(storeUninstallErrorKey('DependencyInUse')).toBe(AppStrings.Store.UninstallDependencyInUse);
    expect(storeUninstallErrorKey('LastProfileProtected')).toBe(AppStrings.Store.UninstallLastProfile);
    expect(storeUninstallErrorKey('NotInstalled')).toBe(AppStrings.Store.UninstallNotInstalled);
    expect(storeUninstallErrorKey('OperationInProgress')).toBe(AppStrings.Store.UninstallOperationInProgress);
  });

  it('returns null for the generic failure and for a code this build does not know, so a caller never duplicates the title as its own detail', () => {
    expect(storeUninstallErrorKey('Failed')).toBeNull();
    expect(storeUninstallErrorKey('SomethingAddedLater')).toBeNull();
    expect(storeUninstallErrorKey(undefined)).toBeNull();
  });
});
