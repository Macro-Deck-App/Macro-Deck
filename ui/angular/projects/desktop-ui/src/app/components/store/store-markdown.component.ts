import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MarkdownBlock, parseMarkdown } from '../../util/markdown';

// Renders registry-supplied markdown (issue #517) through real Angular control flow - never
// innerHTML or DomSanitizer. The safe-subset parser in util/markdown.ts is the only thing
// between third-party content and the page, not a sanitizer bypass.
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
}
