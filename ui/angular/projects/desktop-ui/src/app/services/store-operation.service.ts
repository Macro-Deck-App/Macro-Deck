import { Injectable, Signal, computed, effect, inject, signal } from '@angular/core';
import { AppStrings, StoreExtensionKind, StoreOperationActionResponse, StoreOperationBody, UninstallStoreExtensionResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { isTerminalStoreOperationState, storeOperationErrorKey } from '../util/store-operation-display';

@Injectable({ providedIn: 'root' })
export class StoreOperationService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);

  private readonly operationsById = signal<Map<string, StoreOperationBody>>(new Map());
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  private readonly operationForCache = new Map<string, Signal<StoreOperationBody | null>>();

  private inFlightLoad: Promise<void> | null = null;

  constructor() {
    this.api.onNotification<{ operation: StoreOperationBody }>('StoreOperationChangedEvent')
      .subscribe(event => {
        this.upsert(event.operation);
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
      const response = await this.api.getStoreOperations();
      const next = new Map<string, StoreOperationBody>();
      for (const operation of response.operations ?? []) {
        next.set(operation.id, operation);
      }
      this.operationsById.set(next);
    } catch (error) {
      console.error('Failed to load store operations:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.StoreOperation.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  private upsert(operation: StoreOperationBody): void {
    const next = new Map(this.operationsById());
    next.set(operation.id, operation);
    this.operationsById.set(next);
  }

  readonly all = computed(() =>
    Array.from(this.operationsById().values())
      .sort((a, b) => (a.updatedAt < b.updatedAt ? 1 : a.updatedAt > b.updatedAt ? -1 : 0)));

  readonly hasActiveOperations = computed(() =>
    this.all().some(operation => !isTerminalStoreOperationState(operation.state)));

  operationFor(kind: StoreExtensionKind, packageId: string): Signal<StoreOperationBody | null> {
    const key = `${kind}:${packageId}`;
    let cached = this.operationForCache.get(key);
    if (!cached) {
      cached = computed(() => {
        const matches = Array.from(this.operationsById().values())
          .filter(op => op.extensionKind === kind && op.packageId === packageId);
        if (matches.length === 0) {
          return null;
        }

        const active = matches.find(op => !isTerminalStoreOperationState(op.state));
        if (active) {
          return active;
        }

        return matches.reduce((latest, op) => (op.updatedAt > latest.updatedAt ? op : latest));
      });
      this.operationForCache.set(key, cached);
    }
    return cached;
  }

  async install(kind: StoreExtensionKind,
                packageId: string,
                version?: string,
                allowUnsigned = false): Promise<StoreOperationBody | null> {
    const response = await this.api.installStoreExtension({ kind, packageId, version, allowUnsigned });
    if (response.operation) {
      this.upsert(response.operation);
    } else if (!response.success) {
      this.toasts.show(this.localization.translateKey(AppStrings.Store.InstallationFailed), {
        detail: this.localization.translateKey(storeOperationErrorKey(response.error?.code)),
        variant: 'error',
      });
    }
    return response.operation ?? null;
  }

  async installTestBuild(packageId: string, buildId: string): Promise<StoreOperationActionResponse> {
    const response = await this.api.installStoreTestBuild(packageId, buildId);
    if (response.operation) {
      this.upsert(response.operation);
    }
    return response;
  }

  async retry(operationId: string): Promise<StoreOperationBody | null> {
    const response = await this.api.retryStoreOperation(operationId);
    if (response.operation) {
      this.upsert(response.operation);
    }
    return response.operation ?? null;
  }

  async cancel(operationId: string): Promise<StoreOperationBody | null> {
    const response = await this.api.cancelStoreOperation(operationId);
    if (response.operation) {
      this.upsert(response.operation);
    }
    return response.operation ?? null;
  }

  async dismiss(operationId: string): Promise<void> {
    await this.api.dismissStoreOperation(operationId);
    const next = new Map(this.operationsById());
    next.delete(operationId);
    this.operationsById.set(next);
  }

  uninstall(kind: StoreExtensionKind, packageId: string): Promise<UninstallStoreExtensionResponse> {
    return this.api.uninstallStoreExtension(kind, packageId);
  }
}
