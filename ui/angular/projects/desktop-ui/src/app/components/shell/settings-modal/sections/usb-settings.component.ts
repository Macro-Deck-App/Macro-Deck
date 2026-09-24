import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AppStrings,
  GetNativeUsbSettingsResponse,
  NativeUsbDevice,
  NativeUsbDeviceState,
  NativeUsbRememberedDevice,
} from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, LocalizationService, SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';

type ChipTone = 'ok' | 'pending' | 'neutral';

const STATE_LABEL_KEYS: Record<NativeUsbDeviceState, string> = {
  Available: AppStrings.Settings.Usb.StateAvailable,
  ServedByAdb: AppStrings.Settings.Usb.StateServedByAdb,
  Waiting: AppStrings.Settings.Usb.StateWaiting,
  WaitingForApp: AppStrings.Settings.Usb.StateWaitingForApp,
  Linked: AppStrings.Settings.Usb.StateLinked,
  Closed: AppStrings.Settings.Usb.StateClosed,
  Stopped: AppStrings.Settings.Usb.StateStopped,
  NotSupported: AppStrings.Settings.Usb.StateNotSupported,
  Connecting: AppStrings.Settings.Usb.StateConnecting,
  Switching: AppStrings.Settings.Usb.StateSwitching,
};

const STATE_TONES: Record<NativeUsbDeviceState, ChipTone> = {
  Available: 'neutral',
  ServedByAdb: 'neutral',
  Waiting: 'pending',
  WaitingForApp: 'pending',
  Linked: 'ok',
  Closed: 'neutral',
  Stopped: 'neutral',
  NotSupported: 'neutral',
  Connecting: 'pending',
  Switching: 'pending',
};

@Component({
  selector: 'app-usb-settings',
  standalone: true,
  imports: [
    SettingsSectionComponent,
    SettingsRowComponent,
    ToggleSwitchComponent,
    ButtonComponent,
    ErrorBannerComponent,
    EmptyStateComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './usb-settings.component.html',
  styleUrls: ['./usb-settings.component.scss'],
})
export class UsbSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);
  readonly rescanning = signal(false);
  readonly enabled = signal(false);
  readonly enabledBusy = signal(false);
  readonly androidAvailable = signal(false);
  readonly iosAvailable = signal(false);
  readonly bridgeAvailable = signal(true);
  readonly httpsOnly = signal(false);
  readonly devices = signal<NativeUsbDevice[]>([]);
  readonly rememberedDevices = signal<NativeUsbRememberedDevice[]>([]);
  readonly connectingId = signal<string | null>(null);
  readonly forgettingSerial = signal<string | null>(null);
  readonly revealedSerials = signal<ReadonlySet<string>>(new Set());

  readonly hasAndroid = computed(() => this.devices().some(device => device.platform === 'Android'));
  readonly hasIos = computed(() => this.devices().some(device => device.platform === 'Ios'));

  readonly bridgeNote = computed(() => {
    if (this.bridgeAvailable()) {
      return null;
    }
    return this.localization.translateKey(this.httpsOnly()
      ? AppStrings.Settings.Usb.HttpsOnly
      : AppStrings.Settings.Usb.NoPublicListener);
  });

  constructor() {
    void this.load();

    this.api.onNativeUsbStateChanged()
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
  }

  async setEnabled(value: boolean): Promise<void> {
    if (!this.loaded() || this.enabledBusy()) {
      return;
    }
    this.error.set(null);
    this.enabledBusy.set(true);
    this.enabled.set(value);
    try {
      this.applyState(await this.api.updateNativeUsbSettings({ enabled: value }));
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Usb.SaveFailed));
      await this.load();
    } finally {
      this.enabledBusy.set(false);
    }
  }

  async connect(device: NativeUsbDevice): Promise<void> {
    if (this.connectingId() !== null) {
      return;
    }
    this.error.set(null);
    this.connectingId.set(device.id);
    try {
      const response = await this.api.connectNativeUsbDevice({ id: device.id });
      this.applyState(response);
      if (!response.success) {
        this.error.set(this.localization.translateKey(AppStrings.Settings.Usb.ConnectFailed));
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Usb.ConnectFailed));
    } finally {
      this.connectingId.set(null);
    }
  }

  async forget(serial: string): Promise<void> {
    if (this.forgettingSerial() !== null) {
      return;
    }
    this.error.set(null);
    this.forgettingSerial.set(serial);
    try {
      this.applyState(await this.api.forgetNativeUsbDevice({ serial }));
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Usb.SaveFailed));
    } finally {
      this.forgettingSerial.set(null);
    }
  }

  async rescan(): Promise<void> {
    this.rescanning.set(true);
    try {
      this.applyState(await this.api.refreshNativeUsbSettings());
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Usb.LoadFailed));
    } finally {
      this.rescanning.set(false);
    }
  }

  dismissError(): void {
    this.error.set(null);
  }

  deviceName(device: NativeUsbDevice): string {
    if (device.platform === 'Ios') {
      return device.product ?? this.localization.translateKey(AppStrings.Settings.Usb.PlatformIos);
    }

    const product = device.product?.trim();
    const manufacturer = device.manufacturer?.trim();
    if (!product) {
      return manufacturer || this.localization.translateKey(AppStrings.Settings.Usb.UnknownDevice);
    }

    return !manufacturer || product.toLowerCase().startsWith(manufacturer.toLowerCase())
      ? product
      : `${manufacturer} ${product}`;
  }

  platformIcon(device: NativeUsbDevice): string {
    return device.platform === 'Ios' ? 'icon-apple' : 'icon-device-phone';
  }

  platformLabel(device: NativeUsbDevice): string {
    return this.localization.translateKey(device.platform === 'Ios'
      ? AppStrings.Settings.Usb.PlatformIos
      : AppStrings.Settings.Usb.PlatformAndroid);
  }

  stateLabel(state: NativeUsbDeviceState): string {
    const key = STATE_LABEL_KEYS[state];
    return key ? this.localization.translateKey(key) : state;
  }

  stateTone(state: NativeUsbDeviceState): ChipTone {
    return STATE_TONES[state] ?? 'neutral';
  }

  availabilityLabel(available: boolean): string {
    return this.localization.translateKey(available ? AppStrings.Settings.Usb.Available : AppStrings.Settings.Usb.Unavailable);
  }

  rememberedName(device: NativeUsbRememberedDevice): string {
    const present = this.devices().find(candidate => candidate.serial === device.serial);
    return device.name
      ?? (present ? this.deviceName(present) : this.localization.translateKey(AppStrings.Settings.Usb.UnknownDevice));
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

  private async load(): Promise<void> {
    try {
      this.applyState(await this.api.getNativeUsbSettings());
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Usb.LoadFailed));
    } finally {
      this.loaded.set(true);
    }
  }

  private applyState(state: GetNativeUsbSettingsResponse): void {
    this.enabled.set(state.enabled);
    this.androidAvailable.set(state.androidAvailable);
    this.iosAvailable.set(state.iosAvailable);
    this.bridgeAvailable.set(state.bridgeAvailable);
    this.httpsOnly.set(state.httpsOnly);
    this.devices.set(state.devices);
    this.rememberedDevices.set(state.rememberedDevices ?? []);
  }
}
