import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';

import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService, VariableService } from '@shared';
import type { Variable, VariableCatalogNode, VariableType } from '@macro-deck/runtime';
import { VariableCatalogPage, VariableCatalogService, resolveBoundVariable } from '../../services/variable-catalog.service';
import { variableTypeLabels } from '../../domain/variable-source.util';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { VariableCatalogIdInputComponent } from './variable-catalog-id-input.component';

export type VariableCatalogTreeRow =
  | { kind: 'branch'; key: string; node: VariableCatalogNode; depth: number; expanded: boolean }
  | { kind: 'leaf'; key: string; node: VariableCatalogNode; depth: number }
  | { kind: 'load-more'; key: string; parentId: string | undefined; depth: number }
  | { kind: 'loading'; key: string; parentId: string | undefined; depth: number };

// Same uniform row height convention as template-variable-tree.component.ts, for the same reason:
// a fixed itemSize keeps cdk-virtual-scroll-viewport simple.
const ROW_HEIGHT = 40;

const QUERY_DEBOUNCE_MS = 250;

const LOAD_MORE_INDEX_THRESHOLD = 5;

@Component({
  selector: 'shared-variable-catalog-tree',
  standalone: true,
  imports: [
    FormsModule,
    ScrollingModule,
    EmptyStateComponent,
    ButtonComponent,
    InputComponent,
    VariableCatalogIdInputComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variable-catalog-tree.component.html',
  styleUrls: ['./variable-catalog-tree.component.scss'],
})
export class VariableCatalogTreeComponent implements OnDestroy {
  // Signal inputs, not plain `@Input()` fields: both call sites render this component inside
  // `@if (catalogIntegrationId(); as integrationId)`, which reuses the same instance across
  // catalog switches (the block stays truthy, just with a new id). A plain `@Input` field read
  // inside a `computed()` has no reactive dependency on it, so `rootPage`/`offlineMessage` would
  // silently keep serving the previous provider's data - only a signal input re-triggers them.
  readonly integrationId = input.required<string>();
  readonly integrationName = input('');

  @Input() mode: 'browse' | 'pick' = 'browse';

  readonly acceptedTypes = input<readonly VariableType[]>([]);

  readonly writableOnly = input(false);

  @Output() pick = new EventEmitter<Variable>();

  @Output() bindRequested = new EventEmitter<VariableCatalogNode>();

  private readonly variableCatalog = inject(VariableCatalogService);
  private readonly variableService = inject(VariableService);
  private readonly localization = inject(LocalizationService);

  private readonly viewport = viewChild(CdkVirtualScrollViewport);

  readonly rowHeight = ROW_HEIGHT;

  readonly search = signal('');
  private readonly activeSearch = signal('');
  private readonly expanded = signal<ReadonlySet<string>>(new Set());
  private searchDebounce?: ReturnType<typeof setTimeout>;

  readonly pickFailed = signal(false);

  private readonly providers = this.variableCatalog.providersFor();

  private readonly provider = computed(() =>
    this.providers().find(p => p.integrationId === this.integrationId()));

  readonly supportsSearch = computed(() => this.provider()?.supportsSearch ?? true);

  readonly showManualIdInput = computed(() => this.provider()?.supportsManualIds ?? false);

  readonly isSearching = computed(() => this.activeSearch().trim().length > 0);

  private readonly rootPage = computed<VariableCatalogPage>(() =>
    this.variableCatalog.pageFor(this.integrationId(), undefined, this.activeSearch())());

  readonly available = computed(() => this.rootPage().available);

  readonly rootLoading = computed(() => {
    this.rootPage();
    return !this.variableCatalog.isLoaded(this.integrationId(), undefined, this.activeSearch());
  });

  readonly rows = computed<VariableCatalogTreeRow[]>(() => {
    const flat: VariableCatalogTreeRow[] = [];
    if (this.available()) {
      this.appendRows(undefined, 0, flat);
    }
    return flat;
  });

  readonly isEmpty = computed(() => !this.rootLoading() && this.available() && this.rows().length === 0);

  private readonly displayNameOf = (node: VariableCatalogNode): string => {
    const resolved = resolveLocalizedText(node.displayName, this.localization);
    return resolved || node.name;
  };

  readonly typeLabels = computed(() => variableTypeLabels(key => this.localization.translateKey(key)));

  isBindable(node: VariableCatalogNode): boolean {
    if (!node.type) {
      return false;
    }
    const accepted = this.acceptedTypes();
    if (accepted.length > 0 && !accepted.includes(node.type)) {
      return false;
    }
    return !this.writableOnly() || node.canWrite === true;
  }

  readonly searchPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.SearchPlaceholder));
  readonly emptyHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.EmptyHeading));
  readonly emptyMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.EmptyMessage));
  readonly offlineHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.OfflineHeading));
  readonly offlineMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.OfflineMessage, { integration: this.integrationName() }));
  readonly retryLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.Retry));
  readonly bindActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindAction));
  readonly bindFailedMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindFailed));
  readonly loadMoreLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.LoadMore));
  readonly loadingLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.Loading));
  readonly boundLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BoundLabel));

  ngOnDestroy(): void {
    clearTimeout(this.searchDebounce);
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => {
      // Replaces the list rather than filtering it, so any tree state built up while browsing is
      // meaningless against the new (flat, server-matched) result set - and would otherwise leak
      // back in verbatim once the search is cleared.
      this.expanded.set(new Set());
      this.activeSearch.set(value);
    }, QUERY_DEBOUNCE_MS);
  }

  isExpanded(node: VariableCatalogNode): boolean {
    return !this.isSearching() && this.expanded().has(node.id);
  }

  toggleBranch(node: VariableCatalogNode): void {
    if (this.isSearching()) {
      return;
    }
    this.expanded.update(set => {
      const next = new Set(set);
      if (next.has(node.id)) {
        next.delete(node.id);
      } else {
        next.add(node.id);
      }
      return next;
    });
  }

  activateLeaf(node: VariableCatalogNode): void {
    if (!this.isBindable(node)) {
      return;
    }
    this.pickFailed.set(false);
    if (this.mode === 'pick') {
      void this.pickLeaf(node);
    } else {
      this.bindRequested.emit(node);
    }
  }

  isBound(node: VariableCatalogNode): boolean {
    return !!node.boundVariableId;
  }

  onManualBound(variable: Variable): void {
    if (this.mode === 'pick') {
      this.pick.emit(variable);
    }
  }

  retry(): void {
    void this.variableCatalog.reload(this.integrationId(), undefined, this.activeSearch());
  }

  loadMore(parentId: string | undefined): void {
    void this.variableCatalog.loadMore(this.integrationId(), parentId, this.activeSearch());
  }

  trackRow(_index: number, row: VariableCatalogTreeRow): string {
    return row.key;
  }

  label(node: VariableCatalogNode): string {
    return this.displayNameOf(node);
  }

  onScrolled(): void {
    const viewport = this.viewport();
    if (!viewport) {
      return;
    }
    this.maybeLoadMore(viewport.getRenderedRange().end);
  }

  private maybeLoadMore(renderedRangeEnd: number): void {
    const rows = this.rows();
    const from = Math.max(0, renderedRangeEnd - LOAD_MORE_INDEX_THRESHOLD);
    const to = Math.min(rows.length, renderedRangeEnd + LOAD_MORE_INDEX_THRESHOLD);
    for (let i = from; i < to; i++) {
      const row = rows[i];
      if (row.kind === 'load-more') {
        this.loadMore(row.parentId);
        return;
      }
    }
  }

  private async pickLeaf(node: VariableCatalogNode): Promise<void> {
    const bound = resolveBoundVariable(node, this.variableService.variables());
    if (bound) {
      this.pick.emit(bound);
      return;
    }
    try {
      const response = await this.variableCatalog.bind({
        integrationId: this.integrationId(),
        resourceId: node.id,
        type: node.type,
      });
      if (response.variable) {
        this.pick.emit(response.variable);
      } else {
        this.pickFailed.set(true);
      }
    } catch {
      this.pickFailed.set(true);
    }
  }

  private appendRows(parentId: string | undefined, depth: number, out: VariableCatalogTreeRow[]): void {
    const page = parentId === undefined
      ? this.rootPage()
      : this.variableCatalog.pageFor(this.integrationId(), parentId, this.activeSearch())();

    for (const node of page.nodes) {
      if (node.hasChildren) {
        const expanded = this.isExpanded(node);
        out.push({ kind: 'branch', key: node.id, node, depth, expanded });
        if (expanded) {
          this.appendRows(node.id, depth + 1, out);
        }
      } else {
        out.push({ kind: 'leaf', key: node.id, node, depth });
      }
    }

    if (page.hasMore) {
      out.push({ kind: 'load-more', key: `more:${parentId ?? 'root'}`, parentId, depth });
    }
  }
}
