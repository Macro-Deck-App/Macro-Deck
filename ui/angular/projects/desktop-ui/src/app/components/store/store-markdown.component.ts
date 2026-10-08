import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MarkdownBlock, parseMarkdown } from '../../util/markdown';

// Renders registry-supplied markdown through Angular control flow, never innerHTML or DomSanitizer:
// util/markdown.ts turns it into a typed model, so there is no HTML string to sanitize.
@Component({
  selector: 'shared-store-markdown',
  standalone: true,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-markdown.component.html',
  styleUrls: ['./store-markdown.component.scss'],
})
export class StoreMarkdownComponent {
  readonly text = input('');
  readonly blocks = input<MarkdownBlock[] | null>(null);

  protected readonly rendered = computed<MarkdownBlock[]>(() => this.blocks() ?? parseMarkdown(this.text()));
  protected readonly failedImages = signal<ReadonlySet<string>>(new Set());

  protected imageFailed(src: string): void {
    this.failedImages.update(failed => new Set(failed).add(src));
  }
}
