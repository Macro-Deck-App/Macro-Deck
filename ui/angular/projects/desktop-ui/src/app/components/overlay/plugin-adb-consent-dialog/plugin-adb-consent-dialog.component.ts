import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ButtonComponent, ErrorBannerComponent, ModalComponent, TranslatePipe } from '@shared';
import { PluginAdbConsentService } from '../../../services/plugin-adb-consent.service';

@Component({
  selector: 'shared-plugin-adb-consent-dialog',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, ErrorBannerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plugin-adb-consent-dialog.component.html',
  styleUrls: ['./plugin-adb-consent-dialog.component.scss'],
})
export class PluginAdbConsentDialogComponent {
  private readonly consent = inject(PluginAdbConsentService);

  readonly pending = this.consent.pending;
  readonly offeringDownload = this.consent.offeringDownload;
  readonly downloading = this.consent.downloading;
  readonly downloadError = this.consent.downloadError;
  readonly busy = signal(false);

  async allow(): Promise<void> {
    const request = this.pending();
    if (!request || this.busy()) {
      return;
    }
    this.busy.set(true);
    try {
      await this.consent.allow(request.id);
    } finally {
      this.busy.set(false);
    }
  }

  notNow(): void {
    const request = this.pending();
    if (request && !this.busy()) {
      this.consent.defer(request.id);
    }
  }

  download(): void {
    void this.consent.downloadPlatformTools();
  }

  skipDownload(): void {
    this.consent.skipDownload();
  }

  dismissDownloadError(): void {
    this.downloadError.set(null);
  }
}
