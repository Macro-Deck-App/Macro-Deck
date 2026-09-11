import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AdbDevice, AdbDeviceState, AppStrings, GetAdbSettingsResponse, UpdateAdbSettingsRequest } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';

const STATE_LABEL_KEYS: Partial<Record<AdbDeviceState, string>> = {
  Device: AppStrings.Settings.Adb.StateReady,
  Unauthorized: AppStrings.Settings.Adb.StateUnauthorized,
  Offline: AppStrings.Settings.Adb.StateOffline,
  Disconnected: AppStrings.Settings.Adb.StateDisconnected,
  NoPermissions: AppStrings.Settings.Adb.StateNoPermissions,
  Authorizing: AppStrings.Settings.Adb.StateAuthorizing,
};

const SOURCE_LABEL_KEYS: Record<string, string> = {
  None: AppStrings.Settings.Adb.SourceNotFound,
  Configured: AppStrings.Settings.Adb.SourceConfigured,
  AndroidSdkEnvironment: AppStrings.Settings.Adb.SourceSdkEnvironment,
  WellKnownSdkLocation: AppStrings.Settings.Adb.SourceSdkLocation,
  Path: AppStrings.Settings.Adb.SourcePath,
};

@Component({
  selector: 'app-adb-settings',
  standalone: true,
  imports: [
    FormsModule,
    SettingsSectionComponent,
    SettingsRowComponent,
    ToggleSwitchComponent,
    InputComponent,
    ButtonComponent,
    ErrorBannerComponent,
    EmptyStateComponent,
    ConfirmationModalComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './adb-settings.component.html',
  styleUrls: ['./adb-settings.component.scss'],
})
export class AdbSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);
  readonly rescanning = signal(false);
  readonly revealedSerials = signal<ReadonlySet<string>>(new Set());

  readonly supported = signal(false);
  readonly unsupportedReason = signal<string | null>(null);
  readonly enabled = signal(false);
  readonly enabledBusy = signal(false);

  readonly executablePath = signal<string | null>(null);
  readonly resolvedExecutablePath = signal<string | null>(null);
  readonly executableSource = signal('None');
  readonly pathDraft = signal('');
  readonly pathSaving = signal(false);
  readonly downloadingPlatformTools = signal(false);

  readonly adbVersion = signal<string | null>(null);
  readonly serverReachable = signal(false);
  readonly restarting = signal(false);
  readonly restartPromptOpen = signal(false);

  readonly usbConnectionsEnabled = signal(false);
  readonly usbBusy = signal(false);

  readonly devices = signal<AdbDevice[]>([]);
  readonly defaultDeviceSerial = signal<string | null>(null);
  readonly settingDefaultSerial = signal<string | null>(null);

  readonly lastError = signal<string | null>(null);
  readonly lastErrorAt = signal<string | null>(null);
  readonly previousShutdownWasUnclean = signal(false);
  readonly staleTunnelsCleaned = signal(0);

  readonly executableSourceLabel = computed(() => {
    const key = SOURCE_LABEL_KEYS[this.executableSource()];
    return key ? this.localization.translateKey(key) : this.executableSource();
  });

  readonly notFoundWhileEnabled = computed(() => this.enabled() && !this.resolvedExecutablePath());

  readonly pathChanged = computed(() => this.pathDraft().trim() !== (this.executablePath() ?? ''));
  readonly canSavePath = computed(() =>
    this.loaded() && !this.pathSaving() && !this.downloadingPlatformTools() && this.pathChanged());

  readonly lastErrorAtLabel = computed(() => {
    const at = this.lastErrorAt();
    if (!at) {
      return null;
    }
    const date = new Date(at);
    if (Number.isNaN(date.getTime())) return null;
    const { locale, hourCycle } = this.localization.timeLocale();
    return date.toLocaleString(locale, { hourCycle });
  });

  constructor() {
    void this.load();

    this.api.onAdbStateChanged()
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
  }

  async setEnabled(value: boolean): Promise<void> {
    if (!this.supported() || this.enabledBusy()) {
      return;
    }
    this.enabledBusy.set(true);
    this.enabled.set(value);
    try {
      await this.updateSettings({ enabled: value });
    } finally {
      this.enabledBusy.set(false);
    }
  }

  async setUsbConnectionsEnabled(value: boolean): Promise<void> {
    if (!this.loaded() || this.usbBusy()) {
      return;
    }
    this.usbBusy.set(true);
    this.usbConnectionsEnabled.set(value);
    try {
      await this.updateSettings({ usbConnectionsEnabled: value });
    } finally {
      this.usbBusy.set(false);
    }
  }

  async savePath(): Promise<void> {
    if (!this.canSavePath()) {
      return;
    }
    this.pathSaving.set(true);
    try {
      await this.updateSettings({ executablePath: this.pathDraft().trim() || undefined });
    } finally {
      this.pathSaving.set(false);
    }
  }

  async downloadPlatformTools(): Promise<void> {
    if (this.downloadingPlatformTools()) {
      return;
    }
    this.error.set(null);
    this.downloadingPlatformTools.set(true);
    try {
      const response = await this.api.downloadAdbPlatformTools();
      this.applyState(response);
      if (!response.success) {
        this.error.set(response.error ?? this.localization.translateKey(AppStrings.Settings.Adb.DownloadFailed));
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Adb.DownloadFailed));
      await this.load();
    } finally {
      this.downloadingPlatformTools.set(false);
    }
  }

  async setDefaultDevice(serial: string): Promise<void> {
    if (this.settingDefaultSerial() !== null) {
      return;
    }
    this.settingDefaultSerial.set(serial);
    try {
      await this.updateSettings({ defaultDeviceSerial: serial });
    } finally {
      this.settingDefaultSerial.set(null);
    }
  }

  askToRestartServer(): void {
    this.restartPromptOpen.set(true);
  }

  cancelRestart(): void {
    this.restartPromptOpen.set(false);
  }

  async restartServer(): Promise<void> {
    this.restartPromptOpen.set(false);
    this.error.set(null);
    this.restarting.set(true);
    try {
      const response = await this.api.restartAdbServer();
      this.applyState(response);
      if (!response.success) {
        this.error.set(response.error ?? this.localization.translateKey(AppStrings.Settings.Adb.RestartFailed));
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Adb.RestartFailed));
      await this.load();
    } finally {
      this.restarting.set(false);
    }
  }

  async rescan(): Promise<void> {
    this.rescanning.set(true);
    try {
      await this.load();
    } finally {
      this.rescanning.set(false);
    }
  }

  dismissError(): void {
    this.error.set(null);
  }

  serialRevealed(serial: string): boolean {
    return this.revealedSerials().has(serial);
  }

  displaySerial(serial: string): string {
    if (this.serialRevealed(serial)) {
      return serial;
    }

    const visible = serial.slice(-4);
    return `${'•'.repeat(Math.max(serial.length - visible.length, 0))}${visible}`;
  }

  toggleSerial(serial: string): void {
    this.revealedSerials.update(revealed => {
      const next = new Set(revealed);
      if (!next.delete(serial)) {
        next.add(serial);
      }

      return next;
    });
  }

  deviceName(device: AdbDevice): string {
    return device.model ?? device.serial;
  }

  stateLabel(state: AdbDeviceState): string {
    const key = STATE_LABEL_KEYS[state];
    return key ? this.localization.translateKey(key) : state;
  }

  usbStatus(device: AdbDevice): string {
    if (device.tunnelEstablished) {
      return device.tunnelDevicePort !== null
        ? this.localization.translateKey(AppStrings.Settings.Adb.UsbReadyOnPort, { port: device.tunnelDevicePort })
        : this.localization.translateKey(AppStrings.Settings.Adb.StateReady);
    }
    return device.tunnelError ?? this.localization.translateKey(AppStrings.Settings.Adb.UsbNotConnected);
  }

  private async updateSettings(patch: Partial<UpdateAdbSettingsRequest>): Promise<void> {
    this.error.set(null);
    const request: UpdateAdbSettingsRequest = {
      enabled: this.enabled(),
      executablePath: this.executablePath() ?? undefined,
      usbConnectionsEnabled: this.usbConnectionsEnabled(),
      defaultDeviceSerial: this.defaultDeviceSerial() ?? undefined,
      ...patch,
    };
    try {
      const response = await this.api.updateAdbSettings(request);
      this.applyState(response);
      if (!response.success) {
        this.error.set(response.error ?? this.localization.translateKey(AppStrings.Settings.Adb.SaveFailed));
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Adb.SaveFailed));
      await this.load();
    }
  }

  private async load(): Promise<void> {
    try {
      this.applyState(await this.api.getAdbSettings());
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Adb.LoadFailed));
    } finally {
      this.loaded.set(true);
    }
  }

  private applyState(state: GetAdbSettingsResponse): void {
    this.supported.set(state.supported);
    this.unsupportedReason.set(state.unsupportedReason);
    this.enabled.set(state.enabled);
    this.executablePath.set(state.executablePath);
    this.resolvedExecutablePath.set(state.resolvedExecutablePath);
    this.executableSource.set(state.executableSource);
    this.pathDraft.set(state.executablePath ?? '');
    this.adbVersion.set(state.adbVersion);
    this.serverReachable.set(state.serverReachable);
    this.usbConnectionsEnabled.set(state.usbConnectionsEnabled);
    this.devices.set(state.devices);
    this.defaultDeviceSerial.set(state.defaultDeviceSerial);
    this.lastError.set(state.lastError);
    this.lastErrorAt.set(state.lastErrorAt);
    this.previousShutdownWasUnclean.set(state.previousShutdownWasUnclean);
    this.staleTunnelsCleaned.set(state.staleTunnelsCleaned);
  }
}
