import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { ExternalLinkService } from '../../../services/external-link.service';
import { NavigationService } from '../../../services';

interface FooterLink {
  readonly label: string;
  readonly url: string;
  readonly icon: string;
}

@Component({
  selector: 'app-footer-bar',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './footer-bar.component.html',
  styleUrls: ['./footer-bar.component.scss'],
})
export class FooterBarComponent {
  protected readonly nav = inject(NavigationService);
  private readonly externalLinks = inject(ExternalLinkService);
  private readonly localization = inject(LocalizationService);

  protected readonly links = computed<readonly FooterLink[]>(() => {
    const text = (key: string): string => this.localization.translateKey(key);

    return [
      { label: text(AppStrings.Shell.Footer.Donate), url: 'https://ko-fi.com/manuelmayer', icon: 'heart' },
      { label: text(AppStrings.Shell.Footer.JoinDiscord), url: 'https://discord.macro-deck.app', icon: 'discord' },
      { label: text(AppStrings.Shell.Footer.ReportBug), url: 'https://github.com/Macro-Deck-App/Macro-Deck/issues', icon: 'bug' },
    ];
  });

  onLinkClick(event: MouseEvent, url: string): void {
    event.preventDefault();
    this.externalLinks.open(url);
  }
}
