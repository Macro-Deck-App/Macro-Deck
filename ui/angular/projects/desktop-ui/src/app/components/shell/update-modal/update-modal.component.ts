import { ChangeDetectionStrategy, Component, ViewChild, computed, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';
import { UpdateModalService } from '../../../services/update-modal.service';
import { UpdateService } from '../../../services/update.service';

@Component({
  selector: 'app-update-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, StoreMarkdownComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './update-modal.component.html',
  styleUrls: ['./update-modal.component.scss'],
})
export class UpdateModalComponent {
  protected readonly updates = inject(UpdateService);
  private readonly modalService = inject(UpdateModalService);
  private readonly localization = inject(LocalizationService);
  protected readonly appStrings = AppStrings;

  @ViewChild(ModalComponent) private readonly modal?: ModalComponent;

  protected readonly heading = computed(() => this.localization.translateKey(AppStrings.Settings.Update.SoftwareUpdateLabel));

  protected readonly versionHeading = computed(() => {
    const version = this.updates.version() ?? this.updates.currentVersion();
    return this.localization.translateKey(AppStrings.Update.Details.VersionHeading, { version });
  });

  protected readonly currentVersionLine = computed(() =>
    this.localization.translateKey(AppStrings.Update.Details.CurrentVersion, {
      version: this.updates.currentVersion(),
    }));

  protected readonly releaseDate = computed(() => {
    const publishedAt = this.updates.publishedAt();
    if (!publishedAt) {
      return null;
    }
    const parts = publishedAt.split('-').map(Number);
    if (parts.length !== 3 || parts.some(part => Number.isNaN(part))) {
      return null;
    }
    const [year, month, day] = parts;
    const date = new Date(year, month - 1, day);
    return Number.isNaN(date.getTime()) ? null : date.toLocaleDateString();
  });

  protected readonly channelLabel = computed(() =>
    this.updates.channel() === 'beta'
      ? this.localization.translateKey(AppStrings.Settings.Update.ChannelBeta)
      : this.localization.translateKey(AppStrings.Settings.Update.ChannelStable));

  protected readonly changelogText = computed(() => this.updates.notes() ?? '');
  protected readonly hasChangelog = computed(() => this.changelogText().trim().length > 0);

  protected readonly downloadStatus = computed(() => {
    const percent = this.updates.progressPercent();
    return percent === null
      ? this.localization.translateKey(AppStrings.Update.Details.PreparingStatus)
      : this.localization.translateKey(AppStrings.Update.Details.DownloadingStatus, { percent });
  });

  protected readonly downloadFailedMessage = computed(() =>
    this.localization.translateKey(AppStrings.Update.Details.DownloadFailed, {
      error: this.updates.error() ?? '',
    }));

  install(): void {
    void this.updates.install();
  }

  cancelDownload(): void {
    void this.updates.cancelDownload();
  }

  openDownloadPage(): void {
    this.updates.openDownloadPage();
  }

  retry(): void {
    void this.updates.check();
  }

  later(): void {
    dismissModal(this.modal, () => this.modalService.close());
  }

  onModalClose(): void {
    this.modalService.close();
  }
}
