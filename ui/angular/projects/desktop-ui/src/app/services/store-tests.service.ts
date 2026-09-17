import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { StoreOperationActionResponse, StoreTestBody } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { isTerminalStoreOperationState } from '../util/store-operation-display';
import { StoreOperationService } from './store-operation.service';

@Injectable({ providedIn: 'root' })
export class StoreTestsService {
  private readonly api = inject(ApiService);
  private readonly operations = inject(StoreOperationService);

  readonly tests = signal<StoreTestBody[]>([]);
  readonly isLoading = signal(false);
  readonly loaded = signal(false);
  readonly errorCode = signal<string | null>(null);

  private generation = 0;
  private readonly liveOperationIds = new Set<string>();

  constructor() {
    effect(() => {
      const listed = new Set(this.tests().map(test => test.packageId));
      let installed = false;
      for (const operation of this.operations.all()) {
        if (operation.kind !== 'TestInstall' || !listed.has(operation.packageId)) {
          continue;
        }

        if (!isTerminalStoreOperationState(operation.state)) {
          this.liveOperationIds.add(operation.id);
        } else if (this.liveOperationIds.delete(operation.id) && operation.state === 'Completed') {
          installed = true;
        }
      }

      if (installed) {
        untracked(() => void this.load());
      }
    });
  }

  async load(): Promise<void> {
    const generation = ++this.generation;
    this.isLoading.set(true);
    try {
      const response = await this.api.getStoreTests();
      if (generation !== this.generation) {
        return;
      }

      if (response.success) {
        this.tests.set(response.tests ?? []);
        this.errorCode.set(null);
      } else {
        this.tests.set([]);
        this.errorCode.set(response.error?.code ?? 'unavailable');
      }
    } catch (error) {
      console.error('Failed to load store tests:', error);
      if (generation === this.generation) {
        this.errorCode.set('unavailable');
      }
    } finally {
      if (generation === this.generation) {
        this.isLoading.set(false);
        this.loaded.set(true);
      }
    }
  }

  install(packageId: string, buildId: string): Promise<StoreOperationActionResponse> {
    return this.operations.installTestBuild(packageId, buildId);
  }
}
