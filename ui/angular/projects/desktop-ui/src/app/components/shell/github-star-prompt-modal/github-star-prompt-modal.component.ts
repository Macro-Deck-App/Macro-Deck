import { ChangeDetectionStrategy, Component, ViewChild, inject, signal } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { GitHubStarPromptService } from '../../../services/github-star-prompt.service';
import { ExternalLinkService } from '../../../services/external-link.service';
import { MACRO_DECK_ISSUES_URL, MACRO_DECK_REPOSITORY_URL } from '../../../util/macro-deck-links';

type GitHubStarPromptStep = 'ask' | 'like' | 'dislike';

@Component({
  selector: 'app-github-star-prompt-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './github-star-prompt-modal.component.html',
  styleUrls: ['./github-star-prompt-modal.component.scss'],
})
export class GitHubStarPromptModalComponent {
  private readonly prompt = inject(GitHubStarPromptService);
  private readonly externalLinks = inject(ExternalLinkService);
  protected readonly appStrings = AppStrings;

  @ViewChild(ModalComponent) private readonly modal?: ModalComponent;

  protected readonly step = signal<GitHubStarPromptStep>('ask');

  constructor() {
    this.prompt.markShown();
  }

  protected star(): void {
    this.externalLinks.open(MACRO_DECK_REPOSITORY_URL);
    this.close();
  }

  protected giveFeedback(): void {
    this.externalLinks.open(MACRO_DECK_ISSUES_URL);
    this.close();
  }

  protected close(): void {
    dismissModal(this.modal, () => this.prompt.dismiss());
  }

  protected onModalClose(): void {
    this.prompt.dismiss();
  }
}
