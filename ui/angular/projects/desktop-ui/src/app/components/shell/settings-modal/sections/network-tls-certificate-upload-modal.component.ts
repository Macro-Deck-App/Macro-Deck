import { ChangeDetectionStrategy, Component, ViewChild, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppStrings,
  GetNetworkSettingsResponse,
} from '@macro-deck/runtime';
import {
  ApiService,
  ButtonComponent,
  ErrorBannerComponent,
  InputComponent,
  LocalizationService,
  ModalComponent,
  TranslatePipe,
  dismissModal,
} from '@shared';

@Component({
  selector: 'app-network-tls-certificate-upload-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, InputComponent, ErrorBannerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './network-tls-certificate-upload-modal.component.html',
  styleUrls: ['./network-tls-certificate-upload-modal.component.scss'],
})
export class NetworkTlsCertificateUploadModalComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly saved = output<GetNetworkSettingsResponse>();
  readonly cancelled = output<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly certificatePem = signal('');
  readonly privateKeyPem = signal('');
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly canSave = () => !this.saving() && this.certificatePem().trim() !== '' && this.privateKeyPem().trim() !== '';

  async onCertificateFileSelected(event: Event): Promise<void> {
    const text = await this.readSelectedFile(event);
    if (text !== null) {
      this.certificatePem.set(text);
    }
  }

  async onPrivateKeyFileSelected(event: Event): Promise<void> {
    const text = await this.readSelectedFile(event);
    if (text !== null) {
      this.privateKeyPem.set(text);
    }
  }

  async save(): Promise<void> {
    if (!this.canSave()) {
      return;
    }
    this.error.set(null);
    this.saving.set(true);
    try {
      const response = await this.api.updateNetworkTlsCertificate({
        certificatePem: this.certificatePem().trim(),
        privateKeyPem: this.privateKeyPem().trim(),
      });
      if (!response.success) {
        this.error.set(
          response.error ?? this.localization.translateKey(AppStrings.Settings.Network.Tls.UploadSaveFailed),
        );
        return;
      }
      this.certificatePem.set('');
      this.privateKeyPem.set('');
      dismissModal(this.modal, () => this.saved.emit(response));
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.Network.Tls.UploadSaveFailed));
    } finally {
      this.saving.set(false);
    }
  }

  cancel(): void {
    this.certificatePem.set('');
    this.privateKeyPem.set('');
    dismissModal(this.modal, () => this.cancelled.emit());
  }

  private readSelectedFile(event: Event): Promise<string | null> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    return file ? file.text() : Promise.resolve(null);
  }
}
