import { ChangeDetectionStrategy, Component, ViewChild, computed, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';
import { ExternalLinkService } from '../../../services/external-link.service';
import { PostUpdateChangelogService } from '../../../services/post-update-changelog.service';
import { releaseNoteSections } from '../update-modal/release-notes';

@Component({
  selector: 'app-whats-new-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, StoreMarkdownComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './whats-new-modal.component.html',
  styleUrls: ['./whats-new-modal.component.scss'],
})
export class WhatsNewModalComponent {
  private readonly changelogs = inject(PostUpdateChangelogService);
  private readonly localization = inject(LocalizationService);
  private readonly externalLinks = inject(ExternalLinkService);
  protected readonly appStrings = AppStrings;

  @ViewChild(ModalComponent) private readonly modal?: ModalComponent;

  protected readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.Update.WhatsNew.Heading, {
      version: this.changelogs.changelog()?.version ?? '',
    }));

  protected readonly releaseDate = computed(() => {
    const publishedAt = this.changelogs.changelog()?.publishedAt;
    const parts = publishedAt?.split('-').map(Number) ?? [];
    if (parts.length !== 3 || parts.some(part => Number.isNaN(part))) {
      return null;
    }
    const [year, month, day] = parts;
    const date = new Date(year, month - 1, day);
    return Number.isNaN(date.getTime()) ? null : date.toLocaleDateString();
  });

  protected readonly sections = computed(() => releaseNoteSections(this.changelogs.changelog()?.notes ?? ''));
  protected readonly hasChangelog = computed(() => this.sections().length > 0);
  protected readonly notesUrl = computed(() => this.changelogs.changelog()?.notesUrl ?? null);

  openReleaseNotes(url: string): void {
    this.externalLinks.open(url);
  }

  close(): void {
    dismissModal(this.modal, () => this.changelogs.dismiss());
  }

  onModalClose(): void {
    this.changelogs.dismiss();
  }
}
