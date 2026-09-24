import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslatePipe } from '@shared';
import { ExternalLinkService } from '../../services/external-link.service';
import { StoreGuidelinesModalComponent } from './store-guidelines-modal.component';

export const STORE_FOOTER_LINKS = {
  creatorPortal: 'https://creators.macro-deck.app',
  imprint: 'https://macro-deck.app/imprint',
  privacy: 'https://macro-deck.app/privacy/platform',
} as const;

@Component({
  selector: 'app-store-footer',
  standalone: true,
  imports: [StoreGuidelinesModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-footer.component.html',
  styleUrls: ['./store-footer.component.scss'],
})
export class StoreFooterComponent {
  private readonly externalLinks = inject(ExternalLinkService);

  protected readonly links = STORE_FOOTER_LINKS;
  protected readonly year = new Date().getFullYear();
  protected readonly guidelinesOpen = signal(false);

  protected open(event: MouseEvent, url: string): void {
    event.preventDefault();
    this.externalLinks.open(url);
  }
}
