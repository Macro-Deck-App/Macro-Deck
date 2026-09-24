import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ButtonComponent, DismissibleHintService, TranslatePipe } from '@shared';
import { ExternalLinkService } from '../../services/external-link.service';
import { STORE_FOOTER_LINKS } from './store-footer.component';
import { StoreGuidelinesModalComponent } from './store-guidelines-modal.component';

export const STORE_COMMUNITY_NOTICE_HINT = 'store.communityNotice';

@Component({
  selector: 'app-store-community-notice',
  standalone: true,
  imports: [ButtonComponent, StoreGuidelinesModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-community-notice.component.html',
  styleUrls: ['./store-community-notice.component.scss'],
})
export class StoreCommunityNoticeComponent {
  private readonly hints = inject(DismissibleHintService);
  private readonly externalLinks = inject(ExternalLinkService);

  protected readonly links = STORE_FOOTER_LINKS;
  protected readonly visible = computed(() => !this.hints.isDismissed(STORE_COMMUNITY_NOTICE_HINT));
  protected readonly guidelinesOpen = signal(false);

  protected open(event: MouseEvent, url: string): void {
    event.preventDefault();
    this.externalLinks.open(url);
  }

  protected dismiss(): void {
    this.hints.dismiss(STORE_COMMUNITY_NOTICE_HINT);
  }
}
