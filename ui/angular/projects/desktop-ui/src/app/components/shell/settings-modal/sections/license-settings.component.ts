import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppStrings, CompanionLicenseChangedEvent, CompanionLicenseStatus } from '@macro-deck/runtime';
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
  private readonly destroyRef = inject(DestroyRef);
  private nextAttemptTimer: ReturnType<typeof setTimeout> | undefined;
  private loadRequest = 0;
  readonly developerMode = inject(DeveloperModeService).enabled;

  readonly status = signal<CompanionLicenseStatus | null>(null);
  readonly loadFailed = signal(false);
  readonly issuing = signal(false);
  readonly issueFailed = signal(false);
  readonly revoking = signal(false);
  readonly revokeFailed = signal(false);
  readonly nextAttemptPassed = signal(false);

  readonly sourceLabel = computed(() => {
    const source = this.status()?.source;
    if (!source) {
      return '-';
    }
    const key = SOURCE_LABELS[source];
    return key ? this.localization.translateKey(key) : source;
  });

  readonly issuedAt = computed(() => this.formatTime(this.status()?.issuedAt) ?? '-');

  readonly purchasedAt = computed(() => this.formatTime(this.status()?.purchasedAt));

  readonly nextAttempt = computed(() => (this.nextAttemptPassed() ? null : this.formatTime(this.status()?.nextIssueAttemptAt)));

  constructor() {
    this.api
      .onNotification<CompanionLicenseChangedEvent>('CompanionLicenseChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
    this.destroyRef.onDestroy(() => clearTimeout(this.nextAttemptTimer));
    void this.load();
  }

  async issueTestLicense(): Promise<void> {
    if (this.issuing()) {
      return;
    }
    this.issuing.set(true);
    this.issueFailed.set(false);
    try {
      this.show(await this.api.issueTestCompanionLicense());
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
      this.show(await this.api.revokeTestCompanionLicense());
    } catch {
      this.revokeFailed.set(true);
    } finally {
      this.revoking.set(false);
    }
  }

  private async load(): Promise<void> {
    const request = ++this.loadRequest;
    try {
      const status = await this.api.getCompanionLicense();
      if (request === this.loadRequest) {
        this.show(status);
        this.loadFailed.set(false);
      }
    } catch {
      if (request === this.loadRequest) {
        this.loadFailed.set(true);
      }
    }
  }

  private show(status: CompanionLicenseStatus): void {
    this.status.set(status);
    clearTimeout(this.nextAttemptTimer);
    const remaining = status.issuePending && status.nextIssueAttemptAt != null ? status.nextIssueAttemptAt - Date.now() : 0;
    this.nextAttemptPassed.set(remaining <= 0);
    if (remaining > 0) {
      this.nextAttemptTimer = setTimeout(() => this.nextAttemptPassed.set(true), remaining);
    }
  }

  private formatTime(value: number | null | undefined): string | null {
    if (value == null) {
      return null;
    }
    const { locale, hourCycle } = this.localization.timeLocale();
    return new Date(value).toLocaleString(locale, { hourCycle });
  }
}
