import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, SettingsRowComponent, SettingsSectionComponent, TranslatePipe } from '@shared';

@Component({
  selector: 'app-http-settings',
  standalone: true,
  imports: [
    FormsModule,
    SettingsSectionComponent,
    SettingsRowComponent,
    InputComponent,
    ButtonComponent,
    ErrorBannerComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './http-settings.component.html',
  styleUrls: ['./http-settings.component.scss'],
})
export class HttpSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly maxLength = 256;

  readonly loaded = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly customUserAgent = signal<string | null>(null);
  readonly effectiveUserAgent = signal('');
  readonly defaultUserAgent = signal('');
  readonly draft = signal('');

  readonly canSave = computed(() => {
    const value = this.draft().trim();
    return this.loaded() && !this.saving() && value.length > 0 && value !== this.effectiveUserAgent();
  });

  readonly canRestore = computed(() => this.loaded() && !this.saving() && this.customUserAgent() !== null);

  constructor() {
    void this.load();
  }

  save(): Promise<void> {
    return this.submit(this.draft().trim());
  }

  restoreDefault(): Promise<void> {
    return this.submit(null);
  }

  dismissError(): void {
    this.error.set(null);
  }

  private async load(): Promise<void> {
    try {
      const settings = await this.api.getHttpSettings();
      this.apply(settings.customUserAgent ?? null, settings.effectiveUserAgent, settings.defaultUserAgent);
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Http.LoadFailed));
    } finally {
      this.loaded.set(true);
    }
  }

  private async submit(userAgent: string | null): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      const response = await this.api.updateHttpSettings({ userAgent });
      if (response.success) {
        this.apply(response.customUserAgent ?? null, response.effectiveUserAgent, response.defaultUserAgent);
      } else {
        const message = resolveLocalizedText(response.error?.message, this.localization);
        this.error.set(message || this.localization.translateKey(AppStrings.Settings.Http.SaveFailed));
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Http.SaveFailed));
    } finally {
      this.saving.set(false);
    }
  }

  private apply(custom: string | null, effective: string, fallback: string): void {
    this.customUserAgent.set(custom);
    this.effectiveUserAgent.set(effective);
    this.defaultUserAgent.set(fallback);
    this.draft.set(custom ?? effective);
  }
}
