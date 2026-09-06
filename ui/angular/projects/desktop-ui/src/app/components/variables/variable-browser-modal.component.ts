import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import type { Variable, VariableCatalogNode, VariableCatalogProvider, VariableType } from '@macro-deck/runtime';
import { IntegrationSourceEntry, VariableSourceFilter, integrationSourceEntries, isEventParameter, isScopeLocal, matchesVariableSource } from '../../domain/variable-source.util';
import { RailItemComponent } from '../rail-page/rail-item.component';
import { VariablesManagerComponent } from './variables-manager.component';
import { VariableBindDialogComponent } from './variable-bind-dialog.component';
import { IntegrationService } from '../../services/integration.service';
import { VariableCatalogService } from '../../services/variable-catalog.service';

@Component({
  selector: 'shared-variable-browser',
  standalone: true,
  imports: [
    ModalComponent,
    RailItemComponent,
    VariablesManagerComponent,
    VariableBindDialogComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variable-browser-modal.component.html',
  styleUrls: ['./variable-browser-modal.component.scss'],
})
export class VariableBrowserModalComponent implements OnInit {
  private readonly headingState = signal<string | null>(null);
  @Input() set heading(value: string) { this.headingState.set(value); }

  private readonly localization = inject(LocalizationService);

  readonly headingLabel = computed(() =>
    this.headingState() ?? this.localization.translateKey(AppStrings.Variables.Browser.DefaultHeading));

  @Input() set mode(value: 'manage' | 'pick') {
    this.modeState.set(value);
  }

  @Input({ required: true }) set variables(value: Variable[] | undefined | null) {
    this.variablesState.set(value ?? []);
  }

  @Input() set scopeRefId(value: string | undefined | null) {
    this.scopeRefIdState.set(value ?? null);
  }

  @Input() set scopeLabel(value: string) {
    this.scopeLabelOverride.set(value);
  }

  @Input() set acceptedTypes(value: VariableType[] | undefined | null) {
    this.acceptedTypesState.set(value && value.length > 0 ? value : null);
  }

  @Input() set writableOnly(value: boolean) { this.writableOnlyState.set(value); }

  @Input() set catalogOnly(value: boolean) { this.catalogOnlyState.set(value); }

  @Input() zIndex: number | null = null;

  @Output() pick = new EventEmitter<Variable>();
  @Output() close = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly variablesState = signal<Variable[]>([]);
  private readonly acceptedTypesState = signal<VariableType[] | null>(null);
  readonly writableOnlyState = signal(false);
  private readonly selectedSource = signal<VariableSourceFilter>({ kind: 'all' });
  readonly catalogOnlyState = signal(false);
  private readonly scopeRefIdState = signal<string | null>(null);

  readonly modeState = signal<'manage' | 'pick'>('manage');
  private readonly scopeLabelOverride = signal<string | null>(null);
  readonly scopeLabelState = computed(() =>
    this.scopeLabelOverride() ?? this.localization.translateKey(AppStrings.Variables.ThisWidget));

  readonly scopeRefIdValue = computed(() => this.scopeRefIdState() ?? undefined);

  private readonly integrationService = inject(IntegrationService);
  private readonly variableCatalog = inject(VariableCatalogService);

  readonly catalogProviders = computed<VariableCatalogProvider[]>(() => this.variableCatalog.providersFor()());

  private readonly catalogIntegrationIds = computed<string[]>(() =>
    this.catalogProviders().map(p => p.integrationId));

  readonly bindDialogNode = signal<VariableCatalogNode | null>(null);

  readonly acceptedTypeList = computed<readonly VariableType[]>(() => this.acceptedTypesState() ?? []);

  readonly effectiveVariables = computed<Variable[]>(() => {
    const accepted = this.acceptedTypesState();
    const writableOnly = this.writableOnlyState();
    let list = this.variablesState();
    if (accepted) {
      list = list.filter(v => accepted.includes(v.type));
    }
    if (writableOnly) {
      list = list.filter(v => v.canWrite === true);
    }
    return list;
  });

  readonly source = computed<VariableSourceFilter>(() => {
    const selected = this.selectedSource();
    if (selected.kind === 'integration' &&
      !this.integrationSources().some(entry => entry.integrationId === selected.integrationId)) {
      return { kind: 'all' };
    }

    // Catalog-only hides every non-catalog rail entry, so falling back to them would strand the user
    // on a source they cannot see. Open on the first catalog instead, and follow it as they load.
    if (this.catalogOnlyState() &&
      !(selected.kind === 'integration' && this.catalogIntegrationIds().includes(selected.integrationId))) {
      const first = this.catalogIntegrationIds()[0];
      return first ? { kind: 'integration', integrationId: first } : selected;
    }

    return selected;
  });

  readonly catalogIntegrationId = computed<string | null>(() => {
    const source = this.source();
    if (source.kind !== 'integration') {
      return null;
    }
    return this.catalogIntegrationIds().includes(source.integrationId) ? source.integrationId : null;
  });

  readonly catalogTreeMode = computed<'browse' | 'pick'>(() =>
    this.modeState() === 'pick' ? 'pick' : 'browse');

  readonly totalCount = computed(() => this.effectiveVariables().length);

  readonly userCount = computed(() =>
    this.effectiveVariables().filter(v => matchesVariableSource(v, { kind: 'user' })).length);

  readonly scopeCount = computed(() => this.effectiveVariables().filter(isScopeLocal).length);

  readonly showScopeSource = computed(() =>
    (this.modeState() === 'manage' && this.scopeRefIdState() !== null) || this.scopeCount() > 0);

  readonly integrationSources = computed<IntegrationSourceEntry[]>(() => {
    const catalogIds = this.catalogIntegrationIds();
    const entries = integrationSourceEntries(
      this.effectiveVariables(),
      id => this.integrationDisplayName(id),
      catalogIds);
    return this.catalogOnlyState()
      ? entries.filter(entry => catalogIds.includes(entry.integrationId))
      : entries;
  });

  readonly eventCount = computed(() => this.effectiveVariables().filter(isEventParameter).length);

  readonly managerTitle = computed<string>(() => {
    const source = this.source();
    switch (source.kind) {
      case 'all':
        return this.localization.translateKey(AppStrings.Variables.Browser.AllVariables);
      case 'user':
        return this.localization.translateKey(AppStrings.Variables.Browser.UserVariables);
      case 'scope':
        return this.scopeLabelState();
      case 'event':
        return this.localization.translateKey(AppStrings.Variables.Browser.ThisEvent);
      case 'integration':
        return this.integrationDisplayName(source.integrationId);
    }
  });

  ngOnInit(): void {
    if (this.integrationService.integrations().length === 0) {
      void this.integrationService.loadIntegrations();
    }
    void this.variableCatalog.loadProviders();
  }

  isSelected(kind: 'all' | 'user' | 'scope' | 'event'): boolean {
    return this.source().kind === kind;
  }

  isIntegrationSelected(integrationId: string): boolean {
    const source = this.source();
    return source.kind === 'integration' && source.integrationId === integrationId;
  }

  selectAll(): void {
    this.selectedSource.set({ kind: 'all' });
  }

  selectUser(): void {
    this.selectedSource.set({ kind: 'user' });
  }

  selectScope(): void {
    this.selectedSource.set({ kind: 'scope' });
  }

  selectEvent(): void {
    this.selectedSource.set({ kind: 'event' });
  }

  selectIntegration(integrationId: string): void {
    this.selectedSource.set({ kind: 'integration', integrationId });
  }

  onPick(variable: Variable): void {
    dismissModal(this.modal, () => {
      this.pick.emit(variable);
      this.close.emit();
    });
  }

  protected readonly bindDialogIntegrationId = signal<string | null>(null);

  onCatalogBindRequested(request: { integrationId: string; node: VariableCatalogNode }): void {
    this.bindDialogIntegrationId.set(request.integrationId);
    this.bindDialogNode.set(request.node);
  }

  onCatalogBound(variable: Variable): void {
    this.bindDialogNode.set(null);
    if (this.modeState() === 'pick') {
      this.onPick(variable);
    }
  }

  closeCatalogBindDialog(): void {
    this.bindDialogNode.set(null);
  }

  catalogProviderName(integrationId: string): string {
    const provider = this.catalogProviders().find(p => p.integrationId === integrationId);
    if (!provider) return this.integrationDisplayName(integrationId);
    return resolveLocalizedText(provider.name, this.localization) || this.integrationDisplayName(integrationId);
  }

  onClose(): void {
    this.close.emit();
  }

  private integrationDisplayName(integrationId: string): string {
    return this.integrationService.integrations().find(i => i.id === integrationId)?.name ?? integrationId;
  }
}
