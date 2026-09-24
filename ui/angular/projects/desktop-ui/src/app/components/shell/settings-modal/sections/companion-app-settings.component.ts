import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AppStrings,
  CompanionAppChangedEvent,
  CompanionAppDevice,
  CompanionAppDeviceState,
  CompanionAppError,
  CompanionAppStatus,
  CompanionLicenseChangedEvent,
} from '@macro-deck/runtime';
import {
  ApiService,
  ButtonComponent,
  LocalizationService,
  SettingsRowComponent,
  SettingsSectionComponent,
  ToggleSwitchComponent,
  TranslatePipe,
} from '@shared';
import { FileSaveService } from '../../../../services/file-save.service';
import { SettingsModalService } from '../../../../services/settings-modal.service';
import { LicenseSettingsComponent } from './license-settings.component';

const S = AppStrings.Settings.CompanionApp;

const ERROR_KEYS: Record<CompanionAppError, string> = {
  AdbDisabled: S.Error.AdbDisabled,
  DeviceNotReady: S.Error.DeviceNotReady,
  NotAndroid: S.Error.NotAndroid,
  DeviceTooOld: S.Error.DeviceTooOld,
  NoRelease: S.Error.NoRelease,
  DownloadFailed: S.Error.DownloadFailed,
  VerificationFailed: S.Error.VerificationFailed,
  IncompatibleSignature: S.Error.IncompatibleSignature,
  InstallBlockedOnDevice: S.Error.InstallBlockedOnDevice,
  StorageFull: S.Error.StorageFull,
  InstallFailed: S.Error.InstallFailed,
  PlayStoreInstall: S.Error.PlayStoreInstall,
};

const INSTALLABLE: ReadonlySet<CompanionAppDeviceState> = new Set(['NotInstalled']);
const UPDATABLE: ReadonlySet<CompanionAppDeviceState> = new Set(['UpdateAvailable']);

@Component({
  selector: 'app-companion-app-settings',
  standalone: true,
  imports: [
    ButtonComponent,
    LicenseSettingsComponent,
    SettingsRowComponent,
    SettingsSectionComponent,
    ToggleSwitchComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './companion-app-settings.component.html',
  styleUrls: ['./companion-app-settings.component.scss'],
})
export class CompanionAppSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly settingsModal = inject(SettingsModalService);
  private readonly fileSave = inject(FileSaveService);
  private loadRequest = 0;

  readonly status = signal<CompanionAppStatus | null>(null);
  readonly unlicensed = signal(false);
  readonly loadFailed = signal(false);
  readonly checking = signal(false);
  readonly autoUpdateBusy = signal(false);
  readonly installingSerial = signal<string | null>(null);
  readonly savingApk = signal(false);
  readonly apkMessage = signal<string | null>(null);

  readonly latestReleased = computed(() => this.formatDate(this.status()?.publishedAt));
  readonly checkInProgress = computed(() => this.checking() || this.status()?.checking === true);

  constructor() {
    this.api
      .onNotification<CompanionAppChangedEvent>('CompanionAppChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
    this.api
      .onNotification<CompanionLicenseChangedEvent>('CompanionLicenseChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.loadLicense());
    this.api
      .onAdbStateChanged()
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
    void this.load();
    void this.loadLicense();
  }

  async checkNow(): Promise<void> {
    if (this.checking()) {
      return;
    }
    this.checking.set(true);
    try {
      this.status.set(await this.api.checkCompanionAppUpdate());
      this.loadFailed.set(false);
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.checking.set(false);
    }
  }

  async setAutoUpdate(value: boolean): Promise<void> {
    if (this.autoUpdateBusy()) {
      return;
    }
    this.autoUpdateBusy.set(true);
    try {
      this.status.set(await this.api.updateCompanionAppSettings(value));
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.autoUpdateBusy.set(false);
    }
  }

  async install(device: CompanionAppDevice): Promise<void> {
    if (this.installingSerial() !== null) {
      return;
    }
    this.installingSerial.set(device.serial);
    try {
      this.status.set((await this.api.installCompanionApp(device.serial)).status);
    } catch {
      this.status.update(status => status && {
        ...status,
        devices: status.devices.map(entry =>
          entry.serial === device.serial ? { ...entry, error: 'InstallFailed' as const } : entry),
      });
    } finally {
      this.installingSerial.set(null);
    }
  }

  async saveApk(): Promise<void> {
    if (this.savingApk()) {
      return;
    }
    this.savingApk.set(true);
    this.apkMessage.set(null);
    try {
      const { blob, fileName } = await this.api.downloadCompanionApk();
      const result = await this.fileSave.save(blob, fileName);
      if (result.status === 'saved') {
        this.apkMessage.set(this.localization.translateKey(S.ApkSaved));
      } else if (result.status === 'error') {
        this.apkMessage.set(result.message);
      }
    } catch {
      this.apkMessage.set(this.localization.translateKey(S.Error.DownloadFailed));
    } finally {
      this.savingApk.set(false);
    }
  }

  openAdbSettings(): void {
    this.settingsModal.setCategory('adb');
  }

  canInstall(device: CompanionAppDevice): boolean {
    return INSTALLABLE.has(device.state);
  }

  canUpdate(device: CompanionAppDevice): boolean {
    return UPDATABLE.has(device.state);
  }

  stateText(device: CompanionAppDevice): string {
    const version = device.installedVersion ?? device.installedVersionCode?.toString() ?? '';
    switch (device.state) {
      case 'NotAuthorized':
        return this.localization.translateKey(S.State.NotAuthorized);
      case 'Checking':
        return this.localization.translateKey(S.State.Checking);
      case 'NotAndroid':
        return this.localization.translateKey(S.State.NotAndroid);
      case 'DeviceTooOld':
        return this.localization.translateKey(S.State.DeviceTooOld);
      case 'NotInstalled':
        return this.localization.translateKey(S.State.NotInstalled);
      case 'Installed':
        return this.localization.translateKey(S.State.Installed, { version });
      case 'UpToDate':
        return this.localization.translateKey(S.State.UpToDate, { version });
      case 'UpdateAvailable':
        return this.localization.translateKey(S.State.UpdateAvailable, {
          installed: version,
          latest: this.status()?.latestVersion ?? '',
        });
      case 'InstalledFromPlayStore':
        return this.localization.translateKey(S.State.InstalledFromPlayStore, { version });
      case 'Installing':
        return this.localization.translateKey(S.State.Installing);
      default:
        return this.localization.translateKey(S.State.Unknown);
    }
  }

  errorText(error: CompanionAppError): string {
    return this.localization.translateKey(ERROR_KEYS[error] ?? S.Error.InstallFailed);
  }

  private async load(): Promise<void> {
    const request = ++this.loadRequest;
    try {
      const status = await this.api.getCompanionApp();
      if (request === this.loadRequest) {
        this.status.set(status);
        this.loadFailed.set(false);
      }
    } catch {
      if (request === this.loadRequest) {
        this.loadFailed.set(true);
      }
    }
  }

  private async loadLicense(): Promise<void> {
    try {
      const license = await this.api.getCompanionLicense();
      this.unlicensed.set(!license.licensed || license.isTest);
    } catch {
      this.unlicensed.set(false);
    }
  }

  private formatDate(value: number | null | undefined): string | null {
    if (value == null) {
      return null;
    }
    const { locale } = this.localization.timeLocale();
    return new Date(value).toLocaleDateString(locale, { dateStyle: 'medium' });
  }
}
