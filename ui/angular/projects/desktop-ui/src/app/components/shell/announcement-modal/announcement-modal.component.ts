import { ChangeDetectionStrategy, Component, ViewChild, computed, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { AnnouncementService } from '../../../services/announcement.service';
import { AnnouncementMarkdownComponent } from './announcement-markdown.component';

@Component({
  selector: 'app-announcement-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, AnnouncementMarkdownComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './announcement-modal.component.html',
  styleUrls: ['../whats-new-modal/whats-new-modal.component.scss'],
})
export class AnnouncementModalComponent {
  private readonly announcements = inject(AnnouncementService);
  protected readonly appStrings = AppStrings;

  @ViewChild(ModalComponent) private readonly modal?: ModalComponent;

  protected readonly announcement = this.announcements.pending;

  protected readonly publishedOn = computed(() => {
    const publishedAt = this.announcement()?.publishedAt;
    const date = publishedAt ? new Date(publishedAt) : null;
    return date && !Number.isNaN(date.getTime()) ? date.toLocaleDateString() : null;
  });

  close(): void {
    dismissModal(this.modal, () => this.announcements.dismiss());
  }

  onModalClose(): void {
    this.announcements.dismiss();
  }
}
