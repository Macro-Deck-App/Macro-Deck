import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings, ScriptInput, ScriptInputType } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, CheckboxComponent, InputComponent, LocalizationService, ModalComponent, ToggleSwitchComponent, TranslatePipe, VariableService, dismissModal } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { DropdownMenuComponent } from '../../overlay/dropdown-menu/dropdown-menu.component';

interface InputForm {
  name: string;
  type: ScriptInputType;
  label: string;
  defaultValue: string;
  required: boolean;
}

function emptyForm(): InputForm {
  return { name: '', type: 'text', label: '', defaultValue: '', required: false };
}

@Component({
  selector: 'app-script-inputs-panel',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    ButtonGroupComponent,
    CheckboxComponent,
    ConfirmationModalComponent,
    DropdownMenuComponent,
    EmptyStateComponent,
    InputComponent,
    ModalComponent,
    SelectComponent,
    ToggleSwitchComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './script-inputs-panel.component.html',
  styleUrls: ['./script-inputs-panel.component.scss'],
})
export class ScriptInputsPanelComponent {
  @Input() inputs: ScriptInput[] = [];

  @Input() globalNames: string[] = [];

  @Input() runsOnWidget = false;

  @Output() inputsChange = new EventEmitter<ScriptInput[]>();

  @Output() runsOnWidgetChange = new EventEmitter<boolean>();

  @Output() validityChange = new EventEmitter<boolean>();

  private readonly variables = inject(VariableService);
  private readonly localization = inject(LocalizationService);

  // The same types and the same wording the New variable dialog uses: an input is read as
  // `vars.<name>`, so a caller has to be able to hand a variable of that type straight through.
  protected readonly typeOptions = computed<SelectOption[]>(() => [
    { value: 'text', label: this.localization.translateKey(AppStrings.Scripts.InputTypeText) },
    { value: 'numeric', label: this.localization.translateKey(AppStrings.Scripts.InputTypeNumeric) },
    { value: 'boolean', label: this.localization.translateKey(AppStrings.Scripts.InputTypeBoolean) },
  ]);

  protected readonly booleanOptions = computed<SelectOption[]>(() => [
    { value: 'true', label: this.localization.translateKey(AppStrings.Scripts.BooleanTrue) },
    { value: 'false', label: this.localization.translateKey(AppStrings.Scripts.BooleanFalse) },
  ]);

  protected readonly typeLabels = computed<Record<ScriptInputType, string>>(() => ({
    text: this.localization.translateKey(AppStrings.Scripts.InputTypeText),
    numeric: this.localization.translateKey(AppStrings.Scripts.InputTypeNumeric),
    boolean: this.localization.translateKey(AppStrings.Scripts.InputTypeBoolean),
  }));

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly showCreateModal = signal(false);

  protected readonly editIndex = signal<number | null>(null);

  protected readonly form = signal<InputForm>(emptyForm());

  protected readonly openMenuKey = signal<string | null>(null);

  protected readonly deleteIndex = signal<number | null>(null);

  protected readonly deleteCandidate = computed(() => {
    const index = this.deleteIndex();
    return index === null ? null : this.inputs[index] ?? null;
  });

  protected readonly createNameSanitized = computed(() => this.variables.sanitizeNameLocal(this.form().name));

  protected readonly createNameTaken = computed(() => {
    const sanitized = this.createNameSanitized().sanitized;
    const editing = this.editIndex();
    return this.inputs.some((input, index) => index !== editing && input.name === sanitized);
  });

  protected readonly canCreate = computed(() => this.createNameSanitized().isValid && !this.createNameTaken());

  protected readonly createShadowWarning = computed(() => {
    const sanitized = this.createNameSanitized().sanitized;
    if (!this.createNameSanitized().isValid || this.createNameTaken()) return null;
    return this.globalNames.includes(sanitized)
      ? this.localization.translateKey(AppStrings.Scripts.ShadowsVariableWarning, { name: sanitized })
      : null;
  });

  protected rowKey(index: number): string {
    return `${index}:${this.inputs[index]?.name ?? ''}`;
  }

  protected setRunsOnWidget(runsOnWidget: boolean): void {
    this.runsOnWidget = runsOnWidget;
    this.runsOnWidgetChange.emit(runsOnWidget);
  }

  protected publicName(input: ScriptInput): string {
    return `vars.${input.name}`;
  }

  protected setDropdownOpen(key: string, open: boolean): void {
    this.openMenuKey.set(open ? key : null);
  }

  protected openCreate(): void {
    this.editIndex.set(null);
    this.form.set(emptyForm());
    this.showCreateModal.set(true);
  }

  protected openEdit(index: number): void {
    const input = this.inputs[index];
    if (!input) return;

    this.editIndex.set(index);
    this.form.set({
      name: input.name,
      type: input.type,
      label: input.label ?? '',
      defaultValue: input.defaultValue ?? (input.type === 'boolean' ? 'false' : ''),
      required: input.required === true,
    });
    this.showCreateModal.set(true);
  }

  protected closeCreate(): void {
    dismissModal(this.modal, () => this.showCreateModal.set(false));
  }

  protected setFormName(name: string): void {
    this.form.update(form => ({ ...form, name }));
  }

  protected setFormType(type: ScriptInputType): void {
    this.form.update(form => ({
      ...form,
      type,
      defaultValue: type === 'boolean' ? 'false' : '',
    }));
  }

  protected setFormLabel(label: string): void {
    this.form.update(form => ({ ...form, label }));
  }

  protected setFormDefault(value: string | number | null): void {
    const defaultValue = value === null || value === undefined ? '' : String(value);
    this.form.update(form => ({ ...form, defaultValue }));
  }

  protected setFormRequired(required: boolean): void {
    this.form.update(form => ({ ...form, required }));
  }

  protected submitCreate(): void {
    if (!this.canCreate()) return;

    const form = this.form();
    const declaration: ScriptInput = {
      name: this.createNameSanitized().sanitized,
      type: form.type,
      required: form.required,
    };
    if (form.label) declaration.label = form.label;
    if (form.defaultValue !== '') declaration.defaultValue = form.defaultValue;

    const index = this.editIndex();
    if (index === null) {
      this.emit([...this.inputs, declaration]);
    } else {
      const existing = this.inputs[index];
      if (existing?.description) declaration.description = existing.description;
      this.emit(this.inputs.map((current, i) => (i === index ? declaration : current)));
    }

    this.closeCreate();
  }

  protected requestDelete(index: number): void {
    this.deleteIndex.set(index);
  }

  protected cancelDelete(): void {
    this.deleteIndex.set(null);
  }

  protected confirmDelete(): void {
    const index = this.deleteIndex();
    this.deleteIndex.set(null);
    if (index === null) return;
    this.emit(this.inputs.filter((_, i) => i !== index));
  }

  protected nameError(index: number): string | null {
    const input = this.inputs[index];
    if (!input) return null;
    if (input.name === '') return this.localization.translateKey(AppStrings.Scripts.NameRequiredError);
    if (!this.variables.sanitizeNameLocal(input.name).isValid) {
      return this.localization.translateKey(AppStrings.Scripts.NameInvalidError);
    }
    return this.inputs.some((other, i) => i !== index && other.name === input.name)
      ? this.localization.translateKey(AppStrings.Scripts.NameTakenError)
      : null;
  }

  protected shadowWarning(index: number): string | null {
    const input = this.inputs[index];
    if (!input || input.name === '' || this.nameError(index)) return null;
    return this.globalNames.includes(input.name)
      ? this.localization.translateKey(AppStrings.Scripts.ShadowsVariableWarning, { name: input.name })
      : null;
  }

  private emit(inputs: ScriptInput[]): void {
    this.inputs = inputs;
    this.inputsChange.emit(inputs);
    this.validityChange.emit(this.inputs.every((_, index) => this.nameError(index) === null));
  }
}
