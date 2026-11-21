import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import type { Variable, VariableScope, VariableType } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, ErrorBannerComponent, InputComponent, LocalizationService, ModalComponent, ToggleSwitchComponent, TranslatePipe, VariableService, dismissModal } from '@shared';
import { scopeLabelKey, variableTypeLabels } from '../../domain/variable-source.util';
import { USER_VARIABLES_OPTIONS_SOURCE_ID } from '../../domain/event-parameter-refinement.util';
import { TEMPLATE_PREVIEW_SERVICE } from '../../domain/template-preview.interface';
import type { TemplateVariablePreviewOptions } from '../../domain/template-preview.interface';
import { templateVariableErrorMessage } from '../../domain/template-variable-error.util';
import { ActionOptionsService } from '../../services/action-options.service';
import { SelectComponent, SelectOption } from '../forms/select/select.component';
import { ColorPickerComponent } from '../forms/color-picker/color-picker.component';
import { FilePathInputComponent } from '../forms/file-path-input/file-path-input.component';
import { TemplateVariableFieldComponent } from './template-variable-field.component';

type VariableSource = 'value' | 'file' | 'template';

interface CreateForm {
  rawName: string;
  scope: VariableScope;
  source: VariableSource;
  type: VariableType;
  initialValue: string;
  decimalPlaces: number;
  filePath: string;
  allowWriteBack: boolean;
  template: string;
}

const DEFAULT_COLOR_VALUE = '#000000';

@Component({
  selector: 'shared-variable-create-dialog',
  standalone: true,
  imports: [
    FormsModule,
    ModalComponent,
    ButtonComponent,
    ButtonGroupComponent,
    ColorPickerComponent,
    ErrorBannerComponent,
    FilePathInputComponent,
    InputComponent,
    SelectComponent,
    TemplateVariableFieldComponent,
    ToggleSwitchComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variable-create-dialog.component.html',
  styleUrls: ['./variable-create-dialog.component.scss'],
})
export class VariableCreateDialogComponent {
  @Input() set scopeRefId(value: string | undefined | null) {
    this.scopeRefIdState.set(value ?? null);
    this.resetForm();
  }

  @Input() set scope(value: VariableScope | undefined | null) {
    this.localScopeState.set(value && value !== 'global' ? value : 'widget');
    this.resetForm();
  }

  @Input() set scopeLabel(value: string | undefined | null) {
    this.scopeLabelOverride.set(value ?? null);
  }

  @Input() set initialName(value: string | undefined | null) {
    this.onNameInput(value ?? '');
  }

  @Input() zIndex: number | null = null;

  @Output() created = new EventEmitter<Variable>();
  @Output() closed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly variableService = inject(VariableService);
  private readonly localization = inject(LocalizationService);
  private readonly optionsService = inject(ActionOptionsService);
  private readonly templatePreview = inject(TEMPLATE_PREVIEW_SERVICE, { optional: true });

  private readonly scopeRefIdState = signal<string | null>(null);
  readonly localScopeState = signal<VariableScope>('widget');
  private readonly scopeLabelOverride = signal<string | null>(null);

  readonly createForm = signal<CreateForm>(this.defaultCreateForm());
  readonly createError = signal<string | null>(null);
  readonly nameSanitized = signal<{ sanitized: string; isValid: boolean }>({ sanitized: '', isValid: false });
  readonly templateResult = signal<{ value: string | null; error: string | null } | null>(null);

  private templatePreviewHandle: ReturnType<typeof setTimeout> | null = null;
  private templatePreviewSeq = 0;
  private readonly templatePreviewCleanup = inject(DestroyRef).onDestroy(() => {
    this.templatePreviewSeq++;
    if (this.templatePreviewHandle) clearTimeout(this.templatePreviewHandle);
  });

  private readonly scopeLabelState = computed(() =>
    this.scopeLabelOverride() ?? this.localization.translateKey(scopeLabelKey(this.localScopeState())));

  private readonly typeLabels = computed(() => variableTypeLabels(key => this.localization.translateKey(key)));
  private readonly types: VariableType[] = ['text', 'numeric', 'boolean', 'color'];

  readonly typeOptions = computed<SelectOption[]>(() =>
    this.types.map(t => ({ value: t, label: this.typeLabels()[t] })));

  readonly scopeOptions = computed<SelectOption[]>(() => [
    { value: this.localScopeState(), label: this.scopeLabelState() },
    { value: 'global', label: this.localization.translateKey(AppStrings.Variables.Manager.GlobalScope) },
  ]);

  readonly booleanOptions = computed<SelectOption[]>(() => [
    { value: 'false', label: this.localization.translateKey(AppStrings.Variables.Manager.False) },
    { value: 'true', label: this.localization.translateKey(AppStrings.Variables.Manager.True) },
  ]);

  readonly sourceOptions = computed<SelectOption[]>(() => [
    { value: 'value', label: this.localization.translateKey(AppStrings.Variables.Manager.SourceOwnValue) },
    { value: 'file', label: this.localization.translateKey(AppStrings.Variables.Manager.SourceFile) },
    { value: 'template', label: this.localization.translateKey(AppStrings.Variables.Manager.SourceTemplate) },
  ]);

  readonly canChooseScope = computed(() => this.scopeRefIdState() !== null);

  readonly scopeHintLocal = computed(() => {
    const M = AppStrings.Variables.Manager;
    const scope = this.localScopeState();
    return this.localization.translateKey(
      scope === 'script' ? M.ScopeHintScript : scope === 'automation' ? M.ScopeHintAutomation : M.ScopeHintWidget);
  });

  readonly canSubmitCreate = computed(() => {
    const form = this.createForm();
    return this.nameSanitized().isValid
      && (form.source !== 'file' || form.filePath.trim().length > 0)
      && (form.source !== 'template' || form.template.trim().length > 0);
  });

  readonly createScoped = computed(() =>
    this.createForm().scope !== 'global' && this.scopeRefIdState() !== null);

  readonly createScopeRefId = computed(() =>
    this.createScoped() ? this.scopeRefIdState() ?? undefined : undefined);

  readonly createTemplateVariables = computed(() => this.createScoped()
    ? this.variableService.visibleForContext(this.localScopeState(), this.createScopeRefId())
    : this.variableService.visibleForContext('global'));

  readonly createTemplateOptions = computed(() => {
    const form = this.createForm();
    return {
      decimalPlaces: form.type === 'numeric' ? form.decimalPlaces : undefined,
      variableName: this.nameSanitized().sanitized || undefined,
    };
  });

  protected readonly strings = AppStrings.Variables.Manager;

  private readonly templatePreviewEffect = effect(() => {
    const form = this.createForm();
    if (form.source === 'template') {
      this.scheduleTemplatePreview(form.template, this.createScoped() ? this.localScopeState() : 'global',
        this.createScopeRefId(), { resultType: form.type, ...this.createTemplateOptions() });
    } else {
      this.scheduleTemplatePreview('', 'global', undefined, null);
    }
  });

  close(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }

  onNameInput(raw: string): void {
    this.createForm.update(f => ({ ...f, rawName: raw }));
    this.nameSanitized.set(this.variableService.sanitizeNameLocal(raw));
  }

  setFormScope(scope: VariableScope): void {
    this.createForm.update(f => ({ ...f, scope }));
  }

  setFormType(type: VariableType): void {
    this.createForm.update(f => ({ ...f, type, initialValue: initialValueFor(type) }));
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

  setFormTemplate(template: string): void {
    this.createForm.update(f => ({ ...f, template }));
  }

  setFormAllowWriteBack(allowWriteBack: boolean): void {
    this.createForm.update(f => ({ ...f, allowWriteBack }));
  }

  async submitCreate(): Promise<void> {
    const form = this.createForm();
    const { sanitized, isValid } = this.variableService.sanitizeNameLocal(form.rawName);
    if (!isValid || !this.canSubmitCreate()) return;
    const fromFile = form.source === 'file';
    const fromTemplate = form.source === 'template';

    const initialValue = form.type === 'boolean'
      ? (form.initialValue === 'true' ? 'true' : 'false')
      : form.initialValue;

    const scopeRefId = this.scopeRefIdState() ?? undefined;
    const scoped = form.scope !== 'global' && scopeRefId !== undefined;
    this.createError.set(null);
    const result = await this.variableService.create({
      name: sanitized,
      scope: scoped ? this.localScopeState() : 'global',
      scopeRefId: scoped ? scopeRefId : undefined,
      type: form.type,
      initialValue: fromFile || fromTemplate ? undefined : initialValue,
      decimalPlaces: form.type === 'numeric' ? form.decimalPlaces : undefined,
      fileSource: fromFile ? { path: form.filePath.trim(), allowWriteBack: form.allowWriteBack } : undefined,
      templateSource: fromTemplate ? { template: form.template } : undefined,
    });

    const variable = result.variable;
    if (variable) {
      this.optionsService.invalidateOptionsSource(USER_VARIABLES_OPTIONS_SOURCE_ID);
      dismissModal(this.modal, () => {
        this.created.emit(variable);
        this.closed.emit();
      });
    } else {
      this.createError.set(this.createErrorMessage(result.errorCode, result.errorMessage));
    }
  }

  private createErrorMessage(errorCode: string | null, message?: string | null): string {
    if ((errorCode === 'InvalidTemplate' || errorCode === 'CircularTemplate') && message) {
      return message;
    }
    return this.localization.translateKey(errorCode === 'InvalidFilePath'
      ? AppStrings.Errors.Variables.InvalidFilePath
      : AppStrings.Variables.Manager.CreateFailed);
  }

  private resetForm(): void {
    const rawName = this.createForm().rawName;
    this.createForm.set({ ...this.defaultCreateForm(), rawName });
  }

  private defaultCreateForm(): CreateForm {
    const scoped = this.scopeRefIdState() !== null;
    return {
      rawName: '',
      scope: scoped ? this.localScopeState() : 'global',
      source: 'value',
      type: 'text',
      initialValue: '',
      decimalPlaces: 0,
      filePath: '',
      allowWriteBack: false,
      template: '',
    };
  }

  private scheduleTemplatePreview(
    template: string,
    scope: VariableScope,
    scopeRefId: string | undefined,
    options: TemplateVariablePreviewOptions | null,
  ): void {
    if (this.templatePreviewHandle) clearTimeout(this.templatePreviewHandle);
    const seq = ++this.templatePreviewSeq;
    if (!options || template.trim() === '' || !this.templatePreview?.previewTemplateVariable) {
      this.templateResult.set(null);
      return;
    }

    const preview = this.templatePreview;
    this.templatePreviewHandle = setTimeout(async () => {
      try {
        const result = await preview.previewTemplateVariable!(template, scope, scopeRefId, options);
        if (seq !== this.templatePreviewSeq) return;
        this.templateResult.set(result.error
          ? { value: null, error: templateVariableErrorMessage(this.localization, result.error) }
          : { value: result.value, error: null });
      } catch (err) {
        if (seq !== this.templatePreviewSeq) return;
        this.templateResult.set({ value: null, error: err instanceof Error ? err.message : String(err) });
      }
    }, 250);
  }
}

function initialValueFor(type: VariableType): string {
  switch (type) {
    case 'boolean':
      return 'false';
    case 'color':
      return DEFAULT_COLOR_VALUE;
    default:
      return '';
  }
}
