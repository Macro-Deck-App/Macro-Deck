import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings } from '@macro-deck/runtime';
import { InputComponent, LocalizationService } from '@shared';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { FILTER_CATEGORIES, FilterCategory, LIQUID_CONTROL_SNIPPETS, LIQUID_FILTERS, LiquidControlSnippet, LiquidFilterSnippet, filterCategoryLabelKey } from './liquid-snippets';

export interface SnippetInsertion {
  text: string;
  caret?: number;
  wrapped?: string;
  wrappedCaret?: number;
  bare?: string;
}

@Component({
  selector: 'shared-template-snippet-list',
  standalone: true,
  imports: [FormsModule, InputComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-input
      class="snip-search"
      type="search"
      [placeholder]="searchPlaceholder()"
      [ngModel]="search()"
      (ngModelChange)="search.set($event)" />

    @if (!hasMatches()) {
      <shared-empty-state
        class="snip-empty"
        compact
        icon="crosshair"
        [heading]="noResultsHeading()"
        [message]="noResultsMessage()" />
    } @else if (mode === 'filters') {
      <div class="snip-list">
        @for (category of categories; track category) {
          @if (filtersByCategory(category).length > 0) {
            <div class="snip-category">
              <span class="snip-category-label">{{ categoryLabel(category) }}</span>
              @for (f of filtersByCategory(category); track f.key) {
                <button type="button" class="snip-filter-row" (click)="insertFilter(f)">
                  <span class="snip-filter-head">
                    <span class="snip-filter-label">{{ f.label }}</span>
                    <code class="snip-filter-example">{{ f.insert.trim() }}</code>
                  </span>
                  <span class="snip-filter-hint">{{ hint(f) }}</span>
                </button>
              }
            </div>
          }
        }
      </div>
    } @else {
      <div class="snip-list">
        @for (s of controlSnippets(); track s.key) {
          <button type="button" class="snip-filter-row" (click)="insertControl(s)">
            <span class="snip-filter-head">
              <span class="snip-filter-label">{{ s.label }}</span>
              <code class="snip-filter-example">{{ s.example }}</code>
            </span>
            <span class="snip-filter-hint">{{ hint(s) }}</span>
          </button>
        }
      </div>
    }
  `,
  styleUrls: ['./template-snippet-list.component.scss'],
})
export class TemplateSnippetListComponent {
  @Input() mode: 'filters' | 'control-flow' = 'filters';

  @Output() insert = new EventEmitter<SnippetInsertion>();

  private readonly localization = inject(LocalizationService);

  readonly categories = FILTER_CATEGORIES;

  readonly search = signal('');

  readonly searchPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.SearchSnippetsPlaceholder));
  readonly noResultsHeading = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.NoSearchResultsHeading));
  readonly noResultsMessage = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.NoSnippetResultsMessage));

  private matches(label: string, example: string, hintKey: string): boolean {
    const query = this.search().trim().toLowerCase();
    if (!query) return true;
    return `${label} ${example} ${this.localization.translateKey(hintKey)}`.toLowerCase().includes(query);
  }

  readonly controlSnippets = computed(() =>
    LIQUID_CONTROL_SNIPPETS.filter(s => this.matches(s.label, s.example, s.hintKey)));

  readonly hasMatches = computed(() => this.mode === 'filters'
    ? this.categories.some(c => this.filtersByCategory(c).length > 0)
    : this.controlSnippets().length > 0);

  private readonly filtersByCategoryCache = new Map<FilterCategory, LiquidFilterSnippet[]>(
    FILTER_CATEGORIES.map(category => [category, LIQUID_FILTERS.filter(f => f.category === category)]));

  filtersByCategory(category: FilterCategory): LiquidFilterSnippet[] {
    return (this.filtersByCategoryCache.get(category) ?? [])
      .filter(f => this.matches(f.label, f.insert, f.hintKey));
  }

  categoryLabel(category: FilterCategory): string {
    return this.localization.translateKey(filterCategoryLabelKey(category));
  }

  hint(snippet: { hintKey: string }): string {
    return this.localization.translateKey(snippet.hintKey);
  }

  insertFilter(filter: LiquidFilterSnippet): void {
    // Outside a tag the bare ` | upcase` would render as literal text, so it brings its own braces
    // and the caret lands where the expression to filter belongs.
    const wrapped = `{{ ${filter.insert.trim()} }}`;
    this.insert.emit({
      text: filter.insert,
      wrapped,
      wrappedCaret: '{{ '.length,
    });
  }

  insertControl(snippet: LiquidControlSnippet): void {
    this.insert.emit({ text: snippet.insert, caret: snippet.caret });
  }
}
