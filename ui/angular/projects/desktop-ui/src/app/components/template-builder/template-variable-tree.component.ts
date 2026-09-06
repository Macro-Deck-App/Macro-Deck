import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ScrollingModule } from '@angular/cdk/scrolling';

import { AppStrings, resolveLocalizedText, variableTokenKind, variableTokenLabel, variableTokenText } from '@macro-deck/runtime';
import { InputComponent, LocalizationService, VariableService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { VariableProviderGroup, buildVariableTree, countVariables, filterVariableTree, variableLabelFor } from '../../domain/template-variable-tree.util';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { IntegrationService } from '../../services/integration.service';
import type { SnippetInsertion } from './template-snippet-list.component';
import { TextClipboardService } from '../../services/text-clipboard.service';

type TreeRow =
  | { kind: 'group-header'; key: string; label: string; collapsed: boolean }
  | { kind: 'subgroup-header'; key: string; label: string; collapsed: boolean }
  | { kind: 'variable'; key: string; variable: Variable; primary: string; secondary: string | null;
      nested: boolean };

// Compact, uniform row height for every row kind (header or variable) - the browser pane is a
// narrow rail, and a uniform height keeps `cdk-virtual-scroll-viewport` simple.
const ROW_HEIGHT = 40;

@Component({
  selector: 'shared-template-variable-tree',
  standalone: true,
  imports: [FormsModule, ScrollingModule, EmptyStateComponent, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-input
      class="tvt-search"
      type="search"
      [placeholder]="searchPlaceholder()"
      [ngModel]="search()"
      (ngModelChange)="search.set($event)" />

    @if (!hasAnyVariables()) {
      <shared-empty-state
        class="tvt-empty"
        compact
        icon="code"
        [heading]="noVariablesHeading()"
        [message]="noVariablesMessage()" />
    } @else if (!hasSearchResults()) {
      <shared-empty-state
        class="tvt-empty"
        compact
        icon="crosshair"
        [heading]="noResultsHeading()"
        [message]="noResultsMessage()" />
    } @else {
      <cdk-virtual-scroll-viewport [itemSize]="rowHeight" minBufferPx="300" maxBufferPx="600" class="tvt-list">
        <div class="tvt-row-slot" *cdkVirtualFor="let row of rows(); trackBy: trackRow" [style.height.px]="rowHeight">
          @switch (row.kind) {
            @case ('group-header') {
              <button
                type="button"
                class="tvt-group-header"
                [attr.aria-expanded]="!row.collapsed"
                (click)="toggleCollapse(row.key)">
                <span class="icon icon-xs icon-chevron-right tvt-chevron" [class.tvt-chevron-open]="!row.collapsed" aria-hidden="true"></span>
                <span class="tvt-group-label">{{ row.label }}</span>
              </button>
            }
            @case ('subgroup-header') {
              <button
                type="button"
                class="tvt-subgroup-header"
                [attr.aria-expanded]="!row.collapsed"
                (click)="toggleCollapse(row.key)">
                <span class="icon icon-xs icon-chevron-right tvt-chevron" [class.tvt-chevron-open]="!row.collapsed" aria-hidden="true"></span>
                <span class="tvt-subgroup-label">{{ row.label }}</span>
              </button>
            }
            @case ('variable') {
              <div class="tvt-var-row" [class.tvt-var-nested]="row.nested">
                <button
                  type="button"
                  class="tvt-var-main"
                  [title]="insertTooltip(row.variable)"
                  (click)="insert.emit(insertionFor(row.variable))">
                  <span class="tvt-var-head">
                    <span class="tvt-var-primary">{{ row.primary }}</span>
                    <span class="tvt-var-type type-{{ row.variable.type }}">{{ typeLabel(row.variable) }}</span>
                  </span>
                  <span class="tvt-var-sub">
                    @if (row.secondary) {
                      <span class="tvt-var-secondary">{{ row.secondary }}</span>
                    }
                    <span class="tvt-var-value" [title]="valueOf(row.variable)">{{ valueOf(row.variable) }}</span>
                  </span>
                </button>
                <button
                  type="button"
                  class="tvt-var-copy"
                  [attr.aria-label]="copyAriaLabel(row.variable)"
                  [title]="copyAriaLabel(row.variable)"
                  (click)="copy(row.variable)">
                  <span class="icon icon-xs icon-copy" aria-hidden="true"></span>
                </button>
              </div>
            }
          }
        </div>
      </cdk-virtual-scroll-viewport>
    }
  `,
  styleUrls: ['./template-variable-tree.component.scss'],
})
export class TemplateVariableTreeComponent {
  @Input() variables: Variable[] = [];
  @Input() scopeLabel = '';

  @Output() insert = new EventEmitter<SnippetInsertion>();

  private readonly integrationService = inject(IntegrationService);
  private readonly localization = inject(LocalizationService);
  private readonly clipboard = inject(TextClipboardService);
  private readonly variableStore = inject(VariableService, { optional: true });

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

  private readonly typeLabels = computed<Record<string, string>>(() => ({
    text: this.localization.translateKey(AppStrings.Variables.Manager.TypeText),
    numeric: this.localization.translateKey(AppStrings.Variables.Manager.TypeNumeric),
    boolean: this.localization.translateKey(AppStrings.Variables.Manager.TypeBoolean),
  }));

  readonly search = signal('');
  private readonly collapsedKeys = signal<Set<string>>(new Set());

  readonly rowHeight = ROW_HEIGHT;

  private readonly displayNameOf = (variable: Variable): string =>
    resolveLocalizedText(variable.displayName, this.localization);

  private readonly configurationNameOf = (variable: Variable): string | null => {
    const resolved = resolveLocalizedText(variable.configurationName, this.localization);
    return resolved ? resolved : null;
  };

  private readonly fullTree = computed<VariableProviderGroup[]>(() =>
    buildVariableTree(
      this.variables,
      this.scopeLabel,
      id => this.integrationService.integrations().find(i => i.id === id)?.name ?? id,
      this.configurationNameOf,
      key => this.localization.translateKey(key),
    ));

  private readonly isSearching = computed(() => this.search().trim().length > 0);

  readonly filteredTree = computed<VariableProviderGroup[]>(() =>
    filterVariableTree(this.fullTree(), this.search(), this.displayNameOf));

  private isCollapsed(key: string): boolean {
    if (this.isSearching()) return false;
    return this.collapsedKeys().has(key);
  }

  readonly rows = computed<TreeRow[]>(() => {
    const flat: TreeRow[] = [];
    for (const group of this.filteredTree()) {
      const groupCollapsed = this.isCollapsed(`g:${group.key}`);
      flat.push({ kind: 'group-header', key: `g:${group.key}`, label: group.label, collapsed: groupCollapsed });
      if (groupCollapsed) continue;

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
    }
    return flat;
  });

  readonly hasAnyVariables = computed(() => this.variables.length > 0);
  readonly hasSearchResults = computed(() => countVariables(this.filteredTree()) > 0);

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

  typeLabel(variable: Variable): string {
    return this.typeLabels()[variable.type] ?? variable.type;
  }

  valueOf(variable: Variable): string {
    const current = this.liveValues().get(variable.id) ?? variable;
    return current.available === false
      ? this.localization.translateKey(AppStrings.Variables.Manager.ValueUnavailable)
      : current.value ?? '';
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
