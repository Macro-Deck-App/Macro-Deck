import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AppStrings, CompanionLicenseStatus } from '@macro-deck/runtime';
import {
  ApiService,
  ButtonComponent,
  LocalizationService,
  SettingsRowComponent,
  SettingsSectionComponent,
  TranslatePipe,
} from '@shared';
import { DeveloperModeService } from '../../../../services/developer-mode.service';

const SOURCE_LABELS: Record<string, string> = {
  'google-play': AppStrings.Settings.License.Source.GooglePlay,
  'app-store': AppStrings.Settings.License.Source.AppStore,
  'app-store-legacy': AppStrings.Settings.License.Source.AppStoreLegacy,
  test: AppStrings.Settings.License.Source.Test,
};

@Component({
  selector: 'app-license-settings',
  standalone: true,
  imports: [ButtonComponent, SettingsSectionComponent, SettingsRowComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './license-settings.component.html',
  styleUrls: ['./license-settings.component.scss'],
})
export class LicenseSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  readonly developerMode = inject(DeveloperModeService).enabled;

  readonly status = signal<CompanionLicenseStatus | null>(null);
  readonly loadFailed = signal(false);
  readonly issuing = signal(false);
  readonly issueFailed = signal(false);
  readonly revoking = signal(false);
  readonly revokeFailed = signal(false);

  readonly sourceLabel = computed(() => {
    const source = this.status()?.source;
    if (!source) {
      return '-';
    }
    const key = SOURCE_LABELS[source];
    return key ? this.localization.translateKey(key) : source;
  });

  readonly issuedAt = computed(() => {
    const issuedAt = this.status()?.issuedAt;
    if (issuedAt == null) {
      return '-';
    }
    const { locale, hourCycle } = this.localization.timeLocale();
    return new Date(issuedAt).toLocaleString(locale, { hourCycle });
  });

  constructor() {
    void this.load();
  }

  async issueTestLicense(): Promise<void> {
    if (this.issuing()) {
      return;
    }
    this.issuing.set(true);
    this.issueFailed.set(false);
    try {
      this.status.set(await this.api.issueTestCompanionLicense());
    } catch {
      this.issueFailed.set(true);
    } finally {
      this.issuing.set(false);
    }
  }

  async revokeTestLicense(): Promise<void> {
    if (this.revoking()) {
      return;
    }
    this.revoking.set(true);
    this.revokeFailed.set(false);
    try {
      this.status.set(await this.api.revokeTestCompanionLicense());
    } catch {
      this.revokeFailed.set(true);
    } finally {
      this.revoking.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.status.set(await this.api.getCompanionLicense());
      this.loadFailed.set(false);
    } catch {
      this.loadFailed.set(true);
    }
  }
}
