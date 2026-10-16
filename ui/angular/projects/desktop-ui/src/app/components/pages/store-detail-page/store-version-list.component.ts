import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { StoreVersionHistoryBody } from '@macro-deck/runtime';
import { ButtonComponent, TranslatePipe } from '@shared';
import { SelectOption } from '../../forms/select/select.component';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';
import { formatBytes } from '../../../util/format-bytes';
import { sameVersion } from '../../../util/semver-compare';

const LONG_CHANGELOG_CHARACTERS = 600;
const LONG_CHANGELOG_LINES = 10;
const PAGE_SIZE = 5;

@Component({
  selector: 'app-store-version-list',
  standalone: true,
  imports: [ButtonComponent, DatePipe, StoreMarkdownComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-version-list.component.html',
  styleUrls: ['./store-version-list.component.scss'],
})
export class StoreVersionListComponent {
  readonly history = input.required<StoreVersionHistoryBody[]>();
  readonly options = input<SelectOption[]>([]);
  readonly selectedVersion = input<string | null>(null);

  readonly selectVersion = output<string>();

  protected readonly formatBytes = formatBytes;

  private readonly expanded = signal<ReadonlySet<string>>(new Set());

  private readonly page = signal(0);

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.history().length / PAGE_SIZE)));

  protected readonly currentPage = computed(() => Math.min(this.page(), this.pageCount() - 1));

  protected readonly visibleHistory = computed(() => {
    const start = this.currentPage() * PAGE_SIZE;
    return this.history().slice(start, start + PAGE_SIZE);
  });

  private readonly optionsByVersion = computed(() => new Map(this.options().map(option => [option.value, option])));

  protected readonly selectable = computed(() => this.options().length > 1);

  protected option(entry: StoreVersionHistoryBody): SelectOption | undefined {
    return this.optionsByVersion().get(entry.version);
  }

  protected isSelected(entry: StoreVersionHistoryBody): boolean {
    return sameVersion(entry.version, this.selectedVersion());
  }

  protected goToPage(page: number): void {
    this.page.set(Math.max(0, Math.min(page, this.pageCount() - 1)));
  }

  protected isLong(entry: StoreVersionHistoryBody): boolean {
    const changelog = entry.changelog ?? '';
    return changelog.length > LONG_CHANGELOG_CHARACTERS || changelog.split('\n').length > LONG_CHANGELOG_LINES;
  }

  protected isExpanded(entry: StoreVersionHistoryBody): boolean {
    return this.expanded().has(entry.version);
  }

  protected toggle(entry: StoreVersionHistoryBody): void {
    const next = new Set(this.expanded());
    if (!next.delete(entry.version)) {
      next.add(entry.version);
    }
    this.expanded.set(next);
  }
}
