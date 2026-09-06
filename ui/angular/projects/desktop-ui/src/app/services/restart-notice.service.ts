import { Injectable, computed, inject, signal } from '@angular/core';
import { AppStrings, GetNetworkSettingsResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';

@Injectable({ providedIn: 'root' })
export class RestartNoticeService {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  private readonly network = signal<GetNetworkSettingsResponse | null>(null);

  readonly restarting = signal(false);

  readonly required = computed(() => this.network()?.restartRequired ?? false);

  readonly canRestart = computed(() => this.network()?.restartSupported ?? false);

  readonly unsupportedReason = computed(() => this.network()?.restartUnsupportedReason ?? null);

  readonly message = computed(() => {
    const state = this.network();
    if (!state?.restartRequired) {
      return '';
    }
    if (state.publicPort !== state.activePublicPort) {
      return this.localization.translateKey(AppStrings.Shell.RestartNotice.PortChanging, {
        activePort: state.activePublicPort,
        port: state.publicPort,
      });
    }
    if (state.publicListenerUnavailable) {
      return this.localization.translateKey(AppStrings.Shell.RestartNotice.PortRetry, {
        port: state.activePublicPort,
      });
    }
    // Nothing is pending on the public port, so HTTPS is the only thing left the host wants a
    // restart for. An active TLS mode without an active HTTPS port means the listener was tried
    // this run and did not come up - the mode survives the port being dropped - while a disabled
    // one means HTTPS was configured since and has never been served. HTTPS also has to still be
    // wanted: switching it off after a failed bind leaves the same active state behind, and there
    // is no port to retry then.
    if (state.tlsEnabled && state.activeTlsMode !== 'Disabled' && state.activeTlsHttpsPort === null) {
      return this.localization.translateKey(AppStrings.Shell.RestartNotice.HttpsRetry, {
        port: state.tlsMode === 'Replace' ? state.publicPort : state.tlsHttpsPort,
      });
    }
    return this.localization.translateKey(AppStrings.Shell.RestartNotice.HttpsChanging);
  });

  apply(state: GetNetworkSettingsResponse): void {
    this.network.set(state);
  }

  async refresh(): Promise<void> {
    try {
      this.apply(await this.api.getNetworkSettings());
    } catch {
    }
  }

  async restartNow(reason = 'network-port'): Promise<void> {
    if (!this.canRestart()) {
      this.toasts.show(this.unsupportedReason() ?? this.localization.translateKey(AppStrings.Shell.RestartNotice.CannotRestartHere));
      return;
    }
    if (this.restarting()) {
      return;
    }

    this.restarting.set(true);
    try {
      const response = await this.api.restartApplication(reason);
      if (!response.success) {
        this.toasts.show(response.error ?? this.localization.translateKey(AppStrings.Shell.RestartNotice.RestartFailed), { variant: 'error' });
        this.restarting.set(false);
      }
    } catch {
    }
  }
}
