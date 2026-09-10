import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, GetNetworkSettingsResponse } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { RestartNoticeService } from '../../../../services/restart-notice.service';
import { NetworkTlsSettingsComponent } from './network-tls-settings.component';

@Component({
  selector: 'app-network-settings',
  standalone: true,
  imports: [
    FormsModule,
    SettingsSectionComponent,
    SettingsRowComponent,
    InputComponent,
    ToggleSwitchComponent,
    ButtonComponent,
    ErrorBannerComponent,
    ConfirmationModalComponent,
    NetworkTlsSettingsComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './network-settings.component.html',
  styleUrls: ['./network-settings.component.scss'],
})
export class NetworkSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly restartNotice = inject(RestartNoticeService);
  private readonly localization = inject(LocalizationService);

  readonly loaded = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly restartPromptOpen = signal(false);

  readonly configuredPort = signal(0);
  readonly activePort = signal(0);
  readonly defaultPort = signal(0);
  readonly minimumPort = signal(1024);
  readonly maximumPort = signal(65535);
  readonly overriddenByEnvironment = signal(false);
  readonly configuredPortIgnored = signal(false);
  readonly publicListenerUnavailable = signal(false);
  readonly restartSupported = signal(false);
  readonly restartUnsupportedReason = signal<string | null>(null);

  readonly draft = signal('');

  readonly discoveryEnabled = signal(true);
  readonly discoverySaving = signal(false);
  readonly tlsBusy = signal(false);

  // A save sends every TLS value it read earlier, so two overlapping saves can undo each other.
  readonly discoveryLocked = computed(() =>
    !this.loaded() || this.saving() || this.discoverySaving() || this.tlsBusy());

  readonly networkState = signal<GetNetworkSettingsResponse | null>(null);

  readonly restartRequired = this.restartNotice.required;
  readonly canRestart = this.restartNotice.canRestart;
  readonly restarting = this.restartNotice.restarting;

  readonly parsedPort = computed(() => {
    const raw = this.draft().trim();
    return /^\d+$/.test(raw) ? Number(raw) : null;
  });

  readonly portInvalid = computed(() => {
    const port = this.parsedPort();
    return this.draft().trim().length > 0 &&
      (port === null || port < this.minimumPort() || port > this.maximumPort());
  });

  readonly changed = computed(() => {
    const port = this.parsedPort();
    return port !== null && port !== this.configuredPort();
  });

  readonly canSave = computed(() =>
    this.loaded() &&
    !this.saving() &&
    !this.overriddenByEnvironment() &&
    this.draft().trim().length > 0 &&
    !this.portInvalid() &&
    this.changed());

  readonly listeningPortDescription = computed(() => {
    const key = this.publicListenerUnavailable()
      ? AppStrings.Settings.Network.ListeningPortDescriptionNotListening
      : AppStrings.Settings.Network.ListeningPortDescriptionListening;
    return this.localization.translateKey(key, { port: this.activePort(), defaultPort: this.defaultPort() });
  });

  constructor() {
    void this.load();
  }

  async save(): Promise<void> {
    if (!this.canSave()) {
      return;
    }

    const port = this.parsedPort()!;
    this.saving.set(true);
    this.error.set(null);
    try {
      const response = await this.api.updateNetworkSettings({ publicPort: port });
      this.applyState(response);
      if (!response.success) {
        this.error.set(response.error ?? this.localization.translateKey(AppStrings.Settings.Network.PortSaveFailed));
        return;
      }
      if (response.restartRequired && this.canRestart()) {
        this.restartPromptOpen.set(true);
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Network.PortSaveFailed));
      await this.load();
    } finally {
      this.saving.set(false);
    }
  }

  async setDiscoveryEnabled(value: boolean): Promise<void> {
    if (this.discoveryLocked()) {
      return;
    }

    this.discoverySaving.set(true);
    this.discoveryEnabled.set(value);
    this.error.set(null);
    try {
      const response = await this.api.updateNetworkSettings({
        publicPort: this.configuredPort(),
        discoveryEnabled: value,
      });
      this.applyState(response);
      if (!response.success) {
        this.error.set(response.error ?? this.localization.translateKey(AppStrings.Settings.Network.Tls.SaveFailed));
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Network.Tls.SaveFailed));
      await this.load();
    } finally {
      this.discoverySaving.set(false);
    }
  }

  askToRestart(): void {
    if (this.canRestart()) {
      this.restartPromptOpen.set(true);
    }
  }

  async restartNow(): Promise<void> {
    this.restartPromptOpen.set(false);
    this.error.set(null);
    await this.restartNotice.restartNow();
  }

  cancelRestart(): void {
    this.restartPromptOpen.set(false);
  }

  dismissError(): void {
    this.error.set(null);
  }

  onTlsStateChanged(state: GetNetworkSettingsResponse): void {
    this.applyState(state);
    if (state.restartRequired && this.canRestart()) {
      this.restartPromptOpen.set(true);
    }
  }

  private async load(): Promise<void> {
    try {
      this.applyState(await this.api.getNetworkSettings());
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Network.LoadFailed));
    } finally {
      this.loaded.set(true);
    }
  }

  private applyState(state: GetNetworkSettingsResponse): void {
    this.restartNotice.apply(state);
    this.networkState.set(state);
    this.configuredPort.set(state.publicPort);
    this.activePort.set(state.activePublicPort);
    this.defaultPort.set(state.defaultPublicPort);
    this.minimumPort.set(state.minimumPublicPort);
    this.maximumPort.set(state.maximumPublicPort);
    this.overriddenByEnvironment.set(state.overriddenByEnvironment);
    this.configuredPortIgnored.set(state.configuredPortIgnored);
    this.publicListenerUnavailable.set(state.publicListenerUnavailable);
    this.restartSupported.set(state.restartSupported);
    this.restartUnsupportedReason.set(state.restartUnsupportedReason);
    this.discoveryEnabled.set(state.discoveryEnabled);
    this.draft.set(String(state.publicPort));
  }
}
