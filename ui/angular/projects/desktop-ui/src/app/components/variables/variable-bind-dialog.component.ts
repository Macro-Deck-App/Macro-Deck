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
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, VariableService, dismissModal } from '@shared';
import type { Variable, VariableCatalogNode, VariableType } from '@macro-deck/runtime';
import { VariableCatalogService } from '../../services/variable-catalog.service';
import { variableTypeLabels } from '../../domain/variable-source.util';
import { SelectComponent, SelectOption } from '../forms/select/select.component';

@Component({
  selector: 'shared-variable-bind-dialog',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, ButtonGroupComponent, InputComponent, SelectComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variable-bind-dialog.component.html',
  styleUrls: ['./variable-bind-dialog.component.scss'],
})
export class VariableBindDialogComponent implements OnInit {
  @Input({ required: true }) integrationId!: string;
  @Input({ required: true }) node!: VariableCatalogNode;

  readonly acceptedTypes = input<readonly VariableType[]>([]);

  @Output() bound = new EventEmitter<Variable>();
  @Output() close = new EventEmitter<void>();

  private readonly variableCatalog = inject(VariableCatalogService);
  private readonly variableService = inject(VariableService);
  private readonly localization = inject(LocalizationService);

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly rawName = signal('');
  readonly type = signal<VariableType>('text');
  readonly submitting = signal(false);
  readonly failed = signal(false);

  readonly nameSanitized = computed(() => this.variableService.sanitizeNameLocal(this.rawName()));

  readonly nameTaken = computed(() => {
    const { sanitized, isValid } = this.nameSanitized();
    if (!isValid) return false;
    return this.variableService.globalVariables().some(v => v.name === sanitized);
  });

  readonly canSubmit = computed(() =>
    !this.submitting() && this.nameSanitized().isValid && !this.nameTaken());

  private static readonly allTypes: readonly VariableType[] = ['text', 'numeric', 'boolean'];

  readonly types = computed<readonly VariableType[]>(() => {
    const accepted = this.acceptedTypes();
    return accepted.length > 0 ? accepted : VariableBindDialogComponent.allTypes;
  });

  readonly typeLabels = computed(() => variableTypeLabels(key => this.localization.translateKey(key)));

  readonly typeOptions = computed<SelectOption[]>(() =>
    this.types().map(t => ({ value: t, label: this.typeLabels()[t] })));

  readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindHeading));
  readonly nameFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.NameField));
  readonly typeFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.TypeField));
  readonly bindActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindAction));
  readonly nameTakenMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.NameTaken, { name: this.nameSanitized().sanitized }));
  readonly bindFailedMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindFailed));

  ngOnInit(): void {
    this.rawName.set(this.variableService.sanitizeNameLocal(this.suggestedName()).sanitized);
    const offered = this.types();
    const declared = this.node.type ?? 'text';
    this.type.set(offered.includes(declared) ? declared : offered[0]);
  }

  private suggestedName(): string {
    if (this.node.suggestedName) {
      return this.node.suggestedName;
    }

    const label = resolveLocalizedText(this.node.displayName, this.localization) || this.node.name;
    const segments = this.node.id.split('/').filter(Boolean);
    if (segments.length < 2) {
      return label;
    }
    return `${segments[segments.length - 2]} ${label}`;
  }

  onNameInput(value: string): void {
    this.rawName.set(value);
    this.failed.set(false);
  }

  setType(type: VariableType): void {
    this.type.set(type);
  }

  async submit(): Promise<void> {
    if (!this.canSubmit()) return;
    this.submitting.set(true);
    this.failed.set(false);
    try {
      const response = await this.variableCatalog.bind({
        integrationId: this.integrationId,
        resourceId: this.node.id,
        name: this.nameSanitized().sanitized,
        type: this.type(),
      });
      if (response.variable) {
        dismissModal(this.modal, () => this.bound.emit(response.variable!));
      } else {
        this.failed.set(true);
      }
    } catch {
      this.failed.set(true);
    } finally {
      this.submitting.set(false);
    }
  }

  cancel(): void {
    dismissModal(this.modal, () => this.close.emit());
  }
}
