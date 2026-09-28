import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { ExternalLinkService } from '../../../services/external-link.service';
import { parseAnnouncement } from './announcement-markdown';

@Component({
  selector: 'app-announcement-markdown',
  standalone: true,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './announcement-markdown.component.html',
  styleUrls: ['../../store/store-markdown.component.scss', './announcement-markdown.component.scss'],
})
export class AnnouncementMarkdownComponent {
  private readonly externalLinks = inject(ExternalLinkService);

  readonly text = input('');

  protected readonly blocks = computed(() => parseAnnouncement(this.text()));
  protected readonly failedMedia = signal<ReadonlySet<string>>(new Set());

  protected openLink(event: MouseEvent, href: string): void {
    if (event.button !== 0 && event.button !== 1) {
      return;
    }
    event.preventDefault();
    this.externalLinks.open(href);
  }

  protected mediaFailed(src: string): void {
    this.failedMedia.update(failed => new Set(failed).add(src));
  }
}
