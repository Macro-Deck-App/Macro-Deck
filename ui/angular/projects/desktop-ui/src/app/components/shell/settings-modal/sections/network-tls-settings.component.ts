import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, GetNetworkSettingsResponse, TlsMode } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, SegmentedControlComponent, SegmentedOption, SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { NetworkTlsCertificateUploadModalComponent } from './network-tls-certificate-upload-modal.component';

const TLS_FAILURE_KEYS: Record<string, string> = {
  NotConfigured: AppStrings.Settings.Network.Tls.FailureNotConfigured,
  KeyUnreadable: AppStrings.Settings.Network.Tls.FailureKeyUnreadable,
  CertificateInvalid: AppStrings.Settings.Network.Tls.FailureCertificateInvalid,
};

const TLS_REJECTION_KEYS: Record<string, string> = {
  NoCertificate: AppStrings.Settings.Network.Tls.RejectionNoCertificate,
  HttpsPortConflictsWithPublicPort: AppStrings.Settings.Network.Tls.RejectionHttpsPortConflictsWithPublicPort,
  HttpsPortConflictsWithLoopbackPort: AppStrings.Settings.Network.Tls.RejectionHttpsPortConflictsWithLoopbackPort,
};

const SOURCE_LABEL_KEYS: Record<string, string> = {
  SelfSigned: AppStrings.Settings.Network.Tls.SourceSelfSigned,
  Custom: AppStrings.Settings.Network.Tls.SourceCustom,
  LocalCa: AppStrings.Settings.Network.Tls.SourceLocalCa,
};

@Component({
  selector: 'app-network-tls-settings',
  standalone: true,
  imports: [
    FormsModule,
    SettingsSectionComponent,
    SettingsRowComponent,
    ToggleSwitchComponent,
    SegmentedControlComponent,
    InputComponent,
    ButtonComponent,
    ErrorBannerComponent,
    ConfirmationModalComponent,
    NetworkTlsCertificateUploadModalComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './network-tls-settings.component.html',
  styleUrls: ['./network-tls-settings.component.scss'],
})
export class NetworkTlsSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly state = input.required<GetNetworkSettingsResponse>();
  readonly stateChange = output<GetNetworkSettingsResponse>();

  readonly modeOptions = computed<SegmentedOption[]>(() => [
    { value: 'Replace', label: this.localization.translateKey(AppStrings.Settings.Network.Tls.ModeReplace) },
    { value: 'Additional', label: this.localization.translateKey(AppStrings.Settings.Network.Tls.ModeAdditional) },
  ]);

  readonly error = signal<string | null>(null);

  readonly httpsBusy = signal(false);
  readonly modeBusy = signal(false);
  readonly portSaving = signal(false);
  readonly certificateBusy = signal(false);

  readonly httpsPortDraft = signal('');
  readonly regeneratePromptOpen = signal(false);
  readonly regenerateAuthorityPromptOpen = signal(false);
  readonly authorityBusy = signal(false);
  readonly uploadModalOpen = signal(false);

  readonly sourceLabel = computed(() => {
    const source = this.state().tlsCertificateSource;
    if (!source) {
      return null;
    }
    const key = SOURCE_LABEL_KEYS[source];
    return key ? this.localization.translateKey(key) : source;
  });

  readonly authorityNotAfterLabel = computed(() => this.formatDate(this.state().tlsAuthorityNotAfter));

  readonly httpsToggleReason = computed(() =>
    this.state().tlsCertificateConfigured
      ? null
      : this.localization.translateKey(AppStrings.Settings.Network.Tls.RejectionNoCertificate));

  readonly parsedHttpsPort = computed(() => {
    const raw = this.httpsPortDraft().trim();
    return /^\d+$/.test(raw) ? Number(raw) : null;
  });

  readonly httpsPortOutOfRange = computed(() => {
    const port = this.parsedHttpsPort();
    const state = this.state();
    return port !== null && (port < state.minimumPublicPort || port > state.maximumPublicPort);
  });

  readonly httpsPortConflictsWithPublicPort = computed(() =>
    this.parsedHttpsPort() !== null && this.parsedHttpsPort() === this.state().publicPort);

  readonly httpsPortInvalidReason = computed(() => {
    const raw = this.httpsPortDraft().trim();
    if (raw === '' || this.parsedHttpsPort() === null) {
      return null;
    }
    if (this.httpsPortOutOfRange()) {
      return this.localization.translateKey(AppStrings.Settings.Network.Tls.PortRangeHint, {
        minimum: this.state().minimumPublicPort,
        maximum: this.state().maximumPublicPort,
      });
    }
    if (this.httpsPortConflictsWithPublicPort()) {
      return this.localization.translateKey(AppStrings.Settings.Network.Tls.PortConflictsWithPublicPort);
    }
    return null;
  });

  readonly httpsPortChanged = computed(() => this.parsedHttpsPort() !== this.state().tlsHttpsPort);

  readonly canSaveHttpsPort = computed(() =>
    !this.portSaving() &&
    this.parsedHttpsPort() !== null &&
    !this.httpsPortInvalidReason() &&
    this.httpsPortChanged());

  readonly failureMessage = computed(() => {
    const failure = this.state().tlsFailure;
    if (!failure || failure === 'None') {
      return null;
    }
    const key = TLS_FAILURE_KEYS[failure] ?? AppStrings.Settings.Network.Tls.FailureGeneric;
    return this.localization.translateKey(key);
  });

  readonly rejectionMessage = computed(() => {
    const rejection = this.state().tlsRejection;
    if (!rejection || rejection === 'None') {
      return null;
    }
    const key = TLS_REJECTION_KEYS[rejection] ?? AppStrings.Settings.Network.Tls.RejectionGeneric;
    return this.localization.translateKey(key);
  });

  readonly notBeforeLabel = computed(() => this.formatDate(this.state().tlsCertificateNotBefore));
  readonly notAfterLabel = computed(() => this.formatDate(this.state().tlsCertificateNotAfter));

  constructor() {
    effect(() => this.httpsPortDraft.set(String(this.state().tlsHttpsPort)));
  }

  async setHttpsEnabled(value: boolean): Promise<void> {
    if (this.httpsBusy() || !this.state().tlsCertificateConfigured) {
      return;
    }
    this.httpsBusy.set(true);
    try {
      await this.save({ tlsEnabled: value });
    } finally {
      this.httpsBusy.set(false);
    }
  }

  async setMode(mode: string): Promise<void> {
    if (this.modeBusy()) {
      return;
    }
    this.modeBusy.set(true);
    try {
      await this.save({ tlsMode: mode as TlsMode });
    } finally {
      this.modeBusy.set(false);
    }
  }

  async saveHttpsPort(): Promise<void> {
    const port = this.parsedHttpsPort();
    if (!this.canSaveHttpsPort() || port === null) {
      return;
    }
    this.portSaving.set(true);
    try {
      await this.save({ tlsHttpsPort: port });
    } finally {
      this.portSaving.set(false);
    }
  }

  onGenerateClicked(): void {
    if (this.state().tlsCertificateConfigured) {
      this.regeneratePromptOpen.set(true);
      return;
    }
    void this.generateCertificate();
  }

  confirmRegenerate(): void {
    this.regeneratePromptOpen.set(false);
    void this.generateCertificate();
  }

  cancelRegenerate(): void {
    this.regeneratePromptOpen.set(false);
  }

  openUploadModal(): void {
    this.uploadModalOpen.set(true);
  }

  closeUploadModal(): void {
    this.uploadModalOpen.set(false);
  }

  onCertificateUploaded(response: GetNetworkSettingsResponse): void {
    this.uploadModalOpen.set(false);
    this.apply(response);
  }

  onRegenerateAuthorityClicked(): void {
    this.regenerateAuthorityPromptOpen.set(true);
  }

  cancelRegenerateAuthority(): void {
    this.regenerateAuthorityPromptOpen.set(false);
  }

  confirmRegenerateAuthority(): void {
    this.regenerateAuthorityPromptOpen.set(false);
    void this.regenerateAuthority();
  }

  dismissError(): void {
    this.error.set(null);
  }

  private async regenerateAuthority(): Promise<void> {
    this.error.set(null);
    this.authorityBusy.set(true);
    try {
      const response = await this.api.regenerateNetworkTlsCertificateAuthority();
      this.apply(response);
      if (!response.success) {
        this.error.set(
          response.error ?? this.localization.translateKey(AppStrings.Settings.Network.Tls.GenerateFailed),
        );
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Network.Tls.GenerateFailed));
    } finally {
      this.authorityBusy.set(false);
    }
  }

  private async generateCertificate(): Promise<void> {
    this.error.set(null);
    this.certificateBusy.set(true);
    try {
      const response = await this.api.reissueNetworkTlsCertificate();
      this.apply(response);
      if (!response.success) {
        this.error.set(
          response.error ?? this.localization.translateKey(AppStrings.Settings.Network.Tls.ReissueFailed),
        );
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Network.Tls.ReissueFailed));
    } finally {
      this.certificateBusy.set(false);
    }
  }

  private async save(patch: {
    tlsEnabled?: boolean;
    tlsMode?: TlsMode;
    tlsHttpsPort?: number;
  }): Promise<void> {
    this.error.set(null);
    try {
      const response = await this.api.updateNetworkSettings({
        publicPort: this.state().publicPort,
        ...patch,
      });
      this.apply(response);
      if (!response.success) {
        this.error.set(response.error ?? this.localization.translateKey(AppStrings.Settings.Network.Tls.SaveFailed));
      }
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Network.Tls.SaveFailed));
    }
  }

  private apply(response: GetNetworkSettingsResponse): void {
    this.stateChange.emit(response);
  }

  private formatDate(value: string | null): string | null {
    if (!value) {
      return null;
    }
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toLocaleString();
  }
}
