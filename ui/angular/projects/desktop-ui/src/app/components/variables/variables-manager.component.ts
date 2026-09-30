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
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';
import { AppStrings, VariableCatalogNode, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, ErrorBannerComponent, InputComponent, LocalizationService, ModalComponent, ToggleSwitchComponent, TranslatePipe, VariableService, dismissModal } from '@shared';
import type { Variable, VariableClassification, VariableScope, VariableType } from '@macro-deck/runtime';
import { VariableGroup, VariableSourceFilter, groupVariablesBySource, isEventParameter, matchesVariableSource, variableTypeLabels } from '../../domain/variable-source.util';
import { ConfirmationModalComponent } from '../overlay/confirmation-modal/confirmation-modal.component';
import { DropdownMenuComponent } from '../overlay/dropdown-menu/dropdown-menu.component';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../forms/select/select.component';
import { FilePathInputComponent } from '../forms/file-path-input/file-path-input.component';
import { VariableCatalogService } from '../../services/variable-catalog.service';
import { IntegrationService } from '../../services/integration.service';
import { VariableCatalogIdInputComponent } from './variable-catalog-id-input.component';
import { VariableCatalogRow, createVariableCatalogRows } from './variable-catalog-rows';
import { VariableGroupHeaderComponent } from './variable-group-header.component';
import { VARIABLE_ROW_HEIGHT, VARIABLE_ROW_INDENT, VariableRowComponent } from './variable-row.component';

type VariableSource = 'value' | 'file';

interface CreateForm {
  rawName: string;
  scope: VariableScope;
  source: VariableSource;
  type: VariableType;
  initialValue: string;
  decimalPlaces: number;
  filePath: string;
  allowWriteBack: boolean;
}

interface FileSettingsForm {
  variableId: string;
  path: string;
  allowWriteBack: boolean;
}

type VariableRow =
  | { kind: 'header'; key: string; label: string }
  | { kind: 'unbound-header'; key: string; integrationId: string; count: number | null; expanded: boolean; collapsible: boolean }
  | { kind: 'catalog-state'; key: string; integrationId: string; state: 'loading' | 'offline' | 'empty' }
  | { kind: 'row'; key: string; variable: Variable }
  | VariableCatalogRow;

const CLASSIFICATION_LABEL_KEYS: Record<VariableClassification, string> = {
  user: AppStrings.Variables.Manager.ClassificationUser,
  integration: AppStrings.Variables.Manager.ClassificationIntegration,
  widget: AppStrings.Variables.Manager.ClassificationWidget,
};

const DELEGATE_INTEGRATION_ID = 'app.macro-deck.delegate';

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
    FilePathInputComponent,
    InputComponent,
    SelectComponent,
    ToggleSwitchComponent,
    TranslatePipe,
    VariableCatalogIdInputComponent,
    VariableGroupHeaderComponent,
    VariableRowComponent,
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

  @Input() set unboundExpanded(value: boolean) {
    this.unboundExpandedDefault.set(value);
  }

  @Output() pick = new EventEmitter<Variable>();

  private readonly unboundExpandedDefault = signal(false);
  private readonly unboundGroups = signal<ReadonlyMap<string, boolean>>(new Map());
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
  readonly sourceFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.SourceField));
  readonly sourceOptions = computed<SelectOption[]>(() => [
    { value: 'value', label: this.localization.translateKey(AppStrings.Variables.Manager.SourceOwnValue) },
    { value: 'file', label: this.localization.translateKey(AppStrings.Variables.Manager.SourceFile) },
  ]);
  readonly sourceHintValue = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.SourceHintOwnValue));
  readonly sourceHintFile = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.SourceHintFile));
  readonly fileFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.FileField));
  readonly allowWriteBackLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.AllowWriteBack));
  readonly allowWriteBackHint = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.AllowWriteBackHint));
  readonly fileSettingsLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.FileSettings));
  readonly shareActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ShareAction));
  readonly stopSharingActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.StopSharingAction));
  readonly sharedLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.Shared));
  readonly sharedTooltip = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.SharedTooltip));
  readonly shareFailedMessage = computed(() =>
    this.localization.translateKey(AppStrings.Errors.Variables.SharingNotSaved));
  readonly fileUnavailableLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.FileUnavailable));

  readonly subtitle = computed(() => this.localization.translateKey(
    AppStrings.Variables.Manager.VariableCount, { count: this.listed().length }));

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
  readonly shareFailed = signal(false);
  readonly unbindCandidate = signal<Variable | null>(null);
  readonly unbindFailed = signal(false);

  readonly editingVariable = computed<Variable | null>(() => {
    const id = this.editingVariableId();
    if (!id) return null;
    return this.listed().find(v => v.id === id) ?? null;
  });

  readonly createForm = signal<CreateForm>(this.defaultCreateForm());
  readonly createError = signal<string | null>(null);
  readonly fileSettings = signal<FileSettingsForm | null>(null);
  readonly fileSettingsError = signal<string | null>(null);

  readonly canSubmitCreate = computed(() => {
    const form = this.createForm();
    return this.nameSanitized().isValid && (form.source !== 'file' || form.filePath.trim().length > 0);
  });
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

  readonly rowHeight = VARIABLE_ROW_HEIGHT;
  readonly catalogIndent = VARIABLE_ROW_INDENT;

  readonly loadingLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.Loading));
  readonly notBindableLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.NotBindable));
  readonly bindActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindAction));
  readonly unboundHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.GroupHeading));
  readonly catalogEmptyLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.EmptyMessage));
  readonly retryLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.Retry));

  readonly manualIdIntegrationId = computed<string | null>(() => {
    const source = this.sourceState();
    if (source.kind !== 'integration') {
      return null;
    }
    const provider = this.variableCatalog.providersFor()().find(p => p.integrationId === source.integrationId);
    return provider?.supportsManualIds ? source.integrationId : null;
  });

  readonly acceptedTypeList = computed(() => this.acceptedTypesState());
  readonly writableOnlyValue = computed(() => this.writableOnlyState());

  private readonly catalogProviderIds = computed(
    () => new Set(this.variableCatalog.providersFor()().map(p => p.integrationId)));

  private readonly catalogIdsInScope = computed<string[]>(() =>
    [...this.catalogProviderIds()].filter(id => this.showsCatalogFor(id)));

  private readonly catalog = createVariableCatalogRows({
    integrationIds: this.catalogIdsInScope,
    search: this.search,
    acceptedTypes: this.acceptedTypesState,
    writableOnly: this.writableOnlyState,
    isExpanded: integrationId => this.isUnboundExpanded(integrationId),
  });

  private isUnboundExpanded(integrationId: string): boolean {
    return this.unboundGroups().get(integrationId) ?? this.unboundExpandedDefault();
  }

  toggleUnbound(integrationId: string): void {
    const expanded = !this.isUnboundExpanded(integrationId);
    this.unboundGroups.update(map => new Map(map).set(integrationId, expanded));
  }

  toggleCatalogBranch(integrationId: string, node: VariableCatalogNode): void {
    this.catalog.toggleBranch(integrationId, node);
  }

  retryCatalog(integrationId: string): void {
    this.catalog.retry(integrationId);
  }

  onManualBound(variable: Variable): void {
    if (this.isPickMode()) {
      this.selectVariable(variable);
    }
  }

  readonly rows = computed<VariableRow[]>(() => {
    const showHeaders = this.showGroupHeaders();
    const searching = this.catalog.searching();
    const flat: VariableRow[] = [];
    const covered = new Set<string>();
    for (const group of this.groups()) {
      const catalogId = this.catalogIntegrationOf(group);
      const unbound = catalogId ? this.unboundRows(catalogId) : [];
      if (catalogId) {
        covered.add(catalogId);
      }

      if (showHeaders && group.label) {
        flat.push({ kind: 'header', key: `header:${group.key}`, label: group.label });
      }
      if (!searching) {
        flat.push(...unbound);
      }
      for (const variable of group.variables) {
        flat.push({ kind: 'row', key: variable.id, variable });
      }
      if (searching) {
        flat.push(...unbound);
      }
    }

    for (const integrationId of this.catalogIdsInScope()) {
      if (covered.has(integrationId)) {
        continue;
      }
      const unbound = this.unboundRows(integrationId);
      if (unbound.length === 0) {
        continue;
      }
      if (showHeaders) {
        flat.push({
          kind: 'header',
          key: `header:integration:${integrationId}`,
          label: this.integrationDisplayName(integrationId),
        });
      }
      flat.push(...unbound);
    }

    return flat;
  });

  private unboundRows(integrationId: string): VariableRow[] {
    const group = this.catalog.group(integrationId);
    if (!group) {
      return [];
    }

    const rows: VariableRow[] = [{
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

  catalogPrimary(node: VariableCatalogNode): string {
    return this.catalog.reference(node) ?? this.catalog.displayName(node);
  }

  catalogSecondary(node: VariableCatalogNode): string | null {
    return this.catalog.reference(node) ? this.catalog.displayName(node) : null;
  }

  isCatalogBindable(node: VariableCatalogNode): boolean {
    return this.catalog.isBindable(node);
  }

  unboundAriaLabel(count: number | null): string | null {
    return count === null
      ? null
      : this.localization.translateKey(AppStrings.Variables.Dynamic.GroupHeadingCount, { count });
  }

  catalogStateLabel(integrationId: string, state: 'loading' | 'offline' | 'empty'): string {
    switch (state) {
      case 'loading':
        return this.loadingLabel();
      case 'offline':
        return this.localization.translateKey(AppStrings.Variables.Dynamic.OfflineMessage,
          { integration: this.integrationDisplayName(integrationId) });
      case 'empty':
        return this.catalogEmptyLabel();
    }
  }

  catalogNoteLabel(note: 'loading' | 'not-bindable'): string {
    return note === 'loading' ? this.loadingLabel() : this.notBindableLabel();
  }

  displayNameOf(variable: Variable): string | null {
    return resolveLocalizedText(variable.displayName, this.localization) || null;
  }

  readonly loadMoreLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.LoadMore));

  loadMoreCatalog(integrationId: string, parentId: string | undefined): void {
    this.catalog.loadMore(integrationId, parentId);
  }

  activateCatalogLeaf(integrationId: string, node: VariableCatalogNode): void {
    if (!this.isCatalogBindable(node)) {
      return;
    }
    this.catalogBindRequested.emit({ integrationId, node });
  }

  @Output() catalogBindRequested = new EventEmitter<{ integrationId: string; node: VariableCatalogNode }>();

  private readonly viewport = viewChild(CdkVirtualScrollViewport);

  onCatalogScroll(): void {
    const rendered = this.viewport()?.getRenderedRange() ?? { start: 0, end: 0 };
    this.rows().slice(rendered.start, rendered.end).forEach(row => {
      if (row.kind === 'catalog-grow') {
        this.catalog.grow(row.integrationId);
      }
    });
  }

  growCatalog(integrationId: string): void {
    this.catalog.grow(integrationId);
  }

  readonly openMenuKey = signal<string | null>(null);

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
    this.createError.set(null);
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

  setFormSource(source: VariableSource): void {
    this.createForm.update(f => ({ ...f, source }));
  }

  setFormFilePath(filePath: string): void {
    this.createForm.update(f => ({ ...f, filePath }));
  }

  setFormAllowWriteBack(allowWriteBack: boolean): void {
    this.createForm.update(f => ({ ...f, allowWriteBack }));
  }

  async submitCreate(): Promise<void> {
    const form = this.createForm();
    const { sanitized, isValid } = this.variableService.sanitizeNameLocal(form.rawName);
    if (!isValid || !this.canSubmitCreate()) return;
    const fromFile = form.source === 'file';

    const initialValue = form.type === 'boolean'
      ? (form.initialValue === 'true' ? 'true' : 'false')
      : form.initialValue;

    const scopeRefId = this.scopeRefIdState() ?? undefined;
    const scoped = form.scope === 'widget' && scopeRefId !== undefined;
    this.createError.set(null);
    const result = await this.variableService.create({
      name: sanitized,
      scope: scoped ? 'widget' : 'global',
      scopeRefId: scoped ? scopeRefId : undefined,
      type: form.type,
      initialValue: fromFile ? undefined : initialValue,
      decimalPlaces: form.type === 'numeric' ? form.decimalPlaces : undefined,
      fileSource: fromFile ? { path: form.filePath.trim(), allowWriteBack: form.allowWriteBack } : undefined,
    });

    if (result.variable) {
      dismissModal(this.modal, () => this.showCreateModal.set(false));
    } else {
      this.createError.set(this.saveErrorMessage(result.errorCode, AppStrings.Variables.Manager.CreateFailed));
    }
  }

  openFileSettings(variable: Variable): void {
    if (!variable.fileSource) return;
    this.fileSettingsError.set(null);
    this.fileSettings.set({
      variableId: variable.id,
      path: variable.fileSource.path,
      allowWriteBack: variable.fileSource.allowWriteBack,
    });
  }

  setFileSettingsPath(path: string): void {
    this.fileSettings.update(f => f && { ...f, path });
  }

  setFileSettingsWriteBack(allowWriteBack: boolean): void {
    this.fileSettings.update(f => f && { ...f, allowWriteBack });
  }

  closeFileSettings(): void {
    dismissModal(this.modal, () => this.fileSettings.set(null));
  }

  async saveFileSettings(): Promise<void> {
    const form = this.fileSettings();
    if (!form || form.path.trim().length === 0) return;

    this.fileSettingsError.set(null);
    const result = await this.variableService.update({
      id: form.variableId,
      fileSource: { path: form.path.trim(), allowWriteBack: form.allowWriteBack },
    });

    if (result.variable) {
      this.closeFileSettings();
    } else {
      this.fileSettingsError.set(this.saveErrorMessage(result.errorCode, AppStrings.Variables.Manager.SaveFailed));
    }
  }

  private saveErrorMessage(errorCode: string | null, fallbackKey: string): string {
    return this.localization.translateKey(
      errorCode === 'InvalidFilePath' ? AppStrings.Errors.Variables.InvalidFilePath : fallbackKey);
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

  async toggleShared(variable: Variable): Promise<void> {
    if (!this.canShare(variable)) return;
    this.shareFailed.set(false);
    const result = await this.variableService.setShared(variable.id, !variable.shared);
    this.shareFailed.set(result.variable === null);
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

  canShare(variable: Variable): boolean {
    return variable.scope === 'global' && variable.ownerIntegrationId !== DELEGATE_INTEGRATION_ID;
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
    if (variable.fileSource) {
      return this.fileUnavailableLabel();
    }
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
      source: 'value',
      type: 'text',
      initialValue: '',
      decimalPlaces: 0,
      filePath: '',
      allowWriteBack: false,
    };
  }
}
