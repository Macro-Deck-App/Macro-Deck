import { Injectable, computed, inject, signal } from '@angular/core';
import { AppStrings, GetDataDirectoryResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';

@Injectable({ providedIn: 'root' })
export class DataDirectoryService {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  private readonly state = signal<GetDataDirectoryResponse | null>(null);

  readonly opening = signal(false);

  readonly available = computed(() => this.state() !== null);

  readonly path = computed(() => this.state()?.path ?? null);

  readonly canOpen = computed(() => this.state()?.canOpen ?? false);

  async refresh(): Promise<void> {
    try {
      this.state.set(await this.api.getDataDirectory());
    } catch {
      this.state.set(null);
    }
  }

  async open(): Promise<void> {
    if (!this.canOpen() || this.opening()) {
      return;
    }

    this.opening.set(true);
    try {
      const response = await this.api.openDataDirectory();
      if (!response.success) {
        this.toasts.show(response.error ?? this.localization.translateKey(AppStrings.Shell.DataDirectory.OpenFailed), { variant: 'error' });
      }
    } catch {
      this.toasts.show(this.localization.translateKey(AppStrings.Shell.DataDirectory.OpenFailed), { variant: 'error' });
    } finally {
      this.opening.set(false);
    }
  }
}
