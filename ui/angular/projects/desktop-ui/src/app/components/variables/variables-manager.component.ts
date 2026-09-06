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
  effect,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { AppStrings, VariableCatalogNode, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, ErrorBannerComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, VariableService, dismissModal } from '@shared';
import type { Variable, VariableClassification, VariableScope, VariableType } from '@macro-deck/runtime';
import { VariableGroup, VariableSourceFilter, groupVariablesBySource, isEventParameter, matchesVariableSource, variableTypeLabels } from '../../domain/variable-source.util';
import { ConfirmationModalComponent } from '../overlay/confirmation-modal/confirmation-modal.component';
import { DropdownMenuComponent } from '../overlay/dropdown-menu/dropdown-menu.component';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../forms/select/select.component';
import { VariableCatalogService } from '../../services/variable-catalog.service';
import { IntegrationService } from '../../services/integration.service';

interface CreateForm {
  rawName: string;
  scope: VariableScope;
  type: VariableType;
  initialValue: string;
  decimalPlaces: number;
}

type VariableRow =
  | { kind: 'header'; key: string; label: string }
  | { kind: 'row'; key: string; variable: Variable }
  | { kind: 'catalog-leaf'; key: string; integrationId: string; node: VariableCatalogNode }
  | { kind: 'catalog-more'; key: string; integrationId: string; parentId: string | undefined };

// Bound from the TS constant (`[style.height.px]`) rather than left to SCSS, so `itemSize` on the
// viewport and the rendered row's actual height can never drift apart - the two live different
// places in the two files otherwise, and cdk-virtual-scroll-viewport does not detect that mismatch,
// it just misplaces rows. Measured against the taller manage-mode row (name + meta stack, actions
// column) and includes the vertical gap between rows (previously a flex `gap`, now baked into the
// slot - see the SCSS note on `.vars-list`).
const ROW_HEIGHT = 68;

const CATALOG_PAGE_LEAVES = 100;

interface CatalogRequest {
  integrationId: string;
  parentId: string | undefined;
  more: boolean;
}

const CLASSIFICATION_LABEL_KEYS: Record<VariableClassification, string> = {
  user: AppStrings.Variables.Manager.ClassificationUser,
  integration: AppStrings.Variables.Manager.ClassificationIntegration,
  widget: AppStrings.Variables.Manager.ClassificationWidget,
};

@Component({
  selector: 'shared-variables-manager',
  standalone: true,
  imports: [
    FormsModule,
    ScrollingModule,
    ModalComponent,
    ConfirmationModalComponent,
    ButtonComponent,
    ButtonGroupComponent,
    DropdownMenuComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    InputComponent,
    SelectComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variables-manager.component.html',
  styleUrls: ['./variables-manager.component.scss'],
})
export class VariablesManagerComponent implements OnInit {
  @Input() set scopeRefId(value: string | undefined | null) {
    this.scopeRefIdState.set(value ?? null);
  }

  @Input() set scopeLabel(value: string) {
    this.scopeLabelOverride.set(value);
  }

  @Input() set acceptedTypes(value: readonly VariableType[] | undefined | null) {
    this.acceptedTypesState.set(value ?? []);
  }

  @Input() set writableOnly(value: boolean) {
    this.writableOnlyState.set(value);
  }

  @Input() set mode(value: 'manage' | 'pick') {
    this.modeState.set(value);
  }

  private readonly headingOverride = signal<string | null>(null);
  @Input() set heading(value: string) { this.headingOverride.set(value); }
  readonly headingLabel = computed(() =>
    this.headingOverride() ?? this.localization.translateKey(AppStrings.Variables.Browser.DefaultHeading));

  @Input() showCreateButton = true;

  @Input() set variables(value: Variable[] | undefined | null) {
    this.variablesOverride.set(value ?? null);
  }

  @Input() set source(value: VariableSourceFilter | undefined | null) {
    this.sourceState.set(value ?? { kind: 'all' });
  }

  @Output() pick = new EventEmitter<Variable>();

  private readonly variablesOverride = signal<Variable[] | null>(null);
  private readonly sourceState = signal<VariableSourceFilter>({ kind: 'all' });
  private readonly scopeRefIdState = signal<string | null>(null);
  private readonly scopeLabelOverride = signal<string | null>(null);
  private readonly modeState = signal<'manage' | 'pick'>('manage');
  private readonly acceptedTypesState = signal<readonly VariableType[]>([]);
  private readonly writableOnlyState = signal(false);

  readonly isPickMode = computed(() => this.modeState() === 'pick');

  protected readonly variableService = inject(VariableService);
  private readonly variableCatalog = inject(VariableCatalogService);
  private readonly integrationService = inject(IntegrationService);
  private readonly localization = inject(LocalizationService);

  private readonly scopeLabelState = computed(() =>
    this.scopeLabelOverride() ?? this.localization.translateKey(AppStrings.Variables.ThisWidget));

  readonly typeLabels = computed(() => variableTypeLabels(key => this.localization.translateKey(key)));
  readonly classificationLabels = computed<Record<VariableClassification, string>>(() => ({
    user: this.localization.translateKey(CLASSIFICATION_LABEL_KEYS.user),
    integration: this.localization.translateKey(CLASSIFICATION_LABEL_KEYS.integration),
    widget: this.localization.translateKey(CLASSIFICATION_LABEL_KEYS.widget),
  }));
  readonly types: VariableType[] = ['text', 'numeric', 'boolean'];

  readonly typeOptions = computed<SelectOption[]>(() =>
    this.types.map(t => ({ value: t, label: this.typeLabels()[t] })));

  readonly scopeOptions = computed<SelectOption[]>(() => [
    { value: 'widget', label: this.scopeLabelState() },
    { value: 'global', label: this.localization.translateKey(AppStrings.Variables.Manager.GlobalScope) },
  ]);

  readonly canChooseScope = computed(() => this.scopeRefIdState() !== null);

  setFormScope(scope: VariableScope): void {
    this.createForm.update(f => ({ ...f, scope }));
  }
  readonly booleanOptions = computed<SelectOption[]>(() => [
    { value: 'false', label: this.localization.translateKey(AppStrings.Variables.Manager.False) },
    { value: 'true', label: this.localization.translateKey(AppStrings.Variables.Manager.True) },
  ]);

  readonly newVariableLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.NewVariable));
  readonly searchPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.SearchPlaceholder));
  readonly emptyFilteredHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.EmptyFilteredHeading));
  readonly emptyFilteredMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.EmptyFilteredMessage));
  readonly emptyNoneHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.EmptyNoneHeading));
  readonly emptyNoneMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.EmptyNoneMessage));
  readonly valueUnavailableLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ValueUnavailable));
  readonly writeFailedMessage = computed(() =>
    this.localization.translateKey(AppStrings.Errors.Variables.WriteFailed));
  readonly readOnlyLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ReadOnly));
  readonly readOnlyTooltip = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ReadOnlyTooltip));
  readonly toggleValueLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ToggleValue));
  readonly editValueLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.EditValue));
  readonly nameFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.NameField));
  readonly namePlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.NamePlaceholder));
  readonly nameHint = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.NameHint));
  readonly scopeFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ScopeField));
  readonly scopeHintWidget = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ScopeHintWidget));
  readonly scopeHintGlobal = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ScopeHintGlobal));
  readonly typeFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.TypeField));
  readonly decimalPlacesLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.DecimalPlaces));
  readonly initialValueLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.InitialValue));
  readonly deleteHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.DeleteHeading));
  readonly unbindActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.UnbindAction));
  readonly resourceUnavailableLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.ResourceUnavailable));
  readonly unbindHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.UnbindHeading));
  readonly unbindFailedMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.UnbindFailed));

  readonly subtitle = computed(() => {
    const counted = this.localization.translateKey(
      AppStrings.Variables.Manager.VariableCount, { count: this.listed().length });
    const unbound = this.unboundCount();
    return unbound === null ? counted : `${counted} (${unbound})`;
  });

  deleteMessage(name: string): string {
    return this.localization.translateKey(AppStrings.Variables.Manager.DeleteMessage, { name });
  }

  unbindMessage(name: string): string {
    const message = this.localization.translateKey(AppStrings.Variables.Manager.UnbindMessage, { name });
    return this.unbindFailed() ? `${message} ${this.unbindFailedMessage()}` : message;
  }

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly search = signal('');
  readonly showCreateModal = signal(false);
  readonly editingVariableId = signal<string | null>(null);
  readonly editingValue = signal<string>('');
  readonly deleteCandidate = signal<Variable | null>(null);
  readonly writeFailed = signal(false);
  readonly unbindCandidate = signal<Variable | null>(null);
  readonly unbindFailed = signal(false);

  readonly editingVariable = computed<Variable | null>(() => {
    const id = this.editingVariableId();
    if (!id) return null;
    return this.listed().find(v => v.id === id) ?? null;
  });

  readonly createForm = signal<CreateForm>(this.defaultCreateForm());
  readonly nameSanitized = signal<{ sanitized: string; isValid: boolean }>({
    sanitized: '',
    isValid: false,
  });

  readonly listed = computed<Variable[]>(() => {
    const base = this.variablesOverride() ?? this.variableService.globalVariables();
    const source = this.sourceState();
    let list = base.filter(v => matchesVariableSource(v, source));

    const query = this.search().toLowerCase().trim();
    if (query) {
      list = list.filter(v => ('vars.' + v.name).toLowerCase().includes(query));
    }
    return list;
  });

  readonly groups = computed<VariableGroup[]>(() => {
    const list = this.listed();
    if (this.sourceState().kind !== 'all') {
      return list.length > 0 ? [{ key: 'flat', label: '', variables: list }] : [];
    }
    return groupVariablesBySource(list, this.scopeLabelState(), id => this.integrationDisplayName(id),
      key => this.localization.translateKey(key));
  });

  readonly showGroupHeaders = computed(() => this.sourceState().kind === 'all');

  readonly rowHeight = ROW_HEIGHT;

  readonly unboundCount = computed<number | null>(() => {
    const source = this.sourceState();
    if (source.kind !== 'integration' || !this.catalogProviderIds().has(source.integrationId)) {
      return null;
    }
    return this.variableCatalog.unboundCountFor(source.integrationId);
  });

  private readonly catalogBudget = signal(CATALOG_PAGE_LEAVES);

  private readonly walk = computed<{ rows: VariableRow[]; frontier: CatalogRequest[] }>(() => {
    const showHeaders = this.showGroupHeaders();
    const flat: VariableRow[] = [];
    const frontier: CatalogRequest[] = [];
    const covered = new Set<string>();
    for (const group of this.groups()) {
      if (showHeaders && group.label) {
        flat.push({ kind: 'header', key: `header:${group.key}`, label: group.label });
      }
      // Catalog first: what is still to be bound is what the user came to this list to find, and
      // burying it under an integration's twenty already-materialized variables hides it.
      const catalogId = this.catalogIntegrationOf(group);
      if (catalogId) {
        covered.add(catalogId);
        this.appendCatalogRows(catalogId, undefined, flat, frontier);
      }

      for (const variable of group.variables) {
        flat.push({ kind: 'row', key: variable.id, variable });
      }
    }

    // An integration whose entries are all still unbound has no variables and so no group of its
    // own, and would otherwise show nothing at all - which is precisely the integration whose
    // catalog the user most needs to see.
    for (const integrationId of this.catalogProviderIds()) {
      if (covered.has(integrationId) || !this.showsCatalogFor(integrationId)) {
        continue;
      }
      const before = flat.length;
      this.appendCatalogRows(integrationId, undefined, flat, frontier);
      if (flat.length > before && showHeaders) {
        flat.splice(before, 0, {
          kind: 'header',
          key: `header:integration:${integrationId}`,
          label: this.integrationDisplayName(integrationId),
        });
      }
    }

    return { rows: flat, frontier };
  });

  readonly rows = computed<VariableRow[]>(() => this.walk().rows);

  private showsCatalogFor(integrationId: string): boolean {
    const source = this.sourceState();
    if (source.kind === 'all') {
      return true;
    }
    return source.kind === 'integration' && source.integrationId === integrationId;
  }

  private catalogIntegrationOf(group: VariableGroup): string | null {
    const source = this.sourceState();
    const id = source.kind === 'integration'
      ? source.integrationId
      : group.key.startsWith('integration:') ? group.key.slice('integration:'.length) : null;

    return id && this.catalogProviderIds().has(id) ? id : null;
  }

  private appendCatalogRows(
    integrationId: string,
    parentId: string | undefined,
    out: VariableRow[],
    frontier: CatalogRequest[],
  ): void {
    if (this.countCatalogLeaves(out) >= this.catalogBudget()) {
      return;
    }

    // Not fetched yet: recorded for the walk to pick up rather than read, because reading is what
    // would fetch it, and a computed must not reach out on its own.
    if (!this.variableCatalog.isLoaded(integrationId, parentId, undefined)) {
      frontier.push({ integrationId, parentId, more: false });
      return;
    }

    const page = this.variableCatalog.pageFor(integrationId, parentId, undefined)();
    if (!page.available) {
      return;
    }

    for (const node of page.nodes) {
      if (node.hasChildren) {
        this.appendCatalogRows(integrationId, node.id, out, frontier);
      }

      if (!node.boundVariableId && this.isCatalogBindable(node) && this.matchesSearch(node)) {
        out.push({ kind: 'catalog-leaf', key: `cat:${node.id}`, integrationId, node });
      }

      if (this.countCatalogLeaves(out) >= this.catalogBudget()) {
        return;
      }
    }

    if (page.hasMore) {
      frontier.push({ integrationId, parentId, more: true });
    }
  }

  private countCatalogLeaves(rows: readonly VariableRow[]): number {
    let count = 0;
    for (const row of rows) {
      if (row.kind === 'catalog-leaf') {
        count++;
      }
    }
    return count;
  }

  catalogLabel(node: VariableCatalogNode): string {
    return node.suggestedName
      ? `vars.${node.suggestedName}`
      : resolveLocalizedText(node.displayName, this.localization) || node.name;
  }

  private matchesSearch(node: VariableCatalogNode): boolean {
    const query = this.search().toLowerCase().trim();
    return query.length === 0 || this.catalogLabel(node).toLowerCase().includes(query);
  }

  isCatalogBindable(node: VariableCatalogNode): boolean {
    if (!node.type || node.boundVariableId) {
      return false;
    }
    if (this.writableOnlyState() && node.canWrite !== true) {
      return false;
    }
    const accepted = this.acceptedTypesState();
    return accepted.length === 0 || accepted.includes(node.type);
  }

  readonly loadMoreLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.LoadMore));

  loadMoreCatalog(integrationId: string, parentId: string | undefined): void {
    void this.variableCatalog.loadMore(integrationId, parentId, undefined);
  }

  activateCatalogLeaf(integrationId: string, node: VariableCatalogNode): void {
    if (!this.isCatalogBindable(node)) {
      return;
    }
    this.catalogBindRequested.emit({ integrationId, node });
  }

  private readonly catalogProviderIds = computed(
    () => new Set(this.variableCatalog.providersFor()().map(p => p.integrationId)));

  @Output() catalogBindRequested = new EventEmitter<{ integrationId: string; node: VariableCatalogNode }>();

  private readonly catalogLoader = effect(() => {
    const { rows, frontier } = this.walk();
    if (frontier.length === 0) {
      return;
    }
    const loaded = rows.reduce((n, r) => n + (r.kind === 'catalog-leaf' ? 1 : 0), 0);
    if (loaded >= this.catalogBudget()) {
      return;
    }

    const next = frontier[0];
    untracked(() => {
      void (next.more
        ? this.variableCatalog.loadMore(next.integrationId, next.parentId, undefined)
        : Promise.resolve(this.variableCatalog.pageFor(next.integrationId, next.parentId, undefined)()));
    });
  });

  onCatalogScroll(lastVisibleIndex: number): void {
    if (lastVisibleIndex >= this.rows().length - 5) {
      this.catalogBudget.update(budget => budget + CATALOG_PAGE_LEAVES);
    }
  }

  readonly openMenuKey = signal<string | null>(null);

  readonly dynamicBadgeLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.Badge));

  readonly canCreate = computed(() => {
    if (this.modeState() !== 'manage') return false;
    const source = this.sourceState();
    switch (source.kind) {
      case 'all':
      case 'user':
        return true;
      case 'scope':
        return this.scopeRefIdState() !== null;
      case 'event':
      case 'integration':
        return false;
    }
  });

  readonly hasSourceFilter = computed(() =>
    this.sourceState().kind !== 'all' || this.search().trim().length > 0);

  async ngOnInit(): Promise<void> {
    if (this.integrationService.integrations().length === 0) {
      void this.integrationService.loadIntegrations();
    }
    await this.variableService.loadVariables();
  }

  openCreate(): void {
    this.createForm.set(this.defaultCreateForm());
    this.nameSanitized.set({ sanitized: '', isValid: false });
    this.showCreateModal.set(true);
  }

  closeCreate(): void {
    dismissModal(this.modal, () => this.showCreateModal.set(false));
  }

  async onNameInput(raw: string): Promise<void> {
    this.createForm.update(f => ({ ...f, rawName: raw }));
    this.nameSanitized.set(this.variableService.sanitizeNameLocal(raw));
  }

  setFormType(type: VariableType): void {
    this.createForm.update(f => ({ ...f, type, initialValue: type === 'boolean' ? 'false' : '' }));
  }

  setFormDecimalPlaces(value: number): void {
    this.createForm.update(f => ({ ...f, decimalPlaces: Number(value) || 0 }));
  }

  setFormInitialValue(value: string): void {
    this.createForm.update(f => ({ ...f, initialValue: value }));
  }

  async submitCreate(): Promise<void> {
    const form = this.createForm();
    const { sanitized, isValid } = this.variableService.sanitizeNameLocal(form.rawName);
    if (!isValid) return;

    const initialValue = form.type === 'boolean'
      ? (form.initialValue === 'true' ? 'true' : 'false')
      : form.initialValue;

    const scopeRefId = this.scopeRefIdState() ?? undefined;
    const scoped = form.scope === 'widget' && scopeRefId !== undefined;
    const variable = await this.variableService.create({
      name: sanitized,
      scope: scoped ? 'widget' : 'global',
      scopeRefId: scoped ? scopeRefId : undefined,
      type: form.type,
      initialValue,
      decimalPlaces: form.type === 'numeric' ? form.decimalPlaces : undefined,
    });

    if (variable) {
      dismissModal(this.modal, () => this.showCreateModal.set(false));
    }
  }

  startEdit(variable: Variable): void {
    if (!this.canEditValue(variable)) return;
    this.writeFailed.set(false);
    this.editingVariableId.set(variable.id);
    this.editingValue.set(variable.value ?? '');
  }

  cancelEdit(): void {
    dismissModal(this.modal, () => {
      this.editingVariableId.set(null);
      this.editingValue.set('');
    });
  }

  async commitEdit(variable: Variable): Promise<void> {
    const value = this.editingValue();
    await this.writeValue(variable, value);
    this.cancelEdit();
  }

  async toggleBoolean(variable: Variable): Promise<void> {
    if (!this.canEditValue(variable)) return;
    await this.writeValue(variable, variable.value === 'true' ? 'false' : 'true');
  }

  // The host's own refusal reason is not surfaced: `ApiError.message` is not string-typed for this
  // client, so every failure reads as the same generic message until that is widened separately.
  private async writeValue(variable: Variable, value: string): Promise<void> {
    this.writeFailed.set(false);
    const written = await this.variableService.setValue(variable.id, value);
    this.writeFailed.set(written === null);
  }

  requestDelete(variable: Variable): void {
    if (!this.canManage(variable)) return;
    this.deleteCandidate.set(variable);
  }

  cancelDelete(): void {
    this.deleteCandidate.set(null);
  }

  async confirmDelete(): Promise<void> {
    const v = this.deleteCandidate();
    if (!v) return;
    await this.variableService.delete(v.id);
    this.deleteCandidate.set(null);
  }

  isBoundDynamic(variable: Variable): boolean {
    return !!variable.dynamicResourceId;
  }

  requestUnbind(variable: Variable): void {
    if (!this.isBoundDynamic(variable)) return;
    this.unbindFailed.set(false);
    this.unbindCandidate.set(variable);
  }

  cancelUnbind(): void {
    this.unbindCandidate.set(null);
  }

  async confirmUnbind(): Promise<void> {
    const v = this.unbindCandidate();
    if (!v || !v.ownerIntegrationId) return;
    try {
      const response = await this.variableCatalog.unbind(v.ownerIntegrationId, v.id);
      if (response.success) {
        this.unbindCandidate.set(null);
      } else {
        this.unbindFailed.set(true);
      }
    } catch {
      this.unbindFailed.set(true);
    }
  }

  selectVariable(variable: Variable): void {
    this.pick.emit(variable);
  }

  trackRow(index: number, row: VariableRow): string {
    return row.key;
  }

  setDropdownOpen(key: string, open: boolean): void {
    this.openMenuKey.set(open ? key : null);
  }

  closeOpenMenu(): void {
    if (this.openMenuKey() !== null) {
      this.openMenuKey.set(null);
    }
  }

  publicName(variable: Variable): string {
    return (isEventParameter(variable) ? 'event.' : 'vars.') + variable.name;
  }

  canEditValue(variable: Variable): boolean {
    return !!variable.canWrite;
  }

  canManage(variable: Variable): boolean {
    return variable.classification === 'user';
  }

  formatValue(variable: Variable): string {
    const value = variable.value ?? '';
    if (!variable.unit) {
      return value;
    }
    return this.localization.translateKey(AppStrings.Variables.Format.ValueWithUnit,
      { value, unit: variable.unit });
  }

  unavailableTooltip(variable: Variable): string {
    return this.isBoundDynamic(variable) ? this.resourceUnavailableLabel() : this.valueUnavailableLabel();
  }

  numericStep(variable: Variable): number {
    if (variable.step && variable.step > 0) return variable.step;
    if (!variable.decimalPlaces || variable.decimalPlaces <= 0) return 1;
    return 1 / Math.pow(10, variable.decimalPlaces);
  }

  private integrationDisplayName(integrationId: string): string {
    return this.integrationService.integrations().find(i => i.id === integrationId)?.name ?? integrationId;
  }

  private defaultCreateForm(): CreateForm {
    // Reaching this from a widget means the widget is what you meant - the browser opens on the
    // "all" source, so keying the default off the selected source instead would default to global
    // for everyone who came from a widget editor. Global is one click away in the field.
    const scoped = this.scopeRefIdState() !== null;
    return {
      rawName: '',
      scope: scoped ? 'widget' : 'global',
      type: 'text',
      initialValue: '',
      decimalPlaces: 0,
    };
  }
}
