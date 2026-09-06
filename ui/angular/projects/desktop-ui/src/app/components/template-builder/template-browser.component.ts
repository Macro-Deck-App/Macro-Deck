import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';

import { AppStrings, variableTokenKind, variableTokenLabel, variableTokenText } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { TabBarComponent } from '../tab-bar/tab-bar.component';
import type { TabItem } from '../tab-bar/tab-bar.model';
import { TemplateVariableTreeComponent } from './template-variable-tree.component';
import { TemplateSnippetListComponent, SnippetInsertion } from './template-snippet-list.component';
import { VariableBrowserModalComponent } from '../variables/variable-browser-modal.component';
import { VariableCatalogService } from '../../services/variable-catalog.service';

type BrowserTab = 'variables' | 'filters' | 'control-flow';

@Component({
  selector: 'shared-template-browser',
  standalone: true,
  imports: [TabBarComponent, TemplateVariableTreeComponent, TemplateSnippetListComponent, VariableBrowserModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tbr-pane">
      <shared-tab-bar
        class="tbr-tabs"
        idPrefix="template-browser"
        [items]="tabs()"
        [activeId]="activeTab()"
        (activeIdChange)="setActiveTab($event)" />

      <div class="tbr-panels">
        <div
          class="tbr-panel"
          [id]="'template-browser-panel-variables'"
          [attr.aria-labelledby]="'template-browser-tab-variables'"
          role="tabpanel"
          [hidden]="activeTab() !== 'variables'">
          @if (hasDynamicSources()) {
            <button type="button" class="tbr-dynamic-browse" (click)="openDynamicBrowser()">
              <span class="icon icon-xs icon-puzzle" aria-hidden="true"></span>
              {{ browseDynamicSourcesLabel() }}
            </button>
          }
          <shared-template-variable-tree
            [variables]="variables"
            [scopeLabel]="scopeLabel"
            (insert)="insert.emit($event)" />
        </div>

        <div
          class="tbr-panel"
          [id]="'template-browser-panel-filters'"
          [attr.aria-labelledby]="'template-browser-tab-filters'"
          role="tabpanel"
          [hidden]="activeTab() !== 'filters'">
          <shared-template-snippet-list mode="filters" (insert)="onSnippetInsert($event)" />
        </div>

        <div
          class="tbr-panel"
          [id]="'template-browser-panel-control-flow'"
          [attr.aria-labelledby]="'template-browser-tab-control-flow'"
          role="tabpanel"
          [hidden]="activeTab() !== 'control-flow'">
          <shared-template-snippet-list mode="control-flow" (insert)="onSnippetInsert($event)" />
        </div>
      </div>
    </div>

    @if (showDynamicBrowser()) {
      <shared-variable-browser
        mode="pick"
        [catalogOnly]="true"
        [variables]="variables"
        [scopeLabel]="scopeLabel"
        [zIndex]="1100"
        (pick)="onDynamicPick($event)"
        (close)="closeDynamicBrowser()" />
    }
  `,
  styleUrls: ['./template-browser.component.scss'],
})
export class TemplateBrowserComponent {
  @Input() variables: Variable[] = [];
  @Input() scopeLabel = '';

  @Output() insert = new EventEmitter<SnippetInsertion>();

  private readonly localization = inject(LocalizationService);
  private readonly variableCatalog = inject(VariableCatalogService);

  private readonly activeTabState = signal<BrowserTab>('variables');
  readonly activeTab = this.activeTabState.asReadonly();

  readonly tabs = computed<TabItem[]>(() => [
    { id: 'variables', label: this.localization.translateKey(AppStrings.TemplateBuilder.Variables) },
    { id: 'filters', label: this.localization.translateKey(AppStrings.TemplateBuilder.FiltersHeading) },
    { id: 'control-flow', label: this.localization.translateKey(AppStrings.TemplateBuilder.Logic) },
  ]);

  readonly hasDynamicSources = computed(() => this.variableCatalog.providersFor()().length > 0);

  readonly browseDynamicSourcesLabel = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.BrowseDynamicSources));

  readonly showDynamicBrowser = signal(false);

  setActiveTab(id: string): void {
    if (id === 'variables' || id === 'filters' || id === 'control-flow') {
      this.activeTabState.set(id);
    }
  }

  onSnippetInsert(insertion: SnippetInsertion): void {
    this.insert.emit(insertion);
  }

  openDynamicBrowser(): void {
    this.showDynamicBrowser.set(true);
  }

  closeDynamicBrowser(): void {
    this.showDynamicBrowser.set(false);
  }

  onDynamicPick(variable: Variable): void {
    this.showDynamicBrowser.set(false);
    const kind = variableTokenKind(variable);
    this.insert.emit({ text: variableTokenText(kind, variable.name), bare: variableTokenLabel(kind, variable.name) });
  }
}
