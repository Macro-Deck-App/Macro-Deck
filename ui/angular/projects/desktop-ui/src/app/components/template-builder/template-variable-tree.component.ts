import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';

import { AppStrings, resolveLocalizedText, variableTokenKind, variableTokenLabel, variableTokenText } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService, VariableService } from '@shared';
import type { Variable, VariableCatalogNode } from '@macro-deck/runtime';
import { VariableProviderGroup, buildVariableTree, filterVariableTree, variableLabelFor } from '../../domain/template-variable-tree.util';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { IntegrationService } from '../../services/integration.service';
import type { SnippetInsertion } from './template-snippet-list.component';
import { TextClipboardService } from '../../services/text-clipboard.service';
import { VariableCatalogService } from '../../services/variable-catalog.service';
import { VariableBindDialogComponent } from '../variables/variable-bind-dialog.component';
import { VariableCatalogRow, createVariableCatalogRows } from '../variables/variable-catalog-rows';
import { VariableGroupHeaderComponent } from '../variables/variable-group-header.component';
import { VARIABLE_ROW_HEIGHT, VariableRowComponent } from '../variables/variable-row.component';

type TreeRow =
  | { kind: 'group-header'; key: string; label: string; collapsed: boolean }
  | { kind: 'subgroup-header'; key: string; label: string; collapsed: boolean }
  | { kind: 'variable'; key: string; variable: Variable; primary: string; secondary: string | null;
      nested: boolean }
  | { kind: 'unbound-header'; key: string; integrationId: string; count: number | null; expanded: boolean;
      collapsible: boolean }
  | { kind: 'catalog-state'; key: string; integrationId: string; state: 'loading' | 'offline' | 'empty' }
  | VariableCatalogRow;

const INTEGRATION_GROUP_PREFIX = 'integration:';

// The builder is itself a modal, and a dialog opened from inside it has to stack above it.
const BIND_DIALOG_Z_INDEX = 1100;

@Component({
  selector: 'shared-template-variable-tree',
  standalone: true,
  imports: [
    FormsModule,
    ScrollingModule,
    ButtonComponent,
    EmptyStateComponent,
    InputComponent,
    VariableBindDialogComponent,
    VariableGroupHeaderComponent,
    VariableRowComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-input
      class="tvt-search"
      type="search"
      [placeholder]="searchPlaceholder()"
      [ngModel]="search()"
      (ngModelChange)="search.set($event)" />

    @if (rows().length === 0 && !isSearching()) {
      <shared-empty-state
        class="tvt-empty"
        compact
        icon="code"
        [heading]="noVariablesHeading()"
        [message]="noVariablesMessage()" />
    } @else if (rows().length === 0) {
      <shared-empty-state
        class="tvt-empty"
        compact
        icon="crosshair"
        [heading]="noResultsHeading()"
        [message]="noResultsMessage()" />
    } @else {
      <cdk-virtual-scroll-viewport
        [itemSize]="rowHeight"
        minBufferPx="300"
        maxBufferPx="600"
        class="tvt-list"
        (scrolledIndexChange)="onScrolled()">
        <div class="tvt-row-slot" *cdkVirtualFor="let row of rows(); trackBy: trackRow" [style.height.px]="rowHeight">
          @switch (row.kind) {
            @case ('group-header') {
              <shared-variable-group-header
                class="tvt-group-header"
                [label]="row.label"
                [collapsible]="true"
                [section]="true"
                [expanded]="!row.collapsed"
                (toggle)="toggleCollapse(row.key)" />
            }
            @case ('subgroup-header') {
              <shared-variable-group-header
                class="tvt-subgroup-header"
                [label]="row.label"
                [collapsible]="true"
                [depth]="1"
                [expanded]="!row.collapsed"
                (toggle)="toggleCollapse(row.key)" />
            }
            @case ('variable') {
              <shared-variable-row
                class="tvt-var-row"
                [primary]="row.primary"
                [primaryMono]="row.secondary === null"
                [secondary]="row.secondary"
                [secondaryMono]="true"
                [type]="row.variable.type"
                [value]="valueOf(row.variable)"
                [unavailable]="unavailableOf(row.variable)"
                [depth]="row.nested ? 1 : 0"
                [interactive]="true"
                [title]="insertTooltip(row.variable)"
                (activate)="insert.emit(insertionFor(row.variable))">
                <button
                  row-actions
                  type="button"
                  class="tvt-var-copy"
                  [attr.aria-label]="copyAriaLabel(row.variable)"
                  [title]="copyAriaLabel(row.variable)"
                  (click)="copy(row.variable)">
                  <span class="icon icon-xs icon-copy" aria-hidden="true"></span>
                </button>
              </shared-variable-row>
            }
            @case ('unbound-header') {
              <shared-variable-group-header
                class="tvt-unbound-header"
                [label]="unboundHeading()"
                [count]="row.count"
                [collapsible]="row.collapsible"
                [section]="!row.collapsible"
                [expanded]="row.expanded"
                [ariaLabel]="unboundAriaLabel(row.count)"
                (toggle)="toggleUnbound(row.integrationId)" />
            }
            @case ('catalog-state') {
              <div class="tvt-state-row">
                <span [title]="catalogStateLabel(row.integrationId, row.state)">{{ catalogStateLabel(row.integrationId, row.state) }}</span>
                @if (row.state === 'offline') {
                  <shared-button variant="secondary" size="compact" (click)="catalog.retry(row.integrationId)">{{ retryLabel() }}</shared-button>
                }
              </div>
            }
            @case ('catalog-branch') {
              <shared-variable-row
                [primary]="catalog.displayName(row.node)"
                [primaryMono]="false"
                [secondary]="catalog.reference(row.node)"
                [secondaryMono]="true"
                [type]="row.node.type"
                [depth]="row.depth"
                [unbound]="true"
                [interactive]="true"
                [branch]="true"
                [expanded]="row.expanded"
                (activate)="catalog.toggleBranch(row.integrationId, row.node)">
                @if (catalog.isBindable(row.node)) {
                  <shared-button
                    row-actions
                    variant="secondary"
                    size="compact"
                    (click)="requestBind(row.integrationId, row.node)">{{ bindActionLabel() }}</shared-button>
                }
              </shared-variable-row>
            }
            @case ('catalog-leaf') {
              <shared-variable-row
                class="tvt-catalog-leaf"
                [primary]="catalog.displayName(row.node)"
                [primaryMono]="false"
                [secondary]="catalog.reference(row.node)"
                [secondaryMono]="true"
                [type]="row.node.type"
                [depth]="row.depth"
                [unbound]="true"
                [interactive]="true"
                [actionLabel]="bindActionLabel()"
                (activate)="requestBind(row.integrationId, row.node)" />
            }
            @case ('catalog-note') {
              <div class="tvt-state-row" [style.padding-inline-start.px]="row.depth * 20 + 12">
                <span>{{ catalogNoteLabel(row.note) }}</span>
              </div>
            }
            @case ('catalog-grow') {
              <button type="button" class="tvt-link-row tvt-catalog-grow" (click)="catalog.grow(row.integrationId)">
                <span>{{ loadMoreLabel() }}</span>
              </button>
            }
            @case ('catalog-more') {
              <button
                type="button"
                class="tvt-link-row"
                [style.padding-inline-start.px]="row.depth * 20 + 12"
                (click)="catalog.loadMore(row.integrationId, row.parentId)">
                <span>{{ loadMoreLabel() }}</span>
              </button>
            }
          }
        </div>
      </cdk-virtual-scroll-viewport>
    }

    @if (bindRequest(); as request) {
      <shared-variable-bind-dialog
        [integrationId]="request.integrationId"
        [node]="request.node"
        [zIndex]="bindDialogZIndex"
        (bound)="onBound($event)"
        (close)="bindRequest.set(null)" />
    }
  `,
  styleUrls: ['./template-variable-tree.component.scss'],
})
export class TemplateVariableTreeComponent {
  @Input() set variables(value: Variable[]) { this.passedVariables.set(value ?? []); }
  @Input() set scopeLabel(value: string) { this.scopeLabelState.set(value ?? ''); }

  private readonly passedVariables = signal<Variable[]>([]);
  private readonly scopeLabelState = signal('');

  @Output() insert = new EventEmitter<SnippetInsertion>();

  private readonly integrationService = inject(IntegrationService);
  private readonly localization = inject(LocalizationService);
  private readonly clipboard = inject(TextClipboardService);
  private readonly variableStore = inject(VariableService, { optional: true });
  private readonly variableCatalog = inject(VariableCatalogService);

  // The passed-in list is a snapshot taken when the modal opened, but an integration pushes new
  // values continuously, so the displayed value is resolved against the store on every read. Only
  // the value is taken from there - the list itself also carries synthesized event and input
  // entries the store does not know about.
  private readonly liveValues = computed(() => {
    const map = new Map<string, Variable>();
    for (const v of this.variableStore?.variables() ?? []) {
      map.set(v.id, v);
    }
    return map;
  });

  readonly search = signal('');
  private readonly collapsedKeys = signal<Set<string>>(new Set());

  private readonly unboundGroups = signal<ReadonlySet<string>>(new Set());

  readonly rowHeight = VARIABLE_ROW_HEIGHT;
  readonly bindDialogZIndex = BIND_DIALOG_Z_INDEX;

  readonly bindRequest = signal<{ integrationId: string; node: VariableCatalogNode } | null>(null);

  private readonly catalogIds = computed<string[]>(() =>
    this.variableCatalog.providersFor()().map(p => p.integrationId));

  protected readonly catalog = createVariableCatalogRows({
    integrationIds: this.catalogIds,
    search: this.search,
    isExpanded: integrationId => this.unboundGroups().has(integrationId),
  });

  // A widget-local variable shadows a global of the same name, hence the name check.
  private readonly listedVariables = computed<Variable[]>(() => {
    const passed = this.passedVariables();
    const catalogIds = new Set(this.catalogIds());
    const ids = new Set(passed.map(v => v.id));
    const names = new Set(passed.map(v => v.name));
    const bound = (this.variableStore?.variables() ?? []).filter(v =>
      v.scope === 'global' && !!v.dynamicResourceId && !!v.ownerIntegrationId
      && catalogIds.has(v.ownerIntegrationId) && !ids.has(v.id) && !names.has(v.name));
    return bound.length > 0 ? [...passed, ...bound] : passed;
  });

  private integrationName(integrationId: string): string {
    return this.integrationService.integrations().find(i => i.id === integrationId)?.name ?? integrationId;
  }

  private readonly displayNameOf = (variable: Variable): string =>
    resolveLocalizedText(variable.displayName, this.localization);

  private readonly configurationNameOf = (variable: Variable): string | null => {
    const resolved = resolveLocalizedText(variable.configurationName, this.localization);
    return resolved ? resolved : null;
  };

  private readonly fullTree = computed<VariableProviderGroup[]>(() =>
    buildVariableTree(
      this.listedVariables(),
      this.scopeLabelState(),
      id => this.integrationName(id),
      this.configurationNameOf,
      key => this.localization.translateKey(key),
    ));

  readonly isSearching = computed(() => this.search().trim().length > 0);

  readonly filteredTree = computed<VariableProviderGroup[]>(() =>
    filterVariableTree(this.fullTree(), this.search(), this.displayNameOf));

  private isCollapsed(key: string): boolean {
    if (this.isSearching()) return false;
    return this.collapsedKeys().has(key);
  }

  readonly rows = computed<TreeRow[]>(() => {
    const flat: TreeRow[] = [];
    const searching = this.isSearching();
    const covered = new Set<string>();
    for (const group of this.filteredTree()) {
      const groupCollapsed = this.isCollapsed(`g:${group.key}`);
      flat.push({ kind: 'group-header', key: `g:${group.key}`, label: group.label, collapsed: groupCollapsed });
      const catalogId = this.catalogIdOf(group.key);
      if (catalogId) {
        covered.add(catalogId);
      }
      if (groupCollapsed) continue;

      const unbound = catalogId ? this.unboundRows(catalogId) : [];
      if (!searching) {
        flat.push(...unbound);
      }

      for (const subgroup of group.subgroups) {
        const subKey = `s:${group.key}:${subgroup.key}`;
        const hasSubHeader = subgroup.label !== '';
        const subCollapsed = hasSubHeader && this.isCollapsed(subKey);
        if (hasSubHeader) {
          flat.push({ kind: 'subgroup-header', key: subKey, label: subgroup.label, collapsed: subCollapsed });
        }
        if (subCollapsed) continue;

        for (const variable of subgroup.variables) {
          const label = variableLabelFor(variable, this.displayNameOf(variable));
          flat.push({ kind: 'variable', key: variable.id, variable,
            primary: label.primary, secondary: label.secondary, nested: hasSubHeader });
        }
      }

      if (searching) {
        flat.push(...unbound);
      }
    }

    for (const integrationId of this.catalogIds()) {
      if (covered.has(integrationId)) {
        continue;
      }
      const unbound = this.unboundRows(integrationId);
      if (unbound.length === 0) {
        continue;
      }
      const key = `g:${INTEGRATION_GROUP_PREFIX}${integrationId}`;
      const collapsed = this.isCollapsed(key);
      flat.push({ kind: 'group-header', key, label: this.integrationName(integrationId), collapsed });
      if (!collapsed) {
        flat.push(...unbound);
      }
    }
    return flat;
  });

  private catalogIdOf(groupKey: string): string | null {
    if (!groupKey.startsWith(INTEGRATION_GROUP_PREFIX)) {
      return null;
    }
    const id = groupKey.slice(INTEGRATION_GROUP_PREFIX.length);
    return this.catalogIds().includes(id) ? id : null;
  }

  private unboundRows(integrationId: string): TreeRow[] {
    const group = this.catalog.group(integrationId);
    if (!group) {
      return [];
    }

    const rows: TreeRow[] = [{
      kind: 'unbound-header',
      key: `unbound:${integrationId}`,
      integrationId,
      count: group.count,
      expanded: group.expanded,
      collapsible: group.collapsible,
    }];
    if (group.expanded && group.state !== 'ready') {
      rows.push({ kind: 'catalog-state', key: `state:${integrationId}`, integrationId, state: group.state });
    }
    rows.push(...group.rows);
    return rows;
  }

  toggleUnbound(integrationId: string): void {
    this.unboundGroups.update(set => {
      const next = new Set(set);
      if (!next.delete(integrationId)) {
        next.add(integrationId);
      }
      return next;
    });
  }

  requestBind(integrationId: string, node: VariableCatalogNode): void {
    if (this.catalog.isBindable(node)) {
      this.bindRequest.set({ integrationId, node });
    }
  }

  onBound(variable: Variable): void {
    this.bindRequest.set(null);
    this.insert.emit(this.insertionFor(variable));
  }

  private readonly viewport = viewChild(CdkVirtualScrollViewport);

  onScrolled(): void {
    const renderedEnd = this.viewport()?.getRenderedRange().end ?? 0;
    this.rows().slice(0, renderedEnd).forEach(row => {
      if (row.kind === 'catalog-grow') {
        this.catalog.grow(row.integrationId);
      }
    });
  }

  readonly unboundHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.GroupHeading));
  readonly bindActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindAction));
  readonly retryLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.Retry));
  readonly loadMoreLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.LoadMore));

  unboundAriaLabel(count: number | null): string | null {
    return count === null
      ? null
      : this.localization.translateKey(AppStrings.Variables.Dynamic.GroupHeadingCount, { count });
  }

  catalogStateLabel(integrationId: string, state: 'loading' | 'offline' | 'empty'): string {
    const S = AppStrings.Variables.Dynamic;
    switch (state) {
      case 'loading':
        return this.localization.translateKey(S.Loading);
      case 'offline':
        return this.localization.translateKey(S.OfflineMessage, { integration: this.integrationName(integrationId) });
      case 'empty':
        return this.localization.translateKey(S.EmptyMessage);
    }
  }

  catalogNoteLabel(note: 'loading' | 'not-bindable'): string {
    const S = AppStrings.Variables.Dynamic;
    return this.localization.translateKey(note === 'loading' ? S.Loading : S.NotBindable);
  }

  readonly searchPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.SearchPlaceholder));
  readonly noVariablesHeading = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.NoVariablesInScope));
  readonly noVariablesMessage = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.NoVariablesMessage));
  readonly noResultsHeading = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.NoSearchResultsHeading));
  readonly noResultsMessage = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.NoSearchResultsMessage));

  toggleCollapse(key: string): void {
    this.collapsedKeys.update(set => {
      const next = new Set(set);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }

  trackRow(_index: number, row: TreeRow): string {
    return row.key;
  }

  valueOf(variable: Variable): string | null {
    const current = this.liveValues().get(variable.id) ?? variable;
    return current.available === false ? null : current.value ?? '';
  }

  unavailableOf(variable: Variable): string | null {
    const current = this.liveValues().get(variable.id) ?? variable;
    return current.available === false
      ? this.localization.translateKey(AppStrings.Variables.Manager.ValueUnavailable)
      : null;
  }

  referenceText(variable: Variable): string {
    return variableTokenText(variableTokenKind(variable), variable.name);
  }

  insertionFor(variable: Variable): SnippetInsertion {
    return { text: this.referenceText(variable), bare: this.qualifiedName(variable) };
  }

  insertTooltip(variable: Variable): string {
    return this.localization.translateKey(AppStrings.TemplateBuilder.InsertVariableTooltip, {
      reference: this.referenceText(variable),
    });
  }

  copyAriaLabel(variable: Variable): string {
    return this.localization.translateKey(AppStrings.CopyValue.CopyAriaLabel, {
      label: this.qualifiedName(variable),
    });
  }

  async copy(variable: Variable): Promise<void> {
    await this.clipboard.copyText(this.qualifiedName(variable));
  }

  private qualifiedName(variable: Variable): string {
    return variableTokenLabel(variableTokenKind(variable), variable.name);
  }
}
