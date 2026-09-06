import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, SegmentedControlComponent, SegmentedOption, SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { UpdateModalService } from '../../../../services/update-modal.service';
import { UpdateService, installErrorMessage } from '../../../../services/update.service';

type UpdateModeValue = 'off' | 'notifyOnly' | 'automatic';

@Component({
  selector: 'app-update-check',
  standalone: true,
  imports: [
    SettingsSectionComponent,
    SettingsRowComponent,
    ButtonComponent,
    ConfirmationModalComponent,
    SegmentedControlComponent,
    ToggleSwitchComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './update-check.component.html',
  styleUrls: ['./update-check.component.scss'],
})
export class UpdateCheckComponent {
  private readonly localization = inject(LocalizationService);
  protected readonly updates = inject(UpdateService);
  private readonly updateModal = inject(UpdateModalService);

  readonly hasBridge = this.updates.hasBridge;

  readonly confirming = signal<'none' | 'enable-beta'>('none');

  readonly channel = this.updates.channel;
  readonly channelBusy = signal(false);
  readonly channelError = signal<string | null>(null);
  readonly betaInstalled = this.updates.betaInstalled;
  readonly partialCheck = this.updates.partialCheck;

  readonly betaEnabled = computed(() => this.channel() === 'beta');
  readonly channelKnown = computed(() => this.channel() !== null);
  readonly channelLabel = computed(() =>
    this.localization.translateKey(
      this.channel() === 'beta' ? AppStrings.Settings.Update.ChannelBeta : AppStrings.Settings.Update.ChannelStable,
    ));
  readonly betaDescription = computed(() =>
    this.betaEnabled() ? '' : this.localization.translateKey(AppStrings.Settings.Update.BetaToggleDescription)
  );

  readonly pendingChannel = signal<'stable' | 'beta' | null>(null);
  readonly betaToggleChecked = computed(() => (this.pendingChannel() ?? this.channel()) === 'beta');

  readonly mode = signal<UpdateModeValue | null>(null);
  readonly automaticSupported = signal(true);
  readonly modeError = signal<string | null>(null);

  readonly modeOptions = computed<SegmentedOption[]>(() => {
    const options: SegmentedOption[] = [
      { value: 'off', label: this.localization.translateKey(AppStrings.Settings.Update.ModeOff) },
      { value: 'notifyOnly', label: this.localization.translateKey(AppStrings.Settings.Update.ModeNotifyOnly) },
    ];
    if (this.automaticSupported()) {
      options.push({
        value: 'automatic',
        label: this.localization.translateKey(AppStrings.Settings.Update.ModeAutomatic),
      });
    }
    return options;
  });

  readonly modeDescription = computed(() => {
    switch (this.mode()) {
      case 'off':
        return this.localization.translateKey(AppStrings.Settings.Update.ModeOffDescription);
      case 'automatic':
        return this.localization.translateKey(AppStrings.Settings.Update.ModeAutomaticDescription);
      default:
        return this.localization.translateKey(AppStrings.Settings.Update.ModeNotifyOnlyDescription);
    }
  });

  readonly statusText = computed(() => {
    const t = (key: string, args?: Record<string, unknown>): string => this.localization.translateKey(key, args);
    switch (this.updates.phase()) {
      case 'checking':
        return t(AppStrings.Settings.Update.CheckingStatus);
      case 'upToDate': {
        if (this.betaInstalled() && !this.betaEnabled()) {
          return t(AppStrings.Settings.Update.UpToDateBeta);
        }
        const version = this.updates.currentVersion();
        return version
          ? t(AppStrings.Settings.Update.UpToDateWithVersion, { version })
          : t(AppStrings.Settings.Update.UpToDateNoVersion);
      }
      case 'available':
        return this.updates.externalDownload()
          ? t(AppStrings.Settings.Update.AvailableExternal, { version: this.updates.version() })
          : t(AppStrings.Settings.Update.AvailableInApp, { version: this.updates.version() });
      case 'downloading':
      case 'downloaded':
      case 'installing':
        return t(AppStrings.Settings.Update.Installing);
      case 'failed':
        return this.updates.error() ?? t(AppStrings.Settings.Update.CheckFailed);
      default:
        return t(AppStrings.Settings.Update.IdleStatus);
    }
  });

  readonly checkLabel = computed(() => {
    switch (this.updates.phase()) {
      case 'upToDate':
        return this.localization.translateKey(AppStrings.Settings.Update.CheckAgainAction);
      case 'failed':
        return this.localization.translateKey(AppStrings.Settings.Update.TryAgainAction);
      default:
        return this.localization.translateKey(AppStrings.Settings.Update.CheckForUpdatesAction);
    }
  });

  readonly showDetails = computed(() =>
    ['available', 'downloading', 'downloaded', 'installing', 'failed'].includes(this.updates.phase()));

  constructor() {
    this.loadMode();
  }

  check(): void {
    void this.updates.check();
  }

  openDetails(): void {
    this.updateModal.open();
  }

  onBetaToggled(enabled: boolean): void {
    const next: 'stable' | 'beta' = enabled ? 'beta' : 'stable';
    this.pendingChannel.set(next);
    if (enabled) {
      this.confirming.set('enable-beta');
      return;
    }
    void this.applyChannel(next);
  }

  confirmBetaOptIn(): void {
    this.confirming.set('none');
    void this.applyChannel('beta');
  }

  cancelBetaOptIn(): void {
    this.confirming.set('none');
    this.pendingChannel.set(null);
  }

  private async applyChannel(next: 'stable' | 'beta'): Promise<void> {
    this.channelBusy.set(true);
    this.channelError.set(null);
    try {
      await this.updates.setChannel(next);
      this.pendingChannel.set(null);
      this.channelBusy.set(false);
    } catch (error) {
      this.channelError.set(
        installErrorMessage(error) ?? this.localization.translateKey(AppStrings.Settings.Update.ChannelChangeFailed),
      );
      this.pendingChannel.set(null);
      this.channelBusy.set(false);
    }
  }

  private loadMode(): void {
    this.updates
      .getModeStatus()
      .then(status => {
        if (!status) {
          return;
        }
        this.mode.set(status.mode);
        this.automaticSupported.set(status.automaticSupported === true);
      })
      .catch(() => {
      });
  }

  async selectMode(next: string): Promise<void> {
    const previous = this.mode();
    this.mode.set(next as UpdateModeValue);
    this.modeError.set(null);
    try {
      const status = await this.updates.setMode(next as UpdateModeValue);
      if (!status) {
        this.mode.set(previous);
        return;
      }
      this.mode.set(status.mode);
      this.automaticSupported.set(status.automaticSupported === true);
    } catch (error) {
      this.mode.set(previous);
      this.modeError.set(
        installErrorMessage(error) ?? this.localization.translateKey(AppStrings.Settings.Update.ModeChangeFailed),
      );
    }
  }
}
