import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { ExternalLinkService } from '../../../services/external-link.service';
import { MarkdownOptions, parseMarkdown } from '../../../util/markdown';

const IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp']);
const VIDEO_EXTENSIONS = new Set(['mp4', 'webm']);

// Same options as the Creator Portal preview, which is the reference for how an announcement looks.
const ANNOUNCEMENT: MarkdownOptions = {
  media: url => {
    const extension = url.pathname.split('.').pop()?.toLowerCase() ?? '';
    return IMAGE_EXTENSIONS.has(extension) ? 'image' : VIDEO_EXTENSIONS.has(extension) ? 'video' : null;
  },
  href: href => (/^(https?:|mailto:)/i.test(href.trim()) ? href.trim() : null),
  hideComments: false,
};

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

  protected readonly blocks = computed(() => parseMarkdown(this.text(), ANNOUNCEMENT));
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
